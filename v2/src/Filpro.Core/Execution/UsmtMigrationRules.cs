using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Filpro.Core;

/// <summary>Règles à structure fermée, portées distinctes comme FILPRO.Usmt.ps1 / FILPRO.RestorePolicy.ps1 V1.</summary>
public static class UsmtMigrationRules
{
    private const string SourcePriority = "MigXmlHelper.SourcePriority()";
    private const string DestinationPriority = "MigXmlHelper.DestinationPriority()";
    private const string Coexistence = "MigXmlHelper.FindFilePlaceByPattern(\"<F> (<N>).<E>\")";

    public static byte[] Capture(BackupManifestProfile profile) => Build(profile, null);
    public static byte[] Restore(BackupManifestProfile profile, IReadOnlyList<RestorePolicy> policies) => Build(profile, policies);

    private static byte[] Build(BackupManifestProfile profile, IReadOnlyList<RestorePolicy>? policies)
    {
        _ = SoftwareSelectionCodec.Encode(new(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind,
            DateTimeOffset.UnixEpoch, profile.Selection.Files, profile.Selection.Registry, SoftwareSelectionCodec.DefaultPreferences));
        var decisions = policies?.ToDictionary(p => p.Id, p => p.Mode, StringComparer.Ordinal);
        var migration = new XElement("migration", new XAttribute("urlid", "http://www.microsoft.com/migration/1.0/migxmlext/filpro-v2"));
        // Aucun GenerateDocPatterns système : seuls le profil et les racines explicitement inspectées sont parcourus.
        Add("File", "%USERPROFILE%", ["%USERPROFILE% [*]"], null, personal: true);
        foreach (var folder in (profile.PersonalFolders ?? []).OrderBy(s => s, StringComparer.Ordinal))
        {
            RequirePattern(folder);
            var relative = BackupManifestCodec.RequireRelative(folder).Replace('/', '\\');
            Add("File", "%USERPROFILE%\\" + relative,
                ["%USERPROFILE%\\" + relative + " [*]", "%USERPROFILE%\\" + relative + "\\* [*]"], null, personal: true);
        }
        foreach (var file in profile.Selection.Files)
        {
            RequirePattern(file.Rel);
            var basis = file.Scope == SoftwareScope.Local ? @"%USERPROFILE%\AppData\Local\" : @"%USERPROFILE%\AppData\Roaming\";
            var scope = basis + file.Rel;
            Add("File", scope, [scope + " [*]", scope + "\\* [*]"], SoftwareCatalog.ResolveIdentity(file).Id, personal: false);
        }
        foreach (var key in profile.Selection.Registry)
        {
            RequirePattern(key.Rel);
            var scope = "HKCU\\" + key.Rel;
            Add("Registry", scope, [scope + " [*]", scope + "\\* [*]"], SoftwareCatalog.ResolveIdentity(key).Id, personal: false);
        }
        return Write(new XDocument(migration));

        void Add(string type, string scope, IReadOnlyList<string> patterns, string? groupId, bool personal)
        {
            var identity = type + "|" + scope.ToUpperInvariant();
            var rules = new XElement("rules", ObjectRule("include", null, type, patterns));
            var helpers = new List<string> { SourcePriority, DestinationPriority };
            if (type == "File") helpers.Add(Coexistence);
            if (policies is not null)
            {
                var mode = personal ? RestorePolicyMode.KeepBoth
                    : decisions!.TryGetValue(groupId!, out var selected) ? selected
                    : throw new FilproOperationException("restore.conflict", "Décision absente pour une portée du magasin.");
                var helper = mode switch
                {
                    RestorePolicyMode.UseSaved => SourcePriority,
                    RestorePolicyMode.KeepCurrent => DestinationPriority,
                    RestorePolicyMode.KeepBoth => type == "File" ? Coexistence : DestinationPriority,
                    _ => throw new FilproOperationException("restore.conflict", "Politique inconnue."),
                };
                helpers = [helper];
            }
            foreach (var helper in helpers) rules.Add(ObjectRule("merge", helper, type, patterns));
            if (type == "File")
            {
                var excludes = new List<string> { "%USERPROFILE% [NTUSER.*]", "%USERPROFILE% [ntuser*]" };
                foreach (var excluded in profile.ExcludedPaths ?? [])
                {
                    var path = @"%USERPROFILE%\" + RequireExcludedRelative(excluded).Replace('/', '\\');
                    // L'exclusion couvre fichier ou dossier ; aucune expansion d'une chaîne fournie au navigateur.
                    var parent = path[..path.LastIndexOf('\\')];
                    var name = path[(path.LastIndexOf('\\') + 1)..];
                    excludes.Add(parent + " [" + name + "]");
                    excludes.Add(path + " [*]"); excludes.Add(path + "\\* [*]");
                }
                rules.Add(ObjectRule("exclude", null, "File", excludes));
            }
            migration.Add(new XElement("component", new XAttribute("type", "Documents"), new XAttribute("context", "User"),
                new XElement("displayName", "FILPRO - " + BackupManifestCodec.Hash(Encoding.UTF8.GetBytes(identity))),
                new XElement("role", new XAttribute("role", "Data"), rules)));
        }
    }

    public static byte[] DisableWindowsComponents(ReadOnlyMemory<byte> generated, ReadOnlyMemory<byte> stock)
    {
        var document = Read(generated);
        var extra = Read(stock);
        var root = document.Root;
        if (root?.Name != "Configuration") throw Invalid("Configuration USMT inattendue.");
        var windows = root.Elements("WindowsComponents").SingleOrDefault() ?? throw Invalid("Composants Windows absents.");
        var ids = windows.Descendants("component").Select(c => (string?)c.Attribute("ID") ?? (string?)c.Attribute("id"))
            .Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        foreach (var component in extra.Root?.Element("WindowsComponents")?.Descendants("component") ?? [])
        {
            var id = (string?)component.Attribute("ID") ?? (string?)component.Attribute("id");
            if (id is not null && ids.Add(id)) windows.Add(new XElement(component));
        }
        var components = windows.Descendants("component").ToArray();
        if (components.Length == 0) throw Invalid("La liste des composants Windows est vide.");
        foreach (var component in components) component.SetAttributeValue("migrate", "no");
        if (root.Element("Documents")?.Descendants("component").Any() != true)
            throw Invalid("Les portées de documents sont absentes de la configuration USMT.");
        return UsmtProfileConfiguration.Restrict(Write(document), requireWindowsAlreadyExcluded: true);
    }

    public static (long Store, long Temporary) ReadEstimate(ReadOnlyMemory<byte> bytes) => ReadEstimate(bytes, 0);

    /// <summary>
    /// USMT compressé émet une seule taille. Un magasin non compressé en émet une par taille de cluster :
    /// seule celle du volume de destination est retenue. Deux entrées du même cluster doivent porter la même taille.
    /// </summary>
    public static (long Store, long Temporary) ReadEstimate(ReadOnlyMemory<byte> bytes, int clusterBytes)
    {
        var document = Read(bytes);
        long ReadSize(string name, bool matchCluster)
        {
            var matches = document.Descendants().Where(e => e.Name.LocalName == name).ToArray();
            if (matches.Length != 1) throw Invalid("Estimation USMT absente ou ambiguë.");
            var values = matches[0].Elements().Where(e => e.Name.LocalName == "size").ToArray();
            if (values.Length == 0) throw Invalid("Estimation USMT illisible.");
            if (values.Length > 1)
            {
                if (!matchCluster || clusterBytes <= 0) throw Invalid("Estimation USMT illisible.");
                var marker = clusterBytes.ToString(CultureInfo.InvariantCulture);
                values = values.Where(element => string.Equals((string?)element.Attribute("clusterSize"), marker, StringComparison.Ordinal)).ToArray();
                if (values.Length == 0) throw Invalid("Estimation USMT illisible.");
            }
            long? parsed = null;
            foreach (var element in values)
            {
                if (!long.TryParse(element.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
                    throw Invalid("Estimation USMT illisible.");
                if (parsed is { } previous && previous != value) throw Invalid("Estimation USMT illisible.");
                parsed = value;
            }
            return parsed!.Value;
        }
        return (ReadSize("storeSize", true), ReadSize("temporarySpace", false));
    }
    public static XDocument Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > BackupManifestCodec.MaximumBytes) throw Invalid("Règle XML trop grande ou vide.");
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = BackupManifestCodec.MaximumBytes });
        return XDocument.Load(reader, LoadOptions.None);
    }
    public static string RequireExcludedRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.StartsWith('/') || path.StartsWith('\\'))
            throw Invalid("Exclusion relative invalide.");
        var normalized = path.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
            if (segment.Length is 0 or > 255 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.Any(c => c < 32 || "<>:\"|?*[]%".Contains(c))) throw Invalid("Exclusion non représentable.");
        return normalized;
    }
    private static void RequirePattern(string path)
    {
        if (path.IndexOfAny(['[', ']', '%', '*', '?']) >= 0) throw Invalid("Une portée ne peut pas contenir d'expansion USMT.");
    }
    private static XElement ObjectRule(string name, string? script, string type, IEnumerable<string> patterns)
        => new(name, script is null ? null : new XAttribute("script", script),
            new XElement("objectSet", patterns.Select(p => new XElement("pattern", new XAttribute("type", type), p))));
    private static byte[] Write(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return stream.ToArray();
    }
    private static FilproOperationException Invalid(string message) => new("usmt.rules", message);
}
