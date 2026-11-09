using Filpro.Core;

namespace Filpro.Platform;

/// <summary>Relit les identités ; une session déconnectée reste une session et ne devient jamais une cible libre.</summary>
public sealed class ExecutionProfileGuard(IFilproPosteSource source)
{
    public ProfileCandidate Read(string sid, bool target, CancellationToken token)
    {
        if (source.Demonstration) throw new FilproOperationException("execution.mode", "Le moteur natif refuse le fournisseur de démonstration.");
        SoftwareDiscoveryInputs.ValidateSid(sid);
        var inventory = source.ReadProfiles(token);
        if (inventory.SessionReadStatus != SessionReadStatus.Complete)
            throw new FilproOperationException("profile.session", "L'état des sessions n'est pas entièrement connu.");
        var matches = inventory.Profiles.Where(p => p.Sid == sid).ToArray();
        if (matches.Length != 1) throw new FilproOperationException("profile.identity", "Le profil n'est plus identifié sans ambiguïté.");
        var profile = matches[0];
        if (!profile.Exists || !profile.MigrationSupported || profile.Excluded || profile.Loaded is null
            || profile.IsFslogix || profile.IsCentralProfile || profile.HasRedirected
            || string.IsNullOrWhiteSpace(profile.LocalPath))
            throw new FilproOperationException("profile.unsupported", "Le profil est absent, redirigé, externe, inconnu ou hors du périmètre pris en charge.");
        _ = StoragePathPolicy.NormalizeWindows(profile.LocalPath);
        if (target && (profile.Loaded != false || profile.Session.ReadStatus != SessionReadStatus.Complete
            || profile.Session.State != SessionState.None || profile.Session.Count != 0))
            throw new FilproOperationException("profile.session", "La cible doit être entièrement déconnectée, sans session ni ruche chargée.");
        if (target && source.ReadCurrentAccountSid(token) == sid)
            throw new FilproOperationException("profile.current", "Le profil du compte exécutant ne peut pas être modifié.");
        token.ThrowIfCancellationRequested();
        return profile;
    }

    public ProfileCandidate Match(string sid, string account, string path, bool target, CancellationToken token)
    {
        var profile = Read(sid, target, token);
        if (!profile.Account.Equals(account, StringComparison.OrdinalIgnoreCase)
            || !StoragePathPolicy.NormalizeWindows(profile.LocalPath!).Equals(StoragePathPolicy.NormalizeWindows(path), StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("profile.changed", "L'identité ou le chemin du profil a changé après la revue.");
        return profile;
    }
}

public sealed record ProfileFileScope(IReadOnlyList<string> PersonalFolders, IReadOnlyList<string> ExcludedPaths,
    IReadOnlyList<string> Warnings, long FileCount);

/// <summary>Préparation bornée des exclusions : ne suit jamais un lien et ne lit aucune donnée cloud.</summary>
public sealed class ProfileFileScopeReader(ExecutionFileSystem files)
{
    public ProfileFileScope Read(string root, IReadOnlyList<SoftwareItem> selected, string? keyPath,
        CancellationToken token, int maximumEntries = 2_000_000)
    {
        root = StoragePathPolicy.NormalizeNative(root);
        using var rootLease = files.HoldDirectory(root);
        var personal = new List<string>();
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var pending = new Stack<string>();
        long count = 0;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Admit(directory, name)) continue;
            RequireRule(name, "Ce dossier du profil doit être inclus");
            personal.Add(name); pending.Push(directory);
        }
        foreach (var item in selected)
        {
            if (item.Scope is not (SoftwareScope.Local or SoftwareScope.Roaming)) throw new FilproOperationException("backup.selection", "Portée fichier invalide.");
            var relative = Path.Combine("AppData", item.Scope == SoftwareScope.Local ? "Local" : "Roaming", item.Rel.Replace('\\', Path.DirectorySeparatorChar));
            var path = Path.Combine(root, relative);
            using var scopeLease = files.HoldDirectory(path); // Un choix absent n'est jamais présenté comme sauvegardé.
            RequireRule(relative, "Ce choix logiciel doit être inclus");
            pending.Push(path);
        }
        foreach (var file in Directory.EnumerateFiles(root)) InspectFile(file);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            using var lease = files.HoldDirectory(directory);
            foreach (var child in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (++count > maximumEntries) throw new FilproOperationException("backup.scope", "Périmètre trop vaste pour un contrôle complet ; aucune copie n'a commencé.");
                var relative = Path.GetRelativePath(root, child);
                if (!Admit(child, relative)) continue;
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
                else InspectFile(child);
            }
        }
        if (keyPath is not null && StoragePathPolicy.IsWithin(keyPath, root, false))
            excluded.Add(Path.GetRelativePath(root, keyPath).Replace(Path.DirectorySeparatorChar, '/'));
        token.ThrowIfCancellationRequested();
        return new(personal, excluded.OrderBy(s => s, StringComparer.Ordinal).ToArray(), warnings, count);

        bool Admit(string path, string relative)
        {
            if (++count > maximumEntries) throw new FilproOperationException("backup.scope", "Inventaire des exclusions trop vaste.");
            var attributes = File.GetAttributes(path);
            const FileAttributes cloud = (FileAttributes)(0x1000 | 0x40000 | 0x400000);
            var denied = (attributes & (FileAttributes.ReparsePoint | cloud)) != 0;
            try { _ = StoragePathPolicy.NormalizeNative(path); } catch (FilproOperationException) { denied = true; }
            if (!denied && (attributes & FileAttributes.Directory) != 0 && StoreMarker(path) is { } marker && files.FilePresent(marker)) denied = true;
            // Un nom avec [, ] ou % admis reste couvert par le joker de son dossier : seul ce qui est écarté s'écrit dans une règle USMT.
            if (!denied) return true;
            RequireRule(relative, "Cet élément doit être écarté");
            // Conserver le relatif sans utiliser le helper qui interdit les noms cloud : il sert uniquement à exclure.
            excluded.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
            warnings.Add("Périmètre écarté (lien, cloud ou autre magasin) : " + relative);
            return false;
        }
        void InspectFile(string path)
        {
            var relative = Path.GetRelativePath(root, path);
            if (Path.GetFileName(path).StartsWith("NTUSER", StringComparison.OrdinalIgnoreCase)) return;
            if (!Admit(path, relative)) return;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Encrypted) != 0)
                throw new FilproOperationException("backup.efs", "Fichier EFS détecté ; la sauvegarde est refusée avant USMT.");
        }
    }
    /// <summary>Un marqueur que la politique de chemins refuse (au-delà de 259 caractères sous Windows) n'a pas pu être écrit par FILPRO.</summary>
    internal static string? StoreMarker(string directory)
    {
        var marker = Path.Combine(directory, "filpro-backup.json");
        try { return StoragePathPolicy.NormalizeNative(marker); }
        catch (FilproOperationException) { return null; }
    }

    /// <summary>Refus nommé, avant toute copie, d'un chemin qu'une règle USMT écrirait sans pouvoir l'échapper.</summary>
    internal static void RequireRule(string relative, string role)
    {
        try { _ = UsmtMigrationRules.RequireExcludedRelative(relative); }
        catch (FilproOperationException)
        {
            throw new FilproOperationException("usmt.rules", role + ", mais son chemin contient un caractère que les règles USMT ne savent pas écrire ([, ] ou %) : "
                + relative + ". Le renommer puis relancer ; aucune copie n'a commencé.");
        }
    }
}
