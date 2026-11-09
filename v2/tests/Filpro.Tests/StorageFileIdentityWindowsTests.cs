using System.Runtime.InteropServices;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.Tests;

internal static class StorageFileIdentityWindowsTests
{
    [Test] public static void AbsenceDeDocumentResteDistincteDuRefusDeLectureWindows()
    {
        if (!OperatingSystem.IsWindows()) { Check.Skip("Codes CreateFile Windows requis."); return; }
        using var fixture = new PackTestFixture();
        Check.Throws<FileNotFoundException>(() => StorageFileIdentity.OpenRead(Path.Combine(fixture.Root, "absent.json")), "absence du document");
        Check.Throws<DirectoryNotFoundException>(() => StorageFileIdentity.OpenRead(Path.Combine(fixture.Root, "absent", "document.json")), "absence du dossier");
    }
    [Test] public static void LecteurDeBilanNeBloquePasLeRemplacementAtomiqueWindows()
    {
        if (!OperatingSystem.IsWindows()) { Check.Skip("Partage de suppression Windows requis."); return; }
        using var fixture = new PackTestFixture(); var path = Path.Combine(fixture.Root, "bilan.json");
        File.WriteAllText(path, "avant");
        using var snapshot = StorageFileIdentity.OpenReadSnapshot(path);
        new ExecutionFileSystem().WriteAtomic(path, System.Text.Encoding.UTF8.GetBytes("apres"), true);
        using var reader = new StreamReader(snapshot);
        Check.Equal("avant", reader.ReadToEnd(), "le handle conserve l'ancien instantané");
        Check.Equal("apres", File.ReadAllText(path), "le bilan final est publié malgré le lecteur");
    }
    [Test] public static void HardlinkEstRefuseSurHandleReelWindows()
    {
        if (!OperatingSystem.IsWindows()) { Check.Skip("Le nombre de liens est un contrôle natif Windows, non qualifié hors Windows."); return; }
        using var fixture = new PackTestFixture(); var path = Path.Combine(fixture.Root, "original.bin"); var alias = Path.Combine(fixture.Root, "alias.bin");
        File.WriteAllText(path, "DONNEES-FICTIVES");
        using (var single = File.OpenRead(path)) StorageFileIdentity.RequireSingleLink(single);
        Check.True(CreateHardLink(alias, path, IntPtr.Zero), "hardlink temporaire créé par Windows");
        using (var linked = File.OpenRead(path)) Check.Throws<FilproOperationException>(() => StorageFileIdentity.RequireSingleLink(linked), "alias natif refusé");
        Check.Equal("DONNEES-FICTIVES", File.ReadAllText(alias), "aucune modification du contenu");
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
