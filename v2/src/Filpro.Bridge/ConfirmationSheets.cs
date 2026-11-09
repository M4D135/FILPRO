using System.Buffers;
using System.Globalization;
using System.Text;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Nature de l'emplacement d'une destination, annoncée dans la confirmation Windows. <see cref="Absent"/> : lettre de lecteur
/// que FILPRO élevé ne voit pas (refusée avant toute fenêtre) ; <see cref="Unknown"/> : lecteur de type inconnu, optique ou
/// illisible, jamais présenté comme un disque local.
/// </summary>
public enum LocationKind { Local, Removable, Network, Absent, Unknown }

/// <summary>
/// Rendu d'une valeur dans la fenêtre de confirmation Windows. Chaque caractère de contrôle C0 ou C1 (CR et LF compris), de
/// direction, de largeur nulle, de format ou de séparation de ligne devient visible sous la forme ⟨U+XXXX⟩ et la valeur est
/// signalée ; au-delà de <see cref="MaximumLength"/> caractères, la fin est coupée, jamais au milieu d'un caractère, et la
/// longueur d'origine est annoncée.
/// </summary>
public static class NativeText
{
    public const int MaximumLength = 2000;

    public static (string Text, bool Flagged) Display(string? value)
    {
        if (string.IsNullOrEmpty(value)) return (string.Empty, false);
        var text = new StringBuilder(Math.Min(value.Length, MaximumLength));
        var flagged = false;
        var cut = false;
        var characters = 0;
        for (var index = 0; index < value.Length; characters++)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed);
            consumed = Math.Max(consumed, 1);
            var hidden = status != OperationStatus.Done || IsHidden(rune);
            // Une demi-paire de substitution isolée est rendue par son unité UTF-16, jamais transmise telle quelle.
            var token = hidden ? Token(status == OperationStatus.Done ? rune.Value : value[index]) : null;
            flagged |= hidden;
            cut |= text.Length + (token?.Length ?? consumed) > MaximumLength;
            if (!cut)
            {
                if (token is null) text.Append(value, index, consumed);
                else text.Append(token);
            }
            index += consumed;
        }
        if (cut) text.Append("… (").Append(characters.ToString(CultureInfo.InvariantCulture)).Append(" caractères)");
        return (text.ToString(), flagged);
    }

    private static bool IsHidden(Rune rune) =>
        rune.Value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069)
            or (>= 0x200B and <= 0x200D) or 0x2060 or 0xFEFF
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static string Token(int value) => "⟨U+" + value.ToString("X4", CultureInfo.InvariantCulture) + "⟩";
}

/// <summary>
/// Fiches de la fenêtre de confirmation Windows des opérations privilégiées, construites uniquement à partir des objets que
/// l'hôte détient : plan pris dans le magasin du serveur, reçu relu, magasin inspecté, offre, aperçu ou état enregistrés.
/// Aucun texte libre de la page (objet du plan, déclaration de sauvegarde préalable) n'y figure et chaque valeur passe par
/// <see cref="NativeText"/>. L'empreinte lie la fiche à l'objet qui sera exécuté.
/// </summary>
public static class ConfirmationSheets
{
    public const string HiddenCharactersAttention = "Caractères invisibles ou de contrôle signalés dans une valeur.";
    private const string Arrow = " → ";
    private static readonly string[] SizeUnits = ["o", "Ko", "Mo", "Go", "To"];
    private static readonly string[] DevicePrefixes = [@"\\?\", @"\\.\", @"\??\"];
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static ConfirmationSheet Backup(BackupPlanReview plan, HostSituation? situation, Func<string, DriveType?>? driveType = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sheet = new Sheet();
        foreach (var profile in plan.Profiles)
        {
            sheet.Fact("Profil", sheet.Text(profile.Account) + " — " + Number(profile.Files.Count) + " élément(s) AppData, "
                + Number(profile.Registry.Count) + " clé(s) HKCU\n" + sheet.Text(profile.ProfilePath), FactOrigin.Filpro);
        }
        Location(sheet, "Destination", plan.Destination, driveType,
            "Destination sur un partage réseau : les données quittent ce poste.", "Destination sur un support amovible.");
        Encryption(sheet, plan.Encrypted, plan.KeyPath);
        sheet.Fact("Capture VSS", YesNo(plan.UseVss), FactOrigin.Filpro);
        sheet.Fact("Compression", YesNo(!plan.NoCompress), FactOrigin.Filpro);
        sheet.Fact("Poursuite sur erreur", YesNo(plan.ContinueOnError), FactOrigin.Filpro);
        sheet.Fact("Plan", sheet.Text(plan.Id) + " — créé le "
            + plan.CreatedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC", FactOrigin.Filpro);
        if (!plan.Encrypted) sheet.Attention("Sauvegarde non chiffrée.");
        if (plan.ContinueOnError) sheet.Attention("Poursuite sur erreur : des éléments peuvent manquer sans arrêter la sauvegarde.");
        if (situation is not null && plan.Profiles.Any(profile => string.Equals(profile.Sid, situation.HostSid, StringComparison.OrdinalIgnoreCase)))
            sheet.Attention("Le profil du compte élevé (" + sheet.Text(situation.HostAccount) + ") fait partie de la sauvegarde.");
        return sheet.Build("sauvegarder.confirmPlan", "Lancer la sauvegarde de " + Number(plan.Profiles.Count) + " profil(s)",
            "Lancer la sauvegarde", ConfirmedPlanStore.Fingerprint(plan));
    }

    public static ConfirmationSheet Restore(RestorePlanReview plan, Func<string, DriveType?>? driveType = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sheet = new Sheet();
        sheet.Fact("Magasin", sheet.Text(plan.Bundle), FactOrigin.Interface);
        if (plan.SourceManifestSha256 is { } manifest) sheet.Fact("Empreinte du manifeste", sheet.Text(manifest), FactOrigin.Filpro);
        else sheet.Attention("Manifeste du magasin non relu par FILPRO.", severe: true);
        Optional(sheet, "Ordinateur source", plan.SourceComputerName, FactOrigin.Store);
        Optional(sheet, "Date de la sauvegarde", plan.SourceCreatedUtc, FactOrigin.Store);
        sheet.Fact("État du magasin", (plan.Legacy ? "ancien format" : "format FILPRO") + (plan.SourcePartial ? ", partiel" : "")
            + ", " + (plan.Encrypted ? "chiffré" : "non chiffré"), FactOrigin.Filpro);
        foreach (var pair in plan.IdentityPairs)
        {
            var target = pair.TargetSid is null ? null
                : plan.Targets.FirstOrDefault(candidate => string.Equals(candidate.Sid, pair.TargetSid, StringComparison.OrdinalIgnoreCase));
            var mapping = sheet.Text(pair.SourceAccount) + Arrow + sheet.Text(pair.TargetAccount);
            var profile = target is not null ? sheet.Text(target.ProfilePath) : pair.TargetSid is null ? "nouveau profil" : null;
            sheet.Fact("Correspondance", profile is null ? mapping : mapping + "\n" + profile, FactOrigin.Filpro);
        }
        // Une ligne de la fenêtre par ligne du résumé, sans découper le texte : un nom de groupe déclaré par le magasin reste
        // dans sa ligne et un caractère caché qu'il porte (fin de ligne comprise) reste visible et signalé. Un groupe hors du
        // catalogue porte le nom déclaré par le magasin : toute la ligne prend alors l'origine « magasin ».
        var declaredNames = plan.ConflictPolicies.Any(policy => !SoftwareCatalog.IsCatalogued(policy.Id));
        sheet.Fact("Conflits", plan.ConflictSummaryLines is { } lines ? string.Join("\n", lines.Select(sheet.Text)) : sheet.Text(plan.ConflictSummary),
            declaredNames ? FactOrigin.Store : FactOrigin.Filpro);
        if (plan.PriorBackup.Root is { Length: > 0 } root)
        {
            // Avant toute écriture dans les profils, le moteur y sauvegarde les profils cibles, chiffrés seulement avec une clé.
            Location(sheet, "Sauvegarde préalable", root, driveType,
                "Sauvegarde préalable sur un partage réseau : les données des profils cibles quittent ce poste.",
                "Sauvegarde préalable sur un support amovible : les données des profils cibles y sont copiées.");
            if (plan.PriorBackup.KeyPath is { } priorKey)
            {
                sheet.Fact("Clé de la sauvegarde préalable", sheet.Text(priorKey), FactOrigin.Interface);
                sheet.Attention("FILPRO écrit d’abord dans ce dossier une sauvegarde des profils cibles, chiffrée avec cette clé.");
            }
            else
            {
                sheet.Fact("Clé de la sauvegarde préalable", "aucune", FactOrigin.Filpro);
                sheet.Attention("Sauvegarde préalable non chiffrée : les profils cibles y sont copiés en clair.", severe: true);
            }
        }
        if (plan.Encrypted && plan.SourceKeyPath is { } key) sheet.Fact("Clé du magasin", sheet.Text(key), FactOrigin.Interface);
        var accounts = string.Join(", ", plan.IdentityPairs.Select(pair => pair.TargetAccount).Distinct(StringComparer.OrdinalIgnoreCase).Select(sheet.Text));
        sheet.Attention("Écrit des fichiers et des clés HKCU dans le profil de " + accounts + " : ils seront utilisés à leur prochaine ouverture de session.");
        var useSaved = plan.ConflictPolicies.Count(policy => policy.Mode == RestorePolicyMode.UseSaved);
        if (useSaved > 0) sheet.Attention(Number(useSaved) + " décision(s) « utiliser la sauvegarde » : les données courantes concernées seront remplacées.");
        if (plan.Legacy) sheet.Attention("Magasin d’un ancien format : contrôles du contenu plus limités.");
        if (plan.SourcePartial) sheet.Attention("Magasin partiel : des éléments n’ont pas été capturés et ne seront pas restaurés ; examiner ses journaux.");
        return sheet.Build("sauvegarder.confirmRestorePlan", "Restaurer " + Number(plan.IdentityPairs.Count) + " profil(s) sur ce poste",
            "Lancer la restauration", ConfirmedPlanStore.Fingerprint(plan));
    }

    public static ConfirmationSheet Repair(RepairPlanReview plan, Func<string, DriveType?>? driveType = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sheet = new Sheet();
        var account = sheet.Text(plan.Account);
        sheet.Fact("Compte", account, FactOrigin.Filpro);
        sheet.Fact("SID", sheet.Text(plan.Sid), FactOrigin.Filpro);
        sheet.Fact("Dossier du profil", sheet.Text(plan.ProfilePath), FactOrigin.Filpro);
        sheet.Fact("Dossier conservé", sheet.Text(plan.PreservedProfilePath), FactOrigin.Filpro);
        var location = Location(sheet, "Sauvegarde préalable", plan.SafetyBackupRoot, driveType,
            "Sauvegarde préalable sur un partage réseau : la copie du profil quitte ce poste.",
            "Sauvegarde préalable sur un support amovible : la copie du profil y est écrite.");
        // Le moteur y écrit une sauvegarde USMT non chiffrée du profil et une copie brute de NTUSER.DAT.
        sheet.Attention("Sauvegarde préalable non chiffrée : copie USMT du profil et NTUSER.DAT en clair.", severe: location == LocationKind.Network);
        FactLines(sheet, "Actions", plan.Actions);
        FactLines(sheet, "Avertissements", plan.Warnings);
        sheet.Fact("Remarque", sheet.Text(plan.Notice), FactOrigin.Filpro);
        return sheet.Build("analyser.confirmRepair", "Réparer le profil de " + account, "Lancer la réparation", ConfirmedPlanStore.Fingerprint(plan));
    }

    public static ConfirmationSheet RepairRollback(RepairRollbackReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        var sheet = new Sheet();
        var account = sheet.Text(review.Account);
        sheet.Fact("Reçu", sheet.Text(review.ReceiptPath), FactOrigin.Interface);
        sheet.Fact("Empreinte du reçu", sheet.Text(review.ReceiptSha256), FactOrigin.Filpro);
        sheet.Fact("Compte", account, FactOrigin.Filpro);
        sheet.Fact("SID", sheet.Text(review.Sid), FactOrigin.Filpro);
        sheet.Fact("Étape enregistrée", sheet.Text(review.Stage), FactOrigin.Filpro);
        sheet.Fact("Dossier d’origine", sheet.Text(review.OriginalPath), FactOrigin.Filpro);
        sheet.Fact("Dossier conservé", sheet.Text(review.PreservedPath), FactOrigin.Filpro);
        sheet.Fact("Sauvegardes de la réparation", sheet.Text(review.BackupDirectory) + "\n" + sheet.Text(review.SafetyBundle), FactOrigin.Filpro);
        if (review.NewProfilePreservedPath is { } kept) sheet.Fact("Nouveau profil conservé", sheet.Text(kept), FactOrigin.Filpro);
        return sheet.Build("analyser.rollbackRepair", "Annuler la réparation du profil de " + account, "Lancer le retour arrière",
            ConfirmedPlanStore.Fingerprint(review));
    }

    public static ConfirmationSheet Catalog(ExecutionStoreInspection inspection, string? keyPath)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var sheet = new Sheet();
        var store = inspection.Store;
        sheet.Fact("Magasin", sheet.Text(inspection.Bundle), FactOrigin.Interface);
        sheet.Fact("Empreinte du manifeste", sheet.Text(inspection.ManifestSha256), FactOrigin.Filpro);
        Optional(sheet, "Ordinateur source", store.SourceComputerName, FactOrigin.Store);
        Optional(sheet, "Date de la sauvegarde", store.SourceCreatedUtc, FactOrigin.Store);
        IReadOnlyList<string> accounts = store.ManifestUsers is { Count: > 0 } users ? users : [.. inspection.SourceProfiles.Select(profile => profile.Account)];
        if (accounts.Count > 0) sheet.Fact("Comptes du magasin", string.Join("\n", accounts.Select(sheet.Text)), FactOrigin.Store);
        Encryption(sheet, store.Encrypted, keyPath);
        sheet.Fact("Nature de l’opération", "Lecture longue ; aucune restauration ni copie dans un profil.", FactOrigin.Filpro);
        // Le lecteur place d'abord dans Warnings les avertissements déclarés par le manifeste (texte libre que le magasin peut
        // porter, aussi rendus dans StoreWarnings), puis les siens. Une liste qui ne commence pas par StoreWarnings reste
        // entière sous l'origine « magasin » ; sans StoreWarnings (ancien magasin V1), toutes les lignes sont de FILPRO.
        if (inspection.StoreWarnings is not { } declared) FactLines(sheet, "Avertissements", inspection.Warnings);
        else if (inspection.Warnings.Take(declared.Count).SequenceEqual(declared, StringComparer.Ordinal))
        {
            FactLines(sheet, "Avertissements du magasin", declared, FactOrigin.Store);
            FactLines(sheet, "Avertissements", [.. inspection.Warnings.Skip(declared.Count)]);
        }
        else FactLines(sheet, "Avertissements", inspection.Warnings, FactOrigin.Store);
        return sheet.Build("recuperer.catalog", "Préparer le catalogue de ce magasin", "Préparer le catalogue", ConfirmedPlanStore.Fingerprint(inspection));
    }

    public static ConfirmationSheet Recovery(RecoveryPlanReview plan, Func<string, DriveType?>? driveType = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var sheet = new Sheet();
        sheet.Fact("Magasin", sheet.Text(plan.Bundle), FactOrigin.Interface);
        sheet.Fact("Empreinte du manifeste", sheet.Text(plan.ManifestSha256), FactOrigin.Filpro);
        // Le moteur crée un nouveau dossier FILPRO-recuperation-… dans l'emplacement reçu de la page : l'emplacement reste « interface ».
        Location(sheet, "Dossier de destination", plan.Destination, driveType,
            "Dossier de destination sur un partage réseau : les fichiers récupérés quittent ce poste.",
            "Dossier de destination sur un support amovible : les fichiers récupérés y sont écrits.");
        sheet.Fact("Fichiers", Files(plan.Entries), FactOrigin.Filpro);
        if (plan.Entries.Count > 0)
        {
            var first = plan.Entries.Take(10).Select(entry => sheet.Text(entry.RelativePath)).ToList();
            if (plan.Entries.Count > 10) first.Add("… et " + Number(plan.Entries.Count - 10) + " autres");
            sheet.Fact("Premiers fichiers", string.Join("\n", first), FactOrigin.Store);
        }
        if (plan.Encrypted)
        {
            // La clé déchiffre le magasin : les fichiers extraits sont écrits déchiffrés.
            if (plan.KeyPath is { } key) sheet.Fact("Clé de déchiffrement du magasin", sheet.Text(key), FactOrigin.Interface);
            else sheet.Fact("Clé de déchiffrement du magasin", "aucune clé indiquée", FactOrigin.Filpro);
            sheet.Attention("Les fichiers récupérés sont écrits en clair.");
        }
        else sheet.Fact("Chiffrement", "non", FactOrigin.Filpro);
        // Phrases fixes du moteur (CreateRecoveryPlanAsync), sans texte du manifeste : l'origine reste FILPRO.
        FactLines(sheet, "Avertissements", plan.Warnings);
        return sheet.Build("recuperer.confirmPlan", "Récupérer " + Number(plan.Entries.Count) + " fichier(s) dans un nouveau dossier",
            "Lancer la récupération", ConfirmedPlanStore.Fingerprint(plan));
    }

    public static ConfirmationSheet Defaults(ConfigurationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var sheet = new Sheet();
        var file = PackManifestCodec.DefaultsPath.Replace('/', '\\') + " de ce pack";
        sheet.Fact("Fichier", preview.PackIdentity is { } identity ? file + "\nIdentité du pack : " + sheet.Text(identity) : file, FactOrigin.Filpro);
        // L'aperçu des défauts compare les réglages courants à eux-mêmes : seule la valeur écrite est utile quand rien ne change.
        foreach (var change in preview.Changes)
            sheet.Fact("Réglage", sheet.Text(change.Key) + " : " + (string.Equals(change.Before, change.After, StringComparison.Ordinal)
                ? sheet.Text(change.After) : sheet.Text(change.Before) + Arrow + sheet.Text(change.After)), FactOrigin.Filpro);
        sheet.Attention("Tous les futurs utilisateurs de ce pack recevront ces réglages.");
        if (Writes(preview, "encrypted", "False")) sheet.Attention("Le chiffrement ne sera plus proposé par défaut.", severe: true);
        if (Writes(preview, "conflictPolicy", "useSaved")) sheet.Attention("Les conflits seront résolus par défaut avec la version sauvegardée.");
        if (Writes(preview, "continueOnError", "True")) sheet.Attention("La poursuite sur erreur sera activée par défaut.");
        return sheet.Build("configuration.applyDefaults", "Écrire les réglages par défaut du pack", "Écrire dans le pack", ConfirmedPlanStore.Fingerprint(preview));
    }

    public static ConfirmationSheet UpdatePrepare(ConfirmationRecords.OfferRecord record, string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(record);
        var sheet = new Sheet();
        var offer = record.Offer;
        var version = sheet.Text(offer.Version);
        sheet.Fact("Versions", sheet.Text(currentVersion) + Arrow + version, FactOrigin.Filpro);
        var (source, origin) = Source(sheet, record);
        sheet.Fact("Source", source, origin);
        sheet.Fact("Fichier", sheet.Text(offer.FileName) + " — " + Size(offer.Bytes), FactOrigin.Filpro);
        sheet.Fact("SHA-256", Hash(sheet, offer.Sha256), record.HashTyped ? FactOrigin.Interface : FactOrigin.Filpro);
        sheet.Fact("Origine de l’empreinte", HashOrigin(record), FactOrigin.Filpro);
        FactLines(sheet, "Avertissements", offer.Warnings);
        sheet.Attention("Aucune signature d’éditeur : seule l’empreinte SHA-256 est contrôlée.");
        if (record.HashTyped) sheet.Attention("SHA-256 saisi dans l’interface : il ne prouve pas l’origine du pack.", severe: true);
        return sheet.Build("updates.prepare", "Préparer FILPRO " + version, "Préparer cette version", ConfirmedPlanStore.Fingerprint(offer));
    }

    public static ConfirmationSheet UpdateInstall(UpdateStatus status, ConfirmationRecords.OfferRecord? provenance)
    {
        ArgumentNullException.ThrowIfNull(status);
        var sheet = new Sheet();
        var target = sheet.Text(status.TargetVersion);
        sheet.Fact("Versions", sheet.Text(status.CurrentVersion) + Arrow + target, FactOrigin.Filpro);
        sheet.Fact("Transaction", sheet.Text(status.TransactionId), FactOrigin.Filpro);
        if (provenance is null) sheet.Attention("Provenance non conservée par cette session de FILPRO : non vérifiable.", severe: true);
        else
        {
            var (source, origin) = Source(sheet, provenance);
            sheet.Fact("Provenance", source + " · empreinte : " + HashOrigin(provenance), provenance.HashTyped ? FactOrigin.Interface : origin);
        }
        sheet.Attention("FILPRO se ferme ; la version précédente est conservée ; le nouveau démarrage est contrôlé avant validation.");
        return sheet.Build("updates.install", "Installer FILPRO " + target + " puis fermer FILPRO", "Installer et fermer FILPRO",
            ConfirmedPlanStore.Fingerprint(status));
    }

    public static ConfirmationSheet UpdateRollback(UpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var sheet = new Sheet();
        sheet.Fact("Versions", sheet.Text(status.CurrentVersion) + Arrow
            + (status.PreviousVersion is { } previous ? sheet.Text(previous) : "version conservée"), FactOrigin.Filpro);
        sheet.Fact("Transaction", sheet.Text(status.TransactionId), FactOrigin.Filpro);
        sheet.Attention("FILPRO se ferme ; le retour arrière est contrôlé au nouveau démarrage.");
        return sheet.Build("updates.rollback", "Revenir à la version précédente puis fermer FILPRO", "Revenir et fermer FILPRO",
            ConfirmedPlanStore.Fingerprint(status));
    }

    /// <summary>
    /// Nature d'un emplacement : chemin UNC (\\serveur\partage, \\?\UNC\…) = partage réseau ; lettre de lecteur = type
    /// Windows du lecteur lu par <paramref name="driveType"/> (par défaut <see cref="DriveInfo"/>) : fixe ou RAM = disque
    /// local, réseau, amovible, absent ; tout autre type (inconnu, optique) ou un type illisible = inconnu ; sinon disque local.
    /// </summary>
    public static LocationKind Classify(string path, Func<string, DriveType?>? driveType = null) => Locate(path, driveType).Kind;

    public static string Describe(LocationKind kind) => kind switch
    {
        LocationKind.Network => "partage réseau",
        LocationKind.Removable => "support amovible",
        LocationKind.Absent => "lecteur introuvable",
        LocationKind.Unknown => "lecteur de type inconnu",
        _ => "disque local",
    };

    // Nature de l'emplacement et racine du lecteur interrogé (« Z:\ »), sans racine pour un partage ou un chemin sans lettre.
    private static (LocationKind Kind, string? Root) Locate(string path, Func<string, DriveType?>? driveType)
    {
        ArgumentNullException.ThrowIfNull(path);
        var value = path.Replace('/', '\\');
        var device = DevicePrefixes.FirstOrDefault(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        if (device is not null)
        {
            value = value[device.Length..];
            // Seul un chemin de périphérique vers une lettre de lecteur est classé par son lecteur : \\?\UNC\… et tout autre restent réseau.
            if (!IsDriveLetter(value)) return (LocationKind.Network, null);
        }
        else if (value.StartsWith(@"\\", StringComparison.Ordinal)) return (LocationKind.Network, null);
        if (!IsDriveLetter(value)) return (LocationKind.Local, null);
        var root = char.ToUpperInvariant(value[0]) + @":\";
        var kind = (driveType ?? SystemDriveType)(root) switch
        {
            DriveType.Fixed or DriveType.Ram => LocationKind.Local,
            DriveType.Network => LocationKind.Network,
            DriveType.Removable => LocationKind.Removable,
            DriveType.NoRootDirectory => LocationKind.Absent,
            _ => LocationKind.Unknown,
        };
        return (kind, root);
    }

    /// <summary>
    /// Emplacement choisi dans la page où l'opération écrit : « {chemin} ({nature}) » (interface) et le point d'attention
    /// propre à ce qui y est écrit pour un partage réseau (<paramref name="network"/>, grave) ou un support amovible
    /// (<paramref name="removable"/>). Un lecteur de type inconnu est grave ; une lettre de lecteur que FILPRO élevé ne voit
    /// pas est refusée avant toute fenêtre (destination.absent).
    /// </summary>
    private static LocationKind Location(Sheet sheet, string label, string path, Func<string, DriveType?>? driveType,
        string network, string removable)
    {
        var (kind, root) = Locate(path, driveType);
        if (kind == LocationKind.Absent)
            throw new FilproOperationException("destination.absent", "Lecteur " + root![..2] + " introuvable pour FILPRO élevé : un lecteur réseau "
                + @"connecté dans la session n’est pas visible ici ; indiquer le chemin UNC \\serveur\partage.");
        sheet.Fact(label, sheet.Text(path) + " (" + Describe(kind) + ")", FactOrigin.Interface);
        if (kind == LocationKind.Network) sheet.Attention(network, severe: true);
        else if (kind == LocationKind.Removable) sheet.Attention(removable);
        else if (kind == LocationKind.Unknown)
            sheet.Attention(label + " sur un lecteur de type inconnu : FILPRO ne peut pas dire s’il est local, amovible ou réseau.", severe: true);
        return kind;
    }

    private static bool IsDriveLetter(string value) => value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';

    private static DriveType? SystemDriveType(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Encryption(Sheet sheet, bool encrypted, string? keyPath)
    {
        if (!encrypted) sheet.Fact("Chiffrement", "non", FactOrigin.Filpro);
        else if (keyPath is null) sheet.Fact("Chiffrement", "oui — aucune clé indiquée", FactOrigin.Filpro);
        else sheet.Fact("Chiffrement", "oui — clé " + sheet.Text(keyPath), FactOrigin.Interface);
    }

    private static void Optional(Sheet sheet, string label, string? value, FactOrigin origin)
    {
        if (!string.IsNullOrEmpty(value)) sheet.Fact(label, sheet.Text(value), origin);
    }

    private static void FactLines(Sheet sheet, string label, IReadOnlyList<string> values, FactOrigin origin = FactOrigin.Filpro)
    {
        if (values.Count > 0) sheet.Fact(label, string.Join("\n", values.Select(sheet.Text)), origin);
    }

    private static string Files(IReadOnlyList<ExecutionStoreEntry> entries)
    {
        var text = Number(entries.Count) + " fichier(s), ";
        var unknown = entries.Count(entry => entry.Bytes is null);
        if (unknown == entries.Count) return text + "taille inconnue";
        var size = Size(entries.Sum(entry => entry.Bytes ?? 0));
        return unknown == 0 ? text + size : text + "au moins " + size + " (taille inconnue pour " + Number(unknown) + " fichier(s))";
    }

    // Taille sur la base 1024 en unités françaises, comme l'Explorateur : « 12,4 Mo ».
    private static string Size(long bytes)
    {
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(unit == 0 ? "0" : "0.0", French) + " " + SizeUnits[unit];
    }

    // Empreinte complète en 8 groupes de 8 pour la comparer ; toute autre valeur est rendue telle quelle.
    private static string Hash(Sheet sheet, string value)
    {
        var text = sheet.Text(value);
        return text.Length == 64 && text.All(char.IsAsciiHexDigit) ? string.Join(' ', text.Chunk(8).Select(group => new string(group))) : text;
    }

    private static (string Text, FactOrigin Origin) Source(Sheet sheet, ConfirmationRecords.OfferRecord record) => record.Kind switch
    {
        UpdateSourceKind.Folder => ("Dossier " + sheet.Text(record.Path), FactOrigin.Interface),
        _ => ("ZIP " + sheet.Text(record.Path), FactOrigin.Interface),
    };

    // Origine de la somme attendue, comme UpdateSources la lit : SHA256SUMS du dossier, sinon saisie de la page.
    private static string HashOrigin(ConfirmationRecords.OfferRecord record) => record.HashTyped ? "saisie dans l’interface" : record.Kind switch
    {
        UpdateSourceKind.Folder => "SHA256SUMS du dossier",
        _ => "SHA256SUMS du dossier du ZIP",
    };

    // Valeurs écrites par l'aperçu (ConfigurationCodec.Changes) : booléens « True »/« False », politique de conflit en texte.
    private static bool Writes(ConfigurationPreview preview, string key, string after) =>
        preview.Changes.Any(change => change.Key == key && string.Equals(change.After, after, StringComparison.OrdinalIgnoreCase));

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string YesNo(bool value) => value ? "oui" : "non";

    /// <summary>Fiche en construction : une valeur portant un caractère invisible ou de contrôle ajoute une seule attention grave.</summary>
    private sealed class Sheet
    {
        private readonly List<ConfirmationFact> facts = [];
        private readonly List<ConfirmationAttention> attention = [];
        private bool flagged;

        public string Text(string? value)
        {
            var (text, hidden) = NativeText.Display(value);
            flagged |= hidden;
            return text;
        }

        public void Fact(string label, string value, FactOrigin origin) => facts.Add(new(label, value, origin));

        public void Attention(string text, bool severe = false) => attention.Add(new(text, severe));

        public ConfirmationSheet Build(string operation, string title, string approveLabel, string digest)
        {
            if (flagged) attention.Insert(0, new(HiddenCharactersAttention, Severe: true));
            return new(operation, title, approveLabel, digest, [.. facts], [.. attention]);
        }
    }
}
