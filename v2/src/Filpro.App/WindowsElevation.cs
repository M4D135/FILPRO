using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Filpro.App;

internal static class WindowsElevation
{
    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static string AccountName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.Name;
    }

    internal static void Relaunch()
    {
        // Commande fixe, sans mot de passe, argument de page, profil ni destination fournis par le navigateur.
        var start = new ProcessStartInfo(Path.Combine(AppPaths.AppDirectory, "FILPRO.exe"))
        {
            UseShellExecute = true, Verb = "runas", WorkingDirectory = AppPaths.AppDirectory,
            Arguments = "--poste " + StartupPolicy.ElevatedArgument,
        };
        using var process = Process.Start(start) ?? throw new IOException("Windows n’a pas lancé FILPRO.");
    }

    /// <summary>Relance élevée ; nul si Windows l'a lancée, sinon l'état et le message à présenter.</summary>
    internal static ElevationRefusal? TryRelaunch()
    {
        try { Relaunch(); return null; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { return new ElevationRefusal("cancelled", "L’autorisation Windows a été annulée. Vous pouvez réessayer quand vous le souhaitez."); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            TechnicalLog.Instance.Error("Relancement administrateur impossible.", ex);
            return new ElevationRefusal("error", "Windows n’a pas pu ouvrir FILPRO avec les droits administrateur. Vous pouvez réessayer.");
        }
    }
}

internal sealed record ElevationRefusal(string State, string Message);
