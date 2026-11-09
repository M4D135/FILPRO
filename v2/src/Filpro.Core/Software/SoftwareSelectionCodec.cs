using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>
/// Options mémorisées avec la sélection (reprises des <c>Options</c> de la dernière sélection V1). Aucun compte, SID,
/// destination, fichier de clé, consentement ni acquittement n'est porté ici.
/// </summary>
public sealed record SelectionPreferences(bool MigApp, bool Vss, bool ContinueOnError, bool Encrypted);

/// <summary>
/// Document persisté de la sélection logiciels : fichiers (y compris les modèles communs, catégorie <see cref="SoftwareCategory.Modele"/>),
/// clés du registre et préférences mémorisées. Le SID n'est jamais sérialisé : il sert uniquement de clé de stockage (nom du
/// fichier) côté fournisseur poste. <see cref="SavedUtc"/> vient de l'appelant (aucune horloge dans le codec).
/// </summary>
public sealed record SoftwareSelectionDocument(
    int SchemaVersion,
    string Kind,
    DateTimeOffset SavedUtc,
    IReadOnlyList<SoftwareItem> Files,
    IReadOnlyList<SoftwareItem> Registry,
    SelectionPreferences Preferences)
{
    /// <summary>Reconstruit la dernière sélection pour un compte donné à partir du document relu.</summary>
    public LastSelection ToLastSelection(string sid)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        return new LastSelection(sid, Files, Registry, SavedUtc);
    }
}

/// <summary>
/// Codec pur, déterministe et sans E/S de la sélection logiciels : <see cref="Encode"/> canonise (tri, dédoublonnage,
/// assainissement des libellés), valide la forme fermée puis écrit un JSON canonique ; <see cref="Decode"/> relit et revalide
/// cette même forme. Écriture via <see cref="Utf8JsonWriter"/> et lecture via <see cref="JsonDocument"/> : aucune
/// sérialisation par réflexion, aucune dépendance ajoutée. Les tailles, recommandations et états « connus » ne sont jamais
/// inventés : ils ne font pas partie du document et restent à la charge du fournisseur poste.
/// </summary>
public static class SoftwareSelectionCodec
{
    public const int Schema = 1;
    public const string Kind = "SelectionLogiciels";
    public const long MaxBytes = 4 * 1024 * 1024;
    public const int MaxItems = 500;
    public const int MaxRelLength = 1024;
    public const int MaxRegistryRelLength = 2048;
    public const int MaxNameLength = 260;

    private const string Code = "selection.store";

    private static readonly string[] TopFields = ["schemaVersion", "kind", "savedUtc", "files", "registry", "preferences"];
    private static readonly string[] ItemFields = ["scope", "rel", "name", "category"];
    private static readonly string[] PreferenceFields = ["migApp", "vss", "continueOnError", "encrypted"];

    private static readonly Regex SavedUtcPattern = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?Z$", RegexOptions.CultureInvariant);
    private static readonly Regex ReservedDevicePattern = new(@"^(?i:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.CultureInvariant);
    private static readonly Regex FileForbiddenPattern = new(@"[\x00-\x1f:*?""<>|]", RegexOptions.CultureInvariant);
    private static readonly Regex RegistryForbiddenPattern = new(@"[\x00-\x1f\[\]/*?""<>|:]", RegexOptions.CultureInvariant);
    private static readonly Regex ControlPattern = new(@"[\x00-\x1f]", RegexOptions.CultureInvariant);

    /// <summary>Préférences par défaut, honnêtes : tout décoché, aucune option supposée activée.</summary>
    public static SelectionPreferences DefaultPreferences { get; } = new(false, false, false, false);

    /// <summary>Canonise, valide puis sérialise le document en UTF-8 sans BOM. Refuse toute forme invalide.</summary>
    public static byte[] Encode(SoftwareSelectionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = Canonicalize(document);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = null }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", Schema);
            writer.WriteString("kind", Kind);
            writer.WriteString("savedUtc", normalized.SavedUtc.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture));
            WriteItems(writer, "files", normalized.Files);
            WriteItems(writer, "registry", normalized.Registry);
            writer.WriteStartObject("preferences");
            writer.WriteBoolean("migApp", normalized.Preferences.MigApp);
            writer.WriteBoolean("vss", normalized.Preferences.Vss);
            writer.WriteBoolean("continueOnError", normalized.Preferences.ContinueOnError);
            writer.WriteBoolean("encrypted", normalized.Preferences.Encrypted);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        if (bytes.LongLength > MaxBytes)
        {
            throw new FilproOperationException(Code, "Sélection trop volumineuse pour être enregistrée.");
        }

        return bytes;
    }

    /// <summary>Relit et revalide un document écrit par <see cref="Encode"/>. Lève <see cref="FilproOperationException"/> si la forme est refusée.</summary>
    public static SoftwareSelectionDocument Decode(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length == 0)
        {
            throw Invalid("document vide");
        }

        if (utf8Json.Length > MaxBytes)
        {
            throw Invalid("document trop volumineux");
        }

        using var doc = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("objet attendu");
        }

        RequireExactFields(root, TopFields, "document");
        var schema = root.GetProperty("schemaVersion");
        if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != Schema)
        {
            throw Invalid("version de schéma non prise en charge");
        }

        var kind = ReadString(root, "kind", "document");
        if (!kind.Equals(Kind, StringComparison.Ordinal))
        {
            throw Invalid("nature de la sélection invalide");
        }

        var savedUtc = ReadSavedUtc(root);
        var files = ReadItems(root.GetProperty("files"), file: true);
        var registry = ReadItems(root.GetProperty("registry"), file: false);
        if (files.Count + registry.Count > MaxItems)
        {
            throw Invalid($"plus de {MaxItems} éléments");
        }

        var preferences = ReadPreferences(root.GetProperty("preferences"));
        return new SoftwareSelectionDocument(Schema, Kind, savedUtc, files, registry, preferences);
    }

    private static SoftwareSelectionDocument Canonicalize(SoftwareSelectionDocument document)
    {
        var files = CanonItems(document.Files, file: true);
        var registry = CanonItems(document.Registry, file: false);
        if (files.Count + registry.Count > MaxItems)
        {
            throw Invalid($"plus de {MaxItems} éléments");
        }

        return new SoftwareSelectionDocument(Schema, Kind, document.SavedUtc, files, registry, document.Preferences);
    }

    private static IReadOnlyList<SoftwareItem> CanonItems(IReadOnlyList<SoftwareItem> items, bool file)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<SoftwareItem>();
        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var scope = file ? item.Scope : SoftwareScope.Hkcu;
            if (file && scope is not (SoftwareScope.Local or SoftwareScope.Roaming))
            {
                throw Invalid("portée de fichier invalide");
            }

            var rel = NormalizeRel(item.Rel, file);
            var name = SanitizeName(item.Name, rel);
            var key = (file ? "A|" + ScopeToken(scope) + "|" : "R|") + rel;
            if (!seen.Add(key))
            {
                continue;
            }

            kept.Add(new SoftwareItem(scope, rel, name, item.Category, true, false, false, null, false, false));
        }

        return [.. kept.OrderBy(i => ScopeToken(i.Scope), StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Rel, StringComparer.OrdinalIgnoreCase)];
    }

    private static void WriteItems(Utf8JsonWriter writer, string name, IReadOnlyList<SoftwareItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            writer.WriteStartObject();
            writer.WriteString("scope", ScopeToken(item.Scope));
            writer.WriteString("rel", item.Rel);
            writer.WriteString("name", item.Name);
            writer.WriteString("category", CategoryToken(item.Category));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static IReadOnlyList<SoftwareItem> ReadItems(JsonElement array, bool file)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("liste absente ou invalide");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<SoftwareItem>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("élément attendu");
            }

            RequireExactFields(element, ItemFields, "élément");
            var scope = ParseScope(ReadString(element, "scope", "élément"), file);
            var rel = ReadString(element, "rel", "élément");
            if (!IsValidRel(rel, file))
            {
                throw Invalid("chemin relatif invalide");
            }

            var name = ReadString(element, "name", "élément");
            if (name.Length > MaxNameLength || ControlPattern.IsMatch(name))
            {
                throw Invalid("libellé invalide");
            }

            var category = ParseCategory(ReadString(element, "category", "élément"));
            var key = (file ? "A|" + ScopeToken(scope) + "|" : "R|") + rel;
            if (!seen.Add(key))
            {
                throw Invalid("élément en double");
            }

            items.Add(new SoftwareItem(scope, rel, name, category, true, false, false, null, false, false));
        }

        return items;
    }

    private static SelectionPreferences ReadPreferences(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("préférences attendues");
        }

        RequireExactFields(element, PreferenceFields, "préférences");
        return new SelectionPreferences(
            ReadBoolean(element, "migApp"),
            ReadBoolean(element, "vss"),
            ReadBoolean(element, "continueOnError"),
            ReadBoolean(element, "encrypted"));
    }

    private static DateTimeOffset ReadSavedUtc(JsonElement root)
    {
        var text = ReadString(root, "savedUtc", "document");
        if (!SavedUtcPattern.IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            throw Invalid("horodatage invalide");
        }

        return value.ToUniversalTime();
    }

    private static void RequireExactFields(JsonElement element, string[] allowed, string label)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw Invalid($"propriété interdite ({label}) : {property.Name}");
            }

            present.Add(property.Name);
        }

        foreach (var field in allowed)
        {
            if (!present.Contains(field))
            {
                throw Invalid($"propriété absente ({label}) : {field}");
            }
        }
    }

    private static string ReadString(JsonElement element, string name, string label)
    {
        var property = element.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"champ texte attendu ({label}) : {name}");
        }

        return property.GetString() ?? throw Invalid($"champ texte vide ({label}) : {name}");
    }

    private static bool ReadBoolean(JsonElement element, string name)
    {
        var kind = element.GetProperty(name).ValueKind;
        return kind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Invalid($"champ booléen attendu : {name}"),
        };
    }

    private static SoftwareScope ParseScope(string token, bool file) => token.ToLowerInvariant() switch
    {
        "local" when file => SoftwareScope.Local,
        "roaming" when file => SoftwareScope.Roaming,
        "hkcu" when !file => SoftwareScope.Hkcu,
        _ => throw Invalid("portée invalide"),
    };

    private static SoftwareCategory ParseCategory(string token) => token.ToLowerInvariant() switch
    {
        "donnees" => SoftwareCategory.Donnees,
        "preferences" => SoftwareCategory.Preferences,
        "caches" => SoftwareCategory.Caches,
        "modele" => SoftwareCategory.Modele,
        "autre" => SoftwareCategory.Autre,
        _ => throw Invalid("catégorie invalide"),
    };

    private static string ScopeToken(SoftwareScope scope) => scope switch
    {
        SoftwareScope.Local => "local",
        SoftwareScope.Roaming => "roaming",
        _ => "hkcu",
    };

    private static string CategoryToken(SoftwareCategory category) => category switch
    {
        SoftwareCategory.Donnees => "donnees",
        SoftwareCategory.Preferences => "preferences",
        SoftwareCategory.Caches => "caches",
        SoftwareCategory.Modele => "modele",
        _ => "autre",
    };

    private static string NormalizeRel(string rel, bool file)
    {
        var normalized = (rel ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
        if (!IsValidRel(normalized, file))
        {
            throw Invalid("chemin relatif invalide");
        }

        return normalized;
    }

    private static bool IsValidRel(string rel, bool file)
    {
        if (string.IsNullOrWhiteSpace(rel))
        {
            return false;
        }

        if (file)
        {
            return rel.Length <= MaxRelLength && !rel.StartsWith('\\') && !rel.Contains('/')
                && !FileForbiddenPattern.IsMatch(rel) && IsValidSegments(rel, reserved: true);
        }

        return rel.Length <= MaxRegistryRelLength && !rel.StartsWith('\\')
            && !RegistryForbiddenPattern.IsMatch(rel) && IsValidSegments(rel, reserved: false);
    }

    private static bool IsValidSegments(string rel, bool reserved)
    {
        foreach (var part in rel.Split('\\'))
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                return false;
            }

            if (part[^1] is ' ' or '.')
            {
                return false;
            }

            if (reserved && ReservedDevicePattern.IsMatch(part))
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeName(string name, string fallback)
    {
        var text = ControlPattern.Replace(string.IsNullOrEmpty(name) ? fallback : name, " ");
        return text.Length > MaxNameLength ? text[..MaxNameLength] : text;
    }

    private static FilproOperationException Invalid(string reason) =>
        new(Code, "Sélection de logiciels refusée : " + reason + ".");
}
