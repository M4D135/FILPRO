using System.Text.Json;
using System.Text.RegularExpressions;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Opérations de préparation de l'écran Sauvegarder / Restaurer : lecture seule des profils, de la sélection groupée
/// fichiers+registre à sauvegarder, aperçu des politiques de conflit (coexistence par défaut), aperçu canonisé du plan de
/// sauvegarde (<c>sauvegarder.plan</c>, fonction pure branchée : taille inconnue, exclusions annoncées) et confirmation du
/// plan (<c>sauvegarder.confirmPlan</c>, classe Write) demandée avant toute préparation longue. Le plan de restauration est
/// branché de la même façon : <c>sauvegarder.restorePlan</c> relit les profils cibles et les portées de conflits chez le
/// fournisseur poste puis appelle la fonction pure <see cref="RestorePlan.Validate"/> (correspondances de comptes,
/// sauvegarde préalable, anciens magasins, conflits par portée via <see cref="RestoreConflictPolicies"/>) ;
/// <c>sauvegarder.confirmRestorePlan</c> (classe Write) est demandée avant toute préparation longue et refusée par la garde
/// d'admission en démonstration ou si le moteur n'est pas raccordé. Les confirmations natives consomment le plan serveur
/// et rendent une tâche du moteur. Quand le lecteur est raccordé, l'état du magasin et les portées proviennent du manifeste.
/// </summary>
public sealed class SauvegarderModule : IOperationModule
{
    /// <summary>Borne locale des décisions de conflit soumises par la page (la même que celle de RestoreConflictPolicies).</summary>
    private const int MaxConflictPolicies = 500;

    private static readonly Regex RestorePlanIdPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, RestorePolicyMode> PolicyModes =
        new Dictionary<string, RestorePolicyMode>(StringComparer.OrdinalIgnoreCase)
        {
            ["keepBoth"] = RestorePolicyMode.KeepBoth,
            ["keepCurrent"] = RestorePolicyMode.KeepCurrent,
            ["useSaved"] = RestorePolicyMode.UseSaved,
        };

    private const string DeclaredReadyStatus =
        "Prêt déclaré par l’opérateur : aucun manifeste de magasin n’a été relu par FILPRO dans ce lot.";

    private const string DeclaredNotReadyStatus =
        "Non prêt déclaré par l’opérateur : le plan refuse ce magasin tant qu’il n’est pas déclaré exploitable.";

    private const string ConflictNotice =
        "Portées de conflits lues chez le fournisseur poste pour le profil cible retenu (monde fictif en démonstration) : "
        + "ce ne sont pas les portées du magasin, que le moteur relira avant LoadState. Une décision est exigée pour chaque "
        + "groupe affiché et deux portées qui se chevauchent avec des politiques différentes sont refusées.";

    private static readonly IReadOnlyList<string> RestoreLimitations =
    [
        "Aucun manifeste de magasin n’a été relu : l’exploitabilité du magasin est déclarée par l’opérateur et le moteur devra la contrôler avant LoadState.",
        "Aucun SID ni chemin source n’est emprunté à la cible : la revue des identités ne montre que les comptes, faute de manifeste relu.",
        "Les portées de conflits proviennent du fournisseur poste, pas du magasin : le moteur les revalidera sur le contenu réellement restauré.",
        "Le compte qui exécute FILPRO est relu par le fournisseur ; son propre profil ne peut pas être une cible de restauration.",
        "Aucun exécutant raccordé : cet aperçu conservé côté serveur ne permet pas de restaurer les données.",
        "Aucune restauration exécutée : aucune écriture, aucune ruche montée, aucune mesure d’espace.",
    ];

    public string Screen => "sauvegarder";

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            new OperationDefinition("sauvegarder.profiles", OperationClass.NetworkRead, (args, _, ct) =>
            {
                BridgeArguments.RequireNone(args);
                var poste = RequirePoste(context);
                return Task.FromResult<object?>(poste.ReadProfiles(ct));
            }, "Profils proposés à la sauvegarde (SID, compte, chargement, sessions WTS), en lecture seule.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.software", OperationClass.NetworkRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid"]);
                var sid = BridgeArguments.GetString(values, "sid");
                var poste = RequirePoste(context);
                return Task.FromResult<object?>(poste.ReadSoftware(sid, SoftwareQuery.Empty, ct));
            }, "Sélection groupée fichiers+registre à sauvegarder pour un profil.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.lastSelection", OperationClass.LocalRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid"]);
                var sid = BridgeArguments.GetString(values, "sid").Trim();
                return Task.FromResult<object?>(RequirePoste(context).ReadLastSelection(sid, ct));
            }, "Derniers choix de logiciels du profil, dans les préférences FILPRO ou en mémoire de démonstration.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.saveSelection", OperationClass.LocalWrite, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid", "groupIds"]);
                var sid = BridgeArguments.GetString(values, "sid").Trim();
                var groupIds = BridgeArguments.GetOptionalStringArray(values, "groupIds", SoftwareSelectionCodec.MaxItems)
                    ?? throw BridgeArguments.Invalid("groupIds doit être un tableau, éventuellement vide pour tout décocher.");
                var poste = RequirePoste(context);
                if (!IndexProfiles(poste, ct).ContainsKey(sid))
                {
                    throw new FilproOperationException(BackupPlan.SelectionCode, "Profil inconnu : actualiser les profils avant de mémoriser les choix.");
                }
                var requested = new HashSet<string>(StringComparer.Ordinal);
                foreach (var groupId in groupIds)
                {
                    if (groupId.Length > SoftwareSelectionCodec.MaxRelLength || !requested.Add(groupId))
                    {
                        throw BridgeArguments.Invalid("identifiant de logiciel invalide ou répété.");
                    }
                }
                // La page ne fournit aucun chemin ni clé : les deux portées d'un groupe sont reconstruites ensemble
                // depuis l'inventaire relu. Une sélection vide reste un choix volontaire, sans recommandations réactivées.
                var inventory = poste.ReadSoftware(sid, SoftwareQuery.Empty, ct);
                if (!inventory.Discovery.FilesComplete || !inventory.Discovery.RegistryComplete)
                {
                    throw new FilproOperationException(FilproErrors.SourceUnavailable,
                        "Inventaire fichiers et registre incomplet : les choix mémorisés sont conservés. Actualiser après une lecture complète avant d’enregistrer.");
                }
                var known = inventory.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
                if (!requested.IsSubsetOf(known))
                {
                    throw BridgeArguments.Invalid("logiciel absent de l’inventaire courant : actualiser les choix.");
                }
                var groups = inventory.Groups.Where(group => requested.Contains(group.Id)).ToArray();
                var files = groups.SelectMany(group => group.Files).Select(item => item with { Selected = true }).ToArray();
                var registry = groups.SelectMany(group => group.Registry).Select(item => item with { Selected = true }).ToArray();
                ct.ThrowIfCancellationRequested();
                poste.SaveLastSelection(new LastSelection(sid, files, registry, context.Clock.GetUtcNow()), ct);
                return Task.FromResult<object?>(poste.ReadSoftware(sid, SoftwareQuery.Empty, ct));
            }, "Mémorise les groupes fichiers et registre par profil ; seules les préférences FILPRO sont écrites, jamais le profil Windows.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.conflictPolicies", OperationClass.NetworkRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid"]);
                var sid = BridgeArguments.GetString(values, "sid");
                var poste = RequirePoste(context);
                var inventory = poste.ReadSoftware(sid, SoftwareQuery.Empty, ct);
                // Aperçu honnête : décisions par défaut (coexistence) calculées sans rien écrire.
                var policies = RestoreConflictPolicies.Resolve(inventory.Groups, policies: null);
                var summary = RestoreConflictPolicies.Summarize(inventory.Groups, policies);
                return Task.FromResult<object?>(new RestoreConflictPolicies.ConflictPreview(inventory.Groups, policies, summary));
            }, "Aperçu des politiques de conflit de restauration (coexistence par défaut), sans aucune écriture.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.inspectStore", OperationClass.NetworkRead, async (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["path"]);
                return await ExecutionBridge.Require(context).InspectStoreAsync(BridgeArguments.GetString(values, "path"), ct).ConfigureAwait(false);
            }, "Lit le manifeste du magasin choisi ; les preuves de payload restent distinctes du seul catalogue.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.plan", OperationClass.NetworkRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sids", "destination", "encrypted", "keyPath", "useVss", "noCompress", "continueOnError", "purpose", "migApp"]);
                var sids = BridgeArguments.GetStringArray(values, "sids", BackupPlan.MaxProfiles);
                var destination = BridgeArguments.GetString(values, "destination");
                var encrypted = BridgeArguments.GetOptionalBoolean(values, "encrypted");
                var keyPath = BridgeArguments.GetOptionalString(values, "keyPath");
                var useVss = BridgeArguments.GetOptionalBoolean(values, "useVss");
                var noCompress = BridgeArguments.GetOptionalBoolean(values, "noCompress");
                var continueOnError = BridgeArguments.GetOptionalBoolean(values, "continueOnError");
                var migApp = BridgeArguments.GetOptionalBoolean(values, "migApp");
                var purpose = BridgeArguments.GetOptionalString(values, "purpose");

                // Le plan est une préparation pure : la sélection est relue chez le fournisseur (le JavaScript ne la fabrique
                // pas), puis la fonction pure canonise et refuse. Aucune mesure, aucune écriture, aucune ruche montée, aucune
                // sonde de support : la préparation longue et l'exécution restent du ressort du moteur, après confirmation.
                var poste = RequirePoste(context);
                var profiles = BuildSelection(poste, sids, ct);
                var request = new BackupPlanRequest(
                    profiles,
                    destination,
                    Encrypted: encrypted,
                    KeyPath: keyPath,
                    UseVss: useVss,
                    NoCompress: noCompress,
                    ContinueOnError: continueOnError,
                    Demonstration: poste.Demonstration,
                    Purpose: purpose,
                    MigApp: migApp);
                var planId = Guid.NewGuid().ToString("N");
                var createdUtc = context.Clock.GetUtcNow();
                var plan = BackupPlan.Create(request, planId, createdUtc);
                ExecutionBridge.Plans(context).Add(plan.Id, plan);
                return Task.FromResult<object?>(plan);
            }, "Aperçu canonisé du plan de sauvegarde multi-profils (taille inconnue, exclusions annoncées) : aucune préparation longue, aucune exécution.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.confirmPlan", OperationClass.Write, async (args, _, ct) =>
            {
                // Le plan serveur est consommé une seule fois. Les opérations Write restent refusées en démonstration ;
                // le service natif revalide le poste et le pack avant les préparations longues. La confirmation Windows est
                // réservée avant la prise du plan, puis montre le plan pris, celui-là même qui est lancé.
                var values = BridgeArguments.ReadObject(args, ["planId", "acknowledgeCapacity"]);
                var planId = BridgeArguments.GetString(values, "planId");
                if (!BridgeArguments.GetBoolean(values, "acknowledgeCapacity"))
                {
                    throw new FilproOperationException(BackupPlan.PlanCode, "Accusé de capacité obligatoire : la taille du plan reste inconnue tant que le moteur ne l’a pas mesurée.");
                }
                var service = ExecutionBridge.Require(context, demonstration: false);
                using var turn = NativeConfirmation.Begin(context);
                using var admission = context.Operations.EnterNativeStart();
                var plan = ExecutionBridge.Plans(context).Take<BackupPlanReview>(planId);
                ExecutionBridge.RequirePlanTimeLeft(turn, plan.CreatedUtc);
                return await turn.ConfirmThenAsync(() => ConfirmationSheets.Backup(plan, context.Service<HostSituation>()),
                    () => service.StartBackupAsync(plan, ExecutionBridge.ConfirmedOptions(), ct), ct).ConfigureAwait(false);
            }, "Confirmation du plan serveur avant préparation et sauvegarde natives.", ServedInSimulation: false),

            new OperationDefinition("sauvegarder.restorePlan", OperationClass.NetworkRead, async (args, _, ct) =>
            {
                // Le plan de restauration est une préparation pure : les profils cibles et les portées de conflits sont relus
                // chez le fournisseur poste (la page ne fabrique ni compte, ni chemin, ni état de chargement), puis la fonction
                // pure canonise et refuse. Le magasin n'est pas relu dans ce lot : son exploitabilité est une déclaration de
                // l'opérateur, que le pont ne pose jamais à sa place. Aucune mesure, aucune écriture, aucune ruche montée.
                var values = BridgeArguments.ReadObject(args,
                [
                    "bundle", "storeReady", "manifestUsers", "users", "mappings", "targetSids", "conflictSid", "conflictPolicies",
                    "legacy", "legacyAcknowledged", "partial", "partialAcknowledged", "encrypted", "assumeEncrypted",
                    "sourceKeyPath", "sourceComputerName", "sourceCreatedUtc", "usmtVersion",
                    "priorRoot", "priorKeyPath", "priorIdentified", "priorStatement", "purpose",
                ]);

                var bundle = BridgeArguments.GetString(values, "bundle");
                var hasReader = context.Service<IFilproExecutionService>() is not null;
                var storeReady = hasReader ? false : BridgeArguments.GetBoolean(values, "storeReady");
                IReadOnlyList<string> manifestUsers = hasReader ? [] : BridgeArguments.GetStringArray(values, "manifestUsers", RestorePlan.MaxUsers);
                var users = BridgeArguments.GetStringArray(values, "users", RestorePlan.MaxUsers);
                var targetSids = BridgeArguments.GetOptionalStringArray(values, "targetSids", RestorePlan.MaxTargets) ?? [];
                var conflictSid = BridgeArguments.GetOptionalString(values, "conflictSid");
                var legacy = BridgeArguments.GetOptionalBoolean(values, "legacy");
                var legacyAcknowledged = BridgeArguments.GetOptionalBoolean(values, "legacyAcknowledged");
                var partial = BridgeArguments.GetOptionalBoolean(values, "partial");
                var partialAcknowledged = BridgeArguments.GetOptionalBoolean(values, "partialAcknowledged");
                var encrypted = BridgeArguments.GetOptionalBoolean(values, "encrypted");
                var assumeEncrypted = BridgeArguments.GetOptionalBoolean(values, "assumeEncrypted");
                var sourceKeyPath = BridgeArguments.GetOptionalString(values, "sourceKeyPath");
                var sourceComputerName = BridgeArguments.GetOptionalString(values, "sourceComputerName");
                var sourceCreatedUtc = BridgeArguments.GetOptionalString(values, "sourceCreatedUtc");
                var usmtVersion = BridgeArguments.GetOptionalString(values, "usmtVersion");
                var purpose = BridgeArguments.GetOptionalString(values, "purpose");

                var mappings = ReadMappings(values);
                var policies = ReadConflictPolicies(values);
                var prior = ReadPriorBackup(values);

                var poste = RequirePoste(context);
                var profiles = IndexProfiles(poste, ct);
                var targets = BuildTargets(profiles, targetSids);
                ExecutionStoreInspection? inspection = null;
                if (context.Service<IFilproExecutionService>() is not null)
                    inspection = await ExecutionBridge.Require(context).InspectStoreAsync(bundle, ct).ConfigureAwait(false);
                if (inspection is not null && inspection.ConflictChoices is null)
                    throw new FilproOperationException("restore.catalog", "Les portées de réglages du magasin ne sont pas connues. Les choix de la cible ne prouvent pas le contenu de la sauvegarde.");
                IReadOnlyList<SoftwareGroup> choices = inspection?.ConflictChoices
                    ?? BuildConflictChoices(poste, profiles, targetSids, conflictSid, ct);

                var store = inspection?.Store ?? new RestoreStoreInfo(
                    bundle,
                    Ready: storeReady,
                    Legacy: legacy,
                    Partial: partial,
                    Simulated: poste.Demonstration,
                    Encrypted: encrypted,
                    Status: storeReady ? DeclaredReadyStatus : DeclaredNotReadyStatus,
                    ManifestUsers: manifestUsers,
                    SourceComputerName: sourceComputerName,
                    SourceCreatedUtc: sourceCreatedUtc,
                    UsmtVersion: usmtVersion);

                var request = new RestorePlanRequest(
                    Store: store,
                    Users: users,
                    Mappings: mappings,
                    Targets: targets,
                    // Aucun SID ni chemin source n'est emprunté à la cible : sans manifeste relu, le plan ne montre que les comptes.
                    SourceProfiles: inspection?.SourceProfiles,
                    ConflictChoices: choices,
                    ConflictPolicies: policies,
                    PriorBackup: prior,
                    // Identité relue dans le fournisseur, jamais fournie par la page. Une identité inconnue est refusée.
                    CurrentAccountSid: poste.ReadCurrentAccountSid(ct),
                    SourceKeyPath: sourceKeyPath,
                    LegacyAcknowledged: legacyAcknowledged,
                    PartialAcknowledged: partialAcknowledged,
                    AssumeEncrypted: assumeEncrypted,
                    Demonstration: poste.Demonstration,
                    Purpose: purpose);

                var planId = Guid.NewGuid().ToString("N");
                var createdUtc = context.Clock.GetUtcNow();
                var plan = RestorePlan.Validate(request, planId, createdUtc) with
                {
                    SourceManifestSha256 = inspection?.ManifestSha256,
                    SourceStoreId = inspection?.Id,
                    SourcePayloadSha256 = inspection?.SourcePayloadSha256,
                };
                ExecutionBridge.Plans(context).Add(plan.Id, plan);
                var declaration = new RestoreStoreDeclaration(
                    plan.Bundle,
                    store.Status,
                    store.Ready,
                    DeclaredByOperator: inspection is null,
                    plan.Legacy,
                    store.Partial,
                    plan.Encrypted,
                    store.ManifestUsers?.Count ?? 0,
                    store.UsmtVersion,
                    store.SourceComputerName,
                    store.SourceCreatedUtc);
                return new RestorePlanPreview(plan, declaration,
                    inspection is null ? ConflictNotice : "Portées issues du manifeste du magasin relu ; intégrité et profils seront recontrôlés avant l’écriture.",
                    inspection is null ? RestoreLimitations : inspection.Warnings);
            }, "Aperçu canonisé du plan de restauration (correspondances de comptes, sauvegarde préalable, limites de l’ancien magasin, conflits par portée, taille inconnue) : aucune préparation longue, aucune exécution.", ServedInSimulation: true),

            new OperationDefinition("sauvegarder.confirmRestorePlan", OperationClass.Write, async (args, _, ct) =>
            {
                // Accord explicite sur les trois protections avant préparation. Le moteur relit le plan du serveur,
                // valide le magasin et crée la sauvegarde préalable avant le moindre LoadState.
                var values = BridgeArguments.ReadObject(args, ["planId", "acknowledgeCapacity", "acknowledgePriorBackup", "acknowledgeIdentityReview"]);
                var planId = BridgeArguments.GetString(values, "planId").Trim();
                if (!RestorePlanIdPattern.IsMatch(planId))
                {
                    throw new FilproOperationException(RestorePlan.PlanCode, "Identifiant de plan invalide : recalculer le plan de restauration avant de le confirmer.");
                }
                if (!BridgeArguments.GetBoolean(values, "acknowledgeCapacity"))
                {
                    throw new FilproOperationException(RestorePlan.PlanCode, "Accusé de capacité obligatoire : le besoin d’espace du plan de restauration reste inconnu tant que le moteur ne l’a pas mesuré.");
                }
                if (!BridgeArguments.GetBoolean(values, "acknowledgePriorBackup"))
                {
                    throw new FilproOperationException(RestorePlan.PriorCode, "La sauvegarde préalable des profils de destination doit être confirmée par l’opérateur : le plan ne la déclare jamais vérifiée.");
                }
                if (!BridgeArguments.GetBoolean(values, "acknowledgeIdentityReview"))
                {
                    throw new FilproOperationException(RestorePlan.TargetCode, "La revue des correspondances de comptes (source → cible) doit être confirmée avant toute restauration.");
                }
                var service = ExecutionBridge.Require(context, demonstration: false);
                using var turn = NativeConfirmation.Begin(context);
                using var admission = context.Operations.EnterNativeStart();
                var plan = ExecutionBridge.Plans(context).Take<RestorePlanReview>(planId);
                ExecutionBridge.RequirePlanTimeLeft(turn, plan.CreatedUtc);
                return await turn.ConfirmThenAsync(() => ConfirmationSheets.Restore(plan),
                    () => service.StartRestoreAsync(plan, ExecutionBridge.ConfirmedOptions(), ct), ct).ConfigureAwait(false);
            }, "Confirmation du plan serveur et de ses protections avant préparation et restauration natives.", ServedInSimulation: false),
        ];
    }

    /// <summary>
    /// Correspondances source → cible telles que choisies par l'opérateur. Seuls les comptes sont acceptés (jamais un SID) :
    /// la cible reste <c>null</c> quand le compte cible est le compte source, et c'est la fonction pure qui refuse doublon,
    /// fusion et compte non sélectionné.
    /// </summary>
    private static List<RestoreAccountMapping>? ReadMappings(IReadOnlyDictionary<string, JsonElement> values)
    {
        var rows = BridgeArguments.GetObjectArray(values, "mappings", RestorePlan.MaxMappings, ["source", "target"]);
        if (rows.Count == 0)
        {
            return null;
        }
        var result = new List<RestoreAccountMapping>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(new RestoreAccountMapping(
                BridgeArguments.GetString(row, "source"),
                BridgeArguments.GetOptionalString(row, "target")));
        }
        return result;
    }

    /// <summary>
    /// Décisions de conflit par portée soumises par la page (une par groupe affiché). Le mode est borné aux trois valeurs
    /// du contrat ; la validation des chevauchements de portées appartient à <see cref="RestoreConflictPolicies"/>, appelée
    /// par la fonction pure — aucune seconde implémentation dans le pont.
    /// </summary>
    private static List<RestorePolicy>? ReadConflictPolicies(IReadOnlyDictionary<string, JsonElement> values)
    {
        var rows = BridgeArguments.GetObjectArray(values, "conflictPolicies", MaxConflictPolicies, ["id", "mode"]);
        if (rows.Count == 0)
        {
            return null;
        }
        var result = new List<RestorePolicy>(rows.Count);
        foreach (var row in rows)
        {
            var id = BridgeArguments.GetString(row, "id");
            var mode = BridgeArguments.GetString(row, "mode").Trim();
            if (!PolicyModes.TryGetValue(mode, out var policyMode))
            {
                throw BridgeArguments.Invalid("le mode de politique de conflit est inconnu.");
            }
            result.Add(new RestorePolicy(id, policyMode));
        }
        return result;
    }

    /// <summary>
    /// Sauvegarde préalable déclarée par l'opérateur. <see cref="PriorBackupRequirement.Verified"/> reste <c>false</c> :
    /// aucune saisie de la page ne peut la poser, seul le moteur contrôle. Une absence totale de saisie laisse le champ à
    /// <c>null</c>, et la fonction pure refuse alors de restaurer sur des profils existants.
    /// </summary>
    private static PriorBackupRequirement? ReadPriorBackup(IReadOnlyDictionary<string, JsonElement> values)
    {
        var root = BridgeArguments.GetOptionalString(values, "priorRoot");
        var keyPath = BridgeArguments.GetOptionalString(values, "priorKeyPath");
        var statement = BridgeArguments.GetOptionalString(values, "priorStatement");
        if (string.IsNullOrWhiteSpace(root) && string.IsNullOrWhiteSpace(keyPath) && string.IsNullOrWhiteSpace(statement))
        {
            return null;
        }
        var identified = BridgeArguments.GetOptionalBoolean(values, "priorIdentified");
        return new PriorBackupRequirement(
            identified ? PriorBackupState.Identifiee : PriorBackupState.Declaree,
            root,
            keyPath,
            Verified: false,
            statement ?? string.Empty);
    }

    /// <summary>
    /// Reconstruit les profils cibles à partir de l'inventaire relu chez le fournisseur poste (le JavaScript ne fournit que
    /// les SID) : compte, chemin et chargement tri-état viennent du fournisseur, jamais de la saisie. Un SID inconnu est
    /// refusé (restore.target) ; un chemin manquant est laissé au plan, qui le refuse. Aucune écriture, aucune mesure.
    /// </summary>
    private static List<RestoreTargetProfile> BuildTargets(IReadOnlyDictionary<string, ProfileCandidate> profiles, IReadOnlyList<string> sids)
    {
        if (sids.Count == 0)
        {
            return [];
        }
        var result = new List<RestoreTargetProfile>(sids.Count);
        foreach (var rawSid in sids)
        {
            var sid = rawSid.Trim();
            if (!profiles.TryGetValue(sid, out var candidate))
            {
                throw new FilproOperationException(RestorePlan.TargetCode, "Profil cible inconnu : actualiser la liste des profils avant de préparer la restauration.");
            }
            result.Add(new RestoreTargetProfile(
                candidate.Sid,
                candidate.IdentityKind,
                candidate.Account,
                candidate.LocalPath ?? string.Empty,
                candidate.Loaded));
        }
        return result;
    }

    /// <summary>
    /// Portées de conflits examinées : les groupes fichiers+registre relus chez le fournisseur poste pour un seul profil
    /// cible (la préparation ne lit pas les groupes de tous les profils avant confirmation). Sans profil cible, aucune
    /// portée n'est affichée et aucune décision n'est demandée ; avec plusieurs cibles, la page doit nommer celle qui est
    /// examinée. Les décisions elles-mêmes sont validées par <see cref="RestoreConflictPolicies"/> dans la fonction pure.
    /// </summary>
    private static List<SoftwareGroup> BuildConflictChoices(
        IFilproPosteSource poste,
        IReadOnlyDictionary<string, ProfileCandidate> profiles,
        IReadOnlyList<string> targetSids,
        string? conflictSid,
        CancellationToken ct)
    {
        var sid = (conflictSid ?? string.Empty).Trim();
        if (sid.Length == 0)
        {
            if (targetSids.Count > 1)
            {
                throw BridgeArguments.Invalid("préciser le profil cible dont les réglages sont examinés : la préparation ne lit pas les groupes de tous les profils.");
            }
            if (targetSids.Count == 0)
            {
                return [];
            }
            sid = targetSids[0].Trim();
        }
        else if (!profiles.ContainsKey(sid))
        {
            throw new FilproOperationException(RestorePlan.TargetCode, "Profil cible inconnu pour l’examen des conflits : actualiser la liste des profils.");
        }
        if (!targetSids.Any(target => string.Equals(target.Trim(), sid, StringComparison.Ordinal)))
        {
            throw new FilproOperationException(RestorePlan.TargetCode,
                "Le profil examiné pour les conflits doit faire partie des destinations retenues.");
        }
        return [.. poste.ReadSoftware(sid, SoftwareQuery.Empty, ct).Groups];
    }

    /// <summary>
    /// Reconstruit la sélection multi-profils à partir du fournisseur poste (le JavaScript ne fournit que les SID retenus) :
    /// profils relus via <see cref="IFilproPosteSource.ReadProfiles"/>, éléments fichiers+registre déjà cochés relus via
    /// <see cref="IFilproPosteSource.ReadSoftware"/>. Un SID inconnu est refusé (backup.selection) ; le chemin de profil
    /// manquant est laissé au plan, qui le refuse. Aucune écriture, aucune mesure.
    /// </summary>
    private static List<BackupProfileSelection> BuildSelection(IFilproPosteSource poste, IReadOnlyList<string> sids, CancellationToken ct)
    {
        var bySid = IndexProfiles(poste, ct);
        var result = new List<BackupProfileSelection>(sids.Count);
        foreach (var rawSid in sids)
        {
            var sid = rawSid.Trim();
            if (!bySid.TryGetValue(sid, out var candidate))
            {
                throw new FilproOperationException(BackupPlan.SelectionCode, "Profil inconnu : actualiser la liste des profils avant de préparer le plan.");
            }

            var software = poste.ReadSoftware(sid, SoftwareQuery.Empty, ct);
            if (!software.Discovery.FilesComplete || !software.Discovery.RegistryComplete)
            {
                throw new FilproOperationException(FilproErrors.SourceUnavailable,
                    "Inventaire fichiers et registre incomplet pour un profil retenu. Le plan est refusé pour ne pas omettre des réglages ; les derniers choix restent conservés.");
            }
            var files = new List<SoftwareItem>();
            var registry = new List<SoftwareItem>();
            foreach (var group in software.Groups)
            {
                foreach (var item in group.Files)
                {
                    if (item.Selected && item.Scope is SoftwareScope.Local or SoftwareScope.Roaming)
                    {
                        files.Add(item);
                    }
                }
                foreach (var item in group.Registry)
                {
                    if (item.Selected && item.Scope == SoftwareScope.Hkcu)
                    {
                        registry.Add(item);
                    }
                }
            }

            result.Add(new BackupProfileSelection(
                candidate.Sid,
                candidate.IdentityKind,
                candidate.Account,
                candidate.LocalPath ?? string.Empty,
                candidate.Loaded,
                files,
                registry));
        }

        return result;
    }

    private static Dictionary<string, ProfileCandidate> IndexProfiles(IFilproPosteSource poste, CancellationToken ct)
    {
        var inventory = poste.ReadProfiles(ct);
        var bySid = new Dictionary<string, ProfileCandidate>(StringComparer.Ordinal);
        foreach (var candidate in inventory.Profiles)
        {
            bySid[candidate.Sid] = candidate;
        }
        return bySid;
    }

    private static IFilproPosteSource RequirePoste(EngineContext context)
    {
        var poste = context.Poste ?? throw new FilproOperationException(FilproErrors.SourceUnavailable, FilproErrors.SourceUnavailableMessage);
        if (poste.Demonstration != (context.Mode == AppMode.Simulation))
        {
            throw new FilproOperationException(FilproErrors.SourceUnavailable,
                "Fournisseur incompatible avec le mode de la session : aucune lecture ni écriture autorisée.");
        }
        return poste;
    }
}
