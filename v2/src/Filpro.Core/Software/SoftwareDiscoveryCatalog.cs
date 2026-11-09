namespace Filpro.Core;

/// <summary>
/// Recette de découverte V1. Le domaine d'origine (Bureautique, Navigateur…) reste distinct du type de données V2.
/// <see cref="RequiresQualification"/> conserve le marqueur V1 « NotTested » ; son absence ne prouve aucune validation Windows.
/// </summary>
public sealed record SoftwareDiscoveryRecipe(SoftwareItem Item, string SourceCategory, bool RequiresQualification);

/// <summary>
/// Recettes et exclusions portées de wad/FILPRO.ps1 (AppCatalog, RegCatalog, AppVendorDirs, AppDataNoise, RegVendorKeys)
/// et des découvertes de wad/FILPRO.Profiles.ps1, référence FILPRO 63b89fe704aed7a363cce42fcdfc5d9112f94807.
/// Aucune lecture du poste : une recette ne prouve pas qu'un chemin existe.
/// Les fichiers conservent le classement de données V2 ; les clés sont des préférences. Le domaine V1 est une métadonnée.
/// Les éléments restent non sélectionnés et sans taille ; le fournisseur vérifie leur présence avant d'appliquer les choix.
/// </summary>
public static class SoftwareDiscoveryCatalog
{
    /// <summary>
    /// Les 29 recettes AppCatalog, noms et recommandations V1. Les associations de logiciels n'ajoutent pas de fichiers.
    /// Les chevauchements explicites (Outlook/AutoComplete notamment) sont conservés, comme les trois marqueurs NotTested.
    /// </summary>
    public static IReadOnlyList<SoftwareDiscoveryRecipe> FileRecipes { get; } = Array.AsReadOnly<SoftwareDiscoveryRecipe>(
    [
        File(SoftwareScope.Roaming, @"Microsoft\Signatures", "Signatures Outlook", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Templates", "Modèles Office", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Sticky Notes", "Pense-bêtes (legacy)", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Spelling", "Dictionnaires perso", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Word", "Word (paramètres)", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Excel", "Excel (paramètres)", "Bureautique", true),
        File(SoftwareScope.Roaming, @"Microsoft\Outlook", "Outlook (profil local)", "Bureautique", false),
        File(SoftwareScope.Roaming, @"Mozilla\Firefox", "Firefox (profil)", "Navigateur", true),
        File(SoftwareScope.Roaming, "Thunderbird", "Thunderbird", "Communication", true),
        File(SoftwareScope.Roaming, "Code", "VS Code (paramètres)", "Developpement", true),
        File(SoftwareScope.Roaming, "Code - Insiders", "VS Code Insiders", "Developpement", true),
        File(SoftwareScope.Roaming, "Notepad++", "Notepad++", "Developpement", true),
        File(SoftwareScope.Roaming, "Sublime Text 3", "Sublime Text", "Developpement", true),
        File(SoftwareScope.Roaming, "FileZilla", "FileZilla", "Developpement", true),
        File(SoftwareScope.Roaming, "Postman", "Postman", "Developpement", false),
        File(SoftwareScope.Roaming, "Slack", "Slack", "Communication", true),
        File(SoftwareScope.Roaming, "discord", "Discord", "Personnel", false),
        File(SoftwareScope.Roaming, "Zoom", "Zoom", "Communication", false),
        File(SoftwareScope.Roaming, "Spotify", "Spotify", "Multimedia", false),
        File(SoftwareScope.Roaming, ".minecraft", "Minecraft", "Personnel", false),
        File(SoftwareScope.Roaming, "vlc", "VLC", "Personnel", false),
        File(SoftwareScope.Roaming, @"Microsoft\Teams", "Teams (classique)", "Communication", false),
        File(SoftwareScope.Local, @"Google\Chrome\User Data", "Chrome", "Navigateur", true),
        File(SoftwareScope.Local, @"Microsoft\Edge\User Data", "Edge", "Navigateur", true),
        File(SoftwareScope.Local, @"BraveSoftware\Brave-Browser\User Data", "Brave", "Navigateur", true),
        File(SoftwareScope.Local, "GitHubDesktop", "GitHub Desktop", "Developpement", false),
        File(SoftwareScope.Roaming, @"SAP\SAP GUI", "SAP GUI", "Bureautique", false, requiresQualification: true),
        File(SoftwareScope.Roaming, @"Citrix\ICA Client", "Citrix Workspace", "Bureautique", false, requiresQualification: true),
        File(SoftwareScope.Roaming, @"Microsoft\Outlook\AutoComplete", "Cache saisie Outlook", "Bureautique", false, requiresQualification: true),
    ]);

    /// <summary>
    /// Les 12 recettes RegCatalog puis les clés manquantes des associations V1. Ces ajouts ont toujours Recommend=false,
    /// même si le logiciel associé est recommandé par ses fichiers. Aucun marqueur NotTested n'était défini sur RegCatalog.
    /// </summary>
    public static IReadOnlyList<SoftwareDiscoveryRecipe> RegistryRecipes { get; } = BuildRegistryRecipes();

    public static IReadOnlyList<SoftwareItem> Files { get; } = Array.AsReadOnly(FileRecipes.Select(r => r.Item).ToArray());
    public static IReadOnlyList<SoftwareItem> Registry { get; } = Array.AsReadOnly(RegistryRecipes.Select(r => r.Item).ToArray());

    private static readonly HashSet<string> AppExcludedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        // AppVendorDirs : même liste sous Local et Roaming.
        "Microsoft", "Google", "Mozilla", "Adobe", "Apple", "Packages", "Programs", "Temp", "BraveSoftware", "Comms", "ConnectedDevicesPlatform",
        // AppDataNoise (Temp est déjà présent dans AppVendorDirs).
        "TempState", "CrashDumps", "D3DSCache", "INetCache", "History", "Caches", "Microsoft Help", "ElevatedDiagnostics", "PlaceholderTileLogoFolder", "VirtualStore", "Publishers",
    };

    private static readonly HashSet<string> RegistryExcludedRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Google", "Mozilla", "Apple Computer, Inc.", "Policies", "Classes", "Wow6432Node", "RegisteredApplications",
    };

    /// <summary>
    /// Exclusion de la découverte générique AppData : vendeurs/bruit, recettes explicites et leurs parents.
    /// Le fournisseur ne parcourt que le premier niveau. Les descendants sont aussi exclus pour éviter un doublon si ce
    /// prédicat est réutilisé. Ce filtre n'est ni une validation de chemin ni un contrôle des liens ou des permissions.
    /// </summary>
    public static bool IsGenericFileExcluded(SoftwareScope scope, string rel)
    {
        if (scope is not (SoftwareScope.Local or SoftwareScope.Roaming))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "La découverte AppData attend Local ou Roaming.");
        }
        ArgumentNullException.ThrowIfNull(rel);
        var path = Normalize(rel);
        // Alias de compatibilité créés par Windows sous Local : boucle vers Local et alias du cache.
        // Ils ne sont pas des réglages logiciels. Les choix explicites restent vérifiés par le lecteur, qui reconnaît
        // aussi les alias traduits (« Historique »…) par leurs attributs, pas par leur nom.
        return path.Length == 0 || AppExcludedRoots.Contains(FirstSegment(path))
            || (scope == SoftwareScope.Local && (FirstSegment(path).Equals("Application Data", StringComparison.OrdinalIgnoreCase)
                || FirstSegment(path).Equals("Temporary Internet Files", StringComparison.OrdinalIgnoreCase)))
            || Files.Any(item => item.Scope == scope && Overlaps(path, item.Rel));
    }

    /// <summary>
    /// Exclusion de la découverte générique des enfants de HKCU\Software : vendeurs, recettes et leurs parents/descendants.
    /// Les clés supplémentaires validées (sélection précédente ou modèle commun) doivent aussi être comparées par le
    /// fournisseur : le catalogue statique ne connaît pas leurs portées. Hors Software, seule une recette explicite est lue.
    /// </summary>
    public static bool IsGenericRegistryExcluded(string rel)
    {
        ArgumentNullException.ThrowIfNull(rel);
        var path = Normalize(rel);
        const string prefix = "Software\\";
        return !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || RegistryExcludedRoots.Contains(FirstSegment(path[prefix.Length..]))
            || Registry.Any(item => Overlaps(path, item.Rel));
    }

    private static SoftwareDiscoveryRecipe File(SoftwareScope scope, string rel, string name, string sourceCategory, bool recommend, bool requiresQualification = false) =>
        new(new SoftwareItem(scope, rel, name, SoftwareCatalog.Classify(rel), false, recommend, true, null, false, false), sourceCategory, requiresQualification);

    private static SoftwareDiscoveryRecipe Reg(string rel, string name, string sourceCategory, bool recommend) =>
        new(new SoftwareItem(SoftwareScope.Hkcu, rel, name, SoftwareCategory.Preferences, false, recommend, true, null, false, false), sourceCategory, false);

    private static IReadOnlyList<SoftwareDiscoveryRecipe> BuildRegistryRecipes()
    {
        var recipes = new List<SoftwareDiscoveryRecipe>
        {
            Reg(@"Software\Microsoft\Office", "Outlook / Office (réglages ; fichiers PST non inclus)", "Bureautique", true),
            Reg(@"Software\Microsoft\Office\Outlook\Addins", "Outlook - compléments", "Bureautique", false),
            Reg(@"Software\Microsoft\Exchange", "Exchange (cache/identités)", "Bureautique", false),
            Reg(@"Software\Microsoft\Edge", "Edge (paramètres)", "Navigateur", true),
            Reg(@"Software\Google\Chrome", "Chrome (paramètres)", "Navigateur", true),
            Reg(@"Software\Mozilla\Mozilla Firefox", "Firefox (paramètres)", "Navigateur", true),
            Reg(@"Software\Apple Computer, Inc.\Safari", "Safari (paramètres)", "Navigateur", false),
            Reg("Network", "Lecteurs réseau mappés", "Systeme", true),
            Reg("Printers", "Imprimantes (connexions)", "Systeme", true),
            Reg(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "Proxy / paramètres Internet", "Systeme", false),
            Reg(@"Software\Microsoft\Office\16.0\Common\Identity", "Identités Office 365", "Bureautique", false),
            Reg(@"Software\Policies", "Stratégies utilisateur (GPO locales)", "Systeme", false),
        };
        var known = new HashSet<string>(recipes.Select(r => r.Item.Rel), StringComparer.OrdinalIgnoreCase);
        foreach (var binding in SoftwareCatalog.Bindings)
        {
            foreach (var rel in binding.Registry)
            {
                if (known.Add(rel))
                {
                    recipes.Add(Reg(rel, binding.Name + " (registre)", "Logiciel", false));
                }
            }
        }
        return recipes.AsReadOnly();
    }

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');

    private static string FirstSegment(string path)
    {
        var separator = path.IndexOf('\\', StringComparison.Ordinal);
        return separator < 0 ? path : path[..separator];
    }

    private static bool Overlaps(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase)
        || left.StartsWith(right + '\\', StringComparison.OrdinalIgnoreCase)
        || right.StartsWith(left + '\\', StringComparison.OrdinalIgnoreCase);
}
