namespace Filpro.App;

/// <summary>Each window owns its icon independently of the resource stream and other windows.</summary>
internal static class BrandIcon
{
    internal static Icon CreateOwned()
    {
        using var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream("Filpro.App.Assets.filpro.ico")
            ?? throw new InvalidOperationException("La ressource de marque FILPRO est introuvable.");
        using var resource = new Icon(stream);
        return (Icon)resource.Clone();
    }
}
