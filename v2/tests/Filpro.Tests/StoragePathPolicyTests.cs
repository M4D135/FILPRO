using Filpro.Core;

namespace Filpro.Tests;

internal static class StoragePathPolicyTests
{
    [Test] public static void LocalEtUncCanoniques() { Check.Equal(@"C:\Sauvegardes\Lot", StoragePathPolicy.NormalizeWindows(@"c:/Sauvegardes/Lot"), "local"); Check.Equal(@"\\serveur\partage\FILPRO", StoragePathPolicy.NormalizeWindows(@"\\serveur\partage\FILPRO"), "UNC"); }
    [Test] public static void UriCloudEtNamespacesRefuses() { foreach (var path in new[] { "https://serveur/fichier", "file:///C:/Fichier", @"\\?\C:\Fichier", @"\\.\C:\Fichier", @"C:\Users\Demo\OneDrive\Store", @"\\serveur@SSL\DavWWWRoot\test", @"\\serveur\DavWWWRoot\test", @"\\tenant.sharepoint.com\partage\test" }) Check.Throws<FilproOperationException>(() => StoragePathPolicy.NormalizeWindows(path), "source interdite"); }
    [Test] public static void TraverseesEtAdsRefuses() { foreach (var path in new[] { @"C:\A\..\B", @"C:\A\.\B", @"C:\A\data:secret", @"C:relatif", @"\relatif", @"C:\A\\B" }) Check.Throws<FilproOperationException>(() => StoragePathPolicy.NormalizeWindows(path), "ambiguïté"); }
    [Test] public static void UncAzureEtHoteCloudAvecPointFinalRefuses()
    {
        foreach (var path in new[] { @"\\exemple.file.core.windows.net\partage\FILPRO", @"\\exemple.privatelink.file.core.windows.net\partage\FILPRO",
            @"\\exemple.file.core.usgovcloudapi.net\partage\FILPRO", @"\\tenant.sharepoint.com.\partage\FILPRO" })
            Check.Throws<FilproOperationException>(() => StoragePathPolicy.NormalizeWindows(path), "un UNC cloud n’est pas un partage LAN");
        Check.Equal(@"\\fichiers.exemple.lan\partage\FILPRO", StoragePathPolicy.NormalizeWindows(@"\\fichiers.exemple.lan\partage\FILPRO"), "partage LAN explicite conservé");
    }
    [Test] public static void NomsReservesEtFinalsAmbigusRefuses() { foreach (var path in new[] { @"C:\NUL.txt", @"C:\COM1", "C:\\A.\\B", "C:\\A \\B" }) Check.Throws<FilproOperationException>(() => StoragePathPolicy.NormalizeWindows(path), "nom réservé"); }
    [Test] public static void ConfinementRespecteLaFrontiereDuSegment() { Check.True(StoragePathPolicy.IsWithin(@"C:\Pack\wad\data", @"C:\Pack"), "enfant"); Check.False(StoragePathPolicy.IsWithin(@"C:\Pack-autre\data", @"C:\Pack"), "préfixe voisin"); Check.False(StoragePathPolicy.IsWithin(@"C:\Pack", @"C:\Pack", false), "racine exclue"); }
    [Test] public static void RacineRefuseeSaufAutorisationExplicite() { Check.Throws<FilproOperationException>(() => StoragePathPolicy.NormalizeWindows(@"C:\"), "racine"); Check.Equal(@"C:\", StoragePathPolicy.NormalizeWindows(@"C:\", true), "opt-in"); }
}
