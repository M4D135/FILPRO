using Filpro.Core;

namespace Filpro.Simulation;

/// <summary>Modèle commun fictif : même validation, aucune persistance ni chemin réel.</summary>
public sealed partial class SimulationCommonSelectionService(TimeProvider clock) : IFilproCommonSelectionService
{
    private readonly object gate = new();
    private CommonSelectionState state = new(true, false, [], []);
    public bool Demonstration => true;
    public CommonSelectionState Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return state with { AppData = state.AppData.ToArray(), RegistryKeys = state.RegistryKeys.ToArray() };
    }
    public CommonSelectionState Save(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = SoftwareSelectionCodec.Decode(SoftwareSelectionCodec.Encode(new(SoftwareSelectionCodec.Schema,
            SoftwareSelectionCodec.Kind, clock.GetUtcNow(), files, registry, SoftwareSelectionCodec.DefaultPreferences)));
        lock (gate) { state = new(true, true, document.Files, document.Registry); appliedPatches.Clear(); }
        return Read(cancellationToken);
    }
    public CommonSelectionState Clear(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) { state = new(true, false, [], []); appliedPatches.Clear(); }
        return Read(cancellationToken);
    }
}
