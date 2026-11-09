using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>
/// Création et validation du plan de sauvegarde multi-profils, fidèles aux gardes de la V1 (Get-FilproBackupAccounts,
/// Assert-FilproExplicitAccounts, Get-FilproBackupPreflight, Get-FilproCryptoArguments). Une seule fonction publique,
/// <see cref="Create"/> : pure, sans horloge, sans identifiant aléatoire, sans E/S, sans registre, sans volume et sans
/// moteur. Elle refuse avant toute préparation la sélection vide, le compte non explicite, le profil Entra, le profil
/// chargé sans VSS, la destination relative ou placée dans un profil source et la clé de chiffrement mal placée. Le
/// besoin d'espace reste explicitement inconnu (seul le moteur le mesure) et les exclusions sont annoncées.
/// </summary>
public static class BackupPlan
{
    /// <summary>Version du contrat du plan. Un plan d'une autre version est refusé à la relecture par le moteur.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Code des refus structurels (identifiant, dates, exclusions, options incompatibles).</summary>
    public const string PlanCode = "backup.plan";
    public const string ProfileScopeCode = "backup.profileScope";
    public const string ProfileScopeNotice = "FILPRO sauvegarde uniquement les profils. MigApp peut inclure des réglages machine ; sélectionnez les fichiers AppData et les clés HKCU des applications.";

    /// <summary>Code des refus de sélection (profils, comptes, SID, éléments de réglages).</summary>
    public const string SelectionCode = "backup.selection";

    /// <summary>Code des refus d'emplacement (destination et fichier clé).</summary>
    public const string DestinationCode = "backup.destination";

    public const int MaxProfiles = 200;
    public const int MaxItemsPerProfile = 500;
    public const int MaxExclusions = 200;
    public const int MaxAccountLength = 512;
    public const int MaxPurposeLength = 200;
    public const int MaxReasonLength = 400;

    /// <summary>Objet du plan lorsqu'il n'est pas précisé par l'opérateur.</summary>
    public const string DefaultPurpose = "Sauvegarde des profils sélectionnés";

    private const string EntraPrefix = "S-1-12-1-";

    private static readonly Regex PlanIdPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex SidPattern = new(@"^S-1-\d+(?:-\d+)+$", RegexOptions.CultureInvariant);
    private static readonly Regex ExplicitAccountPattern = new(@"^[^\\*?""\x00-\x1f/:]+\\[^\\*?""\x00-\x1f/:]+$", RegexOptions.CultureInvariant);
    private static readonly Regex RootedPattern = new(@"^(?:[A-Za-z]:\\|\\\\[^\\/:*?""<>|\x00-\x1f]+\\[^\\/:*?""<>|\x00-\x1f]+(?:\\|$))", RegexOptions.CultureInvariant);
    private static readonly Regex InvalidPathChars = new(@"[\x00-\x1f:*?""<>|]", RegexOptions.CultureInvariant);
    private static readonly Regex RelativeEscape = new(@"(^|\\)\.\.?($|\\)", RegexOptions.CultureInvariant);
    private static readonly Regex ControlChars = new(@"[\x00-\x1f]", RegexOptions.CultureInvariant);

    /// <summary>Limites annoncées par tout plan, quelle que soit la demande (aucune de ces exclusions n'est tue).</summary>
    private static readonly BackupExclusion[] RuleExclusions =
    [
        new(BackupExclusionKind.FluxNomme, null, "Flux de données nommés non copiés par ce plan ; le moteur les annoncera s'il en rencontre.", BackupExclusionOrigin.Regle),
        new(BackupExclusionKind.Application, null, "Applications non migrées : elles devront être installées sur le poste de destination.", BackupExclusionOrigin.Regle),
        new(BackupExclusionKind.Ruche, null, "Ruches du registre non montées par ce plan : l'examen du registre appartient au moteur, qui les décharge dans un finally.", BackupExclusionOrigin.Regle),
        new(BackupExclusionKind.DossierVide, null, "Dossiers vides non retenus : un dossier vide n'est pas un contenu à sauvegarder.", BackupExclusionOrigin.Regle),
        new(BackupExclusionKind.Magasin, null, "Magasins de sauvegarde FILPRO voisins écartés du contenu copié, pour éviter de sauvegarder une sauvegarde.", BackupExclusionOrigin.Regle),
    ];

    private const string SpaceNotice =
        "Aucun octet n'a été mesuré par ce plan. Le moteur doit calculer le magasin, le temporaire et les marges avec les " +
        "règles retenues ; la copie reste bloquée tant que cette estimation manque ou dépasse l'espace disponible.";

    private const string PlanNotice =
        "Aperçu construit depuis les inventaires relus : aucun magasin créé et aucune écriture dans les profils. " +
        "La mesure du besoin d'espace et l'exécution commencent seulement après confirmation. Un arrêt demandé " +
        "n'est pas un arrêt achevé.";

    /// <summary>
    /// Fabrique le plan multi-profils à partir d'une demande, ou refuse sans rien produire. <paramref name="planId"/> est
    /// un identifiant de 32 chiffres hexadimaux et <paramref name="createdUtc"/> l'horodatage fourni par l'appelant : la
    /// fonction ne lit aucune horloge et n'invente aucun identifiant. La fraîcheur du plan n'est pas jugée ici (aucune
    /// horloge de référence) : elle sera contrôlée à la confirmation par le moteur.
    /// </summary>
    public static BackupPlanReview Create(BackupPlanRequest request, string planId, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MigApp) throw Invalid(ProfileScopeCode, ProfileScopeNotice);
        if (string.IsNullOrWhiteSpace(planId) || !PlanIdPattern.IsMatch(planId))
        {
            throw Invalid(PlanCode, "Identifiant de plan invalide.");
        }
        if (createdUtc == default)
        {
            throw Invalid(PlanCode, "Date de création du plan invalide.");
        }

        var warnings = new List<string>();
        var profiles = ValidateProfiles(request, warnings);
        var destination = ValidateDestination(request, profiles);
        var keyPath = ValidateKey(request, profiles, destination, warnings);
        var exclusions = ValidateExclusions(request, profiles);
        var purpose = ValidatePurpose(request.Purpose);

        if (request.Demonstration)
        {
            warnings.Add("Plan établi sur le monde fictif de démonstration : aucune donnée réelle, aucune sauvegarde réelle possible à partir de ce plan.");
        }
        if (IsVolumeRoot(destination))
        {
            warnings.Add("Destination à la racine d'un volume ou d'un partage : vérifier le contenu déjà présent et les droits d'écriture.");
        }
        if (profiles.Any(p => p.Loaded == true))
        {
            warnings.Add("Un ou plusieurs profils sont chargés : VSS demandé, sa disponibilité sera contrôlée par le moteur avant toute copie.");
        }
        if (request.ContinueOnError)
        {
            warnings.Add("Poursuite après erreur demandée : les éléments non pris en charge seront annoncés, jamais masqués.");
        }

        var space = new BackupSpaceRequirement(BackupSpaceState.Inconnu, null, null, "Taille à calculer avant confirmation", SpaceNotice, null);
        var scope = string.Format(
            CultureInfo.InvariantCulture,
            "Profils {0} · dossiers AppData {1} · clés HKCU {2}. Contenu principal des fichiers ordinaires. " +
            "Pas de migration des applications, des ruches du registre, des flux nommés, ni des dossiers vides.",
            profiles.Count,
            profiles.Sum(p => p.Files.Count),
            profiles.Sum(p => p.Registry.Count));

        return new BackupPlanReview(
            CurrentSchemaVersion,
            ProductInfo.Version,
            planId,
            createdUtc.ToUniversalTime(),
            request.Demonstration,
            purpose,
            destination,
            request.Encrypted,
            keyPath,
            request.UseVss,
            request.NoCompress,
            request.ContinueOnError,
            profiles,
            space,
            exclusions,
            HasUnknownSpace: true,
            RequiresManualSpaceAcknowledgement: true,
            Scope: scope,
            Warnings: warnings,
            Notice: PlanNotice,
            MigApp: request.MigApp);
    }

    private static List<BackupProfileSelection> ValidateProfiles(BackupPlanRequest request, List<string> warnings)
    {
        var input = request.Profiles;
        if (input is null or { Count: 0 })
        {
            throw Invalid(SelectionCode, "Sélectionner au moins un profil avec un compte explicite avant la sauvegarde.");
        }
        if (input.Count > MaxProfiles)
        {
            throw Invalid(SelectionCode, $"Trop de profils dans un seul plan ({input.Count}). Sauvegarder par lots plus petits.");
        }

        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BackupProfileSelection>(input.Count);
        foreach (var profile in input)
        {
            if (profile is null)
            {
                throw Invalid(SelectionCode, "Profil sélectionné invalide : actualiser la liste et sélectionner les comptes à sauvegarder.");
            }

            var sid = (profile.Sid ?? string.Empty).Trim();
            if (sid.Length == 0 || !SidPattern.IsMatch(sid))
            {
                throw Invalid(SelectionCode, "SID du profil sélectionné invalide : actualiser la liste avant la sauvegarde.");
            }
            if (profile.IdentityKind == ProfileIdentityKind.Entra || sid.StartsWith(EntraPrefix, StringComparison.Ordinal))
            {
                throw Invalid(SelectionCode, "Profil Entra non pris en charge par USMT.");
            }
            if (!sids.Add(sid))
            {
                throw Invalid(SelectionCode, "Deux profils sélectionnés partagent le même SID.");
            }

            var account = (profile.Account ?? string.Empty).Trim();
            if (account.Length == 0 || account.Length > MaxAccountLength || account.StartsWith("S-1-", StringComparison.Ordinal)
                || !ExplicitAccountPattern.IsMatch(account))
            {
                throw Invalid(SelectionCode, "Compte non explicite ou non résolu : "
                    + (string.IsNullOrWhiteSpace(profile.Account) ? "(compte absent)" : profile.Account)
                    + ". Actualiser la liste avant la sauvegarde.");
            }
            if (!accounts.Add(account))
            {
                throw Invalid(SelectionCode, "Compte en double : " + account);
            }

            var path = RequireRooted(profile.ProfilePath, SelectionCode, "Chemin de profil absolu requis : actualiser la liste avant la sauvegarde.");
            if (!paths.Add(path))
            {
                throw Invalid(SelectionCode, "Deux profils sélectionnés partagent le même dossier : " + path);
            }

            if (profile.Loaded is null)
            {
                if (!request.Demonstration)
                {
                    throw Invalid(SelectionCode, "Chargement du profil inconnu : vérification préalable impossible pour " + account + ".");
                }
                warnings.Add("Chargement du profil " + account + " non établi en démonstration : le moteur devra le vérifier avant toute copie.");
            }
            else if (profile.Loaded == true && !request.UseVss)
            {
                throw Invalid(SelectionCode, "Profil chargé : activer VSS ou fermer ce qui utilise ce profil (" + account + ").");
            }

            result.Add(profile with
            {
                Sid = sid,
                Account = account,
                ProfilePath = path,
                Files = ValidateItems(profile.Files, files: true, account),
                Registry = ValidateItems(profile.Registry, files: false, account),
            });
        }

        return [.. result.OrderBy(p => p.Account, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Sid, StringComparer.Ordinal)];
    }

    private static IReadOnlyList<SoftwareItem> ValidateItems(IReadOnlyList<SoftwareItem>? items, bool files, string account)
    {
        if (items is null or { Count: 0 })
        {
            return [];
        }
        if (items.Count > MaxItemsPerProfile)
        {
            throw Invalid(SelectionCode, $"Trop d'éléments de configuration retenus pour {account}. Réduire la sélection.");
        }

        var kind = files ? "dossier" : "clé";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SoftwareItem>(items.Count);
        foreach (var item in items)
        {
            if (item is null)
            {
                throw Invalid(SelectionCode, "Élément de sélection invalide pour " + account + " : actualiser la liste des logiciels.");
            }
            if (!item.Selected)
            {
                throw Invalid(SelectionCode, $"Un élément non sélectionné ne peut pas figurer dans le plan ({account} · {item.Name}).");
            }
            var scopeOk = files
                ? item.Scope is SoftwareScope.Local or SoftwareScope.Roaming
                : item.Scope == SoftwareScope.Hkcu;
            if (!scopeOk)
            {
                throw Invalid(SelectionCode, $"Portée inattendue pour l'élément retenu pour {account} ({item.Name}).");
            }
            var rel = (item.Rel ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
            if (rel.Length == 0 || rel.StartsWith('\\')
                || RelativeEscape.IsMatch(rel) || InvalidPathChars.IsMatch(rel))
            {
                throw Invalid(SelectionCode, $"Chemin relatif non pris en charge pour {account} ({kind} {rel}).");
            }
            if (!seen.Add(item.Scope + "|" + rel))
            {
                throw Invalid(SelectionCode, $"Un élément est retenu deux fois pour {account} ({rel}).");
            }
            result.Add(item with { Rel = rel });
        }
        return [.. result.OrderBy(i => i.Rel, StringComparer.OrdinalIgnoreCase)];
    }

    private static string ValidateDestination(BackupPlanRequest request, IReadOnlyList<BackupProfileSelection> profiles)
    {
        var destination = RequireRooted(request.Destination, DestinationCode, "Destination absolue requise.");
        foreach (var profile in profiles)
        {
            if (IsWithin(destination, profile.ProfilePath, allowRoot: true))
            {
                throw Invalid(DestinationCode, "Destination dans un profil source : choisir un autre emplacement.");
            }
        }
        return destination;
    }

    private static string? ValidateKey(BackupPlanRequest request, IReadOnlyList<BackupProfileSelection> profiles, string destination, List<string> warnings)
    {
        if (!request.Encrypted)
        {
            if (!string.IsNullOrWhiteSpace(request.KeyPath))
            {
                throw Invalid(DestinationCode, "Chemin de clé indiqué sans sauvegarde chiffrée demandée.");
            }
            warnings.Add("Sauvegarde non chiffrée par FILPRO. Protéger le support et son accès.");
            return null;
        }

        if (request.NoCompress)
        {
            throw Invalid(PlanCode, "Le chiffrement ne peut pas être combiné avec un magasin non compressé.");
        }
        var keyPath = RequireRooted(request.KeyPath, DestinationCode, "Fichier clé requis pour ce magasin chiffré : chemin de clé invalide.");
        foreach (var profile in profiles)
        {
            if (IsWithin(keyPath, profile.ProfilePath, allowRoot: true))
            {
                throw Invalid(DestinationCode, "Déplacer la clé hors des profils sources.");
            }
        }
        if (IsWithin(keyPath, destination, allowRoot: true))
        {
            throw Invalid(DestinationCode, "Le fichier clé doit rester hors du dossier de sauvegarde.");
        }
        warnings.Add("Existence, contenu et droits du fichier clé restent à contrôler par le moteur ; la clé n'est jamais affichée ni journalisée.");
        return keyPath;
    }

    private static IReadOnlyList<BackupExclusion> ValidateExclusions(BackupPlanRequest request, IReadOnlyList<BackupProfileSelection> profiles)
    {
        IReadOnlyList<BackupExclusion> declared = request.Exclusions ?? [];
        if (declared.Count > MaxExclusions)
        {
            throw Invalid(PlanCode, "Trop d'exclusions déclarées pour un seul plan.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BackupExclusion>(declared.Count + RuleExclusions.Length);
        foreach (var exclusion in declared)
        {
            if (exclusion is null)
            {
                throw Invalid(PlanCode, "Exclusion invalide : chaque exclusion doit être nommée et justifiée.");
            }
            if (!Enum.IsDefined(exclusion.Kind))
            {
                throw Invalid(PlanCode, "Nature d'exclusion inconnue.");
            }
            if (exclusion.Origin != BackupExclusionOrigin.Operateur)
            {
                throw Invalid(PlanCode, "Une exclusion déclarée par la demande ne peut pas se présenter comme une règle du plan.");
            }
            var reason = (exclusion.Reason ?? string.Empty).Trim();
            if (reason.Length == 0 || reason.Length > MaxReasonLength || ControlChars.IsMatch(reason))
            {
                throw Invalid(PlanCode, "Chaque exclusion doit être justifiée par un motif lisible.");
            }
            var target = (exclusion.Target ?? string.Empty).Trim();
            if (RequiresTarget(exclusion.Kind) && target.Length == 0)
            {
                throw Invalid(PlanCode, "Une exclusion nommée doit indiquer ce qu'elle écarte.");
            }
            if (!RequiresTarget(exclusion.Kind) && target.Length > 0)
            {
                throw Invalid(PlanCode, "Cette nature d'exclusion ne désigne pas un élément nommé.");
            }
            if (exclusion.Kind == BackupExclusionKind.Profil)
            {
                foreach (var profile in profiles)
                {
                    if (string.Equals(profile.Account, target, StringComparison.OrdinalIgnoreCase))
                    {
                        throw Invalid(SelectionCode, "Un profil exclu ne peut pas figurer dans le plan : " + target + ".");
                    }
                }
            }
            if (!seen.Add(exclusion.Kind + "|" + target))
            {
                throw Invalid(PlanCode, "Exclusion en double : " + (target.Length > 0 ? target : exclusion.Kind));
            }
            result.Add(exclusion with { Target = target.Length == 0 ? null : target, Reason = reason });
        }

        return
        [
            .. result.OrderBy(e => e.Kind).ThenBy(e => e.Target, StringComparer.OrdinalIgnoreCase),
            .. RuleExclusions,
        ];
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

    private static bool RequiresTarget(BackupExclusionKind kind) => kind switch
    {
        BackupExclusionKind.Profil or BackupExclusionKind.Dossier or BackupExclusionKind.Cle or BackupExclusionKind.Magasin => true,
        _ => false,
    };

    private static string RequireRooted(string? value, string code, string message)
    {
        // Les deux séparateurs sont acceptés à l'entrée (comme la V1 avec IsPathRooted) ; seul « \ » est retenu ensuite.
        var raw = (value ?? string.Empty).Trim().Replace('/', '\\');
        if (raw.Length == 0 || !RootedPattern.IsMatch(raw))
        {
            throw Invalid(code, message);
        }
        // Le préfixe (« C: » ou « \\ ») est écarté avant la recherche de caractères interdits et de segments « .. ».
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

    private static bool IsVolumeRoot(string normalized)
    {
        if (normalized.Length == 2 && normalized[1] == ':')
        {
            return true;
        }
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }
        return normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length == 2;
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
