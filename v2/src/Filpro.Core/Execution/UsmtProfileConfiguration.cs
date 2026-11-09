using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Filpro.Core;

/// <summary>Configuration de composants fermée ; aucune politique de groupe, règle d'erreur ou script provenant d'un magasin.</summary>
public static class UsmtProfileConfiguration
{
    public static byte[] Restrict(ReadOnlyMemory<byte> bytes, bool requireWindowsAlreadyExcluded)
    {
        var input = UsmtMigrationRules.Read(bytes);
        if (input.Root?.Name != "Configuration" || input.Root.HasAttributes) throw Invalid();
        var output = new XElement("Configuration");
        var sections = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in input.Root.Elements())
        {
            if (section.Name.NamespaceName.Length != 0 || section.HasAttributes || !sections.Add(section.Name.LocalName)) throw Invalid();
            if (section.Name == "Policies") continue; // Comportement d'erreur natif par défaut, aucune règle externe nonFatal.
            if (section.Name == "ProfileControl" && !section.HasElements && string.IsNullOrWhiteSpace(section.Value)) continue;
            if (section.Name != "Applications" && section.Name != "WindowsComponents" && section.Name != "Documents") throw Invalid();
            var target = new XElement(section.Name);
            foreach (var component in section.Elements()) target.Add(Copy(component, section.Name == "WindowsComponents"));
            output.Add(target);
        }
        if (output.Element("WindowsComponents")?.Descendants("component").Any() != true
            || output.Element("Documents")?.Descendants("component").Any() != true) throw Invalid();
        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
            new XDocument(output).Save(writer);
        return memory.ToArray();

        XElement Copy(XElement component, bool windows)
        {
            if (component.Name != "component" || component.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw Invalid();
            if (component.Attributes().Any(a => a.Name.NamespaceName.Length != 0 || a.Name.LocalName is not ("ID" or "id" or "displayname" or "migrate"))) throw Invalid();
            var ids = component.Attributes().Where(a => a.Name.LocalName is "ID" or "id").ToArray();
            if (ids.Length != 1 || string.IsNullOrWhiteSpace(ids[0].Value) || ids[0].Value.Length > 4096 || !identities.Add(ids[0].Value)) throw Invalid();
            var migrate = (string?)component.Attribute("migrate");
            if (migrate is not ("yes" or "no") || (windows && requireWindowsAlreadyExcluded && migrate != "no")) throw Invalid();
            var target = new XElement("component", component.Attributes().Select(a => new XAttribute(a)));
            if (windows) target.SetAttributeValue("migrate", "no");
            foreach (var child in component.Elements()) target.Add(Copy(child, windows));
            return target;
        }
    }
    private static FilproOperationException Invalid() => new("store.rules", "Configuration USMT non reconnue, ambiguë ou autorisant des composants Windows hors profil.");
}
