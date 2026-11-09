using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>Politique commune pure. Elle ne prouve ni permissions, ni absence de liens sur le disque.</summary>
public static partial class StoragePathPolicy
{
    public static string NormalizeWindows(string value, bool allowRoot = false)
    {
        RequireText(value);
        var path = value.Replace('/', '\\');
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal)) throw Invalid();
        string prefix;
        string rest;
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            prefix = char.ToUpperInvariant(path[0]) + @":\";
            rest = path[3..];
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split('\\');
            if (parts.Length < 2 || !HostName().IsMatch(parts[0]) || parts[0].Contains('@') || parts[0].EndsWith('.')
                || parts[1].Equals("DavWWWRoot", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("d.docs.live.net", StringComparison.OrdinalIgnoreCase)
                || parts[0].EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
                || parts[0].EndsWith(".onedrive.com", StringComparison.OrdinalIgnoreCase)
                || parts[0].EndsWith(".file.core.windows.net", StringComparison.OrdinalIgnoreCase)
                || parts[0].EndsWith(".file.core.usgovcloudapi.net", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            RequireSegment(parts[1]);
            prefix = @"\\" + parts[0] + "\\" + parts[1] + "\\";
            rest = string.Join('\\', parts.Skip(2));
        }
        else throw Invalid();
        rest = rest.TrimEnd('\\');
        if (rest.Length == 0)
        {
            if (!allowRoot) throw Invalid();
            return prefix;
        }
        foreach (var part in rest.Split('\\')) RequireSegment(part);
        var normalized = prefix + rest;
        if (normalized.Length > 259) throw Invalid();
        return normalized;
    }

    /// <summary>Utilisé par les adaptateurs fichiers. Hors Windows : uniquement des chemins absolus natifs.</summary>
    public static string NormalizeNative(string value, bool allowRoot = false)
    {
        if (OperatingSystem.IsWindows()) return NormalizeWindows(value, allowRoot);
        RequireText(value);
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\')
            || value.Contains(':') || value.Length > 4096) throw Invalid();
        var parts = value.TrimEnd('/').Split('/').Skip(1);
        foreach (var part in parts) RequireSegment(part);
        var normalized = Path.GetFullPath(value).TrimEnd('/');
        if (normalized.Length == 0)
        {
            if (!allowRoot) throw Invalid();
            return "/";
        }
        return normalized;
    }

    public static bool IsWithin(string candidate, string root, bool allowRoot = true)
    {
        var windows = (root.Length > 2 && root[1] == ':') || root.StartsWith(@"\\", StringComparison.Ordinal);
        var separator = windows ? '\\' : Path.DirectorySeparatorChar;
        var comparison = windows || OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var first = windows ? NormalizeWindows(candidate, true) : NormalizeNative(candidate, true);
        var second = (windows ? NormalizeWindows(root, true) : NormalizeNative(root, true)).TrimEnd(separator);
        return (allowRoot && first.TrimEnd(separator).Equals(second, comparison)) || first.StartsWith(second + separator, comparison);
    }

    public static void RequireSegment(string value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or ".." || value.Length > 255
            || value.EndsWith(' ') || value.EndsWith('.') || value.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
            || ReservedName().IsMatch(value.Split('.')[0]) || IsCloudSegment(value)) throw Invalid();
    }

    private static bool IsCloudSegment(string value) => value.Equals("OneDrive", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Dropbox", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Google Drive", StringComparison.OrdinalIgnoreCase)
        || value.Equals("GoogleDrive", StringComparison.OrdinalIgnoreCase)
        || value.Equals("iCloudDrive", StringComparison.OrdinalIgnoreCase)
        || value.Equals("iCloud Drive", StringComparison.OrdinalIgnoreCase);

    private static void RequireText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(c => c < 32)) throw Invalid();
    }
    private static FilproOperationException Invalid() => new("storage.path", "Chemin refusé : choisir un dossier absolu local, USB ou UNC, sans cloud, lien spécial ni traversée.");
    [GeneratedRegex("^(CON|PRN|AUX|NUL|CONIN\\$|CONOUT\\$|CLOCK\\$|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$", RegexOptions.CultureInvariant)]
    private static partial Regex HostName();
}
