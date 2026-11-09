using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

/// <summary>
/// Liaisons de la confirmation Windows (2.0.8) : reçu de réparation relu puis lié par son SHA-256, catalogue lié au
/// manifeste relu, équivalents du monde fictif et version conservée par une mise à jour préparée. Reçus, magasins et packs
/// sont fictifs, dans des dossiers temporaires : aucun profil, registre ou outil USMT n'est touché. Aucun travail réel ne
/// démarre : chaque démarrage réel testé bute ensuite sur une garde qui refuse toujours (session ou clé du magasin).
/// Prérequis Windows : processus élevé pour les démarrages réels, WTS capable de prouver l'absence de session d'un SID fictif.
/// </summary>
internal static class ConfirmationBindingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string ReceiptSid = "S-1-5-21-3000000001-3000000002-3000000003-1001";
    private const string OtherSid = "S-1-5-21-3000000001-3000000002-3000000003-1002";
    private static readonly string OtherHash = new('0', 64);
    private static readonly JsonSerializerOptions ReceiptJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Test]
    public static async Task RecuFalsifieOuHorsDuDossierPriveRefuseParLaRevue()
    {
        using var operations = new PrivateOperations();
        var service = operations.Service(new ExecutingAccount(OtherSid));
        var receipt = Receipt(operations.Root);
        foreach (var (path, reason) in new[]
        {
            (operations.Write(receipt, key: RandomNumberGenerator.GetBytes(32)), "sceau calculé avec une autre clé"),
            (operations.Write(receipt with { Stage = "RolledBack" }, signedAs: receipt), "contenu changé après un sceau légitime"),
        })
        {
            var refused = await Check.ThrowsAsync<FilproOperationException>(() => service.ReviewRepairRollbackAsync(new(path), Ct), reason);
            Check.Equal("repair.receipt", refused.Code, "refus du reçu : " + reason);
            Check.Contains("a été modifié", refused.Message, "refus par le sceau : " + reason);
        }
        var outside = operations.Write(receipt, path: Path.Combine(operations.Root, "Ailleurs", Guid.NewGuid().ToString("N"), "repair-receipt.json"));
        var misplaced = await Check.ThrowsAsync<FilproOperationException>(() => service.ReviewRepairRollbackAsync(new(outside), Ct),
            "reçu authentique hors du dossier privé");
        Check.Equal("repair.receipt", misplaced.Code, "refus du reçu déplacé");
        Check.Contains("dossier privé", misplaced.Message, "seuls les reçus du dossier privé des opérations sont relus");
        Check.Equal(0, service.List().Count, "aucun travail démarré par une revue refusée");
    }

    [Test]
    public static async Task RecuValideDonneSesChampsEtLEmpreinteDeSesOctets()
    {
        // L'absence de session du SID fictif est prouvée par WTS, comme avant un vrai retour arrière.
        using var operations = new PrivateOperations();
        var service = operations.Service(new ExecutingAccount(OtherSid));
        var receipt = Receipt(operations.Root);
        var path = operations.Write(receipt);
        var bytes = File.ReadAllBytes(path);
        var review = await service.ReviewRepairRollbackAsync(new(path), Ct);
        Check.Equal(BackupManifestCodec.Hash(bytes), review.ReceiptSha256, "SHA-256 des octets vérifiés du reçu");
        Check.Equal(StoragePathPolicy.NormalizeNative(path), review.ReceiptPath, "chemin du reçu relu");
        Check.False(review.Demonstration, "revue du moteur réel");
        Check.Equal(receipt.Sid, review.Sid, "SID scellé");
        Check.Equal(receipt.Account, review.Account, "compte scellé");
        Check.Equal(receipt.Stage, review.Stage, "étape enregistrée");
        Check.Equal(receipt.OriginalPath, review.OriginalPath, "dossier d'origine");
        Check.Equal(receipt.PreservedPath, review.PreservedPath, "dossier conservé");
        Check.Equal(receipt.BackupDirectory, review.BackupDirectory, "sauvegardes de la réparation");
        Check.Equal(receipt.SafetyBundle, review.SafetyBundle, "sauvegarde préalable");
        Check.Equal(receipt.NewProfilePreservedPath, review.NewProfilePreservedPath, "nouveau profil conservé");
        Check.True(bytes.SequenceEqual(File.ReadAllBytes(path)), "la revue ne réécrit pas le reçu");
        Check.Equal(0, service.List().Count, "la revue ne démarre aucun travail");
        var current = await Check.ThrowsAsync<FilproOperationException>(() =>
            operations.Service(new ExecutingAccount(ReceiptSid)).ReviewRepairRollbackAsync(new(path), Ct), "reçu du compte exécutant");
        Check.Equal("profile.current", current.Code, "garde de session appliquée dès la revue, avant toute fenêtre");
    }

    [Test]
    public static async Task RetourArriereReelExigeLeRecuConfirme()
    {
        RequireElevated();
        using var operations = new PrivateOperations();
        // Compte exécutant = compte du reçu : un reçu admis par la liaison bute ensuite sur la garde de session.
        var service = operations.Service(new ExecutingAccount(ReceiptSid));
        var receipt = Receipt(operations.Root);
        var path = operations.Write(receipt);
        // Empreinte montrée par la revue (RecuValideDonneSesChampsEtLEmpreinteDeSesOctets) : celle des octets du reçu.
        var confirmed = BackupManifestCodec.Hash(File.ReadAllBytes(path));
        var other = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartRepairRollbackAsync(new(path, OtherHash), new(true, true), Ct), "empreinte différente de celle montrée");
        Check.Equal("repair.receipt", other.Code, "refus du reçu");
        Check.Contains("changé depuis sa confirmation", other.Message, "refus par la liaison, avant la garde de session");
        var admitted = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartRepairRollbackAsync(new(path, confirmed.ToLowerInvariant()), new(true, true), Ct), "empreinte montrée");
        Check.Equal("profile.current", admitted.Code, "le reçu montré passe la liaison, quelle que soit la casse ; les gardes suivantes restent");
        Check.Equal(confirmed, BackupManifestCodec.Hash(File.ReadAllBytes(path)), "aucun refus ne réécrit le reçu");
        operations.Write(receipt with { Stage = "RollbackPartial" }, path: path);
        var replaced = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartRepairRollbackAsync(new(path, confirmed), new(true, true), Ct), "reçu rescellé après la confirmation");
        Check.Equal("repair.receipt", replaced.Code, "un reçu authentique mais différent de celui montré est refusé");
        Check.Contains("changé depuis sa confirmation", replaced.Message, "refus par la liaison");
        Check.Equal(0, service.List().Count, "aucun travail démarré");
    }

    [Test]
    public static async Task CatalogueReelExigeLeManifesteConfirme()
    {
        RequireElevated();
        using var operations = new PrivateOperations();
        var service = operations.Service(new ExecutingAccount(OtherSid));
        Check.True(service.Capabilities.Recover, "lecture longue disponible : processus élevé et outils USMT déclarés par le harnais");
        // Magasin chiffré sans clé fournie : un manifeste admis par la liaison bute ensuite sur la garde de clé.
        var bundle = WriteEncryptedLegacyStore(Path.Combine(operations.Root, "Magasin"), "V1-magasin-confirme");
        var inspection = await service.InspectStoreAsync(bundle, Ct);
        Check.True(inspection.Store.Ready && inspection.Store.Encrypted, "magasin chiffré exploitable");
        Check.Equal(BackupManifestCodec.Hash(File.ReadAllBytes(Path.Combine(bundle, "filpro-backup.json"))), inspection.ManifestSha256,
            "empreinte du manifeste relu");
        var admitted = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartCatalogAsync(new(bundle, null, inspection.ManifestSha256.ToLowerInvariant()), new(true, true), Ct), "manifeste montré");
        Check.Equal("usmt.crypto", admitted.Code, "le manifeste montré passe la liaison, quelle que soit la casse ; la clé reste exigée");
        var other = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartCatalogAsync(new(bundle, null, OtherHash), new(true, true), Ct), "empreinte différente de celle montrée");
        Check.Equal("store.changed", other.Code, "refus du magasin");
        Check.Contains("changé depuis sa confirmation", other.Message, "refus par la liaison, avant la garde de clé");
        WriteEncryptedLegacyStore(bundle, "V1-magasin-remplace");
        Check.True((await service.InspectStoreAsync(bundle, Ct)).Store.Ready, "le magasin remplacé reste exploitable");
        var replaced = await Check.ThrowsAsync<FilproOperationException>(() =>
            service.StartCatalogAsync(new(bundle, null, inspection.ManifestSha256), new(true, true), Ct), "manifeste réécrit après la confirmation");
        Check.Equal("store.changed", replaced.Code, "un magasin exploitable mais différent de celui montré est refusé");
        Check.Equal(0, service.List().Count, "aucun travail démarré");
    }

    [Test]
    public static async Task RevueFictiveDuRecuResteDansLeMondeDeDemonstration()
    {
        var world = World();
        var (profile, plan, receipt) = await Repaired(world);
        var review = await world.ReviewRepairRollbackAsync(new(receipt), Ct);
        Check.True(review.Demonstration, "revue fictive explicite");
        Check.Equal(receipt, review.ReceiptPath, "reçu émis par ce monde");
        Check.Equal(profile.Sid, review.Sid, "profil fictif réparé");
        Check.Equal(profile.Account, review.Account, "compte fictif réparé");
        Check.Equal(plan.ProfilePath, review.OriginalPath, "dossier d'origine du plan");
        Check.Equal(plan.PreservedProfilePath, review.PreservedPath, "dossier conservé du plan");
        Check.True(review.BackupDirectory.StartsWith(@"D:\FILPRO-DEMO\", StringComparison.OrdinalIgnoreCase)
            && review.SafetyBundle.StartsWith(@"D:\FILPRO-DEMO\", StringComparison.OrdinalIgnoreCase), "sauvegardes du seul catalogue fictif");
        Check.Null(review.NewProfilePreservedPath, "aucun nouveau profil inventé");
        Check.True(review.ReceiptSha256.Length == 64 && review.ReceiptSha256.All(Uri.IsHexDigit), "empreinte SHA-256 hexadécimale");
        Check.Equal(review.ReceiptSha256, (await world.ReviewRepairRollbackAsync(new(receipt), Ct)).ReceiptSha256, "empreinte stable d'une revue à l'autre");
        Check.Equal("repairPrepared", ProfileState(world, profile.Sid), "la revue ne change pas l'état du profil");
        Check.Equal(1, world.ReadCatalog(Ct).RepairReceipts!.Count, "la revue ne consomme pas le reçu");
        var unknown = await Check.ThrowsAsync<FilproOperationException>(() =>
            world.ReviewRepairRollbackAsync(new(@"C:\Reel\repair-receipt.json"), Ct), "reçu hors du monde fictif");
        Check.Equal("demonstration.boundary", unknown.Code, "aucun fichier réel ouvert");
    }

    [Test]
    public static async Task DemarragesFictifsExigentLeRecuEtLeManifesteConfirmes()
    {
        var world = World();
        var (profile, _, receipt) = await Repaired(world);
        var review = await world.ReviewRepairRollbackAsync(new(receipt), Ct);
        var count = world.List().Count;
        var otherReceipt = await Check.ThrowsAsync<FilproOperationException>(() =>
            world.StartRepairRollbackAsync(new(receipt, OtherHash), new(true, true), Ct), "empreinte de reçu différente");
        Check.Equal("demonstration.boundary", otherReceipt.Code, "refus fermé du monde fictif");
        Check.Contains("changé depuis sa confirmation", otherReceipt.Message, "refus par la liaison");
        Check.Equal(count, world.List().Count, "aucun travail fictif sur refus");
        Check.Equal("repairPrepared", ProfileState(world, profile.Sid), "profil fictif inchangé");
        var rollback = await Complete(world, () => world.StartRepairRollbackAsync(new(receipt, review.ReceiptSha256.ToLowerInvariant()), new(true, true), Ct));
        Check.Equal(ExecutionState.Succeeded, rollback.State, "le reçu montré est admis, quelle que soit la casse");
        Check.Equal("original", ProfileState(world, profile.Sid), "retour arrière fictif appliqué");

        var store = await LegacyStore(world);
        count = world.List().Count;
        var otherStore = await Check.ThrowsAsync<FilproOperationException>(() =>
            world.StartCatalogAsync(new(store.Bundle, null, OtherHash), new(true, true), Ct), "empreinte de manifeste différente");
        Check.Equal("demonstration.boundary", otherStore.Code, "refus fermé du monde fictif");
        Check.Contains("changé depuis sa confirmation", otherStore.Message, "refus par la liaison");
        Check.Equal(count, world.List().Count, "aucun catalogue fictif lancé sur refus");
        Check.Equal(0, (await world.InspectStoreAsync(store.Bundle, Ct)).Entries.Count, "aucune entrée publiée sur refus");
        var catalog = await Complete(world, () => world.StartCatalogAsync(new(store.Bundle, null, store.ManifestSha256.ToUpperInvariant()), new(true, true), Ct));
        Check.Equal(ExecutionState.Succeeded, catalog.State, "le manifeste montré est admis, quelle que soit la casse");
    }

    [Test]
    public static async Task PreparationConserveLaVersionInstalleeCommeVersionPrecedente()
    {
        using var fixture = new PackTestFixture();
        var installed = fixture.CreatePack("installed", "2.0.0", "OLD-FAKE");
        var zip = fixture.Zip(fixture.CreatePack("target", "2.1.0", "NEW-FAKE"), "FILPRO-2.1.0-win-x64-PROD.zip");
        using var service = new LocalUpdateService(installed, fixture.Data, () => true);
        Check.Null((await service.ReadAsync(Ct)).PreviousVersion, "aucune version précédente inventée sans transaction");
        var offer = await service.InspectAsync(new(UpdateSourceKind.ManualZip, zip, PackTestFixture.Hash(File.ReadAllBytes(zip))), Ct);
        var prepared = await service.PrepareAsync(offer.Id, true, Ct);
        Check.Equal(UpdateStage.Prepared, prepared.Stage, "pack fictif préparé");
        Check.Equal("2.0.0", prepared.CurrentVersion, "version installée");
        Check.Equal("2.1.0", prepared.TargetVersion, "version préparée");
        Check.Equal("2.0.0", prepared.PreviousVersion, "version conservée par la transaction");
        Check.Equal("2.0.0", (await service.ReadAsync(Ct)).PreviousVersion, "état relu avec la transaction");
        using var reopened = new LocalUpdateService(installed, fixture.Data, () => true);
        var status = await reopened.ReadAsync(Ct);
        Check.Equal(prepared.TransactionId, status.TransactionId, "même transaction après réouverture");
        Check.Equal("2.0.0", status.PreviousVersion, "version précédente relue dans le journal, pas en mémoire");
    }

    // Le démarrage réel refuse un processus non élevé avant de relire le reçu ou le magasin : la liaison ne serait pas atteinte.
    private static void RequireElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Check.True(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            "contrôle à exécuter dans un processus élevé, comme le runner Windows : sinon le démarrage refuse avant la liaison");
    }

    // Reçu d'une reprise après capture du nouveau profil : tous les champs montrés par la revue sont renseignés.
    private static ReceiptDocument Receipt(string root)
    {
        var id = Guid.NewGuid().ToString("N");
        var replacement = Guid.NewGuid().ToString("N");
        var original = Path.Combine(root, "Profils", "fictif");
        return new(1, id, ReceiptSid, @"FILPRO-TEST\fictif", original, original + ".old-filpro-" + id[..8],
            Path.Combine(root, "Prealable", "FILPRO-repair-" + id), Path.Combine(root, "Prealable", "FILPRO-sauvegarde-fictive"),
            new("profile-list.json", new string('1', 64), 512), new("NTUSER.DAT", new string('2', 64), 262144), "RollbackPending",
            original + ".new-filpro-" + replacement[..8], new("new-profile-list-" + replacement + ".json", new string('3', 64), 512),
            new("new-NTUSER-" + replacement + ".DAT", new string('4', 64), 262144));
    }

    // Magasin V1 construit comme dans LegacyExecutionTests, mais chiffré : règle hashée, contenu USMT présent mais jamais lu.
    private static string WriteEncryptedLegacyStore(string bundle, string id)
    {
        Directory.CreateDirectory(Path.Combine(bundle, "Rules")); Directory.CreateDirectory(Path.Combine(bundle, "Store", "USMT"));
        var rules = LegacyUsmtRules.Capture(new(
            [new(SoftwareScope.Roaming, "vlc", "VLC", SoftwareCategory.Preferences, true, false, false, null, false, false)],
            [new(SoftwareScope.Hkcu, @"Software\VideoLAN\VLC", "VLC", SoftwareCategory.Preferences, true, false, false, null, false, false)]));
        File.WriteAllBytes(Path.Combine(bundle, "Rules", "filpro-config.xml"), rules);
        File.WriteAllText(Path.Combine(bundle, "Store", "USMT", "USMT.MIG"), "contenu-fictif-jamais-lu");
        File.WriteAllBytes(Path.Combine(bundle, "filpro-backup.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = 1, Simulated = false, Compressed = true, Encrypted = true, Status = "Completed", Users = new[] { @"LAB\Alice" },
            Rules = new[] { new { Path = @"Rules\filpro-config.xml", SHA256 = BackupManifestCodec.Hash(rules) } },
            SourceProfiles = new[] { new { Account = @"LAB\Alice", SID = "S-1-5-21-1-2-3-1000", Path = @"C:\Users\Alice" } },
            Id = id, UsmtVersion = "10.0.1.0",
        }));
        return bundle;
    }

    private static SimulationWorld World() => new(SimulationOptions.Default, new AcceleratedClock());
    private static string ProfileState(SimulationWorld world, string sid) => world.ReadCatalog(Ct).ProfileStates!.Single(item => item.Sid == sid).State;
    private static async Task<(ProfileCandidate Profile, RepairPlanReview Plan, string Receipt)> Repaired(SimulationWorld world)
    {
        var profile = world.Poste.ReadProfiles(Ct).Profiles.First(candidate => candidate.Account == @"DEMO\sophie");
        var plan = await world.CreateRepairPlanAsync(new(profile.Sid, world.ReadCatalog(Ct).Assets.SafetyBackupRoot), Ct);
        var repaired = await Complete(world, () => world.StartRepairAsync(plan, new(true, true), Ct));
        Check.Equal(ExecutionState.Succeeded, repaired.State, "réparation fictive préalable terminée");
        return (profile, plan, repaired.ResultPath!);
    }
    private static async Task<ExecutionStoreInspection> LegacyStore(SimulationWorld world)
    {
        foreach (var summary in world.ReadCatalog(Ct).Stores)
        {
            var store = await world.InspectStoreAsync(summary.Bundle, Ct);
            if (store.Store.Legacy) return store;
        }
        throw new InvalidOperationException("Le monde de démonstration doit proposer un ancien magasin sans catalogue.");
    }
    private static async Task<ExecutionJobSnapshot> Complete(SimulationWorld world, Func<Task<ExecutionJobSnapshot>> start)
    {
        var completion = new TaskCompletionSource<ExecutionJobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(ExecutionJobSnapshot job) { if (ExecutionJobCoordinator.IsTerminal(job.State)) completion.TrySetResult(job); }
        world.Changed += Observe;
        try { await start(); return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { world.Changed -= Observe; }
    }

    // Mêmes champs que le reçu scellé du moteur (WindowsFilproExecutionService.Repair.cs), sérialisés en camelCase.
    private sealed record ReceiptDocument(int SchemaVersion, string Id, string Sid, string Account, string OriginalPath,
        string PreservedPath, string BackupDirectory, string SafetyBundle, ManifestFileReference RegistryBackup,
        ManifestFileReference HiveBackup, string Stage, string? NewProfilePreservedPath = null,
        ManifestFileReference? NewRegistryBackup = null, ManifestFileReference? NewHiveBackup = null);
    private sealed record SealedReceipt(string Payload, string Signature);

    /// <summary>
    /// Dossier privé fictif des opérations et sa clé locale des reçus. Un reçu est scellé comme le moteur l'écrit : charge
    /// JSON en base64 et HMAC-SHA256 hexadécimal de ces octets.
    /// </summary>
    private sealed class PrivateOperations : IDisposable
    {
        private readonly byte[] sealingKey = RandomNumberGenerator.GetBytes(32);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filpro-binding-" + Guid.NewGuid().ToString("N"));
        public string State => Path.Combine(Root, "Operations");

        public PrivateOperations()
        {
            Directory.CreateDirectory(State);
            File.WriteAllBytes(Path.Combine(State, "repair-authentication.key"), sealingKey);
        }

        // Outils déclarés pour la capacité seulement : aucun processus n'est lancé et le pack n'est jamais relu.
        public WindowsFilproExecutionService Service(IFilproPosteSource poste) => new(poste,
            new WindowsExecutionOptions(Path.Combine(Root, "USMT"), State,
                new[] { "scanstate.exe", "loadstate.exe", "usmtutils.exe" }.ToDictionary(name => name, _ => new string('A', 64), StringComparer.Ordinal),
                _ => throw new TestFailureException("Aucun travail réel ne doit relire le pack.")),
            TimeProvider.System, new RefusedProcesses());

        /// <summary>Sans <paramref name="signedAs"/>, le sceau porte sur le contenu écrit ; sinon sur un autre contenu.</summary>
        public string Write(ReceiptDocument receipt, byte[]? key = null, ReceiptDocument? signedAs = null, string? path = null)
        {
            path ??= Path.Combine(State, Guid.NewGuid().ToString("N"), "repair-receipt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = JsonSerializer.SerializeToUtf8Bytes(receipt, ReceiptJson);
            var signed = signedAs is null ? payload : JsonSerializer.SerializeToUtf8Bytes(signedAs, ReceiptJson);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new SealedReceipt(Convert.ToBase64String(payload),
                Convert.ToHexString(HMACSHA256.HashData(key ?? sealingKey, signed))), ReceiptJson));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>Fournisseur poste non fictif minimal : la revue et les démarrages testés ne lisent que le compte exécutant.</summary>
    private sealed class ExecutingAccount(string sid) : IFilproPosteSource
    {
        public bool Demonstration => false;
        public string ReadCurrentAccountSid(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sid;
        }
        public ProfileInventory ReadProfiles(CancellationToken cancellationToken) => throw new NotSupportedException();
        public SoftwareInventory ReadSoftware(string profileSid, SoftwareQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public LastSelection? ReadLastSelection(string profileSid, CancellationToken cancellationToken) => throw new NotSupportedException();
        public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RefusedProcesses : IExecutionProcessRunner
    {
        public Task<ExecutionProcessResult> RunAsync(ExecutionProcessRequest request, CancellationToken cancellationToken) =>
            throw new TestFailureException("Aucun outil USMT ne doit être lancé.");
    }

    private sealed class AcceleratedClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.FromMilliseconds(1), period);
    }
}
