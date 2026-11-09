using System.Globalization;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Accord de l'opérateur dans la fenêtre de confirmation Windows de l'hôte élevé, exigé avant chaque opération privilégiée
/// demandée par la page en mode poste. Une seule confirmation à la fois par composition ; après un refus, un délai dépassé
/// ou une fenêtre en échec, une nouvelle demande attend <see cref="Cooldown"/>. En démonstration, aucune fenêtre : les
/// écritures restent refusées à l'admission et les autres opérations s'exécutent comme avant.
/// </summary>
public static class NativeConfirmation
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>Délai de réponse de la fenêtre de confirmation Windows : sans décision, elle se ferme (confirmation.timeout).</summary>
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Marge ajoutée au délai de réponse : construction de la fiche, affichage de la fenêtre et démarrage après l'accord.</summary>
    internal static readonly TimeSpan StartMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Réserve la confirmation avant que le gestionnaire ne consomme un plan ou ne prenne un bail : un refus ici ne consomme
    /// rien. Démonstration : tour sans fenêtre. Poste sans fenêtre de confirmation : confirmation.unavailable. FILPRO
    /// verrouillé dans une session d'un autre compte : presence.locked. Fenêtre déjà ouverte ou refus récent :
    /// confirmation.busy.
    /// </summary>
    public static NativeConfirmationTurn Begin(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Mode == AppMode.Simulation) return new NativeConfirmationTurn(null, null, null);
        var confirmer = context.Service<IOperatorConfirmation>()
            ?? throw new FilproOperationException(FilproErrors.ConfirmationUnavailable, FilproErrors.ConfirmationUnavailableMessage);
        if (AcrossAccounts(context) && Locked(context))
            throw new FilproOperationException(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage);
        var slot = context.Shared(() => new ConfirmationSlot(context.Clock));
        if (!slot.TryReserve()) throw new FilproOperationException(FilproErrors.ConfirmationBusy, FilproErrors.ConfirmationBusyMessage);
        return new NativeConfirmationTurn(context, confirmer, slot);
    }

    /// <summary>Affichage sous un autre compte que FILPRO élevé, ou indéterminé : une situation absente compte comme telle.</summary>
    public static bool AcrossAccounts(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (context.Service<HostSituation>()?.Kind ?? SessionSituation.Unknown) != SessionSituation.SameAccount;
    }

    /// <summary>Présence absente, pas encore armée ou verrouillée : seules la lecture locale et le suivi restent admis.</summary>
    public static bool Locked(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Service<IOperatorPresence>() is not { Armed: true, Latched: false };
    }

    /// <summary>
    /// Fiche complétée par l'hôte : case « Verrouiller FILPRO après le lancement » pour une opération sur le poste dans
    /// une session d'un autre compte, et ligne grave quand le compte de la session peut modifier le dossier de FILPRO.
    /// </summary>
    internal static ConfirmationSheet Complete(EngineContext context, ConfirmationSheet sheet, bool acrossAccounts)
    {
        var offerLock = acrossAccounts && PrivilegePolicy.TierOf(sheet.Operation) == PrivilegeTier.System;
        var exposure = context.Service<PackExposure>();
        if (!offerLock && exposure is null) return sheet;
        return sheet with
        {
            OfferLockAfterStart = offerLock,
            Attention = exposure is null ? sheet.Attention : [.. sheet.Attention, new ConfirmationAttention(exposure.Attention, Severe: true)],
        };
    }
}

/// <summary>
/// Tour réservé par <see cref="NativeConfirmation.Begin"/>. <see cref="ConfirmThenAsync{T}"/> ne s'exécute qu'une fois et
/// ne lance l'opération qu'après une décision <see cref="ConfirmationOutcome.Approved"/> ; <see cref="Dispose"/> libère la
/// réservation si elle est encore tenue.
/// </summary>
public sealed class NativeConfirmationTurn : IDisposable
{
    private readonly EngineContext? context;
    private readonly IOperatorConfirmation? confirmer;
    private ConfirmationSlot? slot;
    private int used;

    internal NativeConfirmationTurn(EngineContext? context, IOperatorConfirmation? confirmer, ConfirmationSlot? slot)
    {
        this.context = context;
        this.confirmer = confirmer;
        this.slot = slot;
    }

    /// <summary>
    /// Construit la fiche, attend la décision prise dans la fenêtre Windows puis lance <paramref name="start"/> seulement
    /// après accord. Refus : confirmation.refused ; délai dépassé : confirmation.timeout ; fiche ou fenêtre en échec :
    /// confirmation.unavailable. L'annulation du jeton lève <see cref="OperationCanceledException"/>. En démonstration, la
    /// fiche n'est jamais construite et aucune fenêtre n'est ouverte.
    /// </summary>
    public async Task<T> ConfirmThenAsync<T>(Func<ConfirmationSheet> sheet, Func<Task<T>> start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(start);
        if (Interlocked.Exchange(ref used, 1) != 0) throw new InvalidOperationException("La confirmation de ce tour a déjà été demandée.");
        if (context is null || confirmer is null) return await start().ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref slot) is null, this);
        var acrossAccounts = NativeConfirmation.AcrossAccounts(context);
        var built = NativeConfirmation.Complete(context, Build(context, sheet), acrossAccounts);
        var presence = acrossAccounts ? context.Service<IOperatorPresence>() : null;
        // Verrouillé entre l'admission et la fenêtre : rien n'est montré ni lancé.
        if (acrossAccounts && NativeConfirmation.Locked(context))
        {
            Release(cooldown: false);
            throw new FilproOperationException(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage);
        }
        var started = context.Clock.GetTimestamp();
        ConfirmationResult result;
        try
        {
            result = await confirmer.ConfirmAsync(built, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Décision de confirmation vide.");
        }
        catch (OperationCanceledException)
        {
            context.Log.Info(Line(context, built, "annulée", started));
            throw;
        }
        catch (FilproOperationException)
        {
            // Refus attendu de l'hôte, par exemple un sélecteur de fichiers déjà ouvert : rien n'a été montré ni lancé.
            Release(cooldown: false);
            context.Log.Info(Line(context, built, "non affichée", started));
            throw;
        }
#pragma warning disable CA1031 // Une fenêtre en échec vaut un refus : rien n'est lancé et l'échec est tracé.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Release(cooldown: true);
            context.Log.Error(Line(context, built, "indisponible", started), ex);
            throw new FilproOperationException(FilproErrors.ConfirmationUnavailable, FilproErrors.ConfirmationUnavailableMessage);
        }
        switch (result.Outcome)
        {
            case ConfirmationOutcome.Approved:
                // Verrouillé pendant la décision (session verrouillée, délai, refus répétés) : l'accord ne lance plus rien.
                if (acrossAccounts && NativeConfirmation.Locked(context))
                {
                    context.Log.Info(Line(context, built, "acceptée après le verrouillage, rien n’est lancé", started));
                    Release(cooldown: false);
                    throw new FilproOperationException(FilproErrors.PresenceLocked, FilproErrors.PresenceLockedMessage);
                }
                context.Log.Info(Line(context, built, "acceptée", started));
                // Case cochée sur une fiche qui la proposait : verrou posé dès l'accord, avant de rendre la fenêtre et avant le
                // lancement, pour qu'aucune autre demande ne passe entre les deux ; il tient même si le lancement échoue.
                if (built.OfferLockAfterStart && result.LockAfterStart) presence?.Latch(PresenceReason.LockAfterStart);
                Release(cooldown: false);
                return await start().ConfigureAwait(false);
            case ConfirmationOutcome.TimedOut:
                context.Log.Info(Line(context, built, "sans réponse", started));
                Release(cooldown: true);
                presence?.RecordRefusal();
                throw new FilproOperationException(FilproErrors.ConfirmationTimeout, FilproErrors.ConfirmationTimeoutMessage);
            default:
                // Toute autre décision est un refus.
                context.Log.Info(Line(context, built, "refusée", started));
                Release(cooldown: true);
                presence?.RecordRefusal();
                throw new FilproOperationException(FilproErrors.ConfirmationRefused, FilproErrors.ConfirmationRefusedMessage);
        }
    }

    /// <summary>
    /// Refuse avant la fenêtre un objet qui expirerait pendant qu'elle peut rester ouverte : il doit rester au moins
    /// <see cref="NativeConfirmation.ResponseTimeout"/> plus 30 secondes avant <paramref name="deadline"/>. Le refus porte le
    /// code et le message que le moteur ou le service opposerait à l'objet expiré ; aucune fenêtre n'est ouverte et
    /// <see cref="Dispose"/> rend la réservation sans délai. En démonstration, aucune fenêtre : rien n'est refusé ici.
    /// </summary>
    public void RequireTimeLeft(DateTimeOffset deadline, string code, string message)
    {
        if (context is null || confirmer is null) return;
        if (deadline - context.Clock.GetUtcNow() < NativeConfirmation.ResponseTimeout + NativeConfirmation.StartMargin)
            throw new FilproOperationException(code, message);
    }

    public void Dispose() => Release(cooldown: false);

    private void Release(bool cooldown) => Interlocked.Exchange(ref slot, null)?.Release(cooldown);

    // Un refus attendu (code et message français) ou une annulation se propage ; toute autre erreur de la fiche vaut indisponibilité.
    private static ConfirmationSheet Build(EngineContext context, Func<ConfirmationSheet> sheet)
    {
        try
        {
            return sheet() ?? throw new InvalidOperationException("Fiche de confirmation vide.");
        }
        catch (Exception ex) when (ex is not (FilproOperationException or OperationCanceledException))
        {
            context.Log.Error("Confirmation Windows : fiche impossible à construire, aucune fenêtre ouverte.", ex);
            throw new FilproOperationException(FilproErrors.ConfirmationUnavailable, FilproErrors.ConfirmationUnavailableMessage);
        }
    }

    // Une ligne par décision : opération, début de l'empreinte, situation, issue et durée ; jamais un chemin, un compte ni un texte de la page.
    private static string Line(EngineContext context, ConfirmationSheet sheet, string outcome, long started)
    {
        var operation = OperationRegistry.IsValidName(sheet.Operation) ? sheet.Operation : "inconnue";
        var digest = new string((sheet.Digest ?? "").Where(char.IsAsciiHexDigit).Take(12).ToArray());
        var situation = context.Service<HostSituation>()?.Kind ?? SessionSituation.Unknown;
        var elapsed = (long)context.Clock.GetElapsedTime(started).TotalMilliseconds;
        return string.Create(CultureInfo.InvariantCulture,
            $"Confirmation Windows {operation} : {outcome} (empreinte {digest}, situation {situation}, {elapsed} ms).");
    }
}

/// <summary>Réservation unique de la fenêtre de confirmation d'une composition, avec le délai qui suit un refus.</summary>
internal sealed class ConfirmationSlot(TimeProvider clock)
{
    private readonly object gate = new();
    private bool held;
    private DateTimeOffset availableAt = DateTimeOffset.MinValue;

    public bool TryReserve()
    {
        lock (gate)
        {
            if (held || clock.GetUtcNow() < availableAt) return false;
            held = true;
            return true;
        }
    }

    public void Release(bool cooldown)
    {
        lock (gate)
        {
            held = false;
            if (cooldown) availableAt = clock.GetUtcNow() + NativeConfirmation.Cooldown;
        }
    }
}
