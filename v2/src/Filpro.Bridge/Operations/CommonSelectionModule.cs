using Filpro.Core;

namespace Filpro.Bridge;

public sealed class CommonSelectionModule : IOperationModule
{
    public string Screen => "sauvegarder";
    private static IFilproCommonSelectionService Service(EngineContext context)
    {
        var service = context.Require<IFilproCommonSelectionService>();
        if (service.Demonstration != (context.Mode == AppMode.Simulation))
            throw new FilproOperationException("selection.mode", "Le modèle commun n’appartient pas à cette session.");
        return service;
    }
    public IEnumerable<OperationDefinition> CreateOperations(EngineContext context)
    {
        yield return new("sauvegarder.exportCommonPatch", OperationClass.LocalWrite, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["destination", "confirmed"]);
            return Task.FromResult<object?>(Service(context).Export(BridgeArguments.GetString(values, "destination"), BridgeArguments.GetBoolean(values, "confirmed"), ct));
        }, "Exporte explicitement les choix communs dans un patch versionné et son empreinte.");
        yield return new("sauvegarder.previewCommonPatch", OperationClass.NetworkRead, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["source"]);
            return Task.FromResult<object?>(Service(context).PreviewPatch(BridgeArguments.GetString(values, "source"), ct));
        }, "Relit le patch choisi et son empreinte ; aperçu des ajouts et conflits sans écriture.", ServedInSimulation: true);
        yield return new("sauvegarder.previewCommonPatchFromSource", OperationClass.NetworkRead, async (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args);
            var source = await UpdatesModule.Service(context).ReadSourceAsync(ct).ConfigureAwait(false);
            if (source.Kind != UpdateSourceKind.Folder || source.Folder is null) throw new FilproOperationException("templates.source", "Mémoriser d'abord un dossier de diffusion dans les mises à jour.");
            var path = source.Demonstration ? source.Folder.TrimEnd('\\') + "\\" + CommonSelectionPatch.FileName : Path.Combine(source.Folder, CommonSelectionPatch.FileName);
            return Service(context).PreviewPatch(path, ct);
        }, "Cherche explicitement FILPRO-modeles.json dans le dossier de diffusion mémorisé.", ServedInSimulation: true);
        yield return new("sauvegarder.applyCommonPatch", OperationClass.LocalWrite, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["previewId", "replaceConflicts", "confirmed"]);
            return Task.FromResult<object?>(Service(context).ApplyPatch(BridgeArguments.GetString(values, "previewId"),
                BridgeArguments.GetBoolean(values, "replaceConflicts"), BridgeArguments.GetBoolean(values, "confirmed"), ct));
        }, "Applique le patch frais, conserve les entrées absentes et protège les conflits locaux par défaut.");
        yield return new("sauvegarder.readCommonSelection", OperationClass.LocalRead, (args, _, ct) =>
        {
            BridgeArguments.RequireNone(args); return Task.FromResult<object?>(Service(context).Read(ct));
        }, "Choix communs de l’opérateur, séparés des réglages partageables.");
        yield return new("sauvegarder.clearCommonSelection", OperationClass.LocalWrite, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["confirmed"]);
            if (!BridgeArguments.GetBoolean(values, "confirmed")) throw BridgeArguments.Invalid("confirmer le retrait du modèle commun.");
            return Task.FromResult<object?>(Service(context).Clear(ct));
        }, "Retire les choix communs en conservant une sauvegarde locale du document précédent.");
        yield return new("sauvegarder.setCommonSelection", OperationClass.LocalWrite, (args, _, ct) =>
        {
            var values = BridgeArguments.ReadObject(args, ["sid", "groupIds"]);
            var service = Service(context);
            var poste = context.Poste ?? throw new FilproOperationException(FilproErrors.SourceUnavailable, "Fournisseur poste absent.");
            if (poste.Demonstration != service.Demonstration) throw new FilproOperationException("selection.mode", "Fournisseur incompatible.");
            var sid = BridgeArguments.GetString(values, "sid");
            if (!poste.ReadProfiles(ct).Profiles.Any(profile => profile.Sid == sid))
                throw new FilproOperationException("selection.profile", "Profil inconnu : actualiser avant de mémoriser le modèle commun.");
            var ids = BridgeArguments.GetOptionalStringArray(values, "groupIds", SoftwareSelectionCodec.MaxItems)
                ?? throw BridgeArguments.Invalid("groupIds doit être un tableau.");
            var selected = ids.ToHashSet(StringComparer.Ordinal);
            if (selected.Count != ids.Count) throw BridgeArguments.Invalid("groupes répétés.");
            var inventory = poste.ReadSoftware(sid, SoftwareQuery.Empty, ct);
            if (!inventory.Discovery.FilesComplete || !inventory.Discovery.RegistryComplete)
                throw new FilproOperationException(FilproErrors.SourceUnavailable, "Inventaire incomplet : le modèle commun précédent est conservé.");
            if (!selected.IsSubsetOf(inventory.Groups.Select(group => group.Id))) throw BridgeArguments.Invalid("groupe absent de l’inventaire courant.");
            var groups = inventory.Groups.Where(group => selected.Contains(group.Id)).ToArray();
            return Task.FromResult<object?>(service.Save(
                groups.SelectMany(group => group.Files).Select(item => item with { Selected = true, Category = SoftwareCategory.Modele }).ToArray(),
                groups.SelectMany(group => group.Registry).Select(item => item with { Selected = true, Category = SoftwareCategory.Modele }).ToArray(), ct));
        }, "Mémorise ensemble fichiers et clés des groupes relus, sans chemin fourni par la page.", ServedInSimulation: true);
    }
}
