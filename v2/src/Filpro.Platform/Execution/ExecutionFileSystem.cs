using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>
/// E/S des opérations : chemins locaux/USB/UNC et refus des liens/attributs cloud. Sous Windows les parents existants
/// sont ouverts sans FILE_SHARE_DELETE pendant l'E/S ; ils ne peuvent pas être renommés sous le chemin contrôlé.
/// </summary>
public sealed class ExecutionFileSystem
{
    public IDisposable HoldDirectory(string directory) => DirectoryLease.Acquire(StoragePathPolicy.NormalizeNative(directory, true));

    public void RequireFile(string path)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        using var lease = HoldDirectory(Path.GetDirectoryName(path)!);
        var attributes = File.GetAttributes(path);
        RequireOrdinary(attributes);
        if ((attributes & FileAttributes.Directory) != 0) throw Invalid("Un fichier ordinaire est requis.");
    }

    public bool FilePresent(string path)
    {
        try { RequireFile(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public void CreateDirectories(string path)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) CreateDirectories(parent);
        using var lease = HoldDirectory(parent);
        if (OperatingSystem.IsWindows())
        {
            if (!Native.CreateDirectoryW(path, IntPtr.Zero))
            {
                var code = Marshal.GetLastWin32Error();
                if (code != 183) throw new Win32Exception(code, "Création du dossier refusée.");
            }
        }
        else Directory.CreateDirectory(path);
        using var created = HoldDirectory(path);
    }

    public void CreatePrivateDirectory(string path)
    {
        CreateDirectories(path);
        if (!OperatingSystem.IsWindows()) return;
        using var lease = HoldDirectory(path);
        using var current = WindowsIdentity.GetCurrent();
        var sid = current.User ?? throw Invalid("Compte exécutant inconnu.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var identity in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    public IDisposable CreateNewDirectory(string path)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        using var parent = HoldDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsWindows())
        {
            if (!Native.CreateDirectoryW(path, IntPtr.Zero))
                throw new FilproOperationException("storage.create", "La destination existe déjà ou ne peut pas être créée ; aucun écrasement.");
        }
        else
        {
            if (Directory.Exists(path) || File.Exists(path)) throw Invalid("La destination existe déjà.");
            Directory.CreateDirectory(path); // Façade portable ; l'exécutant Windows utilise CreateDirectoryW.
        }
        return HoldDirectory(path);
    }

    public byte[] ReadBounded(string path, int maximumBytes)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        using var lease = HoldDirectory(Path.GetDirectoryName(path)!);
        RequireFile(path);
        using var stream = StorageFileIdentity.OpenRead(path);
        if (stream.Length > maximumBytes) throw Invalid("Fichier trop volumineux.");
        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(65536, maximumBytes + 1)];
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            if (output.Length + read > maximumBytes) throw Invalid("Fichier trop volumineux.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public async Task<ManifestFileReference> DescribeAsync(string root, string relative, CancellationToken token)
    {
        var path = Resolve(root, relative);
        using var lease = HoldDirectory(Path.GetDirectoryName(path)!);
        RequireFile(path);
        await using var stream = StorageFileIdentity.OpenRead(path, asynchronous: true);
        var size = stream.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
        if (stream.Length != size) throw Invalid("Le fichier a changé pendant sa lecture.");
        token.ThrowIfCancellationRequested();
        return new(BackupManifestCodec.RequireRelative(relative), hash, size);
    }

    public async Task VerifyAsync(string root, ManifestFileReference expected, CancellationToken token)
    {
        BackupManifestCodec.ValidateReference(expected);
        var actual = await DescribeAsync(root, expected.Path, token).ConfigureAwait(false);
        if (actual.Bytes != expected.Bytes || !actual.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("store.integrity", "Un fichier du magasin est absent, modifié ou incomplet : " + expected.Path);
    }

    public void WriteAtomic(string path, ReadOnlySpan<byte> bytes, bool overwrite)
    {
        path = StoragePathPolicy.NormalizeNative(path);
        using var lease = HoldDirectory(Path.GetDirectoryName(path)!);
        var exists = FilePresent(path);
        if (exists && !overwrite) throw Invalid("Le fichier existe déjà ; aucun écrasement.");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            // ReplaceFile permet aux lecteurs FILE_SHARE_DELETE de finir sur l'ancien instantané.
            // MoveFileEx(REPLACE_EXISTING) échoue sur un fichier encore ouvert sous Windows.
            if (exists) File.Replace(temporary, path, null, ignoreMetadataErrors: false);
            else File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void CopyNew(string source, string destination)
    {
        source = StoragePathPolicy.NormalizeNative(source); destination = StoragePathPolicy.NormalizeNative(destination);
        RequireFile(source);
        using var sourceLease = HoldDirectory(Path.GetDirectoryName(source)!);
        using var targetLease = HoldDirectory(Path.GetDirectoryName(destination)!);
        using var input = StorageFileIdentity.OpenRead(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output); output.Flush(true);
    }

    public async Task CopyNewAsync(string source, string destination, CancellationToken token)
    {
        source = StoragePathPolicy.NormalizeNative(source); destination = StoragePathPolicy.NormalizeNative(destination);
        token.ThrowIfCancellationRequested(); RequireFile(source);
        using var sourceLease = HoldDirectory(Path.GetDirectoryName(source)!);
        using var targetLease = HoldDirectory(Path.GetDirectoryName(destination)!);
        await using var input = StorageFileIdentity.OpenRead(source, asynchronous: true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
        await input.CopyToAsync(output, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
    }

    public static string Resolve(string root, string relative)
    {
        var normalized = BackupManifestCodec.RequireRelative(relative);
        var path = Path.Combine(StoragePathPolicy.NormalizeNative(root), normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!StoragePathPolicy.IsWithin(path, root, false)) throw Invalid("Chemin hors du magasin.");
        return path;
    }

    private static void RequireOrdinary(FileAttributes attributes)
    {
        const FileAttributes cloud = (FileAttributes)(0x00001000 | 0x00040000 | 0x00400000);
        if ((attributes & (FileAttributes.ReparsePoint | cloud)) != 0)
            throw Invalid("Lien, espace cloud ou fichier hors ligne refusé.");
    }
    private static FilproOperationException Invalid(string message) => new("storage.unsafe", message);

    private sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        public static DirectoryLease Acquire(string directory)
        {
            var lease = new DirectoryLease();
            try
            {
                var chain = new Stack<string>();
                for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) chain.Push(current);
                foreach (var path in chain)
                {
                    var attributes = File.GetAttributes(path);
                    RequireOrdinary(attributes);
                    if ((attributes & FileAttributes.Directory) == 0) throw Invalid("Un parent n'est pas un dossier.");
                    if (!OperatingSystem.IsWindows()) continue;
                    // FILE_LIST_DIRECTORY impose les règles de partage ; READ_ATTRIBUTES seul ne les impose pas.
                    var handle = Native.CreateFileW(path, 0x1 | 0x80, 0x1 | 0x2, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                    if (handle.IsInvalid) { var code = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(code); }
                    lease.handles.Add(handle);
                    if (!Native.GetFileInformationByHandleEx(handle, 9, out var info, 8)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    RequireOrdinary((FileAttributes)info.Attributes);
                    if ((info.Attributes & 0x10) == 0) throw Invalid("Parent substitué par un fichier.");
                }
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }
        public void Dispose()
        { for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct AttributeTag { public uint Attributes; public uint Tag; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag information, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDirectoryW(string path, IntPtr security);
    }
}
