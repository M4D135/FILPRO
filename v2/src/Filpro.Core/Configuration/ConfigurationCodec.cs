using System.Text.Json;

namespace Filpro.Core;

public sealed record PortableConfiguration(SharedConfiguration Settings, IReadOnlyList<string> PresentFields);

/// <summary>Format portable fermé et borné. Les champs absents conservent leur valeur actuelle.</summary>
public static class ConfigurationCodec
{
    public const int MaximumBytes = 64 * 1024;
    public static readonly IReadOnlyList<string> Fields = Array.AsReadOnly(new[] { "theme", "motion", "navigationCollapsed",
        "autoSelectCurrentTask", "notifyOnCompletion", "recommendedSelection", "migApp", "vss", "continueOnError", "encrypted", "conflictPolicy" });
    public static SharedConfiguration Validate(SharedConfiguration settings)
    {
        if (settings is null || settings.Theme is not ("system" or "light" or "dark" or "contrast")
            || settings.Motion is not ("system" or "full" or "reduced")
            || settings.ConflictPolicy is not ("keepBoth" or "keepCurrent" or "useSaved")) throw Invalid();
        return settings;
    }

    public static byte[] Encode(SharedConfiguration settings)
    {
        Validate(settings);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("application", "FILPRO");
            writer.WriteString("kind", "shared-configuration"); writer.WritePropertyName("settings"); writer.WriteStartObject();
            writer.WriteString("theme", settings.Theme); writer.WriteString("motion", settings.Motion);
            writer.WriteBoolean("navigationCollapsed", settings.NavigationCollapsed);
            writer.WriteBoolean("autoSelectCurrentTask", settings.AutoSelectCurrentTask);
            writer.WriteBoolean("notifyOnCompletion", settings.NotifyOnCompletion);
            writer.WriteBoolean("recommendedSelection", settings.RecommendedSelection);
            writer.WriteBoolean("migApp", settings.MigApp); writer.WriteBoolean("vss", settings.Vss);
            writer.WriteBoolean("continueOnError", settings.ContinueOnError); writer.WriteBoolean("encrypted", settings.Encrypted);
            writer.WriteString("conflictPolicy", settings.ConflictPolicy); writer.WriteEndObject(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static PortableConfiguration Decode(ReadOnlyMemory<byte> utf8, SharedConfiguration? baseline = null)
    {
        if (utf8.Length is 0 or > MaximumBytes) throw Invalid();
        var settings = Validate(baseline ?? new SharedConfiguration());
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 5 });
            var root = document.RootElement;
            RequireFields(root, ["schemaVersion", "application", "kind", "settings"], required: true);
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("application").GetString() != "FILPRO"
                || root.GetProperty("kind").GetString() != "shared-configuration") throw Invalid();
            var values = root.GetProperty("settings");
            var present = RequireFields(values, Fields, required: false);
            foreach (var field in values.EnumerateObject())
            {
                settings = field.Name switch
                {
                    "theme" => settings with { Theme = field.Value.GetString()! },
                    "motion" => settings with { Motion = field.Value.GetString()! },
                    "navigationCollapsed" => settings with { NavigationCollapsed = field.Value.GetBoolean() },
                    "autoSelectCurrentTask" => settings with { AutoSelectCurrentTask = field.Value.GetBoolean() },
                    "notifyOnCompletion" => settings with { NotifyOnCompletion = field.Value.GetBoolean() },
                    "recommendedSelection" => settings with { RecommendedSelection = field.Value.GetBoolean() },
                    "migApp" => settings with { MigApp = field.Value.GetBoolean() },
                    "vss" => settings with { Vss = field.Value.GetBoolean() },
                    "continueOnError" => settings with { ContinueOnError = field.Value.GetBoolean() },
                    "encrypted" => settings with { Encrypted = field.Value.GetBoolean() },
                    "conflictPolicy" => settings with { ConflictPolicy = field.Value.GetString()! },
                    _ => throw Invalid(),
                };
            }
            return new PortableConfiguration(Validate(settings), present);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw Invalid(); }
    }

    public static IReadOnlyList<ConfigurationChange> Changes(SharedConfiguration before, PortableConfiguration after)
    {
        using var first = JsonDocument.Parse(Encode(before));
        using var second = JsonDocument.Parse(Encode(after.Settings));
        return Fields.Select(key => new ConfigurationChange(key,
            first.RootElement.GetProperty("settings").GetProperty(key).ToString(),
            second.RootElement.GetProperty("settings").GetProperty(key).ToString(),
            after.PresentFields.Contains(key, StringComparer.Ordinal) ? "replace" : "preserve")).ToArray();
    }

    internal static IReadOnlyList<string> RequireFields(JsonElement element, IReadOnlyList<string> allowed, bool required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
            if (!allowed.Contains(field.Name, StringComparer.Ordinal) || !seen.Add(field.Name)) throw Invalid();
        if (required && seen.Count != allowed.Count) throw Invalid();
        return seen.ToArray();
    }
    private static FilproOperationException Invalid() => new("configuration.format", "Configuration refusée : format FILPRO inconnu, champ interdit, en double ou invalide. Aucun réglage actuel n'a été remplacé.");
}
