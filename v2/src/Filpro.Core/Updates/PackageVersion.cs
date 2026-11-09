using System.Globalization;
using System.Text.RegularExpressions;

namespace Filpro.Core;

public sealed partial record PackageVersion(int Major, int Minor, int Patch, string? Prerelease, string Text) : IComparable<PackageVersion>
{
    public static PackageVersion Parse(string value)
    {
        if (value is null || value.Length > 100) throw Invalid();
        var match = Format().Match(value);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out var patch)) throw Invalid();
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        if (prerelease is not null && prerelease.Split('.').Any(p => p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0')) throw Invalid();
        return new(major, minor, patch, prerelease, value);
    }
    public int CompareTo(PackageVersion? other)
    {
        if (other is null) return 1;
        foreach (var pair in new[] { (Major, other.Major), (Minor, other.Minor), (Patch, other.Patch) })
        { var result = pair.Item1.CompareTo(pair.Item2); if (result != 0) return result; }
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        var first = Prerelease.Split('.'); var second = other.Prerelease.Split('.');
        for (var i = 0; i < Math.Min(first.Length, second.Length); i++)
        {
            var numericFirst = first[i].All(char.IsAsciiDigit); var numericSecond = second[i].All(char.IsAsciiDigit);
            var result = numericFirst && numericSecond
                ? (first[i].Length == second[i].Length ? string.CompareOrdinal(first[i], second[i]) : first[i].Length.CompareTo(second[i].Length))
                : numericFirst != numericSecond ? (numericFirst ? -1 : 1) : string.CompareOrdinal(first[i], second[i]);
            if (result != 0) return result;
        }
        return first.Length.CompareTo(second.Length);
    }
    private static FilproOperationException Invalid() => new("update.version", "Version de pack FILPRO invalide (SemVer requis).");
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
