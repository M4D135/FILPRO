using System.Collections.Concurrent;
using System.Text.Json;
using Filpro.Core;

namespace Filpro.Platform;

public interface ICopiedHiveMountBackend
{
    bool IsMounted(string tag);
    /// <summary>Marque le tag vivant pour les autres processus du poste ; refuse un tag déjà marqué.</summary>
    IDisposable Claim(string tag);
    int Load(string tag, string copiedPath);
    RegistryKeyReadResult Open(string tag);
    int Unload(string tag);
}

/// <summary>Résultat natif connu malgré un échec de rétablissement des privilèges : permet de nettoyer ce que l'on possède.</summary>
public sealed class HiveNativeInvocationException(string message, int? result) : Exception(message)
{ public int? Result { get; } = result; }

/// <summary>
/// Repli pour une copie dont RegLoadAppKey refuse le descripteur. Jamais de tag SID Windows. Le marqueur du tag est pris
/// avant le chargement et rendu au déchargement confirmé : un autre processus distingue ainsi un montage vivant d'un résidu.
/// </summary>
public sealed class OwnedCopiedHiveMounts(ICopiedHiveMountBackend backend, Action<string>? blockWrites = null)
{
    private sealed class Mount(string tag, string path, IDisposable claim)
    { public string Tag = tag; public string Path = path; public IDisposable Claim = claim; public bool OwnershipProven; public bool HandlesTransferred; }
    private readonly ConcurrentDictionary<string, Mount> mounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();

    public RegistryKeyReadResult Open(string copiedPath)
    { lock (gate) return OpenCore(copiedPath); }

    private RegistryKeyReadResult OpenCore(string copiedPath)
    {
        var tag = "FILPRO_V2_" + Guid.NewGuid().ToString("N");
        var claim = backend.Claim(tag);
        var mount = new Mount(tag, copiedPath, claim);
        try
        {
            if (backend.IsMounted(tag)) throw new FilproOperationException("hive.ownership", "Collision du nom de ruche privée ; aucun chargement ni déchargement.");
            if (!mounts.TryAdd(copiedPath, mount)) throw new FilproOperationException("hive.busy", "Cette copie est déjà utilisée.");
        }
        catch { claim.Dispose(); throw; }
        IReadOnlyUserRegistryKey? root = null;
        var transferred = false;
        try
        {
            int code;
            try { code = backend.Load(tag, copiedPath); }
            catch (HiveNativeInvocationException ex)
            {
                mount.OwnershipProven = ex.Result == 0;
                blockWrites?.Invoke("Rétablissement des privilèges non confirmé.");
                throw;
            }
            mount.OwnershipProven = code == 0;
            if (code != 0) throw new FilproOperationException("hive.load", "Chargement de la copie privée refusé (Win32 " + code + ").");
            var opened = backend.Open(tag); root = opened.Key;
            if (opened.ErrorCode != 0 || root is null) throw new FilproOperationException("hive.open", "Lecture de la ruche privée impossible.");
            mount.HandlesTransferred = true;
            transferred = true; return new(0, root);
        }
        finally
        {
            if (!transferred) { root?.Dispose(); Release(copiedPath); }
        }
    }

    /// <summary>Le montage est suivi avant le chargement et jusqu'au déchargement confirmé ; un échec de nettoyage le garde.</summary>
    internal bool Owns(string tag) => mounts.Values.Any(mount => string.Equals(mount.Tag, tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Appelé après fermeture de TOUS les handles par OfflineUserHiveLease.</summary>
    public void Release(string copiedPath)
    { lock (gate) ReleaseCore(copiedPath); }

    /// <summary>Réessaie uniquement les montages dont Open a échoué avant de rendre des handles à un appelant.</summary>
    public void CleanupUntransferred()
    {
        lock (gate)
        {
            var failures = new List<string>();
            foreach (var mount in mounts.Values.Where(m => !m.HandlesTransferred))
                try { ReleaseCore(mount.Path); } catch (Exception ex) { failures.Add(ex.Message); }
            if (failures.Count != 0) throw new FilproOperationException("hive.cleanup", string.Join(" ", failures));
        }
    }

    private void ReleaseCore(string copiedPath)
    {
        if (!mounts.TryGetValue(copiedPath, out var mount)) return;
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (!backend.IsMounted(mount.Tag)) { Forget(copiedPath, mount); return; }
                // Un nom apparu malgré un Load en échec peut appartenir à autrui : ne jamais le décharger.
                if (!mount.OwnershipProven) throw new FilproOperationException("hive.ownership", "La propriété du montage n'est pas prouvée ; aucun déchargement d'une ruche potentiellement étrangère.");
                _ = backend.Unload(mount.Tag);
                if (!backend.IsMounted(mount.Tag)) { Forget(copiedPath, mount); return; }
                if (attempt < 2) Thread.Sleep(150);
            }
            throw new FilproOperationException("hive.cleanup", "Le déchargement de la copie privée reste non confirmé.");
        }
        catch (Exception ex)
        {
            blockWrites?.Invoke("Une ruche privée n'a pas pu être libérée ; toute nouvelle écriture est bloquée.");
            try
            {
                var evidence = JsonSerializer.SerializeToUtf8Bytes(new
                { schemaVersion = 1, tag = mount.Tag, copiedPath = mount.Path, mount.OwnershipProven, status = "CleanupFailed", error = ex.Message });
                new ExecutionFileSystem().WriteAtomic(Path.Combine(Path.GetDirectoryName(copiedPath)!, "hive-cleanup.json"), evidence, true);
            }
            catch { /* L'échec initial reste bloquant même si le support du reçu a disparu. */ }
            throw new FilproOperationException("hive.cleanup", "Ruche privée non libérée : " + mount.Tag + ". Consulter le reçu dans " + Path.GetDirectoryName(copiedPath) + ".");
        }
    }

    /// <summary>Un montage non confirmé garde son marqueur : il reste vivant tant que ce processus, qui s'est bloqué, existe.</summary>
    private void Forget(string copiedPath, Mount mount)
    { mounts.TryRemove(copiedPath, out _); mount.Claim.Dispose(); }
}

internal static class WindowsNativeExecutionSafety
{
    private static string? reason;
    internal static string? BlockReason => Volatile.Read(ref reason);
    internal static void Block(string value) => Interlocked.CompareExchange(ref reason, value, null);

    internal static IDisposable AcquireMachineLease(ExecutionFileSystem files)
    {
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("platform.windows", "Windows requis.");
        var data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(data)) throw new FilproOperationException("execution.lock", "Le dossier partagé des verrous est indisponible.");
        var root = Path.Combine(data, "FILPRO", "Execution");
        files.CreatePrivateDirectory(root);
        return ConfigurationFileIO.Lock(Path.Combine(root, "machine-operation.lock"));
    }

    /// <summary>Après acquisition du verrou machine : ne jamais adopter ou décharger une ruche d'une autre session.</summary>
    internal static void RequireNoResidualMounts()
    {
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("platform.windows", "Windows requis.");
        using var users = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.Users, Microsoft.Win32.RegistryView.Registry64);
        if (HasResidualMount(users.GetSubKeyNames, WindowsOfflineHiveNative.OwnsLiveMount, WindowsCopiedHiveMountBackend.ClaimedByLiveProcess))
            Block("Une ruche privée FILPRO d'une autre session reste montée. Examiner les reçus de nettoyage et la session d'origine avant toute nouvelle écriture ; aucun déchargement automatique.");
        if (BlockReason is { } blocked) throw new FilproOperationException("hive.cleanup", blocked);
    }

    /// <summary>
    /// Une lecture d'inventaire, de ce processus ou d'une autre session, peut tenir sa copie montée (repli 1009, chemin
    /// normal sous Windows 11 26100) : son bail la libère. Seul un tag FILPRO_V2_ qu'aucun montage vivant ne suit, ni dans
    /// ce processus ni par le marqueur qu'un processus en vie détient encore, est un résidu.
    /// </summary>
    internal static bool IsResidualMount(string name, Func<string, bool> ownedByThisProcess, Func<string, bool> claimedByLiveProcess) =>
        name.StartsWith("FILPRO_V2_", StringComparison.OrdinalIgnoreCase) && !ownedByThisProcess(name) && !claimedByLiveProcess(name);

    /// <summary>
    /// Un montage confirmé déchargé disparaît de HKU avant de rendre son suivi et son marqueur : un candidat relevé sur la
    /// première liste puis absent de la seconde était vivant. Seul un nom encore présent après le contrôle est un résidu.
    /// </summary>
    internal static bool HasResidualMount(Func<string[]> names, Func<string, bool> ownedByThisProcess, Func<string, bool> claimedByLiveProcess)
    {
        var candidates = names().Where(name => IsResidualMount(name, ownedByThisProcess, claimedByLiveProcess)).ToArray();
        return candidates.Length != 0 && names().Intersect(candidates, StringComparer.OrdinalIgnoreCase).Any();
    }
}
