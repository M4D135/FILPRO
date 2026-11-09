using System.Security.Cryptography;
using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>Réglages et documents fictifs en mémoire ; mêmes codec et règles de fraîcheur, aucun accès à un fichier réel.</summary>
public sealed class SimulationConfigurationService : IFilproConfigurationService
{
    private sealed record Pending(ConfigurationPreview Preview, SharedConfiguration Settings, DateTimeOffset Expires);
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly Dictionary<string, byte[]> documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Pending> previews = new(StringComparer.Ordinal);
    private SharedConfiguration settings = new();
    private string revision = Guid.NewGuid().ToString("N");
    private const string ExamplePath = @"D:\FILPRO-DEMO\Local\configuration-exemple.json";
    private const string DefaultsPath = @"D:\FILPRO-DEMO\Local\FILPRO.defaults.json";
    private ConfigurationPreview? bootstrap;

    public SimulationConfigurationService(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
        documents[ExamplePath] = ConfigurationCodec.Encode(new SharedConfiguration(Theme: "dark", Motion: "reduced", NavigationCollapsed: true));
        bootstrap = Preview("bootstrap", ExamplePath, documents[ExamplePath]);
    }
    public bool Demonstration => true;
    private ConfigurationState State()
    {
        if (bootstrap is not null && (bootstrap.ExpectedRevision != revision || !previews.TryGetValue(bootstrap.Id, out var kept) || kept.Expires <= clock.GetUtcNow()))
        {
            previews.Remove(bootstrap.Id);
            bootstrap = Preview("bootstrap", ExamplePath, documents[ExamplePath]);
        }
        return new(true, revision, settings, bootstrap, ["Démonstration — réglages et documents conservés uniquement en mémoire."]);
    }
    public Task<ConfigurationState> ReadAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); lock (gate) return Task.FromResult(State()); }

    public Task<ConfigurationResult> SaveAsync(SharedConfiguration value, string expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valid = ConfigurationCodec.Validate(value);
        lock (gate)
        {
            RequireRevision(expectedRevision);
            var backup = Backup(); settings = valid; revision = Guid.NewGuid().ToString("N");
            return Task.FromResult(new ConfigurationResult(true, State(), null, backup, "Réglages fictifs enregistrés en mémoire ; contexte normal inchangé."));
        }
    }
    public Task<ConfigurationResult> ExportAsync(string destinationPath, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed); SimulationWorld.RequirePath(destinationPath);
        lock (gate)
        {
            documents[destinationPath] = ConfigurationCodec.Encode(settings);
            return Task.FromResult(new ConfigurationResult(true, State(), destinationPath, null,
                "Export fictif disponible uniquement dans la mémoire de cette démonstration. Aucun fichier réel n'a été créé."));
        }
    }
    public Task<ConfigurationPreview> PreviewImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); SimulationWorld.RequirePath(sourcePath);
        lock (gate)
        {
            if (!documents.TryGetValue(sourcePath, out var bytes)) throw SimulationWorld.Refused("Document absent du catalogue fictif ; aucun fichier réel n'est lu.");
            return Task.FromResult(Preview("import", sourcePath, bytes));
        }
    }
    public Task<ConfigurationResult> ApplyImportAsync(string previewId, bool confirmed, CancellationToken cancellationToken) =>
        Apply(previewId, confirmed, "import", cancellationToken);
    public Task<ConfigurationPreview> PreviewDefaultsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(Preview("defaults", DefaultsPath, ConfigurationCodec.Encode(settings)));
    }
    public Task<ConfigurationResult> ApplyDefaultsAsync(string previewId, bool confirmed, CancellationToken cancellationToken) =>
        Apply(previewId, confirmed, "defaults", cancellationToken);
    public Task<ConfigurationResult> DecideBootstrapAsync(string previewId, bool apply, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed);
        lock (gate)
        {
            if (bootstrap?.Id != previewId) throw SimulationWorld.Refused("Cette proposition fictive n'est plus actuelle.");
            if (apply) return Apply(previewId, true, "bootstrap", cancellationToken);
            previews.Remove(previewId); bootstrap = null;
            return Task.FromResult(new ConfigurationResult(true, State(), null, null, "Réglages fictifs conservés ; la même proposition ne sera plus affichée dans cette session."));
        }
    }

    private ConfigurationPreview Preview(string kind, string path, ReadOnlyMemory<byte> bytes)
    {
        var decoded = ConfigurationCodec.Decode(bytes, settings);
        var preview = new ConfigurationPreview(Guid.NewGuid().ToString("N"), kind, true, path,
            Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), revision, ConfigurationCodec.Changes(settings, decoded),
            kind is "defaults" or "bootstrap" ? "FILPRO-DEMO-PACK" : null,
            "Aperçu fictif. Application atomique en mémoire, avec copie préalable des réglages ; aucun secret, identité ou historique exporté.");
        previews[preview.Id] = new(preview, decoded.Settings, clock.GetUtcNow().AddMinutes(10));
        return preview;
    }
    private Task<ConfigurationResult> Apply(string id, bool confirmed, string kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfirmation(confirmed);
        lock (gate)
        {
            if (!previews.TryGetValue(id, out var pending) || pending.Preview.Kind != kind || pending.Expires <= clock.GetUtcNow())
                throw SimulationWorld.Refused("Aperçu fictif absent, expiré ou déjà appliqué.");
            RequireRevision(pending.Preview.ExpectedRevision);
            string? backup;
            string? written = null;
            if (kind == "defaults")
            {
                backup = documents.TryGetValue(DefaultsPath, out var old) ? DefaultsPath + ".avant-" + revision : null;
                if (backup is not null) documents[backup] = old!.ToArray();
                documents[DefaultsPath] = ConfigurationCodec.Encode(pending.Settings);
                written = DefaultsPath;
            }
            else
            {
                backup = Backup(); settings = pending.Settings; revision = Guid.NewGuid().ToString("N");
            }
            previews.Remove(id);
            if (kind == "bootstrap") bootstrap = null;
            return Task.FromResult(new ConfigurationResult(true, State(), written, backup,
                kind == "defaults" ? "Défauts du pack fictif enregistrés en mémoire. Le vrai dossier PROD reste inchangé."
                    : "Configuration fictive appliquée avec copie préalable ; aucun réglage réel modifié."));
        }
    }
    private string Backup()
    {
        var path = @"D:\FILPRO-DEMO\Reserve\configuration-avant-" + revision + ".json";
        documents[path] = ConfigurationCodec.Encode(settings); return path;
    }
    private void RequireRevision(string expected)
    { if (!string.Equals(revision, expected, StringComparison.Ordinal)) throw SimulationWorld.Refused("Les réglages fictifs ont changé depuis l'aperçu. Actualiser avant de continuer."); }
    private static void RequireConfirmation(bool confirmed)
    { if (!confirmed) throw SimulationWorld.Refused("Confirmation explicite requise."); }
}
