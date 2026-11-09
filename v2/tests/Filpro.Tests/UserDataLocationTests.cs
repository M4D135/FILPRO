using System.Security.AccessControl;
using Filpro.App;

namespace Filpro.Tests;

internal sealed class UserDataLocationTests
{
    private const string Admins = "S-1-5-32-544";
    private const string SystemSid = "S-1-5-18";
    private const string Users = "S-1-5-32-545";
    private const string Me = "S-1-5-21-1-2-3-1001";

    [Test]
    public void DossierDeSecoursReserveASystemEtAuxAdministrateurs()
    {
        var full = new[] { (SystemSid, true, FileSystemRights.FullControl), (Admins, true, FileSystemRights.FullControl) };
        Check.Null(UserDataLocation.ProtectionProblem(Admins, Me, true, full), "protection créée par FILPRO acceptée");
        Check.Null(UserDataLocation.ProtectionProblem(Me, Me, true, full), "un administrateur peut posséder son dossier");
        Check.Null(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, true, FileSystemRights.ReadAndExecute))),
            "une lecture seule pour les utilisateurs ne donne aucune écriture");
        Check.Null(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, false, FileSystemRights.FullControl))),
            "un refus explicite ne donne aucun droit");
    }

    [Test]
    public void DossierDeSecoursPrepareParUnAutreCompteEstRefuse()
    {
        var full = new[] { (SystemSid, true, FileSystemRights.FullControl), (Admins, true, FileSystemRights.FullControl) };
        Check.NotNull(UserDataLocation.ProtectionProblem(Users, Me, true, full), "propriétaire étranger refusé");
        Check.NotNull(UserDataLocation.ProtectionProblem("S-1-5-21-1-2-3-1002", Me, true, full), "dossier d’un autre compte refusé");
        Check.NotNull(UserDataLocation.ProtectionProblem(Admins, Me, false, full), "droits hérités de ProgramData refusés");
        foreach (var right in new[] { FileSystemRights.WriteData, FileSystemRights.AppendData, FileSystemRights.Delete,
            FileSystemRights.DeleteSubdirectoriesAndFiles, FileSystemRights.ChangePermissions, FileSystemRights.TakeOwnership, FileSystemRights.Modify })
            Check.NotNull(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, true, right))), "écriture d’un tiers refusée : " + right);
        Check.NotNull(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Me, true, FileSystemRights.Modify))),
            "même le compte courant n’a pas d’écriture propre hors du groupe Administrateurs");
    }

    [Test]
    public void DossierDeSecoursJugeLesDroitsGeneriques()
    {
        var full = new[] { (SystemSid, true, FileSystemRights.FullControl), (Admins, true, FileSystemRights.FullControl) };
        foreach (var right in new[] { (FileSystemRights)0x40000000, (FileSystemRights)0x10000000, (FileSystemRights)0x02000000, (FileSystemRights)0x01000000,
            FileSystemRights.WriteAttributes, FileSystemRights.WriteExtendedAttributes })
            Check.NotNull(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, true, right))),
                "droit au-delà de la lecture refusé pour un tiers : 0x" + ((int)right).ToString("X"));
        Check.Null(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, true, unchecked((FileSystemRights)0xA0000000)))),
            "lecture et exécution génériques acceptées");
        Check.Null(UserDataLocation.ProtectionProblem(Admins, Me, true, full.Append((Users, false, (FileSystemRights)0x10000000))),
            "un refus générique ne donne aucun droit");
    }

    [Test]
    public void SondeEcritRelitEtNeLaisseRien()
    {
        var root = Path.Combine(Path.GetTempPath(), "filpro-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, "FILPRO");
            Check.True(UserDataLocation.TryProbe(folder, refuseLinks: true, out var reason), "dossier inscriptible accepté : " + reason);
            Check.Equal(0, Directory.GetFileSystemEntries(folder).Length, "aucun fichier d’essai laissé");
            var blocker = Path.Combine(root, "fichier");
            File.WriteAllText(blocker, "x");
            Check.False(UserDataLocation.TryProbe(Path.Combine(blocker, "FILPRO"), refuseLinks: false, out reason), "dossier impossible à créer refusé");
            Check.True(reason.Length > 0, "le refus est expliqué pour le journal");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
