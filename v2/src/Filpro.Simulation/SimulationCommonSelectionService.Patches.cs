using Filpro.Core;

namespace Filpro.Simulation;

public sealed partial class SimulationCommonSelectionService
{
    private readonly Dictionary<string, byte[]> exportedPatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (CommonPatchPreview Preview, CommonSelectionState Incoming, DateTimeOffset Expires)> pendingPatches = new(StringComparer.Ordinal);
    private readonly HashSet<string> appliedPatches = new(StringComparer.Ordinal);

    public CommonPatchResult Export(string destination, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); SimulationWorld.RequirePath(destination); ConfirmPatch(confirmed);
        lock (gate)
        {
            if (exportedPatches.Count >= 16 && !exportedPatches.ContainsKey(destination)) throw SimulationWorld.Refused("Limite d'exports fictifs atteinte ; réinitialiser la démo.");
            var bytes = CommonSelectionPatch.Encode(state); exportedPatches[destination] = bytes;
            return new(true, state, CommonSelectionPatch.Hash(bytes), destination, null, false, "Export fictif en mémoire ; aucun fichier ou partage réel modifié.");
        }
    }
    public CommonPatchPreview PreviewPatch(string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); SimulationWorld.RequirePath(source);
        lock (gate)
        {
            var example = new CommonSelectionState(true, true,
                [new(SoftwareScope.Roaming, "FILPRO-Exemple", "Application fictive", SoftwareCategory.Modele, true, false, false, null, false, false)],
                [new(SoftwareScope.Hkcu, @"Software\FILPRO-Exemple", "Réglages fictifs", SoftwareCategory.Modele, true, false, false, null, false, false)]);
            var bytes = exportedPatches.TryGetValue(source, out var exported) ? exported : CommonSelectionPatch.Encode(example);
            var incoming = CommonSelectionPatch.Decode(bytes, true);
            var preview = new CommonPatchPreview(Guid.NewGuid().ToString("N"), true, CommonSelectionPatch.Hash(bytes), CommonSelectionPatch.Revision(state),
                CommonSelectionPatch.Describe(state, incoming), "Patch entièrement fictif. Les choix absents restent conservés ; aucune donnée réelle n'est lue.");
            if (pendingPatches.Count >= 16) pendingPatches.Remove(pendingPatches.Keys.First());
            pendingPatches.Add(preview.Id, (preview, incoming, clock.GetUtcNow().AddMinutes(15)));
            return preview;
        }
    }
    public CommonPatchResult ApplyPatch(string previewId, bool replaceConflicts, bool confirmed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); ConfirmPatch(confirmed);
        lock (gate)
        {
            if (!pendingPatches.TryGetValue(previewId, out var pending) || pending.Expires < clock.GetUtcNow()
                || pending.Preview.ExpectedRevision != CommonSelectionPatch.Revision(state)) throw SimulationWorld.Refused("Aperçu fictif périmé ; relire le patch.");
            var already = appliedPatches.Contains(pending.Preview.Sha256);
            if (pending.Incoming.AppData.Count + pending.Incoming.RegistryKeys.Count == 0)
            { pendingPatches.Remove(previewId); return new(true, state, pending.Preview.Sha256, null, null, false, "Patch fictif vide : aucun changement."); }
            if (!already)
            {
                state = CommonSelectionPatch.Merge(state, pending.Incoming, replaceConflicts);
                if (appliedPatches.Count >= 128) appliedPatches.Remove(appliedPatches.First());
                appliedPatches.Add(pending.Preview.Sha256);
            }
            pendingPatches.Remove(previewId);
            return new(true, state, pending.Preview.Sha256, null, null, already, already ? "Patch fictif déjà appliqué." : "Modèle fictif mis à jour en mémoire ; aucun profil réel modifié.");
        }
    }
    private static void ConfirmPatch(bool confirmed) { if (!confirmed) throw SimulationWorld.Refused("Confirmer le patch fictif."); }
}
