using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Filpro.App;

namespace Filpro.Tests;

internal sealed class SessionAccessTests
{
    private const string Stranger = "S-1-5-21-1-2-3-1009";
    private const string Guests = "S-1-5-32-546";
    private const string Interactive = "S-1-5-4";
    private const uint ReadExecute = SessionAccess.ReadData | SessionAccess.Execute;

    [Test]
    public void DroitsEffectifsEvaluesPourUnCompte()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User!.Value;
        var root = NewRoot();
        try
        {
            var readable = Path.Combine(root, "lisible.txt");
            File.WriteAllText(readable, "x");
            var granted = SessionAccess.Granted(me, readable);
            SkipWhenUndetermined(granted, identity);
            Check.True((granted!.Value & ReadExecute) == ReadExecute, "fichier du compte courant : lecture et exécution accordées");
            var closed = Path.Combine(root, "ferme.txt");
            File.WriteAllText(closed, "x");
            SetAcl(closed, (Stranger, FileSystemRights.FullControl, AccessControlType.Allow));
            Check.True(SessionAccess.Granted(me, closed) is { } owner && (owner & SessionAccess.ReadData) == 0,
                "fichier réservé à un autre compte : aucune lecture, seuls les droits implicites du propriétaire");
            Check.Null(SessionAccess.Granted(me, Path.Combine(root, "absent.txt")), "objet absent : indéterminé, rien n’est conclu");
            Check.Null(SessionAccess.Granted("pas-un-sid", readable), "SID illisible : indéterminé, rien n’est conclu");
        }
        finally { Cleanup(root); }
    }

    [Test]
    public void DroitsEffectifsCalculesAvecLesGroupesDuCompte()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User!.Value;
        // Invité local de ce poste (domaine des comptes locaux lu auprès de la LSA), membre du groupe local Invités, même
        // sur un poste joint à un domaine.
        if (MachineSid() is not { } machine) { Check.Skip("Domaine des comptes locaux illisible."); return; }
        var guest = new SecurityIdentifier(WellKnownSidType.AccountGuestSid, machine).Value;
        var root = NewRoot();
        try
        {
            var file = Path.Combine(root, "groupes.txt");
            File.WriteAllText(file, "x");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow));
            var none = SessionAccess.Granted(guest, file);
            if (none is null) Check.Skip("Compte Invité non évaluable par Authz sur ce poste.");
            Check.Equal<uint?>(0u, none, "autre compte sans entrée ni propriété : aucun droit (refus d’Authz)");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), (Guests, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.True(SessionAccess.Granted(guest, file) is { } viaGroup && (viaGroup & ReadExecute) == ReadExecute,
                "lecture accordée au compte par son groupe Invités");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), (Guests, FileSystemRights.ReadData, AccessControlType.Deny),
                (Guests, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.True(SessionAccess.Granted(guest, file) is { } denied && (denied & SessionAccess.ReadData) == 0, "refus du groupe appliqué au compte");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), (Interactive, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.True(SessionAccess.Granted(guest, file) is { } interactive && (interactive & ReadExecute) == ReadExecute,
                "lecture accordée à INTERACTIVE : comptée comme pour une session ouverte");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), ("S-1-2-1", FileSystemRights.ReadData, AccessControlType.Deny),
                (Guests, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.Null(SessionAccess.Granted(guest, file), "refus visant l’ouverture console, que la session n’a peut-être pas : indéterminé");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), ("S-1-5-2", FileSystemRights.ReadData, AccessControlType.Deny),
                (Guests, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.Null(SessionAccess.Granted(guest, file), "refus visant NETWORK, absent de la session mais possible dans le contexte S4U : indéterminé");
            SetAcl(file, (me, FileSystemRights.FullControl, AccessControlType.Allow), (Interactive, FileSystemRights.ReadData, AccessControlType.Deny),
                (Guests, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            Check.True(SessionAccess.Granted(guest, file) is { } interactiveDenied && (interactiveDenied & SessionAccess.ReadData) == 0,
                "refus visant INTERACTIVE, que toute session porte : appliqué");
        }
        finally { Cleanup(root); }
    }

    [Test]
    public void MoteurIllisiblePourLaSessionNomme()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User!.Value;
        var root = NewRoot();
        try
        {
            var runtime = Directory.CreateDirectory(Path.Combine(root, "runtime")).FullName;
            var web = Directory.CreateDirectory(Path.Combine(root, "web")).FullName;
            var engine = Path.Combine(runtime, "msedgewebview2.exe");
            var index = Path.Combine(web, "index.html");
            File.WriteAllText(engine, "x");
            File.WriteAllText(index, "x");
            SkipWhenUndetermined(SessionAccess.Granted(me, engine), identity);
            Check.Null(SessionAccess.UnreadableEngine(me, runtime, web), "moteur et interface lisibles : aucun motif");
            SetAcl(engine, (Stranger, FileSystemRights.FullControl, AccessControlType.Allow));
            Check.Equal(engine, SessionAccess.UnreadableEngine(me, runtime, web), "exécutable du moteur illisible : nommé");
            Check.Null(SessionAccess.UnreadableEngine(me, null, web), "moteur de Windows : seule l’interface du pack est vérifiée");
            SetAcl(index, (Stranger, FileSystemRights.FullControl, AccessControlType.Allow));
            Check.Equal(index, SessionAccess.UnreadableEngine(me, null, web), "page d’accueil illisible : nommée");
            Check.Null(SessionAccess.UnreadableEngine("pas-un-sid", runtime, web), "compte indéterminable : aucun motif inventé");
            Check.Null(SessionAccess.UnreadableEngine(me, @"\\serveur\partage\runtime", web), "pack sur un partage réseau : rien n’est conclu");
        }
        finally { Cleanup(root); }
    }

    [Test]
    public async Task ConstatDeDemarrageSansJamaisInterrompre()
    {
        var grace = TimeSpan.FromMilliseconds(100);
        var slow = TimeSpan.FromMilliseconds(400);
        Check.Null(await SessionAccess.WatchAsync(Task.CompletedTask, Task.FromResult<string?>("moteur"), grace, slow, default),
            "démarrage déjà terminé : aucun constat");

        var pending = new TaskCompletionSource();
        var named = await SessionAccess.WatchAsync(pending.Task, Task.FromResult<string?>("moteur"), grace, slow, default);
        Check.Equal("moteur", named?.UnreadablePath, "illisible et toujours en attente après la grâce : élément nommé");
        Check.False(pending.Task.IsCompleted, "le démarrage n’est jamais interrompu");

        var late = new TaskCompletionSource();
        var watch = SessionAccess.WatchAsync(late.Task, Task.FromResult<string?>("moteur"), TimeSpan.FromSeconds(5), slow, default);
        late.SetResult();
        Check.Null(await watch, "démarrage abouti pendant la grâce : aucun message, Authz n’était qu’une estimation");

        Check.True(await SessionAccess.WatchAsync(new TaskCompletionSource().Task, Task.FromResult<string?>(null), grace, slow, default) is { UnreadablePath: null },
            "rien d’illisible, démarrage toujours en cours : lenteur signalée");

        var clock = Stopwatch.StartNew();
        var stalled = await SessionAccess.WatchAsync(new TaskCompletionSource().Task, new TaskCompletionSource<string?>().Task, grace, slow, default);
        Check.True(stalled is { UnreadablePath: null } && clock.Elapsed < TimeSpan.FromSeconds(5),
            "contrôle bloqué : lenteur signalée à l’heure, comptée dès le départ");

        Check.True(await SessionAccess.WatchAsync(new TaskCompletionSource().Task, Task.FromException<string?>(new InvalidOperationException()), grace, slow, default)
            is { UnreadablePath: null }, "contrôle en échec : simple lenteur, aucun motif inventé");

        using var closing = new CancellationTokenSource();
        closing.Cancel();
        Check.Null(await SessionAccess.WatchAsync(new TaskCompletionSource().Task, Task.FromResult<string?>("moteur"), grace, slow, closing.Token),
            "fermeture en cours : aucun message");
    }

    // Authz calcule les groupes d'un compte de domaine auprès d'un contrôleur : sans lui, le résultat reste indéterminé.
    // Un compte local (agent de CI) doit toujours être évalué.
    private static void SkipWhenUndetermined(uint? granted, WindowsIdentity identity)
    {
        if (granted is not null) return;
        if (identity.Name.StartsWith(Environment.MachineName + "\\", StringComparison.OrdinalIgnoreCase))
            Check.True(false, "compte local évalué par Authz");
        Check.Skip("Compte de domaine sans contrôleur joignable : droits effectifs indéterminables ici.");
    }

    private static SecurityIdentifier? MachineSid()
    {
        var attributes = new LsaObjectAttributes { Length = Marshal.SizeOf<LsaObjectAttributes>() };
        if (LsaOpenPolicy(IntPtr.Zero, ref attributes, PolicyViewLocalInformation, out var policy) != 0) return null;
        try
        {
            if (LsaQueryInformationPolicy(policy, PolicyAccountDomainInformation, out var buffer) != 0) return null;
            try
            {
                var info = Marshal.PtrToStructure<AccountDomainInfo>(buffer);
                return info.DomainSid == IntPtr.Zero ? null : new SecurityIdentifier(info.DomainSid);
            }
            finally { LsaFreeMemory(buffer); }
        }
        finally { LsaClose(policy); }
    }

    private const uint PolicyViewLocalInformation = 0x1;
    private const int PolicyAccountDomainInformation = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public int Length;
        public IntPtr RootDirectory, ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccountDomainInfo { public LsaUnicodeString DomainName; public IntPtr DomainSid; }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes attributes, uint access, out IntPtr policy);

    [DllImport("advapi32.dll")]
    private static extern uint LsaQueryInformationPolicy(IntPtr policy, int informationClass, out IntPtr buffer);

    [DllImport("advapi32.dll")]
    private static extern uint LsaFreeMemory(IntPtr buffer);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr policy);

    private static string NewRoot() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filpro-access-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void SetDirectoryAcl(string directory, params (string Sid, FileSystemRights Rights)[] rules)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var rule in rules)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(rule.Sid), rule.Rights, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    private static void SetAcl(string file, params (string Sid, FileSystemRights Rights, AccessControlType Type)[] rules)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var rule in rules)
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(rule.Sid), rule.Rights, rule.Type));
        new FileInfo(file).SetAccessControl(security);
    }

    private static void Cleanup(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
