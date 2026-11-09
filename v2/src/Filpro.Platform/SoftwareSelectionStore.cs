using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Filpro.Core;

namespace Filpro.Platform;

/// <summary>
/// Persistance de la sélection logiciels (fichiers + registre + modèles communs) dans le magasin de l'application.
/// <see cref="Save"/> est la seule fonction d'écriture : elle canonise et sérialise entièrement en mémoire avant toute
/// écriture, puis publie le document par un remplacement atomique (fichier temporaire exclusif, <c>Flush</c> disque,
/// <c>File.Move</c> avec reprise sur conflit de partage), fidèle à <c>Write-FilproAtomicJson</c> de la V1.
/// <see cref="Read"/> est le lecteur symétrique minimal : absent ou invalide rend <c>null</c>, jamais d'exception vers
/// l'appelant. L'écriture ne touche jamais un profil Windows, une ruche du registre ni un autre fichier : elle n'écrit que
/// le document qui lui est confié. Les préférences déjà enregistrées sont préservées sauf demande explicite contraire.
/// </summary>
public static class SoftwareSelectionStore
{
    private const string Code = "selection.store";
    private const int ReplaceAttempts = 20;
    private const int ReplaceDelayMs = 50;
    private const int MaxPathWithTemp = 259;
    private static readonly Regex ProfileSidPattern = new(@"^S-1-(?:5-21-[0-9]+-[0-9]+-[0-9]+-[0-9]+|12-1-[0-9]+-[0-9]+-[0-9]+-[0-9]+)$", RegexOptions.CultureInvariant);

    /// <summary>Chemin fixé côté fournisseur, distinct par SID. Aucun nom de compte ni SID n'est écrit dans le document.</summary>
    public static string GetProfilePath(string directory, string sid)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (string.IsNullOrEmpty(sid) || sid.Length > 184 || !ProfileSidPattern.IsMatch(sid))
        {
            throw new FilproOperationException("selection.profile", "SID de profil invalide pour la sélection mémorisée.");
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new FilproOperationException(Code, "Le dossier de sélection doit être absolu.");
        }

        var file = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))) + ".json";
        return Path.Combine(Path.GetFullPath(directory), file);
    }

    /// <summary>
    /// Enregistre atomiquement la sélection de <paramref name="selection"/> dans le document <paramref name="path"/>.
    /// Sans <paramref name="preferences"/> explicites, les préférences déjà présentes dans le document sont conservées
    /// (<paramref name="preserveExistingPreferences"/> par défaut) plutôt que réinitialisées. Rend le chemin absolu écrit.
    /// </summary>
    public static string Save(string path, LastSelection selection, SelectionPreferences? preferences = null,
        bool preserveExistingPreferences = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();

        var full = Path.GetFullPath(path);
        var effective = preferences
            ?? (preserveExistingPreferences ? ReadForPreservation(full)?.Preferences : null)
            ?? SoftwareSelectionCodec.DefaultPreferences;

        var document = new SoftwareSelectionDocument(
            SoftwareSelectionCodec.Schema,
            SoftwareSelectionCodec.Kind,
            selection.CapturedAt,
            selection.Files,
            selection.Registry,
            effective);

        // Sérialisation et validation complètes avant de toucher au document précédent.
        var bytes = SoftwareSelectionCodec.Encode(document);

        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritablePath(full);
        var directory = Path.GetDirectoryName(full) ?? throw new FilproOperationException(Code, "Dossier de la sélection indisponible.");
        Directory.CreateDirectory(directory);
        EnsureNoReparse(directory);

        var temp = full + "." + Guid.NewGuid().ToString("N").AsSpan(0, 8).ToString() + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparse(full);
            ReplaceWithRetry(temp, full, cancellationToken);
        }
        finally
        {
            // Un nettoyage qui échoue ne transforme pas un enregistrement déjà publié en échec.
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // temporaire conservé, document déjà publié
                }
            }
        }

        return full;
    }

    /// <summary>Relit le document de sélection, ou <c>null</c> s'il est absent, illisible ou de forme refusée (jamais d'exception).</summary>
    public static SoftwareSelectionDocument? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return TryRead(Path.GetFullPath(path));
    }

    private static SoftwareSelectionDocument? TryRead(string full)
    {
        try
        {
            return ReadDocument(full);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    private static SoftwareSelectionDocument? ReadForPreservation(string full)
    {
        try
        {
            return ReadDocument(full);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null; // Seule une absence effective autorise des préférences initiales par défaut.
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            throw new FilproOperationException(Code,
                "Les préférences existantes n'ont pas pu être relues et validées. Aucun remplacement effectué ; fermer les autres éditeurs ou examiner le document avant de réessayer.");
        }
    }

    private static SoftwareSelectionDocument ReadDocument(string full)
    {
        // Ne pas passer par File.Exists : il assimile notamment certains refus d'accès à une absence.
        EnsureNoReparse(full);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > SoftwareSelectionCodec.MaxBytes)
        {
            throw new FilproOperationException(Code, "Document de sélection vide ou trop volumineux.");
        }
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new FilproOperationException(Code, "Document de sélection modifié pendant sa lecture.");
        }

        return SoftwareSelectionCodec.Decode(bytes);
    }

    private static void ReplaceWithRetry(string temp, string full, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temp, full, overwrite: true);
                return;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && attempt < ReplaceAttempts)
            {
                Thread.Sleep(ReplaceDelayMs);
            }
        }
    }

    private static void EnsureWritablePath(string full)
    {
        if ((full + ".00000000.tmp").Length > MaxPathWithTemp)
        {
            throw new FilproOperationException(Code, "Chemin de la sélection trop long (limite Windows 259).");
        }

        EnsureNoReparse(full);
    }

    private static void EnsureNoReparse(string path)
    {
        string? current = path;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new FilproOperationException(Code, "Chemin avec lien ou jonction refusé : " + current + ".");
                }
            }
            catch (FilproOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Composant à créer : rien à contrôler à ce niveau, on remonte au parent.
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // Refus sécurisé : un parent inaccessible est traité comme un refus.
                throw new FilproOperationException(Code, "Chemin avec lien ou jonction refusé : " + current + ".");
            }

            var parent = Path.GetDirectoryName(current?.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(parent) || parent == current)
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or FilproOperationException
        or ArgumentException or FormatException or NotSupportedException or PathTooLongException or JsonException
        or System.Security.SecurityException;
}
