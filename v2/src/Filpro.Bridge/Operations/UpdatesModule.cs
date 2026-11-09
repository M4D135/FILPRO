using Filpro.Core;

namespace Filpro.Bridge;

public sealed class UpdatesModule : IOperationModule
{
    // Refus du service pour une offre absente ou expirée (LocalUpdateService.PrepareAsync), opposé aussi par le pont.
    private const string OfferCode = "update.offer";
    private const string OfferMessage = "Offre absente ou expirée ; refaire l'inspection.";

    public string Screen => "updates";

    internal static IFilproUpdateService Service(EngineContext context, bool? simulation = null)
    {
        var expected = context.Mode == AppMode.Simulation;
        var service = context.Require<IFilproUpdateService>();
        if (service.Demonstration != expected || (simulation.HasValue && simulation != expected))
            throw new FilproOperationException("update.mode", "Le fournisseur de mise à jour ne correspond pas au mode courant.");
        return service;
    }

    internal static void RequireIdle(EngineContext context)
    {
        if (context.Service<IFilproExecutionService>()?.Active().Any() == true)
            throw new FilproOperationException("update.busy", "Terminez les opérations en cours avant de remplacer l’application.");
    }

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("updates.source", OperationClass.LocalRead, async (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            return await Service(context).ReadSourceAsync(ct).ConfigureAwait(false);
        }, "Source mémorisée localement ; aucune connexion au partage.");
        yield return new("updates.saveSource", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["kind", "folder"]);
            var kind = BridgeArguments.GetString(values, "kind") switch
            {
                "folder" => UpdateSourceKind.Folder,
                "manualZip" => UpdateSourceKind.ManualZip,
                _ => throw BridgeArguments.Invalid("source de mise à jour inconnue."),
            };
            return await Service(context).SaveSourceAsync(kind, BridgeArguments.GetOptionalString(values, "folder"), ct).ConfigureAwait(false);
        }, "Mémorise le choix et le dossier dans les données locales ; aucun accès réseau ni export partageable.");
        yield return new("updates.read", OperationClass.LocalRead, async (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            return await Service(context).ReadAsync(ct).ConfigureAwait(false);
        }, "État local des mises à jour ; aucune recherche réseau implicite.");
        yield return new("updates.inspect", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["kind", "path", "sha256"]);
            var kind = BridgeArguments.GetString(values, "kind") switch
            {
                "folder" => UpdateSourceKind.Folder,
                "manualZip" => UpdateSourceKind.ManualZip,
                _ => throw BridgeArguments.Invalid("source de mise à jour inconnue."),
            };
            var request = new UpdateSourceRequest(kind, BridgeArguments.GetOptionalString(values, "path"), BridgeArguments.GetOptionalString(values, "sha256"));
            // Instant pris avant l'inspection : l'échéance enregistrée ne dépasse jamais celle de l'offre chez le service.
            var requested = context.Clock.GetUtcNow();
            var offer = await Service(context).InspectAsync(request, ct).ConfigureAwait(false);
            // La confirmation Windows de la préparation montrera cette offre et sa source, jamais les arguments de la page.
            if (context.Mode == AppMode.Poste) ConfirmationRecords.For(context).RecordOffer(offer, request, requested);
            return offer;
        }, "Recherche explicitement une offre dans un dossier local/UNC ou un ZIP choisi.", ServedInSimulation: true);
        yield return new("updates.prepare", OperationClass.LocalWrite, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["offerId", "confirmed"]);
            var offerId = BridgeArguments.GetString(values, "offerId");
            var confirmed = BridgeArguments.GetBoolean(values, "confirmed");
            var service = Service(context);
            if (context.Mode == AppMode.Simulation || !confirmed) return await service.PrepareAsync(offerId, confirmed, ct).ConfigureAwait(false);
            using var turn = NativeConfirmation.Begin(context);
            var records = ConfirmationRecords.For(context);
            var record = records.Offer(offerId) ?? throw new FilproOperationException(OfferCode, OfferMessage);
            var status = await service.ReadAsync(ct).ConfigureAwait(false);
            turn.RequireTimeLeft(record.Deadline, OfferCode, OfferMessage);
            return await turn.ConfirmThenAsync(() => ConfirmationSheets.UpdatePrepare(record, status.CurrentVersion), async () =>
            {
                var prepared = await service.PrepareAsync(offerId, true, ct).ConfigureAwait(false);
                if (prepared.TransactionId is { } transactionId) records.RecordProvenance(transactionId, record);
                return prepared;
            }, ct).ConfigureAwait(false);
        }, "Après accord, prépare et valide une copie isolée sans remplacer l’application.");
        foreach (var rollback in new[] { false, true })
        {
            var operation = rollback ? "rollback" : "install";
            yield return new("updates." + operation, OperationClass.Write, async (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["transactionId", "confirmed"]);
                var transactionId = BridgeArguments.GetString(values, "transactionId");
                var confirmed = BridgeArguments.GetBoolean(values, "confirmed");
                using var turn = confirmed ? NativeConfirmation.Begin(context) : null;
                using var admission = context.Operations.EnterUpdateHandoff(() =>
                    context.Service<IFilproExecutionService>()?.Active().Any() != true);
                RequireIdle(context);
                var service = Service(context, simulation: false);
                async Task<UpdateHandoff> HandOffAsync()
                {
                    var result = rollback
                        ? await service.RollbackAsync(transactionId, confirmed, ct).ConfigureAwait(false)
                        : await service.InstallAsync(transactionId, confirmed, ct).ConfigureAwait(false);
                    if (result.CloseApplicationRequired) await context.Require<IApplicationSessionControl>().RequestExitAsync(ct).ConfigureAwait(false);
                    return result;
                }
                if (turn is null) return await HandOffAsync().ConfigureAwait(false);
                // Seule la transaction courante, dans l'état exigé, est montrée puis remise au moteur de remplacement.
                var status = await service.ReadAsync(ct).ConfigureAwait(false);
                if (!string.Equals(status.TransactionId, transactionId, StringComparison.Ordinal)
                    || (rollback ? !status.CanRollback : status.Stage != UpdateStage.Prepared || !status.CanInstall))
                    throw rollback
                        ? new FilproOperationException("update.rollback", "Aucune version précédente ni reprise disponible pour cette transaction.")
                        : new FilproOperationException("update.state", "La transaction n'est pas préparée pour installation.");
                var provenance = ConfirmationRecords.For(context).Provenance(transactionId);
                return await turn.ConfirmThenAsync(() => rollback ? ConfirmationSheets.UpdateRollback(status) : ConfirmationSheets.UpdateInstall(status, provenance),
                    HandOffAsync, ct).ConfigureAwait(false);
            }, "Remplacement transactionnel ou retour arrière explicite ; l’hôte se ferme proprement avant le travail externe.");
        }
    }
}
