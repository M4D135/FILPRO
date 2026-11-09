using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>
/// Lecteurs natifs Windows en lecture seule pour les écrans Analyser/réparer et Sauvegarder : profils (ProfileList),
/// comptes (traduction SID → NTAccount), sessions WTS (wtsapi32) et dossiers AppData. ReadProfiles ne monte aucune ruche ; aucune
/// impersonation : le jeton WTS n'est jamais exposé. Seules les préférences FILPRO de l'opérateur peuvent être écrites
/// sur demande explicite. Le registre chargé est lu sous HKU\SID. Pour un profil entièrement déconnecté, ReadSoftware
/// peut lire une copie privée de NTUSER.DAT avec RegLoadAppKey, ou par un montage RegLoadKey possédé de cette seule
/// copie quand Windows en refuse le descripteur (87, 1009) ; l'original et les ruches Windows restent intacts.
/// </summary>
public sealed class WindowsPosteSource(TimeProvider clock, string? selectionDirectory = null, string? privateHiveDirectory = null) : IFilproPosteSource
{
    private const string ProfileListPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private static readonly Regex SidPattern = new(@"^S-1-(?:5-21-\d+-\d+-\d+-\d+|12-1-\d+-\d+-\d+-\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int MaxProfiles = 4096;

    public bool Demonstration => false;

    internal static SessionEvidence ReadNativeSessionEvidence(string sid, CancellationToken cancellationToken)
    {
        SoftwareDiscoveryInputs.ValidateSid(sid);
        return SessionEvidenceReader.Derive(sid, WtsRead(cancellationToken));
    }

    public string ReadCurrentAccountSid(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            throw CurrentAccountUnavailable();
        }
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (string.IsNullOrEmpty(sid))
            {
                throw CurrentAccountUnavailable();
            }
            return sid;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException
            or System.Security.SecurityException or ArgumentException)
        {
            throw CurrentAccountUnavailable();
        }
    }

    public ProfileInventory ReadProfiles(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProfileInventory(SessionReadStatus.Unavailable, "API Windows indisponible sur cet hôte.", []);
        }

        var sessions = WtsRead(cancellationToken);
        var sids = EnumerateProfileSids(out var listError);
        if (listError is not null)
        {
            return new ProfileInventory(sessions.ReadStatus,
                string.Join(" ", new[] { sessions.Error, listError }.Where(s => !string.IsNullOrWhiteSpace(s))), []);
        }

        var candidates = new List<ProfileCandidate>();
        var containers = WindowsProfileSafety.ReadContainers();
        var unreadable = 0;
        foreach (var sid in sids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = ReadProfileImagePath(sid);
                var account = TranslateAccount(sid);
                var loaded = ProbeLoaded(sid);
                var entra = sid.StartsWith("S-1-12-1-", StringComparison.Ordinal);
                var exists = !string.IsNullOrEmpty(path) && Directory.Exists(path);
                var central = WindowsProfileSafety.ReadCentralProfile(sid);
                var warnings = new List<string>();
                if (containers.Error is not null) warnings.Add(containers.Error);
                if (containers.Fslogix) warnings.Add("FSLogix configuré ou actif : conteneur hors du périmètre de migration locale.");
                if (central) warnings.Add("Profil itinérant détecté dans ProfileList.");
                warnings.Add("Les redirections de dossiers seront relues dans la ruche du compte avant exécution.");
                candidates.Add(new ProfileCandidate(
                    sid,
                    entra ? ProfileIdentityKind.Entra : ProfileIdentityKind.CompteLocalOuDomaine,
                    MigrationSupported: !entra && containers.Error is null && !containers.Fslogix && !central,
                    Account: account,
                    LocalPath: path,
                    Loaded: loaded,
                    Session: null!,
                    Exists: exists,
                    Warnings: warnings,
                    IsFslogix: containers.Fslogix,
                    IsCentralProfile: central,
                    HasRedirected: false,
                    Excluded: false,
                    LastUse: null));
            }
#pragma warning disable CA1031 // un profil illisible est ignoré, jamais fatal à la liste
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or FilproOperationException)
            {
                // Profil ignoré (accès ou lecture impossible) : la liste reste honnête sur ce qui a été lu.
                unreadable++;
            }
#pragma warning restore CA1031
        }

        var withSessions = candidates
            .Select(p => p with { Session = SessionEvidenceReader.Derive(p.Sid, sessions) })
            .ToList();
        var notice = unreadable == 0 ? sessions.Error : string.Join(" ", new[] { sessions.Error,
            unreadable + " entrée(s) de profil illisible(s) ; cette liste ne prouve pas leur absence." }.Where(s => !string.IsNullOrEmpty(s)));
        return new ProfileInventory(sessions.ReadStatus, notice, SessionEvidenceReader.Sort(withSessions));
    }

    public SoftwareInventory ReadSoftware(string sid, SoftwareQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        ArgumentNullException.ThrowIfNull(query);
        SoftwareDiscoveryInputs.ValidateSid(sid);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new SoftwareInventory(sid, [], "Lecteur Windows indisponible sur cet hôte.", new DiscoveryNotice(false, false, "hôte non Windows", "hôte non Windows"), clock.GetUtcNow());
        }

        cancellationToken.ThrowIfCancellationRequested();
        var last = ReadLastSelection(sid, cancellationToken);
        // Tous les chemins supplémentaires/mémorisés sont validés avant les lectures dans le profil.
        var additionalDocument = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(new SoftwareSelectionDocument(
            SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind, clock.GetUtcNow(),
            [.. query.AdditionalFiles.Select(item => item with { Category = SoftwareCategory.Modele }), .. (last?.Files ?? [])],
            [.. query.AdditionalRegistryKeys.Select(key => new SoftwareItem(SoftwareScope.Hkcu, key, key,
                SoftwareCategory.Modele, false, false, false, null, false, false)), .. (last?.Registry ?? [])],
            SoftwareSelectionCodec.DefaultPreferences)));
        var root = ReadProfileImagePath(sid);
        var files = new WindowsSoftwareFileReader(clock: clock).Read(root, additionalDocument.Files, cancellationToken);
        var registry = new WindowsLoadedRegistryReader(clock: clock).Read(sid, additionalDocument.Registry, cancellationToken);
        if (!registry.Complete && ProbeLoaded(sid) == false && !string.IsNullOrWhiteSpace(root))
            registry = ReadPrivateOfflineRegistry(sid, root, additionalDocument.Registry, cancellationToken);
        var groups = LastSelectionRules.Apply(sid, files.Items, registry.Items, last);
        var discovery = new DiscoveryNotice(files.Complete, registry.Complete, files.Error, registry.Error);
        return new SoftwareInventory(sid, groups, registry.Error, discovery, clock.GetUtcNow());
    }

    private SoftwareDiscoveryResult ReadPrivateOfflineRegistry(string sid, string root, IReadOnlyList<SoftwareItem> additional,
        CancellationToken cancellationToken)
    {
        var sessions = WtsRead(cancellationToken);
        var evidence = SessionEvidenceReader.Derive(sid, sessions);
        if (sessions.ReadStatus != SessionReadStatus.Complete || evidence.State != SessionState.None || evidence.Count != 0)
            return new([], false, "La lecture privée exige un profil entièrement déconnecté et des sessions connues.");
        // L'hôte fournit le dossier de données réellement choisi pour cette identité ; le repli local reste l'ancien emplacement.
        var directory = privateHiveDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FILPRO", "PrivateHives");
        if (!Path.IsPathFullyQualified(directory))
            return new([], false, "Dossier local des copies privées indisponible.");
        var manager = new OfflineUserHiveManager(new ExecutionFileSystem(), new WindowsOfflineHiveNative(), directory);
        try
        {
            var lease = manager.OpenCopyAsync(Path.Combine(root, "NTUSER.DAT"), cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
            try
            {
                return new WindowsLoadedRegistryReader(new PrivateHiveView(sid, lease.Root), clock: clock)
                    .Read(sid, additional, cancellationToken);
            }
            finally { lease.DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult(); }
        }
        catch (FilproOperationException ex) when (ex.Code != "hive.cleanup") { return new([], false, ex.Message); }
        catch (Exception ex) when (SoftwareDiscoveryInputs.IsReadFailure(ex))
        { return new([], false, "La copie privée de la ruche n'a pas pu être lue ; aucune sélection incomplète ne sera enregistrée."); }
        finally { manager.CleanupAsync().ConfigureAwait(false).GetAwaiter().GetResult(); }
    }

    private sealed class PrivateHiveView(string expectedSid, IReadOnlyUserRegistryKey root) : ILoadedUserRegistry
    {
        public RegistryKeyReadResult OpenUserHive(string sid) => sid == expectedSid ? new(0, root) : new(5, null);
    }

    public LastSelection? ReadLastSelection(string sid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSelectionPath(sid);
        return SoftwareSelectionStore.Read(path)?.ToLastSelection(sid);
    }

    public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSelectionPath(selection.Sid);
        var dated = selection with { CapturedAt = clock.GetUtcNow() };
        SoftwareSelectionStore.Save(path, dated, cancellationToken: cancellationToken);
        // Ne pas annoncer une mémorisation réussie si le document publié ne peut plus être relu.
        return SoftwareSelectionStore.Read(path)?.ToLastSelection(selection.Sid)
            ?? throw new FilproOperationException("selection.store", "Sélection enregistrée mais relecture impossible ; vérifiez les réglages avant de poursuivre.");
    }

    private string GetSelectionPath(string sid)
    {
        var directory = selectionDirectory;
        if (directory is null)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
            {
                throw new FilproOperationException("selection.store", "Dossier local des préférences FILPRO indisponible.");
            }
            directory = Path.Combine(local, "FILPRO", "Selections");
        }
        return SoftwareSelectionStore.GetProfilePath(directory, sid);
    }

    private static FilproOperationException CurrentAccountUnavailable() => new(FilproErrors.SourceUnavailable,
        "Identité du compte exécutant FILPRO indisponible : préparation de restauration refusée.");

    private static string TranslateAccount(string sid)
    {
        try
        {
            return ((NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount))).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or InvalidOperationException)
        {
            return sid;
        }
    }

    private static bool? ProbeLoaded(string sid)
    {
        var code = NativeMethods.RegOpenKeyEx(NativeMethods.HKU, sid, 0, NativeMethods.KeyRead, out var h);
        if (code == 0)
        {
            _ = NativeMethods.RegCloseKey(h);
            return true;
        }
        return code == NativeMethods.ErrorFileNotFound ? false : (bool?)null;
    }

    private static string? ReadProfileImagePath(string sid)
    {
        var code = NativeMethods.RegOpenKeyEx(NativeMethods.HKLM, ProfileListPath + "\\" + sid, 0, NativeMethods.KeyRead, out var h);
        if (code != 0)
        {
            return null;
        }
        try
        {
            var size = 0;
            if (NativeMethods.RegQueryValueEx(h, "ProfileImagePath", IntPtr.Zero, out _, null, ref size) != 0 || size <= 0)
            {
                return null;
            }
            var buffer = new byte[size];
            if (NativeMethods.RegQueryValueEx(h, "ProfileImagePath", IntPtr.Zero, out _, buffer, ref size) != 0)
            {
                return null;
            }
            var raw = Encoding.Unicode.GetString(buffer, 0, size & ~1).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(raw) ? null : Environment.ExpandEnvironmentVariables(raw);
        }
        finally
        {
            _ = NativeMethods.RegCloseKey(h);
        }
    }

    private static IReadOnlyList<string> EnumerateProfileSids(out string? error)
    {
        error = null;
        var result = new List<string>();
        var code = NativeMethods.RegOpenKeyEx(NativeMethods.HKLM, ProfileListPath, 0, NativeMethods.KeyRead, out var h);
        if (code != 0)
        {
            error = "Liste des profils illisible (Win32 " + code.ToString(CultureInfo.InvariantCulture) + ").";
            return result;
        }
        try
        {
            var index = 0;
            while (result.Count < MaxProfiles)
            {
                var name = new StringBuilder(256);
                var len = name.Capacity;
                var rc = NativeMethods.RegEnumKeyEx(h, index, name, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (rc == NativeMethods.ErrorNoMoreItems)
                {
                    break;
                }
                if (rc != 0)
                {
                    error = "Lecture de la liste des profils interrompue (Win32 " + rc.ToString(CultureInfo.InvariantCulture) + ").";
                    break;
                }
                var sid = name.ToString(0, len);
                if (SidPattern.IsMatch(sid))
                {
                    result.Add(sid);
                }
                index++;
            }
        }
        finally
        {
            _ = NativeMethods.RegCloseKey(h);
        }
        return result;
    }

    private static WindowsSessionInventory WtsRead(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            return new WindowsSessionInventory(SessionReadStatus.Unavailable, [], "API WTS indisponible sur cet hôte.");
        }
        var buffer = IntPtr.Zero;
        var rows = new List<WindowsSession>();
        try
        {
            if (!NativeMethods.WTSEnumerateSessions(IntPtr.Zero, 0, 1, out buffer, out var count))
            {
                return new WindowsSessionInventory(SessionReadStatus.Unavailable, [], "Énumération WTS indisponible (Win32 " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ").");
            }
            if (count < 0 || count > 4096 || (count > 0 && buffer == IntPtr.Zero))
            {
                return new WindowsSessionInventory(SessionReadStatus.Unavailable, [], "Inventaire WTS incohérent ou trop grand.");
            }
            var size = Marshal.SizeOf<NativeMethods.WtsSessionInfo>();
            var complete = true;
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = Marshal.PtrToStructure<NativeMethods.WtsSessionInfo>(IntPtr.Add(buffer, i * size));
                if (info.SessionId <= 0 || info.State == 6)
                {
                    continue;
                }
                var user = ReadSessionText(info.SessionId, 5);
                var domain = ReadSessionText(info.SessionId, 7);
                var sid = ReadTokenSid(info.SessionId);
                var source = string.Empty;
                if (sid.Length > 0)
                {
                    source = "WTS token SID";
                }
                else if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(domain))
                {
                    try
                    {
                        sid = ((SecurityIdentifier)new NTAccount(domain, user).Translate(typeof(SecurityIdentifier))).Value;
                        source = "WTS account resolved to SID";
                    }
                    catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or InvalidOperationException)
                    {
                        sid = string.Empty;
                    }
                }
                if (sid.Length == 0 && string.IsNullOrEmpty(user) && info.State != 0 && info.State != 4)
                {
                    continue;
                }
                var account = (string.IsNullOrEmpty(domain) ? string.Empty : domain + "\\") + (user ?? string.Empty);
                rows.Add(new WindowsSession(info.SessionId, info.State, sid.Length > 0 ? sid : null, account, source));
                if (sid.Length == 0)
                {
                    complete = false;
                }
            }
            var status = complete ? SessionReadStatus.Complete : (rows.Count > 0 ? SessionReadStatus.Partial : SessionReadStatus.Unavailable);
            var error = complete ? null : "Une session WTS n’a pu être rattachée à un SID ; absence de session non prouvée.";
            return new WindowsSessionInventory(status, rows, error);
        }
#pragma warning disable CA1031 // toute erreur WTS devient un état indisponible, jamais une liste de sessions inventée
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WindowsSessionInventory(SessionReadStatus.Unavailable, [], "Lecture WTS incomplète : " + ex.GetType().Name + ".");
        }
#pragma warning restore CA1031
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NativeMethods.WTSFreeMemory(buffer);
            }
        }
    }

    private static string? ReadSessionText(int sessionId, int infoClass)
    {
        if (!NativeMethods.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var value, out var bytes))
        {
            return null;
        }
        try
        {
            if (bytes < 0 || bytes > 65536 || (bytes % 2) != 0 || value == IntPtr.Zero)
            {
                return bytes == 0 ? string.Empty : null;
            }
            return Marshal.PtrToStringUni(value, bytes / 2).TrimEnd('\0');
        }
        finally
        {
            NativeMethods.WTSFreeMemory(value);
        }
    }

    private static string ReadTokenSid(int sessionId)
    {
        if (!NativeMethods.WTSQueryUserToken(sessionId, out var token) || token == IntPtr.Zero)
        {
            return string.Empty;
        }
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User?.Value ?? string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or ArgumentException)
        {
            return string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static class NativeMethods
    {
        public const int KeyRead = 0x20019;
        public const int ErrorFileNotFound = 2;
        public const int ErrorNoMoreItems = 259;

        public static readonly IntPtr HKLM = new(unchecked((int)0x80000002));
        public static readonly IntPtr HKU = new(unchecked((int)0x80000003));

        [StructLayout(LayoutKind.Sequential)]
        public struct WtsSessionInfo
        {
            public int SessionId;
            public IntPtr WinStationName;
            public int State;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegOpenKeyEx(IntPtr hKey, string? lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegEnumKeyEx(IntPtr hKey, int dwIndex, StringBuilder lpName, ref int lpcchName, IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, out int lpType, byte[]? lpData, ref int lpcbData);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
        public static extern bool WTSEnumerateSessions(IntPtr hServer, int reserved, int version, out IntPtr ppSessionInfo, out int pCount);

        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
        public static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int infoClass, out IntPtr ppBuffer, out int pBytesReturned);

        [DllImport("wtsapi32.dll")]
        public static extern void WTSFreeMemory(IntPtr pMemory);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        public static extern bool WTSQueryUserToken(int sessionId, out IntPtr phToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
