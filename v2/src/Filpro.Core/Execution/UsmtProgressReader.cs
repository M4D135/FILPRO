using System.Globalization;
using System.Text.RegularExpressions;

namespace Filpro.Core;

public sealed record UsmtProgressSnapshot(string? Phase, int? Percent, int? MinutesRemaining, int? IgnoredErrors);

/// <summary>Champs réellement publiés dans /progress (V1 Get-FilproNativeProgress). Une nouvelle phase efface les anciennes mesures.</summary>
public static partial class UsmtProgressReader
{
    public static UsmtProgressSnapshot Parse(string tail)
    {
        if (tail.Length > 65536) throw new ArgumentOutOfRangeException(nameof(tail));
        string? phase = null; int? percent = null, minutes = null, ignored = null;
        foreach (var line in tail.Split('\n'))
        {
            var phaseMatch = PhasePattern().Match(line);
            if (phaseMatch.Success) { phase = phaseMatch.Groups[1].Value; percent = null; minutes = null; }
            foreach (Match match in NumberPattern().Matches(line))
            {
                if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) continue;
                switch (match.Groups[1].Value)
                {
                    case "totalPercentageCompleted" when value <= 100: percent = value; break;
                    case "totalMinutesRemaining" when value <= 10080: minutes = value; break;
                    case "numberOfIgnoredErrors": ignored = value; break;
                }
            }
        }
        return new(phase, percent, minutes, ignored);
    }
    [GeneratedRegex("(?:^|,)\\s*PHASE\\s*[,=:]\\s*(Initializing|Scanning|Collecting|Saving|Estimating|Applying)(?:,|\\s*$)", RegexOptions.CultureInvariant)]
    private static partial Regex PhasePattern();
    [GeneratedRegex("(?:^|,)\\s*(totalPercentageCompleted|totalMinutesRemaining|numberOfIgnoredErrors)\\s*[,=:]\\s*\"?(\\d+)\"?(?:,|\\s*$)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
