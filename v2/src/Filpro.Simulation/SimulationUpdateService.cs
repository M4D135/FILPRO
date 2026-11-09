using System.Security.Cryptography;
using System.Text;
using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>Transaction de mise à jour fictive : modifie seulement la version en mémoire, sans fichier ni HTTP.</summary>
public sealed class SimulationUpdateService : IFilproUpdateService
{
    private readonly object gate = new();
    private UpdateOffer? offer;
    private string? previousVersion;
    private UpdateSourcePreference sourcePreference = new(true, UpdateSourceKind.Folder, null);
    private UpdateStatus status = new(true, UpdateStage.Idle, null, ProductInfo.Version + "-demo", null,
        "Aucune recherche de mise à jour engagée.", null, false, false, null, ["Démonstration — aucun binaire réel remplacé."]);
    public bool Demonstration => true;
    public event Action<UpdateStatus>? Changed;
    public Task<UpdateSourcePreference> ReadSourceAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); lock (gate) return Task.FromResult(sourcePreference); }
    public Task<UpdateSourcePreference> SaveSourceAsync(UpdateSourceKind kind, string? folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind) || kind != UpdateSourceKind.Folder && folder is not null) throw SimulationWorld.Refused("Source fictive invalide.");
        if (kind == UpdateSourceKind.Folder) SimulationWorld.RequirePath(folder ?? "");
        lock (gate) { sourcePreference = new(true, kind, folder); return Task.FromResult(sourcePreference); }
    }
    public Task<UpdateStatus> ReadAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); lock (gate) return Task.FromResult(status); }

    public Task<UpdateOffer> InspectAsync(UpdateSourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.Kind)) throw SimulationWorld.Refused("Source de mise à jour fictive inconnue.");
        if (request.Path is null) throw SimulationWorld.Refused("Choisir la source fictive du catalogue.");
        SimulationWorld.RequirePath(request.Path);
        if (request.Sha256 is not null && (request.Sha256.Length != 64 || request.Sha256.Any(value => !Uri.IsHexDigit(value))))
            throw SimulationWorld.Refused("Empreinte SHA-256 fictive invalide.");
        lock (gate)
        {
            if (status.Stage is UpdateStage.Preparing or UpdateStage.WaitingForExit or UpdateStage.AwaitingHealth)
                throw SimulationWorld.Refused("Une transaction fictive est déjà en cours.");
            var newer = status.CurrentVersion != "2.1.0-demo";
            offer = new(Guid.NewGuid().ToString("N"), true, request.Kind, "2.1.0-demo", "FILPRO_2.1.0_DEMO_PROD_Windows-x64.zip", 80_000_000,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("FILPRO démonstration uniquement"))), newer,
                "Contrôles du pack fictifs et locaux en mémoire ; aucune archive réelle téléchargée ou ouverte.", []);
            Set(status with { Stage = UpdateStage.Available, TargetVersion = offer.Version, Action = newer ? "Version fictive disponible" : "Version fictive déjà actuelle", ProgressPercent = null, CanInstall = false, Error = null });
            return Task.FromResult(offer);
        }
    }
    public Task<UpdateStatus> PrepareAsync(string offerId, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed);
        lock (gate)
        {
            if (offer?.Id != offerId || !offer.Newer) throw SimulationWorld.Refused("Offre fictive absente ou déjà actuelle.");
            Set(status with { Stage = UpdateStage.Preparing, Action = "Validation fictive de l'inventaire et des empreintes", ProgressPercent = 40 });
            Set(status with { Stage = UpdateStage.Prepared, TransactionId = Guid.NewGuid().ToString("N"), Action = "Pack fictif préparé — installation à confirmer", ProgressPercent = 100, CanInstall = true, CanRollback = false });
            return Task.FromResult(status);
        }
    }
    public Task<UpdateHandoff> InstallAsync(string transactionId, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed);
        lock (gate)
        {
            RequireTransaction(transactionId);
            if (!status.CanInstall || status.Stage != UpdateStage.Prepared) throw SimulationWorld.Refused("Préparer le pack fictif avant son installation.");
            previousVersion = status.CurrentVersion;
            Set(status with { Stage = UpdateStage.AwaitingHealth, Action = "Simulation du démarrage et de l'accusé de santé", CanInstall = false, ProgressPercent = 80 });
            Set(status with { Stage = UpdateStage.Committed, CurrentVersion = status.TargetVersion!, Action = "Mise à jour fictive validée en mémoire", ProgressPercent = 100, CanRollback = true });
            return Task.FromResult(new UpdateHandoff(transactionId, true, false, "Version fictive mise à jour ; aucun EXE ni fichier du pack réel modifié."));
        }
    }
    public Task<UpdateHandoff> RollbackAsync(string transactionId, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed);
        lock (gate)
        {
            RequireTransaction(transactionId);
            if (!status.CanRollback || previousVersion is null) throw SimulationWorld.Refused("Aucune version fictive précédente disponible.");
            Set(status with { Stage = UpdateStage.RollingBack, Action = "Retour à la version fictive précédente", CanRollback = false });
            Set(status with { Stage = UpdateStage.RolledBack, CurrentVersion = previousVersion, Action = "Retour arrière fictif terminé", ProgressPercent = 100, CanInstall = false });
            return Task.FromResult(new UpdateHandoff(transactionId, true, false, "Version fictive précédente rétablie ; aucun fichier réel modifié."));
        }
    }
    private void Set(UpdateStatus value)
    {
        status = value;
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        { try { ((Action<UpdateStatus>)handler)(value); } catch { /* L'observateur ne pilote pas la transaction. */ } }
    }
    private void RequireTransaction(string id)
    { if (string.IsNullOrEmpty(id) || id != status.TransactionId) throw SimulationWorld.Refused("Transaction absente du monde fictif."); }
    private static void RequireConfirmation(bool confirmed)
    { if (!confirmed) throw SimulationWorld.Refused("Confirmation explicite requise pour la mise à jour fictive."); }
}
