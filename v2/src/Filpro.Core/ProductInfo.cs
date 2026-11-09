namespace Filpro.Core;

/// <summary>Identité du produit partagée par toutes les couches (titre de fenêtre, journaux, rapports).</summary>
public static class ProductInfo
{
    public const string Name = "FILPRO";
    // Version candidate. Le manifeste DEV/PROD et les preuves de qualification restent des contrôles distincts.
    public const string Version = "2.0.13";

    /// <summary>Nom d'hôte virtuel sous lequel l'interface web est servie par WebView2. Jamais résolu sur le réseau.</summary>
    public const string WebHost = "filpro.local";
}
