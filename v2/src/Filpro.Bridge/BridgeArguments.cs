using System.Text.Json;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Validation stricte des « args » d'une requête. Les échecs lèvent arguments.invalid avec une précision fixe ; la saisie
/// de la page n'est jamais recopiée dans le message.
/// </summary>
public static class BridgeArguments
{
    public const string NoneAccepted = "cette opération n’en accepte aucun.";
    public const string ObjectExpected = "un objet JSON est attendu.";
    public const string PropertyNotAllowed = "propriété non admise.";

    /// <summary>Opération sans arguments : « args » absent, null ou {}.</summary>
    public static void RequireNone(JsonElement arguments)
    {
        switch (arguments.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return;
            case JsonValueKind.Object:
                if (arguments.EnumerateObject().Any())
                {
                    throw Invalid(NoneAccepted);
                }
                return;
            default:
                throw Invalid(ObjectExpected);
        }
    }

    /// <summary>Objet avec uniquement les propriétés admises, chacune au plus une fois.</summary>
    public static IReadOnlyDictionary<string, JsonElement> ReadObject(JsonElement arguments, IReadOnlyCollection<string> allowedNames)
    {
        ArgumentNullException.ThrowIfNull(allowedNames);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(ObjectExpected);
        }
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name, StringComparer.Ordinal) || !values.TryAdd(property.Name, property.Value))
            {
                throw Invalid(PropertyNotAllowed);
            }
        }
        return values;
    }

    public static string GetString(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid(FrenchText.Quote(name) + " est obligatoire et doit être un texte.");
    }

    public static bool GetBoolean(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.TryGetValue(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }
        throw Invalid(FrenchText.Quote(name) + " est obligatoire et doit valoir true ou false.");
    }

    /// <summary>Texte optionnel : absent ou null donne null ; tout autre type est refusé.</summary>
    public static string? GetOptionalString(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw Invalid(FrenchText.Quote(name) + " doit être un texte ou null.");
    }

    /// <summary>Booléen optionnel : absent ou null donne la valeur par défaut ; tout autre type est refusé.</summary>
    public static bool GetOptionalBoolean(IReadOnlyDictionary<string, JsonElement> values, string name, bool defaultValue = false)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Invalid(FrenchText.Quote(name) + " doit valoir true, false ou être absent.");
    }

    /// <summary>
    /// Tableau obligatoire de textes non vides, borné en longueur. Une liste absente, hors tableau, vide, trop longue ou
    /// contenant un élément non textuel ou vide est refusée par arguments.invalid (la saisie n'est jamais recopiée).
    /// </summary>
    public static IReadOnlyList<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> values, string name, int max)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(FrenchText.Quote(name) + " est obligatoire et doit être un tableau de textes.");
        }
        return ReadStrings(value, name, max, requireAtLeastOne: true);
    }

    /// <summary>
    /// Tableau optionnel de textes non vides, borné en longueur : absent ou null donne <c>null</c>, un tableau vide est
    /// admis (une absence explicite n'est pas une valeur inventée). Tout autre type, élément vide ou dépassement de borne
    /// est refusé par arguments.invalid.
    /// </summary>
    public static IReadOnlyList<string>? GetOptionalStringArray(IReadOnlyDictionary<string, JsonElement> values, string name, int max)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(FrenchText.Quote(name) + " doit être un tableau de textes ou être absent.");
        }
        return ReadStrings(value, name, max, requireAtLeastOne: false);
    }

    /// <summary>
    /// Tableau optionnel d'objets fermés, borné en longueur : absent ou null donne une liste vide. Chaque élément doit être
    /// un objet ne contenant que les propriétés admises, chacune au plus une fois ; sinon arguments.invalid, sans recopier
    /// la saisie de la page.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> GetObjectArray(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int max,
        IReadOnlyCollection<string> allowedNames)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(allowedNames);
        if (!values.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(FrenchText.Quote(name) + " doit être un tableau d’objets ou être absent.");
        }
        if (max <= 0)
        {
            throw Invalid("la borne attendue est invalide.");
        }
        var result = new List<IReadOnlyDictionary<string, JsonElement>>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(FrenchText.Quote(name) + " ne doit contenir que des objets.");
            }
            var row = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!allowedNames.Contains(property.Name, StringComparer.Ordinal) || !row.TryAdd(property.Name, property.Value))
                {
                    throw Invalid(FrenchText.Quote(name) + " contient une propriété non admise.");
                }
            }
            result.Add(row);
            if (result.Count > max)
            {
                throw Invalid(FrenchText.Quote(name) + " dépasse le nombre d’éléments admis.");
            }
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement value, string name, int max, bool requireAtLeastOne)
    {
        if (max <= 0)
        {
            throw Invalid("la borne attendue est invalide.");
        }
        var result = new List<string>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            {
                throw Invalid(FrenchText.Quote(name) + " ne doit contenir que des textes non vides.");
            }
            result.Add(element.GetString()!);
            if (result.Count > max)
            {
                throw Invalid(FrenchText.Quote(name) + " dépasse le nombre d’éléments admis.");
            }
        }
        if (requireAtLeastOne && result.Count == 0)
        {
            throw Invalid(FrenchText.Quote(name) + " doit contenir au moins un élément.");
        }
        return result;
    }

    /// <summary>arguments.invalid avec une précision fixe (jamais la saisie de la page).</summary>
    public static BridgeOperationException Invalid(string precision) =>
        new(FilproErrors.ArgumentsInvalid, "Arguments invalides" + FrenchText.Colon + precision);
}
