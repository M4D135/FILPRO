using System.Globalization;
using System.Text.RegularExpressions;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Découverte du catalogue déclaré et du premier niveau générique ; aucune taille n'est mesurée ici.</summary>
public sealed record SoftwareDiscoveryResult(IReadOnlyList<SoftwareItem> Items, bool Complete, string? Error);

/// <summary>Bornes entre appels de lecture, pas un délai maximal garanti pour un appel système bloqué.</summary>
public sealed record SoftwareDiscoveryLimits(int MaximumGenericEntries = 1000, TimeSpan? MaximumGenericDuration = null)
{
    internal TimeSpan Duration => MaximumGenericDuration ?? TimeSpan.FromSeconds(4);

    internal void Validate()
    {
        if (MaximumGenericEntries is < 1 or > 5000 || Duration <= TimeSpan.Zero || Duration > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumGenericEntries), "Bornes de découverte invalides.");
        }
    }
}

internal static class SoftwareDiscoveryInputs
{
    private static readonly Regex SidPattern = new(@"\AS-1-(?:5-21-[0-9]+-[0-9]+-[0-9]+-[0-9]+|12-1-[0-9]+-[0-9]+-[0-9]+-[0-9]+)\z", RegexOptions.CultureInvariant);

    internal static void ValidateSid(string sid)
    {
        if (string.IsNullOrEmpty(sid) || sid.Length > 184 || !SidPattern.IsMatch(sid)
            || sid.Split('-').Skip(4).Any(part => !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || !part.Equals(value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
        {
            throw new FilproOperationException("selection.profile", "SID de profil invalide pour la découverte.");
        }
    }

    internal static IReadOnlyList<SoftwareItem> Normalize(IReadOnlyList<SoftwareItem> items, bool files)
    {
        ArgumentNullException.ThrowIfNull(items);
        var document = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(new SoftwareSelectionDocument(
            SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind, DateTimeOffset.UnixEpoch,
            files ? items : [], files ? [] : items, SoftwareSelectionCodec.DefaultPreferences)));
        return [.. (files ? document.Files : document.Registry).Select(item => item with
        {
            Selected = false, Recommend = false, Known = false, Size = null, SizeMeasured = false, SizeComplete = false,
        })];
    }

    internal static IReadOnlyList<SoftwareItem> Merge(IReadOnlyList<SoftwareItem> catalog, IReadOnlyList<SoftwareItem> extra)
    {
        var items = new Dictionary<string, SoftwareItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog) items.Add(item.Scope + "|" + item.Rel, item);
        foreach (var item in extra)
        {
            var key = item.Scope + "|" + item.Rel;
            if (!items.TryGetValue(key, out var known)) items.Add(key, item);
            else if (item.Category == SoftwareCategory.Modele) items[key] = known with { Category = SoftwareCategory.Modele };
        }
        return [.. items.Values];
    }

    internal static bool IsReadFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ArgumentException or NotSupportedException;

    internal static bool Overlaps(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + '\\', StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + '\\', StringComparison.OrdinalIgnoreCase);
}
