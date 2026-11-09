using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Filpro.Core;

public sealed record LegacySoftwareSelection(IReadOnlyList<SoftwareItem> Files, IReadOnlyList<SoftwareItem> Registry);

/// <summary>Portage déterministe des règles V1 FILPRO.Usmt.ps1/RestorePolicy/Safeguards, pas un interpréteur XML libre.</summary>
public static class LegacyUsmtRules
{
    private const string Prefix = "http://www.microsoft.com/migration/1.0/migxmlext/";
    private const string Source = "MigXmlHelper.SourcePriority()";
    private const string Destination = "MigXmlHelper.DestinationPriority()";
    private const string Both = "MigXmlHelper.FindFilePlaceByPattern(\"<F> (<N>).<E>\")";

    public static LegacySoftwareSelection ReadSelection(ReadOnlyMemory<byte> bytes)
    {
        var input = ReadCanonical(bytes);
        var files = new List<SoftwareItem>(); var registry = new List<SoftwareItem>();
        foreach (var component in input.Root?.Elements("component") ?? [])
        {
            var patterns = component.Elements("role").Elements("rules").Elements("include").Elements("objectSet").Elements("pattern").ToArray();
            if (patterns.Length != 2) throw Invalid("paire de motifs absente");
            var kind = (string?)patterns[0].Attribute("type");
            var first = patterns[0].Value;
            if (!first.EndsWith(" [*]", StringComparison.Ordinal) || patterns[1].Value != first[..^4] + "\\* [*]"
                || (string?)patterns[1].Attribute("type") != kind) throw Invalid("paire de motifs incohérente");
            var scope = first[..^4];
            SoftwareScope type;
            string relative;
            const string localPrefix = @"%CSIDL_LOCAL_APPDATA%\";
            const string roamingPrefix = @"%CSIDL_APPDATA%\";
            if (kind == "File" && scope.StartsWith(localPrefix, StringComparison.Ordinal))
            { type = SoftwareScope.Local; relative = scope[localPrefix.Length..]; }
            else if (kind == "File" && scope.StartsWith(roamingPrefix, StringComparison.Ordinal))
            { type = SoftwareScope.Roaming; relative = scope[roamingPrefix.Length..]; }
            else if (kind == "Registry" && scope.StartsWith(@"HKCU\", StringComparison.Ordinal))
            { type = SoftwareScope.Hkcu; relative = scope[5..]; }
            else throw Invalid("portée non reconnue");
            if (relative.IndexOfAny(['%', '*', '?', '[', ']']) >= 0) throw Invalid("chemin non littéral");
            var item = new SoftwareItem(type, relative, relative, type == SoftwareScope.Hkcu ? SoftwareCategory.Preferences : SoftwareCatalog.Classify(relative),
                true, false, false, null, false, false);
            if (type == SoftwareScope.Hkcu) registry.Add(item); else files.Add(item);
        }
        var selection = new LegacySoftwareSelection(files, registry);
        if (files.Count + registry.Count > 500) throw Invalid();
        _ = SoftwareSelectionCodec.Encode(new(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind, DateTimeOffset.UnixEpoch,
            files, registry, SoftwareSelectionCodec.DefaultPreferences));
        if (!XNode.DeepEquals(input.Root, Build(selection, null).Root)) throw Invalid("structure fermée différente");
        return selection;
    }

    public static byte[] Restore(ReadOnlyMemory<byte> bytes, IReadOnlyList<RestorePolicy> policies)
    {
        var selection = ReadSelection(bytes);
        var groups = SoftwareCatalog.Group(selection.Files, selection.Registry);
        var decisions = RestoreConflictPolicies.Resolve(groups, policies);
        return Write(Build(selection, decisions));
    }
    public static byte[] Capture(LegacySoftwareSelection selection) => Write(Build(selection, null));

    public static void RequireProfileMigDocs(ReadOnlyMemory<byte> bytes, ReadOnlyMemory<byte> trustedMigDocs)
    {
        var expected = ReadCanonical(trustedMigDocs);
        var components = expected.Root?.Elements("component").ToArray() ?? [];
        if (components.Length != 2) throw Invalid();
        var system = components.SingleOrDefault(c => (string?)c.Attribute("context") == "System" && (string?)c.Element("displayName") == "MigDocSystem");
        var user = components.SingleOrDefault(c => (string?)c.Attribute("context") == "User" && (string?)c.Element("displayName") == "MigDocUser");
        if (system is null || user is null) throw Invalid();
        system.Remove();
        if (!XNode.DeepEquals(expected.Root, ReadCanonical(bytes).Root)) throw Invalid();
    }

    public static void RequireStoreExclusion(ReadOnlyMemory<byte> bytes)
    {
        var actual = ReadCanonical(bytes);
        var patterns = actual.Descendants("pattern").ToArray();
        if (patterns.Length is < 4 or > 2000) throw Invalid();
        const string picturePrefix = @"%CSIDL_COMMON_APPDATA%\Microsoft\User Account Pictures [";
        var pictures = new List<string>();
        foreach (var pattern in patterns.Skip(2).SkipLast(2))
        {
            if ((string?)pattern.Attribute("type") != "File" || !pattern.Value.StartsWith(picturePrefix, StringComparison.Ordinal)
                || !pattern.Value.EndsWith(']')) throw Invalid();
            var name = pattern.Value[picturePrefix.Length..^1];
            StoragePathPolicy.RequireSegment(name);
            if (name.IndexOfAny(['[', ']', '%', '^', '\\', '/']) >= 0) throw Invalid();
            pictures.Add(name);
        }
        var objects = new XElement("objectSet", new XElement("script", "MigXmlHelper.GenerateDrivePatterns (\"* [USMT.MIG]\", \"Fixed\")"),
            Pattern("File", @"%CSIDL_COMMON_APPDATA%\Microsoft\Windows Defender\* [*]"), Pattern("File", @"%CSIDL_WINDOWS%\* [*]"),
            pictures.Select(name => Pattern("File", picturePrefix + name + "]")),
            Pattern("Registry", @"HKLM\SOFTWARE\Microsoft\Windows Defender [*]"), Pattern("Registry", @"HKLM\SOFTWARE\Microsoft\Windows Defender\* [*]"));
        var expected = Exclusion("filpro-store-exclusion", "FILPRO - exclusion des magasins USMT, du dossier Windows et des donnees internes de Windows Defender", objects);
        if (!XNode.DeepEquals(expected.Root, actual.Root)) throw Invalid();
    }

    public static void RequireKeyExclusion(ReadOnlyMemory<byte> bytes)
    {
        var actual = ReadCanonical(bytes);
        var patterns = actual.Descendants("pattern").ToArray();
        if (patterns.Length != 1 || (string?)patterns[0].Attribute("type") != "File") throw Invalid();
        var pattern = patterns[0].Value;
        var split = pattern.LastIndexOf(" [", StringComparison.Ordinal);
        if (split < 3 || !pattern.EndsWith(']') || pattern.IndexOfAny(['%', '^', '*', '?']) >= 0) throw Invalid();
        var path = pattern[..split] + "\\" + pattern[(split + 2)..^1];
        _ = StoragePathPolicy.NormalizeWindows(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOfAny(['[', ']']) >= 0) throw Invalid();
        var expected = Exclusion("filpro-key-exclusion", "FILPRO - exclusion de la cle", new XElement("objectSet", Pattern("File", pattern)));
        if (!XNode.DeepEquals(expected.Root, actual.Root)) throw Invalid();
    }

    private static XDocument Build(LegacySoftwareSelection selection, IReadOnlyList<RestorePolicy>? policies)
    {
        var decisions = policies?.ToDictionary(p => p.Id, p => p.Mode, StringComparer.Ordinal);
        var root = new XElement("migration", new XAttribute("urlid", Prefix + "filpro-v2"));
        foreach (var item in selection.Files.Concat(selection.Registry))
        {
            var kind = item.Scope == SoftwareScope.Hkcu ? "Registry" : "File";
            var scope = item.Scope switch
            {
                SoftwareScope.Local => @"%CSIDL_LOCAL_APPDATA%\" + item.Rel,
                SoftwareScope.Roaming => @"%CSIDL_APPDATA%\" + item.Rel,
                SoftwareScope.Hkcu => @"HKCU\" + item.Rel,
                _ => throw Invalid(),
            };
            var patterns = new[] { scope + " [*]", scope + "\\* [*]" };
            var rules = new XElement("rules", Rule("include", null));
            var helpers = kind == "File" ? new[] { Source, Destination, Both } : [Source, Destination];
            if (decisions is not null)
            {
                if (!decisions.TryGetValue(SoftwareCatalog.ResolveIdentity(item).Id, out var decision)) throw Invalid();
                helpers = [decision switch
                { RestorePolicyMode.UseSaved => Source, RestorePolicyMode.KeepCurrent => Destination,
                    RestorePolicyMode.KeepBoth => kind == "File" ? Both : Destination, _ => throw Invalid() }];
            }
            foreach (var helper in helpers) rules.Add(Rule("merge", helper));
            root.Add(new XElement("component", new XAttribute("type", "Documents"), new XAttribute("context", "User"),
                new XElement("displayName", "FILPRO - " + BackupManifestCodec.Hash(Encoding.UTF8.GetBytes(kind + "|" + scope.ToUpperInvariant()))),
                new XElement("role", new XAttribute("role", "Data"), rules)));
            XElement Rule(string name, string? script) => new(name, script is null ? null : new XAttribute("script", script),
                new XElement("objectSet", patterns.Select(p => Pattern(kind, p))));
        }
        return new(root);
    }
    private static XElement Pattern(string kind, string value) => new("pattern", new XAttribute("type", kind), value);
    private static XDocument ReadCanonical(ReadOnlyMemory<byte> bytes)
    {
        var document = UsmtMigrationRules.Read(bytes);
        // Avec un XmlReader explicite, LoadOptions.None ne supprime pas les blancs rendus par ce lecteur.
        // Seule l'indentation entre éléments est ignorée. Les motifs, scripts, CDATA, commentaires et attributs restent exacts.
        foreach (var whitespace in document.DescendantNodes().OfType<XText>()
            .Where(node => node.GetType() == typeof(XText) && node.Parent?.HasElements == true && string.IsNullOrWhiteSpace(node.Value)).ToArray())
            whitespace.Remove();
        return document;
    }
    private static XDocument Exclusion(string name, string display, XElement objects) => new(new XElement("migration", new XAttribute("urlid", Prefix + name),
        new XElement("component", new XAttribute("context", "UserAndSystem"), new XAttribute("type", "Documents"), new XElement("displayName", display),
            new XElement("role", new XAttribute("role", "Data"), new XElement("rules", new XElement("unconditionalExclude", objects))))));
    private static byte[] Write(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) document.Save(writer);
        return stream.ToArray();
    }
    private static FilproOperationException Invalid(string? reason = null) => new("restore.compatibility", "Règle V1 non reconnue exactement"
        + (reason is null ? "" : " (" + reason + ")") + " ; aucun XML arbitraire n'est exécuté.");
}
