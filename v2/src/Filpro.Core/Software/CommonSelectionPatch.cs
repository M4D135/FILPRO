using System.Security.Cryptography;
using System.Text.Json;

namespace Filpro.Core;

public sealed record CommonPatchChange(string Id, string Scope, string RelativePath, string? Before, string After, string Disposition);
public sealed record CommonPatchPreview(string Id, bool Demonstration, string Sha256, string ExpectedRevision,
    IReadOnlyList<CommonPatchChange> Changes, string Notice);
public sealed record CommonPatchResult(bool Demonstration, CommonSelectionState State, string Sha256, string? WrittenPath,
    string? BackupPath, bool AlreadyApplied, string Notice);

/// <summary>Patch de modèle, jamais un script, un consentement ou une sélection personnelle de profil.</summary>
public static class CommonSelectionPatch
{
    public const int MaximumBytes = 1024 * 1024;
    public const string FileName = "FILPRO-modeles.json";
    private static SoftwareSelectionDocument Document(CommonSelectionState state) => new(SoftwareSelectionCodec.Schema,
        SoftwareSelectionCodec.Kind, DateTimeOffset.UnixEpoch, state.AppData, state.RegistryKeys, SoftwareSelectionCodec.DefaultPreferences);
    public static string Revision(CommonSelectionState state) => (state.HasSelection ? "1:" : "0:") + Hash(SoftwareSelectionCodec.Encode(Document(state)));
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Identity(SoftwareItem item) => item.Scope + ":" + item.Rel.ToUpperInvariant();

    public static byte[] Encode(CommonSelectionState state)
    {
        using var selection = JsonDocument.Parse(SoftwareSelectionCodec.Encode(Document(state)));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("application", "FILPRO");
            writer.WriteString("kind", "software-template-patch"); writer.WritePropertyName("selection");
            selection.RootElement.WriteTo(writer); writer.WriteEndObject();
        }
        var bytes = stream.ToArray(); if (bytes.Length > MaximumBytes) throw Invalid(); return bytes;
    }
    public static CommonSelectionState Decode(ReadOnlyMemory<byte> bytes, bool demonstration)
    {
        if (bytes.Length is < 1 or > MaximumBytes) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "application", "kind", "schemaVersion", "selection" })
                || root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("application").GetString() != "FILPRO"
                || root.GetProperty("kind").GetString() != "software-template-patch") throw Invalid();
            var selection = SoftwareSelectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.GetProperty("selection").GetRawText()));
            if (selection.Preferences != SoftwareSelectionCodec.DefaultPreferences) throw Invalid();
            return new(demonstration, true, selection.Files, selection.Registry);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException) { throw Invalid(); }
    }
    public static IReadOnlyList<CommonPatchChange> Describe(CommonSelectionState current, CommonSelectionState incoming)
    {
        var existing = current.AppData.Concat(current.RegistryKeys).ToDictionary(Identity, StringComparer.Ordinal);
        return incoming.AppData.Concat(incoming.RegistryKeys).Select(item =>
        {
            existing.TryGetValue(Identity(item), out var old);
            return new CommonPatchChange(Identity(item), item.Scope.ToString(), item.Rel, old?.Name, item.Name,
                old is null ? "add" : old.Name == item.Name && old.Category == item.Category ? "identical" : "conflict");
        }).ToArray();
    }
    public static CommonSelectionState Merge(CommonSelectionState current, CommonSelectionState incoming, bool replaceConflicts)
    {
        if (incoming.AppData.Count + incoming.RegistryKeys.Count == 0) return current;
        var merged = current.AppData.Concat(current.RegistryKeys).ToDictionary(Identity, StringComparer.Ordinal);
        foreach (var item in incoming.AppData.Concat(incoming.RegistryKeys))
            if (!merged.ContainsKey(Identity(item)) || replaceConflicts) merged[Identity(item)] = item;
        var result = new CommonSelectionState(current.Demonstration, true,
            merged.Values.Where(item => item.Scope != SoftwareScope.Hkcu).ToArray(), merged.Values.Where(item => item.Scope == SoftwareScope.Hkcu).ToArray());
        // Réutilise les limites et gardes de chemins du modèle réellement consommé par le moteur.
        return Decode(Encode(result), current.Demonstration);
    }
    private static FilproOperationException Invalid() => new("templates.patch", "Patch de modèles invalide, trop volumineux ou destiné à une autre application.");
}
