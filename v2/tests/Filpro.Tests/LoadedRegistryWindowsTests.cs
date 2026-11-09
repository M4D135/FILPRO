using System.Security.Principal;
using Filpro.Platform;

namespace Filpro.Tests;

/// <summary>Contrôle natif séparé : ne crée aucune clé, ne monte aucune ruche et ne s'exécute jamais hors Windows.</summary>
internal sealed class LoadedRegistryWindowsTests
{
    [Test]
    public void LePropreSidEstLuSousHkuEtSeulsNosHandlesSontFermes()
    {
        if (!OperatingSystem.IsWindows()) Check.Skip("Lecture native HKU du propre SID réservée à Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        Check.NotNull(identity.User, "identité Windows disponible");
        var source = new WindowsLoadedUserRegistry();
        var opened = source.OpenUserHive(identity.User!.Value);
        using var hive = opened.Key;
        Check.Equal(0, opened.ErrorCode, "le propre SID doit s'ouvrir sous HKEY_USERS, pas HKEY_CURRENT_CONFIG");
        Check.NotNull(hive, "handle de ruche réellement ouvert");
        var softwareResult = hive!.OpenSubKey("Software");
        using var software = softwareResult.Key;
        Check.Equal(0, softwareResult.ErrorCode, "racine Software du propre compte lisible");
        Check.NotNull(software, "handle de sous-clé détenu");
        var first = software!.ReadSubKeyName(0);
        Check.True(first.ErrorCode is 0 or 259, "énumération native réussie ou liste réellement vide");
        var absent = hive.OpenSubKey(@"Software\FILPRO_ReadOnly_Absent_" + Guid.NewGuid().ToString("N"));
        using (absent.Key) Check.Equal(2, absent.ErrorCode, "absence native explicitement distinguée d'un refus d'accès");
        software.Dispose();
        hive.Dispose();
        Check.Throws<ObjectDisposedException>(() => hive.OpenSubKey("Software"), "un handle rendu n'est plus utilisable après Dispose");
        var reopened = source.OpenUserHive(identity.User.Value);
        using (reopened.Key) Check.Equal(0, reopened.ErrorCode, "la ruche Windows reste chargée après fermeture de nos handles");
    }
}
