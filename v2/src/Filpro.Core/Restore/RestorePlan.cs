using System.Text;
using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>
/// Validation du plan de restauration : correspondances de comptes, portées et politiques de conflits, état de la
/// sauvegarde préalable et compatibilité des anciens magasins. Fidèle aux gardes de la V1 (Resolve-FilproAccountMappings,
/// Assert-FilproRestoreAccounts, New-FilproRestorePlan, Get-FilproUsmtStoreInfo, Get-FilproBackupEvidence). Une seule
/// fonction publique, <see cref="Validate"/> : pure, sans horloge, sans identifiant aléatoire, sans E/S, sans registre,
/// sans résolution de compte et sans moteur. Les politiques de conflits et la détection des chevauchements de portées
/// sont déléguées à <see cref="RestoreConflictPolicies"/> (réutilisation, pas de seconde implémentation).
/// </summary>
public static class RestorePlan
{
    /// <summary>Version du contrat du plan. Un plan d'une autre version est refusé à la relecture par le moteur.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Code des refus structurels (identifiant, dates, objet du plan).</summary>
    public const string PlanCode = "restore.plan";

    /// <summary>Code des refus de sélection des comptes source.</summary>
    public const string SelectionCode = "restore.selection";

    /// <summary>Code des refus de correspondance de comptes (doublon, fusion, compte non sélectionné).</summary>
    public const string MappingCode = "restore.mapping";

    /// <summary>Code des refus des profils cibles (SID, chargement, compte exécutant, hors correspondance).</summary>
    public const string TargetCode = "restore.target";

    /// <summary>Code des refus du magasin source (prêt, mélange simulation/réel, manifeste, partiel, ancien, clé).</summary>
    public const string StoreCode = "restore.store";

    /// <summary>Code des refus de la sauvegarde préalable (absence, emplacement, clé, fausse vérification).</summary>
    public const string PriorCode = "restore.prior";

    public const int MaxUsers = 200;
    public const int MaxTargets = 200;
    public const int MaxMappings = 200;
    public const int MaxAccountLength = 512;
    public const int MaxPurposeLength = 200;
    public const int MaxStatementLength = 400;

    /// <summary>Objet du plan lorsqu'il n'est pas précisé par l'opérateur.</summary>
    public const string DefaultPurpose = "Restauration des comptes sélectionnés sur ce poste";

    private const string EntraPrefix = "S-1-12-1-";

    private static readonly Regex PlanIdPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex ExplicitAccountPattern = new(@"^[^\\*?""\x00-\x1f/:]+\\[^\\*?""\x00-\x1f/:]+$", RegexOptions.CultureInvariant);
    private static readonly Regex TargetSidPattern = new(@"^S-1-5-21-\d+-\d+-\d+-\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex RootedPattern = new(@"^(?:[A-Za-z]:\\|\\\\[^\\/:*?""<>|\x00-\x1f]+\\[^\\/:*?""<>|\x00-\x1f]+(?:\\|$))", RegexOptions.CultureInvariant);
    private static readonly Regex InvalidPathChars = new(@"[\x00-\x1f:*?""<>|]", RegexOptions.CultureInvariant);
    private static readonly Regex RelativeEscape = new(@"(^|\\)\.\.?($|\\)", RegexOptions.CultureInvariant);
    private static readonly Regex ControlChars = new(@"[\x00-\x1f]", RegexOptions.CultureInvariant);

    private const string PriorIdentifiedStatement =
        "Sauvegarde préalable déclarée identifiée. Ce plan ne prouve ni la lecture du magasin, ni le contrôle de ses " +
        "règles, ni l'intégrité actuelle de son contenu.";

    private const string PriorDeclaredStatement =
        "Sauvegarde externe déclarée par l'opérateur, non contrôlée par FILPRO.";

    private const string PlanNotice =
        "Aperçu construit depuis les identités et le magasin relus : aucune écriture dans les profils, aucun LoadState exécuté. La mesure " +
        "de l'espace, le contrôle des règles et de la clé, la sauvegarde préalable effective et la restauration appartiennent " +
        "au moteur. Un arrêt demandé n'est pas un arrêt achevé.";

    /// <summary>
    /// Valide les correspondances de comptes, les portées et politiques de conflits (via
    /// <see cref="RestoreConflictPolicies"/>), l'état de la sauvegarde préalable et la compatibilité de l'ancien magasin,
    /// puis canonise le plan — ou refuse sans rien produire. <paramref name="planId"/> est un identifiant de 32 chiffres
    /// hexadimaux et <paramref name="createdUtc"/> l'horodatage fourni par l'appelant : la fonction ne lit aucune horloge,
    /// n'invente aucun identifiant et ne résout aucun compte. La fraîcheur du plan n'est pas jugée ici ; elle sera
    /// contrôlée à la confirmation par le moteur.
    /// </summary>
    public static RestorePlanReview Validate(RestorePlanRequest request, string planId, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(planId) || !PlanIdPattern.IsMatch(planId))
        {
            throw Invalid(PlanCode, "Identifiant de plan invalide.");
        }
        if (createdUtc == default)
        {
            throw Invalid(PlanCode, "Date de création du plan invalide.");
        }

        var warnings = new List<string>();
        var store = ValidateStore(request);
        var users = ValidateUsers(request);
        ValidateStoreAccounts(request, store, users, warnings);
        var mappings = ValidateMappings(request, users);
        var targets = ValidateTargets(request, mappings, warnings);

        var targetSidByAccount = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            targetSidByAccount[target.Account] = target.Sid;
        }
        mappings = [.. mappings.Select(m => targetSidByAccount.TryGetValue(m.Target, out var sid) ? m with { TargetSid = sid } : m)];

        var prior = ValidatePriorBackup(request, store, targets, warnings);
        var sourceKey = ValidateSourceKey(request, store, warnings);
        var (policies, summary, summaryLines) = ResolveConflicts(request);
        var pairs = BuildIdentityPairs(store, request.SourceProfiles, mappings, targets);
        var purpose = ValidatePurpose(request.Purpose);

        if (request.Demonstration)
        {
            warnings.Add("Plan établi sur le monde fictif de démonstration : aucune donnée réelle, aucune restauration réelle à partir de ce plan.");
        }

        return new RestorePlanReview(
            CurrentSchemaVersion,
            ProductInfo.Version,
            planId,
            createdUtc.ToUniversalTime(),
            request.Demonstration,
            purpose,
            store.Bundle,
            store.Legacy,
            store.Encrypted || request.AssumeEncrypted,
            sourceKey,
            store.SourceComputerName,
            store.SourceCreatedUtc,
            users,
            mappings,
            pairs,
            targets,
            prior,
            policies,
            summary,
            HasUnknownSpace: true,
            RequiresManualSpaceAcknowledgement: true,
            Warnings: warnings,
            IdentityReview: BuildIdentityReview(store, pairs),
            Notice: PlanNotice,
            ConflictSummaryLines: summaryLines,
            SourcePartial: store.Partial);
    }

    private static RestoreStoreInfo ValidateStore(RestorePlanRequest request)
    {
        var store = request.Store ?? throw Invalid(StoreCode, "Aucun magasin source indiqué : choisir une sauvegarde à restaurer.");
        var bundle = RequireRooted(store.Bundle, StoreCode, "Chemin du magasin source absolu requis : choisir une sauvegarde à restaurer.");
        if (!store.Ready)
        {
            throw Invalid(StoreCode, "Source absente, incomplète ou non interprétable.");
        }
        if (store.Simulated != request.Demonstration)
        {
            throw Invalid(StoreCode, "Mélange simulation et données réelles interdit.");
        }
        return store with { Bundle = bundle };
    }

    private static List<string> ValidateUsers(RestorePlanRequest request)
    {
        var input = request.Users;
        if (input is null or { Count: 0 })
        {
            throw Invalid(SelectionCode, "Sélectionner au moins un compte source à restaurer.");
        }
        if (input.Count > MaxUsers)
        {
            throw Invalid(SelectionCode, $"Trop de comptes dans un seul plan ({input.Count}). Restaurer par lots plus petits.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(input.Count);
        foreach (var user in input)
        {
            var account = RequireExplicitAccount(user, SelectionCode, "compte source");
            if (IsEntra(account))
            {
                throw Invalid(SelectionCode, "Identité Entra non prise en charge par cette restauration : " + account + ".");
            }
            if (!seen.Add(account))
            {
                throw Invalid(SelectionCode, "Compte source en double : " + account);
            }
            result.Add(account);
        }
        return [.. result.OrderBy(a => a, StringComparer.OrdinalIgnoreCase)];
    }

    private static void ValidateStoreAccounts(RestorePlanRequest request, RestoreStoreInfo store, IReadOnlyList<string> users, List<string> warnings)
    {
        if (store.Legacy)
        {
            if (!request.LegacyAcknowledged)
            {
                throw Invalid(StoreCode, "Ancien magasin : cocher « J'ai examiné les limites du magasin ancien et ses règles » dans « Clé de déchiffrement et magasins anciens » avant de restaurer.");
            }
            foreach (var source in request.SourceProfiles ?? [])
            {
                if (source is null)
                {
                    throw Invalid(StoreCode, "Profil source invalide dans un ancien magasin.");
                }
                if (!string.IsNullOrWhiteSpace(source.Sid) || !string.IsNullOrWhiteSpace(source.ProfilePath))
                {
                    throw Invalid(StoreCode, "Un ancien magasin ne fournit pas de SID ni de chemin source : ne jamais emprunter ceux de la cible.");
                }
            }
            warnings.Add("Ancien magasin sans manifeste FILPRO : comptes source non garantis et règles non vérifiables ; la restauration reste à valider en session cible.");
            return;
        }

        var manifest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in store.ManifestUsers ?? [])
        {
            manifest.Add(account);
        }
        foreach (var user in users)
        {
            if (!manifest.Contains(user))
            {
                throw Invalid(StoreCode, "Compte source absent du manifeste : " + user + ".");
            }
        }
        if (store.Partial)
        {
            if (!request.PartialAcknowledged)
            {
                throw Invalid(StoreCode, "Le magasin contient des omissions. Examiner ses journaux puis reconnaître explicitement cet état partiel.");
            }
            warnings.Add("Magasin partiel reconnu par l'opérateur : les omissions restent à examiner dans les journaux du magasin.");
        }
        if (string.IsNullOrWhiteSpace(store.UsmtVersion))
        {
            warnings.Add("Version USMT de création non enregistrée dans le manifeste : aucune équivalence supposée, relire le plan et tester la restauration.");
        }
    }

    private static List<RestoreMappingResolution> ValidateMappings(RestorePlanRequest request, IReadOnlyList<string> users)
    {
        var input = request.Mappings ?? [];
        if (input.Count > MaxMappings)
        {
            throw Invalid(MappingCode, "Trop de correspondances dans un seul plan.");
        }

        var selected = new HashSet<string>(users, StringComparer.OrdinalIgnoreCase);
        var rowsBySource = new Dictionary<string, List<RestoreAccountMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in input)
        {
            if (row is null)
            {
                throw Invalid(MappingCode, "Correspondance invalide : chaque correspondance doit indiquer un compte source.");
            }
            var source = RequireExplicitAccount(row.Source, MappingCode, "compte source d'une correspondance");
            if (!selected.Contains(source))
            {
                throw Invalid(MappingCode, "Correspondance portant sur un compte non sélectionné : " + source + ".");
            }
            if (!rowsBySource.TryGetValue(source, out var rows))
            {
                rows = [];
                rowsBySource[source] = rows;
            }
            rows.Add(row with { Source = source });
        }

        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RestoreMappingResolution>(users.Count);
        foreach (var user in users)
        {
            var rows = rowsBySource.TryGetValue(user, out var found) ? found : null;
            if (rows is { Count: > 1 })
            {
                throw Invalid(MappingCode, "Correspondance de compte en double : " + user + ".");
            }
            var target = rows is { Count: 1 } ? RequireExplicitAccount(rows[0].Target, MappingCode, "compte cible") : user;
            if (IsEntra(target))
            {
                throw Invalid(MappingCode, "Identité Entra non prise en charge comme cible : " + target + ".");
            }
            if (!destinations.Add(target))
            {
                throw Invalid(MappingCode, "Fusion de plusieurs comptes vers une destination interdite. Créer des opérations distinctes : " + target + ".");
            }
            result.Add(new RestoreMappingResolution(user, target, null));
        }
        return [.. result.OrderBy(m => m.Source, StringComparer.OrdinalIgnoreCase)];
    }

    private static List<RestoreTargetProfile> ValidateTargets(RestorePlanRequest request, IReadOnlyList<RestoreMappingResolution> mappings, List<string> warnings)
    {
        var input = request.Targets ?? [];
        if (input.Count > MaxTargets)
        {
            throw Invalid(TargetCode, "Trop de profils cibles dans un seul plan.");
        }

        var mappingTargets = new HashSet<string>(mappings.Select(m => m.Target), StringComparer.OrdinalIgnoreCase);
        var sids = new HashSet<string>(StringComparer.Ordinal);
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executing = (request.CurrentAccountSid ?? string.Empty).Trim();
        var result = new List<RestoreTargetProfile>(input.Count);
        foreach (var target in input)
        {
            if (target is null)
            {
                throw Invalid(TargetCode, "Profil cible invalide : recalculer les cibles avant la restauration.");
            }
            var sid = (target.Sid ?? string.Empty).Trim();
            if (!TargetSidPattern.IsMatch(sid))
            {
                throw Invalid(TargetCode, "Identité de destination non prise en charge pour cette restauration : " + sid + ".");
            }
            if (target.IdentityKind == ProfileIdentityKind.Entra || sid.StartsWith(EntraPrefix, StringComparison.Ordinal))
            {
                throw Invalid(TargetCode, "Profil cible Entra non pris en charge par USMT.");
            }
            if (executing.Length > 0 && string.Equals(sid, executing, StringComparison.Ordinal))
            {
                throw Invalid(TargetCode, "Le compte qui exécute FILPRO ne peut pas être une destination.");
            }
            if (!sids.Add(sid))
            {
                throw Invalid(TargetCode, "Deux profils cibles partagent le même SID.");
            }
            var account = RequireExplicitAccount(target.Account, TargetCode, "compte cible");
            if (!accounts.Add(account))
            {
                throw Invalid(TargetCode, "Compte cible en double : " + account);
            }
            if (!mappingTargets.Contains(account))
            {
                throw Invalid(TargetCode, "Profil cible hors correspondance : " + account + ".");
            }
            var path = RequireRooted(target.ProfilePath, TargetCode, "Chemin du profil cible absolu requis : recalculer les cibles.");
            if (target.Loaded == true)
            {
                throw Invalid(TargetCode, "Profil cible encore chargé : fermer la session du compte " + account + " avant la restauration.");
            }
            if (target.Loaded is null)
            {
                if (!request.Demonstration)
                {
                    throw Invalid(TargetCode, "Chargement du profil cible inconnu : vérification préalable impossible pour " + account + ".");
                }
                warnings.Add("Chargement du profil cible " + account + " non établi en démonstration : le moteur devra le vérifier avant LoadState.");
            }
            result.Add(target with { Sid = sid, Account = account, ProfilePath = path });
        }
        return [.. result.OrderBy(t => t.Account, StringComparer.OrdinalIgnoreCase)];
    }

    private static PriorBackupRequirement ValidatePriorBackup(RestorePlanRequest request, RestoreStoreInfo store, IReadOnlyList<RestoreTargetProfile> targets, List<string> warnings)
    {
        var declared = request.PriorBackup;

        if (targets.Count == 0)
        {
            if (declared is { Verified: true })
            {
                throw Invalid(PriorCode, "Le plan ne peut pas déclarer la sauvegarde préalable vérifiée : le moteur seul la contrôle.");
            }
            return new PriorBackupRequirement(
                PriorBackupState.NonApplicable,
                string.IsNullOrWhiteSpace(declared?.Root) ? null : RequireRooted(declared!.Root, PriorCode, "Dossier de sauvegarde préalable absolu requis."),
                string.IsNullOrWhiteSpace(declared?.KeyPath) ? null : RequireRooted(declared!.KeyPath, PriorCode, "Clé de sauvegarde préalable absolue requise."),
                false,
                "Aucun profil de destination existant à sauvegarder préalablement.");
        }

        if (declared is null || declared.State is PriorBackupState.Absente or PriorBackupState.NonApplicable)
        {
            throw Invalid(PriorCode, "Dossier de sauvegarde préalable existant requis avant de restaurer sur un profil déjà présent.");
        }
        if (declared.Verified)
        {
            throw Invalid(PriorCode, "Le plan ne peut pas déclarer la sauvegarde préalable vérifiée : le moteur seul la contrôle.");
        }

        var root = RequireRooted(declared.Root, PriorCode, "Dossier de sauvegarde préalable existant requis.");
        if (IsWithin(root, store.Bundle, allowRoot: true))
        {
            throw Invalid(PriorCode, "La sauvegarde préalable doit être hors du magasin à restaurer.");
        }
        foreach (var target in targets)
        {
            if (IsWithin(root, target.ProfilePath, allowRoot: true))
            {
                throw Invalid(PriorCode, "La sauvegarde préalable ne peut pas être dans un profil de destination.");
            }
        }

        string? key = null;
        if (!string.IsNullOrWhiteSpace(declared.KeyPath))
        {
            key = RequireRooted(declared.KeyPath, PriorCode, "Clé de sauvegarde préalable absolue requise.");
        }
        else if (!request.Demonstration)
        {
            throw Invalid(PriorCode, "Une clé indépendante est requise pour chiffrer la sauvegarde préalable.");
        }
        if (key is not null)
        {
            foreach (var target in targets)
            {
                if (IsWithin(key, target.ProfilePath, allowRoot: true))
                {
                    throw Invalid(PriorCode, "La clé de sécurité ne doit pas être dans un profil sauvegardé.");
                }
            }
        }

        var statement = (declared.Statement ?? string.Empty).Trim();
        if (statement.Length > MaxStatementLength || ControlChars.IsMatch(statement))
        {
            throw Invalid(PriorCode, "Mention de la sauvegarde préalable invalide.");
        }
        if (statement.Length == 0)
        {
            statement = declared.State == PriorBackupState.Identifiee ? PriorIdentifiedStatement : PriorDeclaredStatement;
        }
        if (declared.State == PriorBackupState.Declaree)
        {
            warnings.Add("Sauvegarde préalable déclarée par l'opérateur, non contrôlée par FILPRO : le moteur devra la vérifier avant LoadState.");
        }
        else
        {
            warnings.Add("Sauvegarde préalable déclarée identifiée : le moteur devra relire le magasin et vérifier ses règles et son contenu avant LoadState.");
        }
        return declared with { Root = root, KeyPath = key, Verified = false, Statement = statement };
    }

    private static string? ValidateSourceKey(RestorePlanRequest request, RestoreStoreInfo store, List<string> warnings)
    {
        var encrypted = store.Encrypted || request.AssumeEncrypted;
        if (!encrypted)
        {
            if (!string.IsNullOrWhiteSpace(request.SourceKeyPath))
            {
                throw Invalid(StoreCode, "Chemin de clé indiqué sans magasin chiffré.");
            }
            return null;
        }
        var key = RequireRooted(request.SourceKeyPath, StoreCode, "Fichier clé de la sauvegarde requis pour ce magasin chiffré : chemin de clé invalide.");
        warnings.Add("Existence, contenu et correspondance de la clé avec le magasin restent à contrôler par le moteur ; la clé n'est jamais affichée ni journalisée.");
        return key;
    }

    private static (IReadOnlyList<RestorePolicy> Policies, string Summary, IReadOnlyList<string> SummaryLines) ResolveConflicts(RestorePlanRequest request)
    {
        var choices = request.ConflictChoices ?? [];
        var policies = RestoreConflictPolicies.Resolve(choices, request.ConflictPolicies);
        // Le texte de la page et les lignes de la confirmation Windows viennent du même résumé.
        var summary = RestoreConflictPolicies.Summarize(choices, policies);
        return (policies, summary, RestoreConflictPolicies.SummaryLines(choices, policies));
    }

    private static List<RestoreIdentityPair> BuildIdentityPairs(
        RestoreStoreInfo store,
        IReadOnlyList<RestoreSourceProfile>? sourceProfiles,
        IReadOnlyList<RestoreMappingResolution> mappings,
        IReadOnlyList<RestoreTargetProfile> targets)
    {
        var sourceByAccount = new Dictionary<string, RestoreSourceProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceProfiles ?? [])
        {
            if (source is null)
            {
                continue;
            }
            var account = (source.Account ?? string.Empty).Trim();
            if (account.Length > 0)
            {
                sourceByAccount[account] = source;
            }
        }
        var targetSidByAccount = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            targetSidByAccount[target.Account] = target.Sid;
        }

        var result = new List<RestoreIdentityPair>(mappings.Count);
        foreach (var mapping in mappings)
        {
            var source = sourceByAccount.TryGetValue(mapping.Source, out var s) ? s : null;
            var sourceSid = store.Legacy ? null : source?.Sid;
            var sourcePath = store.Legacy ? null : source?.ProfilePath;
            var targetSid = targetSidByAccount.TryGetValue(mapping.Target, out var sid) ? sid : null;
            result.Add(new RestoreIdentityPair(mapping.Source, sourceSid, sourcePath, mapping.Target, targetSid));
        }
        return [.. result.OrderBy(p => p.SourceAccount, StringComparer.OrdinalIgnoreCase)];
    }

    private static string BuildIdentityReview(RestoreStoreInfo store, IReadOnlyList<RestoreIdentityPair> pairs)
    {
        if (pairs.Count == 0)
        {
            return "Aucun compte sélectionné pour la restauration.";
        }
        var header = "Correspondances de comptes pour la restauration"
            + (string.IsNullOrWhiteSpace(store.SourceComputerName) ? string.Empty : " (poste source : " + store.SourceComputerName + ")")
            + " :";
        var lines = new List<string> { header };
        foreach (var pair in pairs)
        {
            var source = pair.SourceSid is null ? pair.SourceAccount : pair.SourceAccount + " [" + pair.SourceSid + "]";
            var target = pair.TargetSid is null ? pair.TargetAccount + " (nouveau profil)" : pair.TargetAccount + " [" + pair.TargetSid + "]";
            lines.Add("  " + source + " → " + target);
        }
        lines.Add("Validation utilisateur restante : l'activation effective des réglages (registre, services, sessions) doit être contrôlée dans la session cible après restauration.");
        return string.Join("\r\n", lines);
    }

    private static string ValidatePurpose(string? purpose)
    {
        var value = (purpose ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return DefaultPurpose;
        }
        if (value.Length > MaxPurposeLength || ControlChars.IsMatch(value))
        {
            throw Invalid(PlanCode, "Objet du plan invalide.");
        }
        return value;
    }

    private static bool IsEntra(string account) =>
        account.StartsWith(EntraPrefix, StringComparison.Ordinal)
        || account.StartsWith("AzureAD\\", StringComparison.OrdinalIgnoreCase);

    private static string RequireExplicitAccount(string? value, string code, string who)
    {
        var account = (value ?? string.Empty).Trim();
        if (account.Length == 0 || account.Length > MaxAccountLength || account.StartsWith("S-1-", StringComparison.Ordinal)
            || !ExplicitAccountPattern.IsMatch(account))
        {
            throw Invalid(code, "Compte non explicite ou non résolu (" + who + ") : "
                + (string.IsNullOrWhiteSpace(value) ? "(compte absent)" : value)
                + ". Choisir explicitement un compte existant.");
        }
        return account;
    }

    private static string RequireRooted(string? value, string code, string message)
    {
        var raw = (value ?? string.Empty).Trim().Replace('/', '\\');
        if (raw.Length == 0 || !RootedPattern.IsMatch(raw))
        {
            throw Invalid(code, message);
        }
        var tail = raw.Substring(2);
        if (InvalidPathChars.IsMatch(tail) || RelativeEscape.IsMatch(tail))
        {
            throw Invalid(code, message);
        }
        return Normalize(raw);
    }

    private static string Normalize(string path)
    {
        var value = path.Replace('/', '\\');
        var unc = value.StartsWith(@"\\", StringComparison.Ordinal);
        var builder = new StringBuilder(value.Length + 2);
        if (unc)
        {
            builder.Append(@"\\");
        }
        foreach (var c in value.AsSpan(unc ? 2 : 0))
        {
            if (c == '\\' && builder.Length > 0 && builder[^1] == '\\')
            {
                continue;
            }
            builder.Append(c);
        }
        return builder.ToString().TrimEnd('\\');
    }

    /// <summary>Le <paramref name="candidate"/> est-il dans le <paramref name="parent"/> (comparaison de chemins Windows, sans E/S).</summary>
    private static bool IsWithin(string candidate, string parent, bool allowRoot)
    {
        if (candidate.Length == 0 || parent.Length == 0)
        {
            return false;
        }
        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
        {
            return allowRoot;
        }
        return candidate.Length > parent.Length
            && candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            && candidate[parent.Length] == '\\';
    }

    private static FilproOperationException Invalid(string code, string message) => new(code, message);
}
