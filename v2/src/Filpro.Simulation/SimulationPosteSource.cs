using System.Collections.Concurrent;
using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>
/// Fournisseur « poste » de démonstration pour les écrans Analyser/réparer et Sauvegarder : monde fictif en mémoire,
/// déterministe pour une graine donnée. Il ne lit ni n'écrit jamais le système réel (aucun fichier, registre, WMI ni WTS)
/// et se déclare <see cref="Demonstration"/>. Il reproduit les formes du fournisseur natif, y compris les états inconnus
/// (chargement non prouvé, absence de dernière sélection), pour développer l'interface sans toucher au poste.
/// </summary>
public sealed class SimulationPosteSource(SimulationOptions options, TimeProvider clock) : IFilproPosteSource
{
    private const string ExecutingSid = "S-1-5-21-111222333-444555666-777888999-1000";
    private readonly ConcurrentDictionary<string, byte[]> selections = new(StringComparer.Ordinal);
    private static readonly (string Name, string Folder)[] People =
    [
        ("Camille Martin", "camille"), ("Léa Dubois", "lea"), ("Karim Benali", "karim"),
        ("Sophie Nguyen", "sophie"), ("Hugo Lefèvre", "hugo"), ("Nadia Cherif", "nadia"),
        ("Thomas Moreau", "thomas"), ("Yasmine Haddad", "yasmine"),
    ];

    public bool Demonstration => true;

    public string ReadCurrentAccountSid(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecutingSid;
    }

    public ProfileInventory ReadProfiles(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validated = options.Validate();
        var rng = SeededRandom.FromSeed(validated.Seed);
        var now = clock.GetUtcNow();

        var count = Math.Clamp(validated.ProfileCount, 1, People.Length);
        var sessions = new List<WindowsSession>();
        var candidates = new List<ProfileCandidate>(count);
        for (var i = 0; i < count; i++)
        {
            var (name, folder) = People[i];
            var sid = $"S-1-5-21-111222333-444555666-777888999-{1000 + i}";
            var entra = i == count - 1 && count > 3;
            if (entra)
            {
                sid = $"S-1-12-1-{700000000 + i}-{1000 + i}-{2000 + i}-{3000 + i}";
            }
            var active = i == 0;
            if (active)
            {
                sessions.Add(new WindowsSession(1, 0, sid, "DEMO\\" + folder, "Simulation"));
            }
            else if (i == 1)
            {
                sessions.Add(new WindowsSession(2, 4, sid, "DEMO\\" + folder, "Simulation"));
            }
            // Un profil sans ligne WTS mais lecture complète : « aucune session » prouvée.
            var loaded = active ? true : (i == 2 ? null : (bool?)false); // i==2 : chargement non prouvé, honnêtement null.
            var warnings = new List<string>();
            var central = i == 3 && count > 4;
            if (central)
            {
                warnings.Add("copie locale peut-être plus ancienne");
            }
            candidates.Add(new ProfileCandidate(
                sid,
                entra ? ProfileIdentityKind.Entra : ProfileIdentityKind.CompteLocalOuDomaine,
                MigrationSupported: !entra,
                Account: "DEMO\\" + folder,
                LocalPath: $"C:\\Users\\{folder}",
                Loaded: loaded,
                Session: null!,
                Exists: true,
                Warnings: warnings,
                IsFslogix: false,
                IsCentralProfile: central,
                HasRedirected: false,
                Excluded: false,
                LastUse: active ? now.AddMinutes(-rng.Next(1, 120)) : now.AddDays(-rng.Next(1, 30))));
        }

        var inventory = new WindowsSessionInventory(SessionReadStatus.Complete, sessions, null);
        var withSessions = candidates
            .Select(p => p with { Session = SessionEvidenceReader.Derive(p.Sid, inventory, "Simulation") })
            .ToList();
        return new ProfileInventory(SessionReadStatus.Complete, null, SessionEvidenceReader.Sort(withSessions));
    }

    public SoftwareInventory ReadSoftware(string sid, SoftwareQuery query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        RequireProfile(sid, cancellationToken);
        // string.GetHashCode varie entre processus : cette graine reste stable d'un lancement à l'autre.
        var seed = options.Seed;
        foreach (var character in sid)
        {
            seed = unchecked(seed * 31 + character);
        }
        var rng = SeededRandom.FromSeed(seed);

        var files = new List<SoftwareItem>
        {
            File(SoftwareScope.Roaming, "Notepad++", SoftwareCategory.Donnees, recommend: true, sizeMb: 42, rng),
            File(SoftwareScope.Local, @"Google\Chrome\User Data", SoftwareCategory.Donnees, recommend: true, sizeMb: 810, rng),
            File(SoftwareScope.Roaming, @"Microsoft\Word", SoftwareCategory.Donnees, recommend: false, sizeMb: 18, rng),
            File(SoftwareScope.Roaming, "vlc", SoftwareCategory.Donnees, recommend: false, sizeMb: 6, rng),
            File(SoftwareScope.Local, @"Google\Chrome\User Data\Default\Cache", SoftwareCategory.Caches, recommend: false, sizeMb: 120, rng),
        };
        foreach (var additional in query.AdditionalFiles)
        {
            files.Add(additional with { Category = SoftwareCategory.Modele, Known = true });
        }

        var registry = new List<SoftwareItem>
        {
            Reg(@"Software\Notepad++", recommend: true),
            Reg(@"Software\Google\Chrome", recommend: true),
            Reg(@"Software\Microsoft\Office", recommend: false),
            Reg(@"Software\VideoLAN\VLC", recommend: false),
        };
        foreach (var key in query.AdditionalRegistryKeys)
        {
            registry.Add(Reg(key, recommend: false));
        }

        var groups = LastSelectionRules.Apply(sid, files, registry, ReadLastSelection(sid, cancellationToken));
        var discovery = new DiscoveryNotice(true, true, null, null);
        return new SoftwareInventory(sid, groups, null, discovery, clock.GetUtcNow());
    }

    public LastSelection? ReadLastSelection(string sid, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        cancellationToken.ThrowIfCancellationRequested();
        if (selections.TryGetValue(sid, out var document))
        {
            return SoftwareSelectionCodec.Decode(document).ToLastSelection(sid);
        }
        // Honnête : seul le premier compte a une dernière sélection enregistrée ; les autres n'en ont aucune (jamais inventée).
        if (!string.Equals(sid, ExecutingSid, StringComparison.Ordinal))
        {
            return null;
        }
        var files = new List<SoftwareItem>
        {
            File(SoftwareScope.Roaming, "Notepad++", SoftwareCategory.Donnees, recommend: false, sizeMb: null, null, selected: true),
        };
        var registry = new List<SoftwareItem>
        {
            Reg(@"Software\Notepad++", recommend: false, selected: true),
        };
        return new LastSelection(sid, files, registry, clock.GetUtcNow().AddDays(-2));
    }

    public LastSelection SaveLastSelection(LastSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        RequireProfile(selection.Sid, cancellationToken);
        var document = new SoftwareSelectionDocument(SoftwareSelectionCodec.Schema, SoftwareSelectionCodec.Kind,
            clock.GetUtcNow(), selection.Files, selection.Registry, SoftwareSelectionCodec.DefaultPreferences);
        var encoded = SoftwareSelectionCodec.Encode(document);
        var canonical = SoftwareSelectionCodec.Decode(encoded);
        cancellationToken.ThrowIfCancellationRequested();
        selections[selection.Sid] = encoded;
        return canonical.ToLastSelection(selection.Sid);
    }

    private void RequireProfile(string sid, CancellationToken cancellationToken)
    {
        if (!ReadProfiles(cancellationToken).Profiles.Any(profile => string.Equals(profile.Sid, sid, StringComparison.Ordinal)))
        {
            throw new FilproOperationException("selection.profile", "Profil absent du monde de démonstration.");
        }
    }

    private static SoftwareItem File(SoftwareScope scope, string rel, SoftwareCategory category, bool recommend, long? sizeMb, SeededRandom? rng, bool selected = false)
    {
        var measured = sizeMb.HasValue;
        var bytes = sizeMb.HasValue ? sizeMb.Value * 1024 * 1024 + (rng?.Next(0, 900_000) ?? 0) : (long?)null;
        var complete = measured && (rng?.NextBool(70) ?? true);
        return new SoftwareItem(scope, rel, rel, category, selected, recommend, false, bytes, measured, complete);
    }

    private static SoftwareItem Reg(string rel, bool recommend, bool selected = false) =>
        new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, selected, recommend, false, null, false, false);
}
