using System.Globalization;
using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>
/// Logique pure de la sélection groupée des logiciels (fichiers + registre), reprise des fonctions « pures » de la V1
/// (Get-FilproSoftwareBindings, Get-FilproSoftwareIdentity, Get-FilproSoftwareGroups, Set-FilproRecommendedSoftwareSelection,
/// Get-FilproSoftwareSizeSummary). Aucune lecture de fichier, de registre ou de preuve : les éléments sont fournis par le
/// fournisseur. Les tailles ne sont jamais inventées : sans mesure, l'état reste « Inconnue ».
/// </summary>
public static class SoftwareCatalog
{
    /// <summary>Associations connues explicites (aucun rapprochement par sous-chaîne d'éditeur), portées de la V1.</summary>
    public sealed record Binding(string Id, string Name, string[] Files, string[] Registry);

    public static IReadOnlyList<Binding> Bindings { get; } =
    [
        new("office", "Microsoft Office / Outlook", ["Roaming|Microsoft\\Signatures", "Roaming|Microsoft\\Templates", "Roaming|Microsoft\\Spelling", "Roaming|Microsoft\\Word", "Roaming|Microsoft\\Excel", "Roaming|Microsoft\\Outlook"], ["Software\\Microsoft\\Office"]),
        new("chrome", "Google Chrome", ["Local|Google\\Chrome\\User Data"], ["Software\\Google\\Chrome"]),
        new("edge", "Microsoft Edge", ["Local|Microsoft\\Edge\\User Data"], ["Software\\Microsoft\\Edge"]),
        new("firefox", "Firefox", ["Roaming|Mozilla\\Firefox"], ["Software\\Mozilla\\Mozilla Firefox", "Software\\Mozilla\\Firefox"]),
        new("thunderbird", "Thunderbird", ["Roaming|Thunderbird"], ["Software\\Mozilla\\Thunderbird"]),
        new("brave", "Brave", ["Local|BraveSoftware\\Brave-Browser\\User Data"], ["Software\\BraveSoftware\\Brave-Browser"]),
        new("notepadpp", "Notepad++", ["Roaming|Notepad++"], ["Software\\Notepad++"]),
        new("vlc", "VLC", ["Roaming|vlc"], ["Software\\VideoLAN\\VLC"]),
        new("filezilla", "FileZilla", ["Roaming|FileZilla"], ["Software\\FileZilla"]),
        new("sublime", "Sublime Text", ["Roaming|Sublime Text 3", "Roaming|Sublime Text"], ["Software\\Sublime Text 3", "Software\\Sublime Text"]),
        new("zoom", "Zoom", ["Roaming|Zoom"], ["Software\\Zoom"]),
        new("sap", "SAP GUI", ["Roaming|SAP\\SAP GUI"], ["Software\\SAP\\SAPGUI Front", "Software\\SAP\\SAPLogon"]),
        new("citrix", "Citrix Workspace", ["Roaming|Citrix\\ICA Client"], ["Software\\Citrix\\ICA Client"]),
    ];

    /// <summary>Classe un dossier AppData par son nom (caches, préférences, sinon données).</summary>
    public static SoftwareCategory Classify(string rel)
    {
        var normalized = (rel ?? string.Empty).Replace('/', '\\');
        if (Regex.IsMatch(normalized, @"(^|\\)(Cache|Temp|Temporary|Logs|CrashDumps|WebCache|GPUCache|Code Cache|Service Worker)(\\|$)", RegexOptions.IgnoreCase))
        {
            return SoftwareCategory.Caches;
        }
        if (Regex.IsMatch(normalized, @"(^|\\)(Settings|Preferences|Config|Configs|Options)(\\|$)", RegexOptions.IgnoreCase))
        {
            return SoftwareCategory.Preferences;
        }
        return SoftwareCategory.Donnees;
    }

    private static string BaseOf(SoftwareScope scope) => scope switch
    {
        SoftwareScope.Local => "Local",
        SoftwareScope.Roaming => "Roaming",
        _ => "HKCU",
    };

    private static bool IsRegistry(SoftwareItem item) => item.Scope == SoftwareScope.Hkcu;

    /// <summary>Préfixe des groupes reliés au catalogue de FILPRO ; tout autre groupe porte le nom déclaré par sa sélection.</summary>
    public const string KnownPrefix = "known:";

    /// <summary>Vrai pour un groupe du catalogue de FILPRO, dont le nom vient du catalogue et non du magasin ou du poste.</summary>
    public static bool IsCatalogued(string id) => id is not null && id.StartsWith(KnownPrefix, StringComparison.Ordinal);

    /// <summary>Identité d'un élément : logiciel connu (préfixe exact d'une portée déclarée) ou périmètre propre non lié.</summary>
    public static (string Id, string Name, bool Linked) ResolveIdentity(SoftwareItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var kind = IsRegistry(item) ? "Registry" : "Files";
        var path = IsRegistry(item) ? item.Rel : BaseOf(item.Scope) + "|" + item.Rel;
        foreach (var binding in Bindings)
        {
            var roots = IsRegistry(item) ? binding.Registry : binding.Files;
            foreach (var root in roots)
            {
                if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase))
                {
                    return (KnownPrefix + binding.Id, binding.Name, true);
                }
            }
        }
        return ((kind + "|" + path).ToLowerInvariant(), item.Name, false);
    }

    /// <summary>Regroupe fichiers et clés par logiciel, calcule la sélection tri-état, le résumé et la taille des fichiers.</summary>
    public static IReadOnlyList<SoftwareGroup> Group(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(registry);
        var map = new Dictionary<string, (string Name, bool Linked, List<SoftwareItem> Files, List<SoftwareItem> Registry)>(StringComparer.Ordinal);
        foreach (var item in files)
        {
            if (item is null)
            {
                continue;
            }
            var (id, name, linked) = ResolveIdentity(item);
            Add(map, id, name, linked, item, file: true);
        }
        foreach (var item in registry)
        {
            if (item is null)
            {
                continue;
            }
            var (id, name, linked) = ResolveIdentity(item);
            Add(map, id, name, linked, item, file: false);
        }

        var groups = new List<SoftwareGroup>(map.Count);
        foreach (var (id, entry) in map)
        {
            groups.Add(BuildGroup(id, entry.Name, entry.Linked, entry.Files, entry.Registry));
        }
        return [.. groups.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Id, StringComparer.Ordinal)];
    }

    private static void Add(
        Dictionary<string, (string Name, bool Linked, List<SoftwareItem> Files, List<SoftwareItem> Registry)> map,
        string id, string name, bool linked, SoftwareItem item, bool file)
    {
        if (!map.TryGetValue(id, out var entry))
        {
            entry = (name, linked, new List<SoftwareItem>(), new List<SoftwareItem>());
            map[id] = entry;
        }
        (file ? entry.Files : entry.Registry).Add(item);
    }

    private static SoftwareGroup BuildGroup(string id, string name, bool linked, IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry)
    {
        var members = files.Count + registry.Count;
        var selectedCount = files.Count(f => f.Selected) + registry.Count(r => r.Selected);
        bool? selected = selectedCount == 0 ? false : selectedCount == members ? true : null;
        var summary = $"{files.Count} dossier(s) · {registry.Count} clé(s) · {selectedCount}/{members} retenus";
        return new SoftwareGroup(id, name, linked, files, registry, selected, selectedCount, summary, SummarizeSizes(files));
    }

    /// <summary>
    /// Applique les recommandations : fichiers et clés d'un même logiciel sont cochés ensemble si l'un de leurs membres est
    /// recommandé. Une liste déjà lue (non fraîche) garde ses choix ; les nouveaux membres suivent le choix conservé.
    /// </summary>
    public static IReadOnlyList<SoftwareGroup> ApplyRecommendations(
        IReadOnlyList<SoftwareGroup> groups, bool freshFiles = true, bool freshRegistry = true)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (!freshFiles && !freshRegistry)
        {
            return groups;
        }
        var result = new List<SoftwareGroup>(groups.Count);
        foreach (var group in groups)
        {
            var kept = new List<SoftwareItem>();
            if (!freshFiles)
            {
                kept.AddRange(group.Files);
            }
            if (!freshRegistry)
            {
                kept.AddRange(group.Registry);
            }
            var members = group.Files.Concat(group.Registry).ToList();
            var on = kept.Count > 0
                ? kept.Any(i => i.Selected)
                : members.Any(m => m.Recommend);
            IReadOnlyList<SoftwareItem> newFiles = freshFiles ? [.. group.Files.Select(f => f with { Selected = on })] : group.Files;
            IReadOnlyList<SoftwareItem> newRegistry = freshRegistry ? [.. group.Registry.Select(r => r with { Selected = on })] : group.Registry;
            result.Add(BuildGroup(group.Id, group.Name, group.Linked, newFiles, newRegistry));
        }
        return result;
    }

    /// <summary>Mesure groupée des fichiers (jamais des clés HKCU) : doublons, chevauchements et mesures contradictoires honnêtes.</summary>
    public static SoftwareSizeSummary SummarizeSizes(IReadOnlyList<SoftwareItem> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var paths = new Dictionary<string, (long? Bytes, bool Measured, bool Complete)>(StringComparer.Ordinal);
        var invalid = 0;
        var duplicates = 0;
        foreach (var item in files)
        {
            if (item is null)
            {
                continue;
            }
            var rel = (item.Rel ?? string.Empty).Replace('/', '\\').TrimEnd('\\');
            var bad = (item.Scope != SoftwareScope.Local && item.Scope != SoftwareScope.Roaming)
                || rel.Length == 0 || rel.StartsWith('\\')
                || Regex.IsMatch(rel, @"(^|\\)\.\.?($|\\)|[\x00-\x1f:*?""<>|]");
            if (bad)
            {
                invalid++;
                continue;
            }
            var key = BaseOf(item.Scope) + "|" + rel;
            var measured = item.SizeMeasured && item.Size.HasValue && item.Size.Value >= 0;
            var complete = measured && item.SizeComplete;
            if (paths.TryGetValue(key, out var old))
            {
                duplicates++;
                if (old.Measured != measured || (measured && old.Bytes != item.Size))
                {
                    old = (null, false, false);
                }
                else if (!complete)
                {
                    old = (old.Bytes, old.Measured, false);
                }
                paths[key] = old;
            }
            else
            {
                paths[key] = (item.Size, measured, complete);
            }
        }

        var covered = new List<string>();
        long total = 0;
        var measuredCount = 0;
        var overlaps = 0;
        var completeAll = invalid == 0;
        var unknown = invalid;
        foreach (var entry in paths.OrderBy(e => e.Key.Length).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            if (covered.Any(parent => entry.Key.StartsWith(parent + '\\', StringComparison.OrdinalIgnoreCase)))
            {
                overlaps++;
                continue;
            }
            if (!entry.Value.Measured)
            {
                completeAll = false;
                unknown++;
                continue;
            }
            var bytes = entry.Value.Bytes!.Value;
            if (bytes > long.MaxValue - total)
            {
                completeAll = false;
                unknown++;
                continue;
            }
            total += bytes;
            measuredCount++;
            covered.Add(entry.Key);
            if (!entry.Value.Complete)
            {
                completeAll = false;
            }
        }

        var anyMeasured = measuredCount > 0;
        var count = paths.Count + invalid;
        if (count == 0)
        {
            return new SoftwareSizeSummary(SoftwareSizeState.Aucun, null, false, false, "Aucun dossier", unknown, overlaps, duplicates, Notice);
        }
        if (!anyMeasured)
        {
            return new SoftwareSizeSummary(SoftwareSizeState.Inconnue, null, false, false, "Taille inconnue (non mesurée)", unknown, overlaps, duplicates, Notice);
        }
        var value = (double)total;
        var unit = 0;
        string[] units = ["octets", "Kio", "Mio", "Gio", "Tio"];
        while (value >= 1024 && unit < 4)
        {
            value /= 1024;
            unit++;
        }
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var text = value.ToString(unit == 0 ? "N0" : "N1", fr) + " " + units[unit];
        return completeAll
            ? new SoftwareSizeSummary(SoftwareSizeState.Mesuree, total, true, true, text + " mesurés", unknown, overlaps, duplicates, Notice)
            : new SoftwareSizeSummary(SoftwareSizeState.Partielle, total, true, false, "au moins " + text + " (partiel)", unknown, overlaps, duplicates, Notice);
    }

    private const string Notice =
        "Fichiers uniquement ; taille des clés HKCU non mesurée. Dossiers imbriqués comptés une seule fois par base AppData. " +
        "Mesures indicatives, hors caches exclus et sans instantané.";
}
