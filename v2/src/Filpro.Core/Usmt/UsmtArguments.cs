using System.Text;
using System.Text.RegularExpressions;

namespace Filpro.Core;

/// <summary>
/// Construction sûre des arguments USMT à partir du plan typé, fidèle à la V1 (<c>Get-FilproUsmtArguments</c>,
/// <c>Get-FilproCryptoArguments</c>, <c>Assert-FilproExplicitAccounts</c>, <c>Resolve-FilproAccountMappings</c>). Une
/// seule fonction publique, <see cref="Build"/> : pure, sans horloge, sans identifiant aléatoire, sans E/S, sans registre,
/// sans processus et sans shell. Elle produit une <b>liste fermée de jetons</b> destinée à
/// <c>ProcessStartInfo.ArgumentList</c> — jamais une chaîne de commande libre : aucun caractère n'est interprété par un
/// shell, et un chemin contenant une espace reste un seul jeton. Elle reproduit l'ordre et les gardes de la V1 : journaux
/// hors du magasin, comptes explicites et uniques, <c>/ui:</c>/<c>/ue:</c>/<c>/mu:</c> bornés, <c>/localonly</c>,
/// <c>/efs:abort</c>, VSS explicite, estimation <c>/p:</c> exigée, chiffrement borné (jamais avec la non-compression, clé
/// hors du lot) et <b>jamais de <c>/o</c></b> (un magasin n'écrase jamais un autre). L'exécution, la résolution de
/// l'exécutable, la disponibilité USMT, l'existence et les droits de la clé relèvent du moteur.
/// </summary>
public static class UsmtArguments
{
    /// <summary>Code des refus structurels (action inconnue, journaux dans le magasin, estimation absente).</summary>
    public const string PlanCode = "usmt.plan";

    /// <summary>Code des refus de comptes (comptes inclus/exclus, correspondances de migration).</summary>
    public const string SelectionCode = "usmt.selection";

    /// <summary>Code des refus d'emplacements (magasin, journaux, règles, configuration, estimation).</summary>
    public const string StoreCode = "usmt.store";

    /// <summary>Code des refus de chiffrement (non-compression incompatible, clé absente ou mal placée).</summary>
    public const string CryptoCode = "usmt.crypto";

    public const int MaxAccounts = 200;
    public const int MaxManifests = 200;
    public const int MaxMappings = 200;
    public const int MaxAccountLength = 512;

    private static readonly Regex ExplicitAccountPattern = new(@"^[^\\*?""\x00-\x1f/:]+\\[^\\*?""\x00-\x1f/:]+$", RegexOptions.CultureInvariant);
    private static readonly Regex MigrationAccountPattern = new(@"^[^\\*?""\r\n]+\\[^\\*?""\r\n]+$", RegexOptions.CultureInvariant);
    private static readonly Regex RootedPattern = new(@"^(?:[A-Za-z]:\\|\\\\[^\\/:*?""<>|\x00-\x1f]+\\[^\\/:*?""<>|\x00-\x1f]+(?:\\|$))", RegexOptions.CultureInvariant);
    private static readonly Regex InvalidPathChars = new(@"[\x00-\x1f:*?""<>|]", RegexOptions.CultureInvariant);
    private static readonly Regex RelativeEscape = new(@"(^|\\)\.\.?($|\\)", RegexOptions.CultureInvariant);

    private const string BuildNotice =
        "Construction des arguments USMT à partir du plan validé : aucune commande shell, aucune exécution. Passer chaque " +
        "jeton à ProcessStartInfo.ArgumentList (jamais une chaîne de commande libre). Résoudre l'exécutable depuis le " +
        "dossier USMT vérifié de la distribution, jamais depuis une variable d'environnement. Existence, contenu et droits " +
        "de la clé, disponibilité USMT et contrôle de plateforme relèvent du moteur. Aucun /o : un magasin n'écrase jamais " +
        "un autre. Un arrêt demandé n'est pas un arrêt achevé.";

    /// <summary>
    /// Construit la liste fermée des arguments d'une action USMT à partir d'une demande typée, ou refuse sans rien
    /// produire. La fonction ne lit aucune horloge, n'invente aucun identifiant, ne touche aucun fichier et ne lance aucun
    /// processus : elle ne fait que mettre en forme, dans l'ordre de la V1, les jetons que le moteur passera à
    /// <c>ArgumentList</c>.
    /// </summary>
    public static UsmtInvocation Build(UsmtArgumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Action))
        {
            throw Invalid(PlanCode, "Action USMT inconnue.");
        }

        var store = RequireRooted(request.Store, StoreCode, "Chemin du magasin USMT absolu requis.");
        var logDirectory = RequireRooted(request.LogDirectory, StoreCode, "Dossier des journaux USMT absolu requis.");
        if (IsWithin(logDirectory, store, allowRoot: true))
        {
            throw Invalid(PlanCode, "Les journaux USMT doivent être hors du magasin.");
        }

        var users = ValidateUsers(request.Users);

        var args = new List<string> { store };
        var manifests = request.Manifests;
        if (manifests.Count > MaxManifests)
        {
            throw Invalid(StoreCode, $"Trop de règles USMT dans une seule invocation ({manifests.Count}).");
        }
        foreach (var manifest in manifests)
        {
            if (string.IsNullOrWhiteSpace(manifest))
            {
                continue;
            }
            args.Add("/i:" + RequireRooted(manifest, StoreCode, "Chemin de règle USMT absolu requis."));
        }

        if (!string.IsNullOrWhiteSpace(request.Config))
        {
            args.Add("/config:" + RequireRooted(request.Config, StoreCode, "Chemin de configuration USMT absolu requis."));
        }
        if (request.NoCompress)
        {
            args.Add("/nocompress");
        }
        if (request.ContinueOnError)
        {
            args.Add("/c");
        }

        var prefix = request.Action switch
        {
            UsmtAction.Load => "loadstate",
            UsmtAction.Estimate => "estimate",
            _ => "scanstate",
        };
        var logFile = Combine(logDirectory, prefix + ".log");
        var progressFile = Combine(logDirectory, prefix + "-progress.log");
        args.Add("/v:5");
        args.Add("/l:" + logFile);
        args.Add("/progress:" + progressFile);
        var logPaths = new List<string> { logFile, progressFile };

        if (request.Action is UsmtAction.Scan or UsmtAction.Estimate)
        {
            args.Add("/localonly");
            args.Add("/ue:*");
            args.Add("/efs:abort");
            foreach (var account in users)
            {
                if (!MigrationAccountPattern.IsMatch(account) || account.StartsWith("S-1-", StringComparison.Ordinal))
                {
                    throw Invalid(SelectionCode, "Compte non résolu ou non explicite : " + account);
                }
                args.Add("/ui:" + account);
            }
            if (request.UseVss)
            {
                args.Add("/vsc");
            }
        }

        if (request.Action == UsmtAction.Load)
        {
            args.Add("/ue:*");
            var excludes = request.ExcludeUsers ?? [];
            foreach (var account in users)
            {
                if (excludes.Any(e => string.Equals(e, account, StringComparison.OrdinalIgnoreCase)))
                {
                    throw Invalid(SelectionCode, "Un compte ne peut être simultanément inclus et exclu.");
                }
                args.Add("/ui:" + account);
            }
            foreach (var excluded in excludes)
            {
                if (string.IsNullOrWhiteSpace(excluded))
                {
                    continue;
                }
                if (!MigrationAccountPattern.IsMatch(excluded))
                {
                    throw Invalid(SelectionCode, "Compte à exclure non explicite : " + excluded);
                }
                args.Add("/ue:" + excluded);
            }
            foreach (var map in ResolveMappings(users, request.AccountMappings))
            {
                args.Add("/mu:" + map.Source + ":" + map.Target);
            }
        }

        if (request.Action == UsmtAction.Estimate)
        {
            if (string.IsNullOrWhiteSpace(request.EstimateFile))
            {
                throw Invalid(PlanCode, "Fichier d'estimation requis.");
            }
            args.Add("/p:" + RequireRooted(request.EstimateFile, StoreCode, "Chemin du fichier d'estimation absolu requis."));
        }

        if (request.Action == UsmtAction.Scan)
        {
            var listFile = Combine(logDirectory, "captured-files.txt");
            args.Add("/listfiles:" + listFile);
            logPaths.Add(listFile);
        }

        if (request.Encrypted && request.Action != UsmtAction.Estimate)
        {
            args.AddRange(CryptoArguments(request));
        }

        var executable = request.Action == UsmtAction.Load ? "loadstate.exe" : "scanstate.exe";
        return new UsmtInvocation(request.Action, executable, [.. args], [.. logPaths], BuildNotice);
    }

    private static IReadOnlyList<string> CryptoArguments(UsmtArgumentRequest request)
    {
        if (request.NoCompress)
        {
            throw Invalid(CryptoCode, "Le chiffrement ne peut pas être combiné avec un magasin non compressé.");
        }
        if (string.IsNullOrWhiteSpace(request.KeyFile))
        {
            throw Invalid(CryptoCode, "Fichier clé requis pour ce magasin chiffré.");
        }
        var key = RequireRooted(request.KeyFile, CryptoCode, "Chemin du fichier clé absolu requis.");
        if (!string.IsNullOrWhiteSpace(request.Bundle))
        {
            var bundle = RequireRooted(request.Bundle, CryptoCode, "Chemin du dossier de sauvegarde absolu requis.");
            if (IsWithin(key, bundle, allowRoot: true))
            {
                throw Invalid(CryptoCode, "Le fichier clé doit rester hors du dossier de sauvegarde.");
            }
        }
        var mode = request.Action == UsmtAction.Scan ? "/encrypt:AES_256" : "/decrypt:AES_256";
        return [mode, "/keyfile:" + key];
    }

    private static IReadOnlyList<UsmtAccountMapping> ResolveMappings(IReadOnlyList<string> users, IReadOnlyList<UsmtAccountMapping>? mappings)
    {
        if (mappings is { Count: > MaxMappings })
        {
            throw Invalid(SelectionCode, "Trop de correspondances de comptes dans une seule invocation.");
        }

        var rowsBySource = new Dictionary<string, List<UsmtAccountMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in mappings ?? [])
        {
            if (map is null)
            {
                throw Invalid(SelectionCode, "Correspondance de compte invalide.");
            }
            var source = RequireExplicitAccount(map.Source, "compte source d'une correspondance");
            if (!rowsBySource.TryGetValue(source, out var rows))
            {
                rows = [];
                rowsBySource[source] = rows;
            }
            rows.Add(map with { Source = source });
        }

        var selected = new HashSet<string>(users, StringComparer.OrdinalIgnoreCase);
        foreach (var source in rowsBySource.Keys)
        {
            if (!selected.Contains(source))
            {
                throw Invalid(SelectionCode, "Correspondance portant sur un compte non sélectionné : " + source + ".");
            }
        }

        // Comme la V1 (Resolve-FilproAccountMappings) : chaque compte sélectionné a une cible (la sienne faute de
        // correspondance). La fusion est détectée sur l'ensemble des comptes, y compris un compte non mappé dont la cible
        // implicite entrerai en conflit avec une correspondance explicite.
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<UsmtAccountMapping>();
        foreach (var user in users)
        {
            var rows = rowsBySource.TryGetValue(user, out var found) ? found : null;
            if (rows is { Count: > 1 })
            {
                throw Invalid(SelectionCode, "Correspondance de compte en double : " + user + ".");
            }
            var target = rows is { Count: 1 } ? RequireExplicitAccount(rows[0].Target, "compte cible") : user;
            if (!destinations.Add(target))
            {
                throw Invalid(SelectionCode, "Fusion de plusieurs comptes vers une destination interdite. Créer des opérations distinctes : " + target + ".");
            }
            if (!string.Equals(user, target, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new UsmtAccountMapping(user, target));
            }
        }
        return result;
    }

    private static IReadOnlyList<string> ValidateUsers(IReadOnlyList<string>? users)
    {
        if (users is null or { Count: 0 })
        {
            throw Invalid(SelectionCode, "Sélection explicite de comptes obligatoire.");
        }
        if (users.Count > MaxAccounts)
        {
            throw Invalid(SelectionCode, $"Trop de comptes dans une seule invocation ({users.Count}).");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(users.Count);
        foreach (var user in users)
        {
            var account = RequireExplicitAccount(user, "compte");
            if (!seen.Add(account))
            {
                throw Invalid(SelectionCode, "Compte en double : " + account);
            }
            result.Add(account);
        }
        return result;
    }

    private static string RequireExplicitAccount(string? value, string who)
    {
        var account = (value ?? string.Empty).Trim();
        if (account.Length == 0 || account.Length > MaxAccountLength || account.StartsWith("S-1-", StringComparison.Ordinal)
            || !ExplicitAccountPattern.IsMatch(account))
        {
            throw Invalid(SelectionCode, "Compte non explicite ou non résolu (" + who + ") : "
                + (string.IsNullOrWhiteSpace(value) ? "(compte absent)" : value) + ".");
        }
        return account;
    }

    private static string RequireRooted(string? value, string code, string message)
    {
        var raw = (value ?? string.Empty).Trim().Replace('/', '\\');
        if (raw.Length == 0 || !RootedPattern.IsMatch(raw))
        {
            throw Invalid(code, message);
        }
        var tail = raw.Substring(2);
        if (InvalidPathChars.IsMatch(tail) || RelativeEscape.IsMatch(tail))
        {
            throw Invalid(code, message);
        }
        return Normalize(raw);
    }

    private static string Normalize(string path)
    {
        var value = path.Replace('/', '\\');
        var unc = value.StartsWith(@"\\", StringComparison.Ordinal);
        var builder = new StringBuilder(value.Length + 2);
        if (unc)
        {
            builder.Append(@"\\");
        }
        foreach (var c in value.AsSpan(unc ? 2 : 0))
        {
            if (c == '\\' && builder.Length > 0 && builder[^1] == '\\')
            {
                continue;
            }
            builder.Append(c);
        }
        return builder.ToString().TrimEnd('\\');
    }

    private static string Combine(string directory, string leaf) => directory + "\\" + leaf;

    /// <summary>Le <paramref name="candidate"/> est-il dans le <paramref name="parent"/> (comparaison de chemins Windows, sans E/S).</summary>
    private static bool IsWithin(string candidate, string parent, bool allowRoot)
    {
        if (candidate.Length == 0 || parent.Length == 0)
        {
            return false;
        }
        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
        {
            return allowRoot;
        }
        return candidate.Length > parent.Length
            && candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            && candidate[parent.Length] == '\\';
    }

    private static FilproOperationException Invalid(string code, string message) => new(code, message);
}
