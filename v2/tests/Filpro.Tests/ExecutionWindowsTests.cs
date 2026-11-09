using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class ExecutionWindowsTests
{
    [Test]
    public static void UnParentWindowsTenuNePeutPasEtreRenomme()
    {
        if (!OperatingSystem.IsWindows()) Check.Skip("Contrôle des handles et partages Win32 réservé à Windows.");
        var root = Path.Combine(Path.GetTempPath(), "filpro-execution-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var original = Path.Combine(root, "original"); var renamed = Path.Combine(root, "renamed"); Directory.CreateDirectory(original);
        try
        {
            using (new ExecutionFileSystem().HoldDirectory(original))
                Check.Throws<IOException>(() => Directory.Move(original, renamed), "Le handle sans FILE_SHARE_DELETE bloque le renommage");
            Directory.Move(original, renamed);
            Check.True(Directory.Exists(renamed), "Après fermeture, le renommage devient possible");
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public static async Task EntreeWindowsHasheeResteNonModifiableJusquaFermeture()
    {
        if (!OperatingSystem.IsWindows()) Check.Skip("Contrôle du partage de lecture Win32 réservé à Windows.");
        var root = Path.Combine(Path.GetTempPath(), "filpro-input-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "rule.xml"); var bytes = Encoding.UTF8.GetBytes("<migration/>"); File.WriteAllBytes(path, bytes);
        try
        {
            using (var lease = new ExecutionInputLease(new ExecutionFileSystem()))
            {
                await lease.HoldAsync(path, BackupManifestCodec.Hash(bytes), bytes.Length, default);
                Check.Throws<IOException>(() => File.WriteAllText(path, "remplacée"), "Écriture bloquée pendant l'usage du fichier vérifié");
                Check.Throws<IOException>(() => File.Move(path, path + ".other"), "Substitution bloquée pendant l'usage du fichier vérifié");
            }
            File.WriteAllText(path, "après fermeture");
            Check.Equal("après fermeture", File.ReadAllText(path), "Le handle a bien été libéré");
        }
        finally { Directory.Delete(root, true); }
    }
}
