using System.Security.Principal;
using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

internal sealed class PosteSelectionPersistenceTests
{
    private const string Lea = "S-1-5-21-111222333-444555666-777888999-1001";
    private const string Karim = "S-1-5-21-111222333-444555666-777888999-1002";
    private static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    [Test]
    public void FournisseurNatifRelitLesChoixEntreInstancesSansToucherUnProfil()
    {
        var directory = Path.Combine(Path.GetTempPath(), "filpro-poste-selection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new WindowsPosteSource(Clock, directory);
            var file = new SoftwareItem(SoftwareScope.Roaming, "Notepad++", "Notepad++", SoftwareCategory.Modele,
                true, true, true, 100, true, true);
            var registry = new SoftwareItem(SoftwareScope.Hkcu, @"Software\Notepad++", "Notepad++", SoftwareCategory.Autre,
                true, true, true, null, false, false);
            var saved = first.SaveLastSelection(new LastSelection(Lea, [file], [registry], DateTimeOffset.UnixEpoch), CancellationToken.None);
            Check.Equal(Clock.GetUtcNow(), saved.CapturedAt, "l'heure vient du fournisseur, pas de l'interface");

            var second = new WindowsPosteSource(Clock, directory);
            var reloaded = second.ReadLastSelection(Lea, CancellationToken.None);
            Check.NotNull(reloaded, "une nouvelle instance relit les préférences enregistrées");
            Check.Equal(SoftwareCategory.Modele, reloaded!.Files.Single().Category, "le modèle commun est conservé");
            Check.Equal(@"Software\Notepad++", reloaded.Registry.Single().Rel, "la clé est conservée avec le fichier");
            Check.Null(reloaded.Files.Single().Size, "une ancienne mesure n'est pas persistée comme preuve fraîche");
            Check.Null(second.ReadLastSelection(Karim, CancellationToken.None), "aucune sélection d'un autre compte empruntée");
            Check.Equal(1, Directory.GetFiles(directory).Length, "seul le document de préférences est créé dans le dossier temporaire");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DemonstrationConserveSesChoixEnMemoireSansContaminerUneAutreInstance()
    {
        var source = new SimulationPosteSource(SimulationOptions.Default, Clock);
        var chosen = source.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.Single(g => g.Id == "known:notepadpp");
        source.SaveLastSelection(new LastSelection(Lea, chosen.Files, chosen.Registry, DateTimeOffset.UnixEpoch), CancellationToken.None);
        var inventory = source.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None);
        Check.True(inventory.Groups.Single(g => g.Id == "known:notepadpp").Selected == true, "fichiers et clés du choix conservés");
        Check.True(inventory.Groups.Single(g => g.Id == "known:chrome").Selected == false, "Chrome n'est pas recoché par les recommandations");

        source.SaveLastSelection(new LastSelection(Lea, [], [], DateTimeOffset.UnixEpoch), CancellationToken.None);
        Check.True(source.ReadSoftware(Lea, SoftwareQuery.Empty, CancellationToken.None).Groups.All(g => g.Selected == false),
            "désélection explicite mémorisée");
        Check.Null(new SimulationPosteSource(SimulationOptions.Default, Clock).ReadLastSelection(Lea, CancellationToken.None),
            "une nouvelle démonstration n'emprunte pas la mémoire d'une autre instance");
    }

    [Test]
    public void AnnulationNeRemplacePasLesChoixFictifs()
    {
        var source = new SimulationPosteSource(SimulationOptions.Default, Clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check.Throws<OperationCanceledException>(() => source.SaveLastSelection(new LastSelection(Lea, [], [], DateTimeOffset.UnixEpoch), cancelled.Token),
            "la demande déjà annulée ne publie pas de choix");
        Check.Null(source.ReadLastSelection(Lea, CancellationToken.None), "aucun document fictif enregistré après annulation");
        Check.Throws<FilproOperationException>(() => source.SaveLastSelection(new LastSelection("S-1-5-21-0-0-0-9999", [], [], DateTimeOffset.UnixEpoch), CancellationToken.None),
            "un SID hors monde fictif ne peut pas y être ajouté par les préférences");
    }

    [Test]
    public void IdentiteExecutanteNativeProvientDuJetonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            Check.NotNull(identity.User, "le jeton Windows fournit une identité");
            Check.Equal(identity.User!.Value, new WindowsPosteSource(Clock).ReadCurrentAccountSid(CancellationToken.None),
                "le SID exposé correspond au jeton exécutant FILPRO");
        }
        else
        {
            Check.Skip("Lecture du jeton exécutant : contrôle natif Windows non exécuté sur cet hôte.");
        }
    }

    [Test]
    public void IdentiteExecutanteFictiveResteDansLeMondeDeDemonstration()
    {
        var source = new SimulationPosteSource(SimulationOptions.Default, Clock);
        var sid = source.ReadCurrentAccountSid(CancellationToken.None);
        Check.Equal("DEMO\\camille", source.ReadProfiles(CancellationToken.None).Profiles.Single(p => p.Sid == sid).Account,
            "l'identité de démonstration vient du fournisseur fictif");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
