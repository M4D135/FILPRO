using System.Text.Json;
using Filpro.Core;

namespace Filpro.Bridge;

public sealed class ConfigurationModule : IOperationModule
{
    // Refus du service pour un aperçu absent ou expiré (LocalConfigurationService), opposé aussi par le pont.
    private const string PreviewCode = "configuration.preview";
    private const string PreviewMessage = "Aperçu absent ou expiré. Refaire l'aperçu.";

    public string Screen => "configuration";
    private static IFilproConfigurationService Service(EngineContext context)
    {
        var service = context.Require<IFilproConfigurationService>();
        if (service.Demonstration != (context.Mode == AppMode.Simulation))
            throw new FilproOperationException("configuration.mode", "Le fournisseur de réglages ne correspond pas au mode courant.");
        return service;
    }

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("configuration.read", OperationClass.LocalRead, async (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            return await Service(context).ReadAsync(ct).ConfigureAwait(false);
        }, "Réglages courants et proposition éventuelle du nouveau pack.");
        yield return new("configuration.save", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["settings", "expectedRevision"]);
            if (!values.TryGetValue("settings", out var settings)) throw BridgeArguments.Invalid("réglages absents.");
            var service = Service(context);
            var current = await service.ReadAsync(ct).ConfigureAwait(false);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("application", "FILPRO");
                writer.WriteString("kind", "shared-configuration"); writer.WritePropertyName("settings");
                settings.WriteTo(writer); writer.WriteEndObject();
            }
            var validated = ConfigurationCodec.Decode(stream.ToArray(), current.Settings).Settings;
            return await service.SaveAsync(validated, BridgeArguments.GetString(values, "expectedRevision"), ct).ConfigureAwait(false);
        }, "Enregistre les réglages validés si la révision affichée est encore courante.");
        yield return new("configuration.export", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["destinationPath", "confirmed"]);
            return await Service(context).ExportAsync(BridgeArguments.GetString(values, "destinationPath"), BridgeArguments.GetBoolean(values, "confirmed"), ct).ConfigureAwait(false);
        }, "Exporte manuellement les seuls réglages partageables, après accord.");
        yield return new("configuration.previewImport", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["sourcePath"]);
            return await Service(context).PreviewImportAsync(BridgeArguments.GetString(values, "sourcePath"), ct).ConfigureAwait(false);
        }, "Valide le document et montre les champs remplacés ou conservés.", ServedInSimulation: true);
        yield return new("configuration.applyImport", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["previewId", "confirmed"]);
            return await Service(context).ApplyImportAsync(BridgeArguments.GetString(values, "previewId"), BridgeArguments.GetBoolean(values, "confirmed"), ct).ConfigureAwait(false);
        }, "Applique un aperçu d’import frais après sauvegarde des réglages actuels.");
        yield return new("configuration.previewDefaults", OperationClass.LocalRead, async (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            // Instant pris avant l'aperçu : l'échéance enregistrée ne dépasse jamais celle de l'aperçu chez le service.
            var requested = context.Clock.GetUtcNow();
            var preview = await Service(context).PreviewDefaultsAsync(ct).ConfigureAwait(false);
            // La confirmation Windows de l'écriture montrera cet aperçu, jamais les arguments de la page.
            if (context.Mode == AppMode.Poste) ConfirmationRecords.For(context).RecordPreview(preview, requested);
            return preview;
        }, "Prépare les réglages partageables à distribuer dans ce pack.");
        yield return new("configuration.applyDefaults", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["previewId", "confirmed"]);
            var previewId = BridgeArguments.GetString(values, "previewId");
            var confirmed = BridgeArguments.GetBoolean(values, "confirmed");
            var service = Service(context);
            if (context.Mode == AppMode.Simulation || !confirmed) return await service.ApplyDefaultsAsync(previewId, confirmed, ct).ConfigureAwait(false);
            using var turn = NativeConfirmation.Begin(context);
            var record = ConfirmationRecords.For(context).Preview(previewId) ?? throw new FilproOperationException(PreviewCode, PreviewMessage);
            turn.RequireTimeLeft(record.Deadline, PreviewCode, PreviewMessage);
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.Defaults(record.Preview),
                () => service.ApplyDefaultsAsync(previewId, true, ct), ct).ConfigureAwait(false);
        }, "Définit les réglages comme configuration par défaut du pack, après validation de son intégrité.");
        yield return new("configuration.decideBootstrap", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["previewId", "apply", "confirmed"]);
            return await Service(context).DecideBootstrapAsync(BridgeArguments.GetString(values, "previewId"),
                BridgeArguments.GetBoolean(values, "apply"), BridgeArguments.GetBoolean(values, "confirmed"), ct).ConfigureAwait(false);
        }, "Mémorise le choix appliquer ou conserver pour la configuration de ce pack.");
    }
}
