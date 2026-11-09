using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>Mode immuable, génération de session et demandes explicites à l'hôte.</summary>
public sealed class SessionModule : IOperationModule
{
    public string Screen => "session";

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        yield return new("session.info", OperationClass.LocalRead, (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            ct.ThrowIfCancellationRequested();
            var decision = context.Decision;
            var execution = context.Service<IFilproExecutionService>();
            var capabilities = execution is not null && execution.Demonstration == (context.Mode == AppMode.Simulation) ? execution.Capabilities : null;
            return Task.FromResult<object?>(new SessionInfo(
                decision.Mode == AppMode.Simulation ? ModeTexts.SimulationLabel : ModeTexts.PosteLabel,
                decision.Mode == AppMode.Simulation, decision.Banner, context.Clock.GetUtcNow(), context.Generation,
                new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["backup"] = capabilities?.Backup == true,
                    ["restore"] = capabilities?.Restore == true,
                    ["repair"] = capabilities?.Repair == true,
                    ["recover"] = capabilities?.Recover == true,
                    ["configuration"] = context.Service<IFilproConfigurationService>()?.Demonstration == (context.Mode == AppMode.Simulation),
                    ["updates"] = context.Service<IFilproUpdateService>()?.Demonstration == (context.Mode == AppMode.Simulation),
                    ["filePicker"] = context.Mode == AppMode.Poste && context.Service<IApplicationSessionControl>() is not null,
                    // Indication d'affichage seulement : la décision reste prise dans la fenêtre Windows de l'hôte.
                    ["nativeConfirmation"] = context.Mode == AppMode.Poste && context.Service<IOperatorConfirmation>() is not null,
                    // Indications d'affichage seulement : l'admission du pont applique ces règles à chaque demande.
                    ["crossAccount"] = context.Mode == AppMode.Poste && NativeConfirmation.AcrossAccounts(context),
                    ["codeUpdates"] = context.Mode != AppMode.Poste || !NativeConfirmation.AcrossAccounts(context),
                    ["presenceLocked"] = context.Mode == AppMode.Poste && NativeConfirmation.AcrossAccounts(context) && NativeConfirmation.Locked(context),
                    ["packModifiable"] = context.Mode == AppMode.Poste && context.Service<PackExposure>() is not null,
                }, capabilities?.UnavailableReasons ?? (context.Mode == AppMode.Poste
                    ? ["Les opérations natives nécessitent le pack complet avec son manifeste d’intégrité et les outils USMT vérifiés."] : [])));
        }, "Mode, génération et capacités effectives de cette session.");

        foreach (var (name, target) in new[] { ("demoEnter", AppMode.Simulation), ("demoExit", AppMode.Poste), ("demoReset", AppMode.Simulation) })
        {
            yield return new("session." + name, OperationClass.Local, async (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["confirmed"]);
                if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer le changement de session.");
                if (name == "demoReset" && context.Mode != AppMode.Simulation)
                    throw new FilproOperationException("simulation.mode", "La réinitialisation concerne uniquement la démonstration.");
                var execution = context.Service<IFilproExecutionService>();
                if (execution?.Active().Any() == true)
                    throw new FilproOperationException("session.busy", "Terminez ou annulez l’opération en cours avant de changer de session.");
                if (context.Service<IFilproUpdateService>() is { } updates)
                {
                    var status = await updates.ReadAsync(ct).ConfigureAwait(false);
                    if (status.Stage is UpdateStage.Preparing or UpdateStage.WaitingForExit or UpdateStage.OldMoved or UpdateStage.NewInstalled or UpdateStage.AwaitingHealth or UpdateStage.RollingBack)
                        throw new FilproOperationException("session.busy", "Terminez la mise à jour en cours avant de changer de session.");
                }
                await context.Require<IApplicationSessionControl>().RequestRestartAsync(target, ct).ConfigureAwait(false);
                return new { Restarting = true };
            }, "Recrée la session et ses fournisseurs après confirmation, sans conserver les données du mode précédent.");
        }

        yield return new("session.confirmClose", OperationClass.Local, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["confirmed"]);
            if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer la fermeture et l’arrêt des opérations.");
            await context.Require<IApplicationSessionControl>().RequestExitAsync(ct).ConfigureAwait(false);
            return new { Closing = true };
        }, "Confirme la fermeture ; l’hôte attend la fin du nettoyage des opérations.");

        yield return new("session.ready", OperationClass.Local, async (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["generation"]);
            var generation = BridgeArguments.GetString(values, "generation");
            if (!string.Equals(generation, context.Generation, StringComparison.Ordinal))
                throw new FilproOperationException("session.stale", "Cette page appartient à une autre session.");
            await context.Require<IApplicationSessionControl>().AcknowledgeReadyAsync(generation, ct).ConfigureAwait(false);
            return new { Ready = true, Generation = generation };
        }, "Confirme le rendu de la page actuelle ; l’hôte vérifie aussi le chargement WebView2.");

        yield return new("session.choosePath", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            if (context.Mode != AppMode.Poste) throw new FilproOperationException(FilproErrors.SimulationUnavailable, FilproErrors.SimulationUnavailableMessage);
            var values = BridgeArguments.ReadObject(args, ["kind"]);
            var kind = BridgeArguments.GetString(values, "kind") switch
            {
                "folder" => ApplicationPathKind.Folder,
                "openConfiguration" => ApplicationPathKind.OpenConfiguration,
                "saveConfiguration" => ApplicationPathKind.SaveConfiguration,
                "updateZip" => ApplicationPathKind.UpdateZip,
                "encryptionKey" => ApplicationPathKind.EncryptionKey,
                "repairReceipt" => ApplicationPathKind.RepairReceipt,
                _ => throw BridgeArguments.Invalid("type de sélection de fichier inconnu."),
            };
            return new { Path = await context.Require<IApplicationSessionControl>().PickPathAsync(kind, ct).ConfigureAwait(false) };
        }, "Sélection explicite d’un fichier ou dossier par le dialogue de l’hôte, uniquement en mode poste.");
    }
}
