using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Filpro.Core;

public sealed record ManifestFileReference(string Path, string Sha256, long Bytes);
public sealed record BackupManifestProfile(string Id, BackupProfileSelection Selection, string Store,
    string Status, IReadOnlyList<ManifestFileReference> Rules, ManifestFileReference? Payload,
    ManifestFileReference? Catalog, int? ExitCode, string Verification,
    IReadOnlyList<string>? PersonalFolders = null, IReadOnlyList<string>? ExcludedPaths = null,
    ManifestFileReference? ProfileConfig = null);
public sealed record BackupManifest(int SchemaVersion, string Kind, string Id, string ProductVersion,
    DateTimeOffset CreatedUtc, string Computer, string UsmtVersion, string Status, bool Simulated,
    bool Compressed, bool Encrypted, bool ContinueOnError, bool Vss,
    IReadOnlyList<BackupManifestProfile> Profiles, IReadOnlyList<string> Warnings, bool MigApp = false);

/// <summary>Manifeste JSON de dossiers USMT ordinaires. Aucun contenu MIG ni archive WAD propriétaire.</summary>
public static partial class BackupManifestCodec
{
    public const int Schema = 2;
    public const string Kind = "FILPRO.USMT.MultiProfile";
    public const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
    private static readonly string[] States = ["Preparing", "Running", "Ready", "Partial", "Failed", "Cancelled"];

    public static byte[] Encode(BackupManifest manifest)
    {
        Validate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
        if (bytes.Length > MaximumBytes) throw Invalid("Manifeste trop volumineux.");
        return bytes;
    }

    public static BackupManifest Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid("Taille de manifeste invalide.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            RejectDuplicateProperties(document.RootElement);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(bytes.Span, Options) ?? throw Invalid("Manifeste vide.");
            Validate(manifest);
            return manifest;
        }
        catch (JsonException) { throw Invalid("Manifeste JSON invalide ou champs inconnus."); }
    }

    public static void Validate(BackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != Schema || manifest.Kind != Kind || !Guid.TryParseExact(manifest.Id, "N", out _)
            || manifest.CreatedUtc == default || !States.Contains(manifest.Status, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.ProductVersion) || manifest.ProductVersion.Length > 80
            || string.IsNullOrWhiteSpace(manifest.Computer) || manifest.Computer.Length > 260
            || string.IsNullOrWhiteSpace(manifest.UsmtVersion) || manifest.UsmtVersion.Length > 80
            || manifest.Profiles is null || manifest.Profiles.Count is < 1 or > BackupPlan.MaxProfiles
            || manifest.Warnings is null || manifest.Warnings.Count > 1000
            || manifest.Warnings.Any(w => w is null || w.Length > 4000)
            || (manifest.Encrypted && !manifest.Compressed)) throw Invalid("En-tête ou état du manifeste invalide.");
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var stores = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in manifest.Profiles)
        {
            if (profile is null || profile.Selection is null || !Guid.TryParseExact(profile.Id, "N", out _)
                || !ids.Add(profile.Id) || !States.Contains(profile.Status, StringComparer.Ordinal)
                || !identities.Add(profile.Selection.Sid) || !accounts.Add(profile.Selection.Account)
                || !AccountPattern().IsMatch(profile.Selection.Account) || profile.Selection.Account.Length > 512
                || !IsCanonicalSid(profile.Selection.Sid)
                || !Enum.IsDefined(profile.Selection.IdentityKind)) throw Invalid("Profil du manifeste invalide ou dupliqué.");
            _ = StoragePathPolicy.NormalizeWindows(profile.Selection.ProfilePath);
            var selection = new SoftwareSelectionDocument(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind,
                manifest.CreatedUtc, profile.Selection.Files, profile.Selection.Registry, SoftwareSelectionCodec.DefaultPreferences);
            _ = SoftwareSelectionCodec.Encode(selection);
            var personal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relative in profile.PersonalFolders ?? [])
            {
                _ = RequireRelative(relative);
                if (relative.IndexOfAny(['/', '\\', '[', ']', '%', '*', '?']) >= 0
                    || relative.Equals("AppData", StringComparison.OrdinalIgnoreCase) || !personal.Add(relative))
                    throw Invalid("Dossier personnel hors de la portée de premier niveau ou dupliqué.");
            }
            foreach (var relative in profile.ExcludedPaths ?? []) _ = UsmtMigrationRules.RequireExcludedRelative(relative);
            if ((profile.PersonalFolders?.Count ?? 0) > 2000 || (profile.ExcludedPaths?.Count ?? 0) > 10000)
                throw Invalid("Périmètre de fichiers trop grand.");
            var store = RequireRelative(profile.Store);
            if (!store.Equals("Store/" + profile.Id, StringComparison.Ordinal) || !stores.Add(store))
                throw Invalid("Sous-magasin de profil incohérent.");
            if (profile.Rules is null || profile.Rules.Count is < 1 or > 200) throw Invalid("Règles absentes.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in profile.Rules)
            {
                ValidateReference(rule);
                if (!rule.Path.Replace('\\', '/').StartsWith("Rules/" + profile.Id + "/", StringComparison.Ordinal)
                    || !rule.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || !paths.Add(rule.Path))
                    throw Invalid("Règle hors du profil ou dupliquée.");
            }
            if (profile.Payload is not null)
            {
                ValidateReference(profile.Payload);
                var expectedPayload = manifest.Compressed ? profile.Store + "/USMT/USMT.MIG" : "Indexes/" + profile.Id + ".jsonl";
                if (profile.Payload.Path != expectedPayload || (!manifest.Compressed && profile.Payload.Bytes > PlainStoreIndexCodec.MaximumBytes))
                    throw Invalid("Charge ou index USMT inattendu.");
            }
            if (profile.Catalog is not null)
            {
                ValidateReference(profile.Catalog);
                if (profile.Catalog.Path != "Catalog/" + profile.Id + ".json") throw Invalid("Catalogue inattendu.");
            }
            if (profile.ProfileConfig is not null)
            {
                ValidateReference(profile.ProfileConfig);
                if (profile.ProfileConfig.Path != "Rules/" + profile.Id + "/profile-config.xml")
                    throw Invalid("Configuration USMT inattendue.");
            }
            if (profile.Verification is not ("NotRun" or "Verified" or "FileHashesVerified" or "Failed")) throw Invalid("Preuve inconnue.");
            if (profile.Status is "Ready" or "Partial")
            {
                var expectedProof = manifest.Compressed ? "Verified" : "FileHashesVerified";
                if (profile.Payload is null || profile.ProfileConfig is null || profile.Verification != expectedProof || profile.ExitCode is not (0 or 3))
                    throw Invalid("Profil déclaré exploitable sans empreinte ni contrôle USMT.");
            }
        }
        if (manifest.Status == "Ready" && manifest.Profiles.Any(p => p.Status != "Ready"))
            throw Invalid("Réussite globale incompatible avec un profil incomplet.");
        if (manifest.Status == "Partial" && !manifest.Profiles.Any(p => p.Status is "Ready" or "Partial"))
            throw Invalid("Magasin partiel sans profil exploitable.");
    }

    public static string RequireRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.StartsWith('/') || path.StartsWith('\\'))
            throw Invalid("Chemin relatif requis.");
        var normalized = path.Replace('\\', '/');
        foreach (var segment in normalized.Split('/')) StoragePathPolicy.RequireSegment(segment);
        return normalized;
    }
    public static void ValidateReference(ManifestFileReference reference)
    {
        if (reference is null || reference.Bytes < 0 || reference.Sha256 is null || !HashPattern().IsMatch(reference.Sha256))
            throw Invalid("Empreinte de fichier invalide.");
        _ = RequireRelative(reference.Path);
    }
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("Champ JSON dupliqué.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }
    private static FilproOperationException Invalid(string message) => new("store.manifest", message);
    private static bool IsCanonicalSid(string sid) => sid is not null && SidPattern().IsMatch(sid)
        && sid.Split('-').TakeLast(4).All(p => uint.TryParse(p, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            && p == value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    [GeneratedRegex(@"\AS-1-(?:5-21|12-1)-[0-9]+-[0-9]+-[0-9]+-[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SidPattern();
    [GeneratedRegex(@"\A[^\\*?""\x00-\x1f/:]+\\[^\\*?""\x00-\x1f/:]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
    [GeneratedRegex(@"\A[0-9A-Fa-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
