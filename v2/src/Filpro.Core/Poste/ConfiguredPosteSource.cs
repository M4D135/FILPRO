namespace Filpro.Core;

/// <summary>Derniers choix du profil, puis modèle commun, puis recommandations configurées, dans cet ordre.</summary>
public sealed class ConfiguredPosteSource : IFilproPosteSource
{
    private readonly IFilproPosteSource source;
    private readonly IFilproConfigurationService configuration;
    private readonly IFilproCommonSelectionService common;

    public ConfiguredPosteSource(IFilproPosteSource source, IFilproConfigurationService configuration, IFilproCommonSelectionService common)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(configuration); ArgumentNullException.ThrowIfNull(common);
        if (source.Demonstration != configuration.Demonstration || source.Demonstration != common.Demonstration)
            throw new ArgumentException("Les fournisseurs doivent appartenir au même mode.");
        this.source = source; this.configuration = configuration; this.common = common;
    }

    public bool Demonstration => source.Demonstration;
    public string ReadCurrentAccountSid(CancellationToken cancellationToken) => source.ReadCurrentAccountSid(cancellationToken);
    public ProfileInventory ReadProfiles(CancellationToken cancellationToken) => source.ReadProfiles(cancellationToken);
    public LastSelection? ReadLastSelection(string sid, CancellationToken cancellationToken) => source.ReadLastSelection(sid, cancellationToken);
    public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken) => source.SaveLastSelection(selection, cancellationToken);

    public SoftwareInventory ReadSoftware(string sid, SoftwareQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var template = common.Read(cancellationToken);
        var combined = query with
        {
            AdditionalFiles = [.. query.AdditionalFiles, .. template.AppData],
            AdditionalRegistryKeys = [.. query.AdditionalRegistryKeys, .. template.RegistryKeys.Select(item => item.Rel)],
        };
        var inventory = source.ReadSoftware(sid, combined, cancellationToken);
        // Une sélection explicitement vide doit elle aussi l'emporter sur le modèle et sur les recommandations.
        var last = source.ReadLastSelection(sid, cancellationToken);
        if (last is not null) return inventory;
        if (template.HasSelection)
        {
            return inventory with { Groups = LastSelectionRules.Apply(sid,
                inventory.Groups.SelectMany(group => group.Files).ToArray(),
                inventory.Groups.SelectMany(group => group.Registry).ToArray(),
                new LastSelection(sid, template.AppData, template.RegistryKeys, inventory.CapturedAt)) };
        }
        var settings = configuration.ReadAsync(cancellationToken).GetAwaiter().GetResult().Settings;
        if (settings.RecommendedSelection) return inventory;
        return inventory with { Groups = SoftwareCatalog.Group(
            inventory.Groups.SelectMany(group => group.Files).Select(item => item with { Selected = false }).ToArray(),
            inventory.Groups.SelectMany(group => group.Registry).Select(item => item with { Selected = false }).ToArray()) };
    }
}
