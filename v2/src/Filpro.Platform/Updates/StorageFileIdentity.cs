using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Contrôle natif du handle réellement ouvert ; aucune équivalence Windows revendiquée hors Windows.</summary>
internal static class StorageFileIdentity
{
    internal static FileStream OpenRead(string path, bool asynchronous = false)
        => Open(path, FileAccess.Read, FileMode.Open, FileShare.Read, asynchronous);

    internal static FileStream OpenExclusiveRead(string path)
        => Open(path, FileAccess.Read, FileMode.Open, FileShare.None, asynchronous: false);

    internal static FileStream OpenReadForRename(string path)
        => Open(path, FileAccess.Read, FileMode.Open, FileShare.Delete, asynchronous: false);

    internal static FileStream OpenReadSnapshot(string path)
        => Open(path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, asynchronous: false);

    internal static FileStream OpenLock(string path)
        => Open(path, FileAccess.ReadWrite, FileMode.OpenOrCreate, FileShare.None, asynchronous: false);

    private static FileStream Open(string path, FileAccess access, FileMode mode, FileShare sharing, bool asynchronous)
    {
        if (!OperatingSystem.IsWindows()) return new FileStream(path, mode, access, sharing, 128 * 1024,
            asynchronous ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.None);
        var handle = Native.CreateFileW(path, access == FileAccess.Read ? 0x80000000u : 0xc0000000u,
            (uint)sharing, IntPtr.Zero, mode == FileMode.Open ? 3u : 4u,
            0x00200000u | 0x08000000u | (asynchronous ? 0x40000000u : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            var cause = new Win32Exception(error);
            // Les lecteurs distinguent l'absence initiale d'un document d'un refus d'accès.
            throw error switch
            {
                2 => new FileNotFoundException("Le fichier demandé n'existe pas.", path, cause),
                3 => new DirectoryNotFoundException("Le dossier demandé n'existe pas.", cause),
                5 => new UnauthorizedAccessException("Accès au fichier refusé.", cause),
                _ => new IOException("Impossible d'ouvrir le fichier sans suivre de lien.", cause),
            };
        }
        FileStream? stream = null;
        try { stream = new FileStream(handle, access, 128 * 1024, asynchronous); RequireSingleLink(stream); return stream; }
        catch { if (stream is not null) stream.Dispose(); else handle.Dispose(); throw; }
    }

    internal static void RequireSingleLink(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!OperatingSystem.IsWindows()) return;
        if (!Native.GetFileInformationByHandle(stream.SafeFileHandle, out var information))
            throw new IOException("Impossible de contrôler l'identité du fichier ouvert.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (information.NumberOfLinks != 1 || (information.Attributes & 0x400) != 0)
            throw new FilproOperationException("storage.hardlink", "Alias de fichier (hardlink) ou point de réanalyse refusé pour cette opération.");
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Creation;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Access;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Write;
            internal uint VolumeSerial;
            internal uint SizeHigh;
            internal uint SizeLow;
            internal uint NumberOfLinks;
            internal uint IndexHigh;
            internal uint IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    }
}
