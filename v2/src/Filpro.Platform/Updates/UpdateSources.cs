using System.Text;
using Filpro.Core;

namespace Filpro.Platform;

internal sealed record UpdateCandidate(UpdateOffer Offer, string LocalPath);

/// <summary>Dossier local ou UNC, ou ZIP choisi : FILPRO n'interroge aucun service en ligne, et ReadAsync ne lit aucune source.</summary>
internal static class UpdateSources
{
    private const string Prefix = "FILPRO-";
    private const string Notice = "SHA-256 : intégrité contrôlée, sans signature d'éditeur. Confirmer uniquement une source de confiance.";

    internal static UpdateCandidate Inspect(UpdateSourceRequest request, string currentVersion, string flavor)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PackManifestCodec.IsProductionFlavor(flavor)) throw Invalid("La recherche de mises à jour exige un pack de production installé.");
        if (!Enum.IsDefined(request.Kind)) throw Invalid("Type de source inconnu.");
        if (string.IsNullOrWhiteSpace(request.Path)) throw Invalid("Indiquer le dossier ou le ZIP de la mise à jour.");
        var path = StoragePathPolicy.NormalizeNative(request.Path); ConfigurationFileIO.NoLinks(path);
        string archive;
        if (request.Kind == UpdateSourceKind.Folder)
        {
            if (request.Sha256 is not null || !Directory.Exists(path)) throw Invalid("Dossier local ou UNC introuvable.");
            using var sourceLease = ConfigurationFileIO.FileSystem.HoldDirectory(path);
            var candidates = new List<(string Path, PackageVersion Version)>(); var count = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                if (++count > 2000) throw Invalid("Dossier trop volumineux : utiliser un dépôt de mises à jour dédié.");
                var version = VersionOf(Path.GetFileName(file), flavor); if (version is not null) { ConfigurationFileIO.NoLinks(file); candidates.Add((file, version)); }
            }
            if (candidates.Count == 0) throw Invalid($"Aucun ZIP {flavor} Windows x64 stable compatible.");
            candidates.Sort((a, b) => b.Version.CompareTo(a.Version));
            if (candidates.Count > 1 && candidates[0].Version.CompareTo(candidates[1].Version) == 0) throw Invalid("Version la plus récente ambiguë.");
            archive = candidates[0].Path;
        }
        else archive = path;
        var packageVersion = VersionOf(Path.GetFileName(archive), flavor) ?? throw Invalid($"Nom attendu : FILPRO-<version>-win-x64-{flavor}.zip (version stable).");
        ConfigurationFileIO.NoLinks(archive); var length = new FileInfo(archive).Length;
        if (length is < 1 or > PackManifestCodec.MaximumPackBytes) throw Invalid("Taille du ZIP refusée.");
        var hash = request.Kind == UpdateSourceKind.ManualZip && request.Sha256 is not null
            ? request.Sha256.ToLowerInvariant()
            : ParseChecksums(ConfigurationFileIO.Read(Path.Combine(Path.GetDirectoryName(archive)!, "SHA256SUMS"), 1024 * 1024), Path.GetFileName(archive));
        if (!PackManifestCodec.IsSha256(hash)) throw Invalid("Somme SHA-256 manquante ou invalide.");
        return new(Offer(request.Kind, packageVersion, Path.GetFileName(archive), length, hash, currentVersion), archive);
    }

    internal static async Task CopyAsync(UpdateCandidate candidate, string target, IProgress<PackValidationProgress>? progress, CancellationToken token)
    {
        ConfigurationFileIO.NoLinks(target); ConfigurationFileIO.FileSystem.CreateDirectories(Path.GetDirectoryName(target)!);
        using var targetLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(target)!);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        ConfigurationFileIO.NoLinks(candidate.LocalPath);
        using var sourceLease = ConfigurationFileIO.FileSystem.HoldDirectory(Path.GetDirectoryName(candidate.LocalPath)!);
        await using var input = StorageFileIdentity.OpenRead(candidate.LocalPath, asynchronous: true);
        if (input.Length != candidate.Offer.Bytes) throw Invalid("La source a changé. Refaire l'inspection.");
        await CopyBoundedAsync(input, output, candidate.Offer, progress, token).ConfigureAwait(false);
        output.Flush(true);
    }

    private static async Task CopyBoundedAsync(Stream source, Stream target, UpdateOffer offer, IProgress<PackValidationProgress>? progress, CancellationToken token)
    {
        var buffer = new byte[128 * 1024]; long total = 0; int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += count; if (total > offer.Bytes) throw Invalid("La source dépasse la taille annoncée.");
            await target.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            progress?.Report(new(offer.FileName, 0, 1, total, offer.Bytes));
        }
        if (total != offer.Bytes) throw Invalid("Copie incomplète.");
    }
    internal static string ParseChecksums(byte[] bytes, string expectedName)
    {
        string? result = null; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw Invalid("SHA256SUMS n'est pas un texte UTF-8 valide."); }
        foreach (var line in text.Split('\n'))
        {
            var value = line.TrimEnd('\r'); if (value.Length == 0) continue;
            if (value.Length < 67 || value[64] != ' ' || (value[65] != ' ' && value[65] != '*')) throw Invalid("Format SHA256SUMS invalide.");
            var hash = value[..64].ToLowerInvariant(); var name = value[66..];
            if (!PackManifestCodec.IsSha256(hash) || name.Contains('/') || name.Contains('\\') || !names.Add(name)) throw Invalid("Sommes SHA-256 ambiguës.");
            StoragePathPolicy.RequireSegment(name); if (name == expectedName) result = hash;
        }
        return result ?? throw Invalid("Somme absente pour le ZIP choisi.");
    }
    private static PackageVersion? VersionOf(string name, string flavor)
    {
        var suffix = $"-win-x64-{flavor}.zip";
        if (name.Length <= Prefix.Length + suffix.Length || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal)) return null;
        try { var version = PackageVersion.Parse(name[Prefix.Length..^suffix.Length]); return version.Prerelease is null ? version : null; }
        catch (FilproOperationException) { return null; }
    }
    private static UpdateOffer Offer(UpdateSourceKind kind, PackageVersion version, string name, long bytes, string hash, string current) =>
        new(Guid.NewGuid().ToString("N"), false, kind, version.Text, name, bytes, hash, version.CompareTo(PackageVersion.Parse(current)) > 0, Notice, [Notice]);
    private static FilproOperationException Invalid(string message) => new("update.source", message);
}
