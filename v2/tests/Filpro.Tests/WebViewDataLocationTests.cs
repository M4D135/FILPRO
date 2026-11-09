using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Filpro.App;
using Filpro.Core;
using static Filpro.App.WebViewDataLocation;
using AccessEntry = Filpro.App.UserDataLocation.AccessEntry;

namespace Filpro.Tests;

internal sealed class WebViewDataLocationTests
{
    private const string Admins = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";
    private const string Users = "S-1-5-32-545";
    private const string Session = "S-1-5-21-1-2-3-1001";
    private const string Admin = "S-1-5-21-1-2-3-1002";
    private const string Stranger = "S-1-5-21-1-2-3-1009";
    private const InheritanceFlags All = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    private const FileSystemRights SessionModify = FileSystemRights.Modify | FileSystemRights.Synchronize;
    private const FileSystemRights GenericRead = unchecked((FileSystemRights)0x80000000);
    private const FileSystemRights GenericWrite = (FileSystemRights)0x40000000;
    private const FileSystemRights GenericExecute = (FileSystemRights)0x20000000;
    private const FileSystemRights GenericAll = (FileSystemRights)0x10000000;
    private const FileSystemRights MaximumAllowed = (FileSystemRights)0x02000000;

    [Test]
    public void ProfilDAffichageDeplaceSeulementPourUneSessionDUnAutreCompte()
    {
        Check.False(NeedsSessionFolder(false, Admin, Session), "sans élévation, WebView2 tourne sous le compte de FILPRO");
        Check.False(NeedsSessionFolder(true, Admin, Admin), "élévation du compte de la session : dossier inchangé");
        Check.False(NeedsSessionFolder(true, Admin, Admin.ToLowerInvariant()), "comparaison de SID sans casse");
        Check.False(NeedsSessionFolder(true, Admin, null), "session introuvable : comportement historique conservé");
        Check.True(NeedsSessionFolder(true, Admin, Session), "UAC avec un autre administrateur : dossier de la session");
    }

    [Test]
    public void DossierFinalReserveALaSessionSansDroitSurLesAutorisations()
    {
        AccessEntry[] full = [Ace(SystemSid, FileSystemRights.FullControl), Ace(Admins, FileSystemRights.FullControl)];
        AccessEntry[] created = [.. full, Ace(Session, SessionModify)];
        Check.Null(SessionFolderProblem(Admins, Session, true, created), "protection créée par FILPRO acceptée");
        Check.Null(SessionFolderProblem(SystemSid, Session, true, created), "propriétaire SYSTEM accepté");
        Check.Null(SessionFolderProblem(Admins, Session.ToLowerInvariant(), true, created), "SID de session comparé sans casse");
        Check.NotNull(SessionFolderProblem(Session, Session, true, created), "dossier possédé par la session refusé");
        Check.NotNull(SessionFolderProblem(null, Session, true, created), "propriétaire illisible refusé");
        Check.NotNull(SessionFolderProblem(Admins, Session, false, created), "héritage du parent refusé");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, full), "session sans écriture refusée");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Users, FileSystemRights.WriteData)]), "écriture d’un tiers refusée");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. full, Ace(Session, FileSystemRights.FullControl)]), "contrôle total de la session refusé");
        foreach (var right in new[] { FileSystemRights.ChangePermissions, FileSystemRights.TakeOwnership })
            Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Session, right)]), "la session ne change pas les autorisations : " + right);
        Check.Null(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Users, FileSystemRights.ReadAndExecute)]),
            "une lecture seule pour les utilisateurs ne donne aucune écriture");
    }

    [Test]
    public void DossierFinalAvecRefusOuHeritageDifferentRefuse()
    {
        AccessEntry[] partial = [Ace(SystemSid, FileSystemRights.FullControl), Ace(Admins, FileSystemRights.FullControl)];
        AccessEntry[] created = [.. partial, Ace(Session, SessionModify)];
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Session, FileSystemRights.WriteData, allow: false)]),
            "refus d’écriture pour la session : protection différente refusée");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Stranger, FileSystemRights.ReadData, allow: false)]),
            "même un refus de lecture pour un tiers est une protection différente");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(SystemSid, FileSystemRights.ReadData, inherited: true)]),
            "entrée héritée refusée même sous un héritage coupé");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, SessionModify, inheritance: InheritanceFlags.None)]),
            "droits de la session limités au dossier : sous-dossiers de WebView2 inaccessibles");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, SessionModify, inheritance: InheritanceFlags.ObjectInherit)]),
            "droits de la session sans les sous-dossiers refusés");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, SessionModify, propagation: PropagationFlags.InheritOnly)]),
            "droits de la session absents du dossier lui-même refusés");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, SessionModify, propagation: PropagationFlags.NoPropagateInherit)]),
            "droits de la session arrêtés au premier niveau refusés");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Session, FileSystemRights.Read, inheritance: InheritanceFlags.None)]),
            "une entrée de la session sans héritage complet est refusée, même en lecture");
        Check.Null(SessionFolderProblem(Admins, Session, true,
                [.. partial, Ace(Session, FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Synchronize), Ace(Session, FileSystemRights.ReadAndExecute | FileSystemRights.Delete)]),
            "Modification et Synchronize répartis sur deux entrées complètes acceptés");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, FileSystemRights.Modify)]),
            "Modification sans Synchronize refusée : WebView2 ne pourrait rouvrir aucun fichier");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, FileSystemRights.WriteData), Ace(Session, FileSystemRights.AppendData)]),
            "écriture partielle sans suppression ni lecture refusée");
    }

    [Test]
    public void DroitsGeneriquesJugesSelonLeurSensPourUnFichier()
    {
        AccessEntry[] created = [Ace(SystemSid, FileSystemRights.FullControl), Ace(Admins, FileSystemRights.FullControl), Ace(Session, SessionModify)];
        foreach (var right in new[] { GenericWrite, GenericAll, MaximumAllowed })
            Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Users, right, propagation: PropagationFlags.InheritOnly)]),
                "droit générique transmis aux fichiers d’un tiers refusé : 0x" + ((int)right).ToString("X"));
        Check.Null(SessionFolderProblem(Admins, Session, true, [.. created, Ace(Users, GenericRead | GenericExecute, propagation: PropagationFlags.InheritOnly)]),
            "lecture générique d’un tiers acceptée");
        AccessEntry[] partial = [created[0], created[1]];
        Check.NotNull(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, GenericAll)]),
            "GENERIC_ALL de la session donne les autorisations : refusé");
        Check.Null(SessionFolderProblem(Admins, Session, true, [.. partial, Ace(Session, GenericRead | GenericWrite | GenericExecute | FileSystemRights.Delete)]),
            "lecture, écriture et exécution génériques plus suppression valent Modification et Synchronize");
        Check.Equal(FileSystemRights.FullControl, UserDataLocation.Specific(GenericAll), "GENERIC_ALL vaut FILE_ALL_ACCESS");
    }

    [Test]
    public void ListeDAccesBruteSansEntreeInvisible()
    {
        var system = new CommonAce(AceFlags.ObjectInherit | AceFlags.ContainerInherit, AceQualifier.AccessAllowed, (int)FileSystemRights.FullControl,
            new SecurityIdentifier(SystemSid), false, null);
        Check.Null(UserDataLocation.ReadEntries(Descriptor(system), out var entries), "entrée simple lue");
        Check.True(entries.Length == 1 && entries[0] == Ace(SystemSid, FileSystemRights.FullControl), "entrée relue fidèlement");
        var session = new CommonAce(AceFlags.ObjectInherit | AceFlags.ContainerInherit, AceQualifier.AccessAllowed, (int)SessionModify,
            new SecurityIdentifier(Session), false, null);
        Check.Null(UserDataLocation.ReadEntries(Descriptor(system, session), out entries), "liste conforme lue");
        Check.Null(SessionFolderProblem(Admins, Session, true, entries), "liste conforme acceptée");
        var conditional = new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, (int)FileSystemRights.Modify, new SecurityIdentifier(Users), true, new byte[4]);
        Check.Null(UserDataLocation.ReadEntries(Descriptor(system, session, conditional), out entries), "entrée conditionnelle lue comme accordée");
        Check.True(entries.Length == 3 && entries.Contains(Ace(Users, FileSystemRights.Modify, inheritance: InheritanceFlags.None)),
            "l’entrée conditionnelle reste visible pour la règle");
        Check.NotNull(SessionFolderProblem(Admins, Session, true, entries), "écriture conditionnelle d’un tiers refusée");
        var objectAce = new ObjectAce(AceFlags.None, AceQualifier.AccessAllowed, (int)FileSystemRights.ReadData, new SecurityIdentifier(Users),
            ObjectAceFlags.None, Guid.Empty, Guid.Empty, false, null);
        Check.NotNull(UserDataLocation.ReadEntries(Descriptor(system, objectAce), out _), "entrée d’objet refusée");
        Check.NotNull(UserDataLocation.ReadEntries(Descriptor(system, new CustomAce((AceType)200, AceFlags.None, new byte[4])), out _), "entrée inconnue refusée");
        var noAcl = new RawSecurityDescriptor(ControlFlags.SelfRelative, new SecurityIdentifier(Admins), null, null, null);
        Check.NotNull(UserDataLocation.ReadEntries(noAcl, out _), "liste d’accès absente refusée");
    }

    [Test]
    public void NomDeProfilVerifieDansLesDeuxEmplacements()
    {
        Check.Equal(2, ProfileSegments(Path.Combine("WebView2-Simulation", "generation-1")).Length, "profil de démonstration en deux segments");
        foreach (var bad in new[] { "", ".", "..", Path.Combine("..", "x"), "WebView2\\", "a/../b", "C:\\x", "x*y" })
            Check.Throws<ArgumentException>(() => ProfileSegments(bad), "nom de profil refusé : " + bad);
    }

    [Test]
    public void CompteDeLaSessionRenduSeulementApresPreparationReussie()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-webview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User!;
            var local = PrepareProfile(Path.Combine("WebView2-Simulation", "g1"), false, user, Session, root, root);
            Check.Equal(Path.Combine(root, "WebView2-Simulation", "g1"), local.Path, "sans élévation : profil sous le dossier de FILPRO");
            Check.Null(local.SessionAccount, "aucun compte de session annoncé");
            Check.True(Directory.Exists(local.Path), "dossier local créé");
            var same = PrepareProfile("WebView2", true, user, user.Value, root, root);
            Check.Null(same.SessionAccount, "session du même compte : profil local, aucun compte annoncé");
            Check.Throws<ArgumentException>(() => PrepareProfile(Path.Combine("..", "x"), false, user, null, root, root),
                "traversée refusée aussi pour le dossier de FILPRO");
            Check.Throws<FilproOperationException>(() => PrepareProfile("WebView2", false, user, null, root, null), "sans dossier de données : refus explicite");
            Check.Throws<IOException>(() => PrepareProfile("WebView2", true, user, Session, Path.Combine(root, "absent"), root),
                "échec sous ProgramData : exception, aucun compte rendu");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public void ProfilPrepareCreeUneChaineProtegeeEtNeReparePas()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Check.Skip("La création d’un dossier possédé par les Administrateurs exige un processus élevé.");
        var root = Path.Combine(Path.GetTempPath(), "filpro-webview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var user = identity.User!;
            var leaf = Prepare(root, Users, user, Path.Combine("WebView2-Simulation", "generation-1"));
            Check.Equal(Path.Combine(root, "FILPRO-V2", "WebView2", Users, user.Value, "WebView2-Simulation", "generation-1"), leaf, "chemin attendu");
            Check.True(SessionCanWrite(leaf, Users), "le compte de la session écrit dans le dossier final");
            foreach (var parent in new[] { Path.GetDirectoryName(leaf)!, Path.Combine(root, "FILPRO-V2", "WebView2", Users) })
                Check.False(SessionCanWrite(parent, Users), "aucune écriture de la session au-dessus du dossier final : " + parent);
            Check.Equal(leaf, Prepare(root, Users, user, Path.Combine("WebView2-Simulation", "generation-1")), "dossier existant conforme réutilisé");

            var prepared = PrepareProfile("WebView2-Session", true, user, Users, root, null);
            Check.Equal(Path.Combine(root, "FILPRO-V2", "WebView2", Users, user.Value, "WebView2-Session"), prepared.Path, "profil de session sous ProgramData");
            Check.Equal(AccountName(Users), prepared.SessionAccount, "compte de la session rendu après préparation réussie");
            Check.Equal(Users, prepared.SessionSid, "SID de la session rendu avec le dossier");

            var tampered = Prepare(root, Users, user, "WebView2");
            AddRule(tampered, new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Modify, AccessControlType.Allow));
            Check.Throws<UnauthorizedAccessException>(() => Prepare(root, Users, user, "WebView2"), "protection modifiée refusée, jamais réparée");

            var denied = Prepare(root, Users, user, "WebView2-Deny");
            AddRule(denied, new FileSystemAccessRule(new SecurityIdentifier(Stranger), FileSystemRights.ReadData, All, PropagationFlags.None, AccessControlType.Deny));
            Check.Throws<UnauthorizedAccessException>(() => Prepare(root, Users, user, "WebView2-Deny"), "refus ajouté sur le disque : dossier refusé");

            var generic = Prepare(root, Users, user, "WebView2-Generic");
            Check.True(Run("icacls.exe", "\"" + generic + "\" /grant *S-1-1-0:(OI)(CI)(IO)(GW)"), "GENERIC_WRITE héritable posé par icacls");
            Check.Throws<UnauthorizedAccessException>(() => Prepare(root, Users, user, "WebView2-Generic"), "GENERIC_WRITE héritable posé sur le disque : dossier refusé");

            var parentOfLink = Path.Combine(root, "FILPRO-V2", "WebView2", Users, user.Value);
            var target = Path.Combine(root, "cible");
            Directory.CreateDirectory(target);
            Check.True(MakeJunction(Path.Combine(parentOfLink, "WebView2-Login"), target), "jonction de test créée");
            Check.Throws<UnauthorizedAccessException>(() => Prepare(root, Users, user, "WebView2-Login"), "lien posé à la place du dossier refusé");
            Check.Throws<ArgumentException>(() => Prepare(root, Users, user, Path.Combine("..", "x")), "traversée refusée");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public void ProprietairesDesProcessusRelusAuBonPas()
    {
        using var self = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        var owners = SessionProcessOwners((uint)self.SessionId);
        Check.True(owners.TryGetValue((uint)self.Id, out var sid) && sid == identity.User!.Value, "le processus de test est relu avec son propre compte");
        var alive = new HashSet<uint>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.SessionId == self.SessionId) alive.Add((uint)process.Id); }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
        var known = owners.Keys.Count(alive.Contains);
        Check.True(owners.Count >= 2 && known * 2 >= owners.Count,
            "PID relus cohérents avec les processus de la session (" + known + " sur " + owners.Count + ")");
    }

    [Test]
    public void CompteDeLaSessionLuSansErreur()
    {
        // Sur un agent de CI sans bureau, aucun compte n'est trouvé ; sur un poste, c'est un SID valide.
        var sid = SessionAccountSid();
        if (sid is not null) Check.True(new SecurityIdentifier(sid).IsAccountSid() || sid.StartsWith("S-1-5-", StringComparison.Ordinal), "SID lisible");
    }

    private static AccessEntry Ace(string sid, FileSystemRights rights, bool allow = true, InheritanceFlags inheritance = All,
        PropagationFlags propagation = PropagationFlags.None, bool inherited = false)
        => new(sid, allow, rights, inheritance, propagation, inherited);

    private static RawSecurityDescriptor Descriptor(params GenericAce[] aces)
    {
        var acl = new RawAcl(GenericAcl.AclRevision, aces.Length);
        for (var i = 0; i < aces.Length; i++) acl.InsertAce(i, aces[i]);
        return new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            new SecurityIdentifier(Admins), null, null, acl);
    }

    private static void AddRule(string path, FileSystemAccessRule rule)
    {
        var acl = new DirectoryInfo(path).GetAccessControl();
        acl.AddAccessRule(rule);
        new DirectoryInfo(path).SetAccessControl(acl);
    }

    private static bool SessionCanWrite(string path, string sid)
    {
        var rules = new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        return rules.Any(r => r.AccessControlType == AccessControlType.Allow && ((SecurityIdentifier)r.IdentityReference).Value == sid
            && (r.FileSystemRights & FileSystemRights.WriteData) != 0);
    }

    private static bool MakeJunction(string link, string target)
        => Run("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") && Directory.Exists(link);

    private static bool Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        if (process is null) return false;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
}
