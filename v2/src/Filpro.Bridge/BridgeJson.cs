using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filpro.Bridge;

/// <summary>
/// Règles JSON du contrat : noms camelCase, énumérations en texte camelCase, dates UTC à la seconde avec « Z », dates en
/// yyyy-MM-dd, champs null écrits. Seul <see cref="BridgeResponse.Error"/> est omis quand il est nul.
/// </summary>
public static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new UtcSecondsConverter());
        options.Converters.Add(new IsoDateConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Instant en « 2026-10-23T16:14:00Z » : toujours UTC, tronqué à la seconde.</summary>
    private sealed class UtcSecondsConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
                ? value
                : throw new JsonException("Invalid date and time.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Jour calendaire local en « 2026-10-23 ».</summary>
    private sealed class IsoDateConverter : JsonConverter<DateOnly>
    {
        private const string Format = "yyyy'-'MM'-'dd";

        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateOnly.TryParseExact(reader.GetString(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value
                : throw new JsonException("Invalid date.");

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
        }
    }
}
