using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Opérations de l'écran Analyser / réparer : lecture seule des profils (SID, sessions WTS), de la sélection groupée
/// fichiers+registre et de la dernière sélection, via le fournisseur poste actif (natif ou démonstration). Aucune écriture
/// système n'est exposée ici ; le moteur de réparation réel viendra dans un lot ultérieur.
/// </summary>
public sealed class AnalyserModule : IOperationModule
{
    public string Screen => "analyser";

    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            new OperationDefinition("analyser.profiles", OperationClass.NetworkRead, (args, _, ct) =>
            {
                BridgeArguments.RequireNone(args);
                var poste = RequirePoste(context);
                return Task.FromResult<object?>(poste.ReadProfiles(ct));
            }, "Profils du poste (SID, compte, chargement, sessions WTS), en lecture seule.", ServedInSimulation: true),

            new OperationDefinition("analyser.software", OperationClass.NetworkRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid"]);
                var sid = BridgeArguments.GetString(values, "sid");
                var poste = RequirePoste(context);
                return Task.FromResult<object?>(poste.ReadSoftware(sid, SoftwareQuery.Empty, ct));
            }, "Sélection groupée fichiers+registre d’un profil, avec recommandations et modèles communs.", ServedInSimulation: true),

            new OperationDefinition("analyser.lastSelection", OperationClass.NetworkRead, (args, _, ct) =>
            {
                var values = BridgeArguments.ReadObject(args, ["sid"]);
                var sid = BridgeArguments.GetString(values, "sid");
                var poste = RequirePoste(context);
                return Task.FromResult<object?>(poste.ReadLastSelection(sid, ct));
            }, "Dernière sélection enregistrée pour ce compte, ou null s’il n’y en a pas (jamais inventée).", ServedInSimulation: true),
        ];
    }

    private static IFilproPosteSource RequirePoste(EngineContext context)
    {
        var source = context.Poste;
        if (source is null || source.Demonstration != (context.Mode == AppMode.Simulation))
            throw new FilproOperationException(FilproErrors.SourceUnavailable, FilproErrors.SourceUnavailableMessage);
        return source;
    }
}
