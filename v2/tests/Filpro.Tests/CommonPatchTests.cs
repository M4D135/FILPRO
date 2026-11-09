using Filpro.Core;
using Filpro.Platform;
using Filpro.Simulation;

namespace Filpro.Tests;

internal static class CommonPatchTests
{
    private static SoftwareItem Item(string path, string name) => new(SoftwareScope.Roaming, path, name, SoftwareCategory.Modele, true, false, false, null, false, false);
    private static string WritePatch(string root, params SoftwareItem[] items)
    {
        var path = Path.Combine(root, "modele-" + Guid.NewGuid().ToString("N") + ".json");
        var bytes = CommonSelectionPatch.Encode(new(false, true, items, []));
        File.WriteAllBytes(path, bytes); File.WriteAllText(path + ".sha256", CommonSelectionPatch.Hash(bytes));
        return path;
    }

    [Test] public static void AjoutsConflitsConservationEtReapplicationIdempotente()
    {
        using var fixture = new PackTestFixture();
        var service = new LocalCommonSelectionService(fixture.Data, TimeProvider.System);
        service.Save([Item("Editeur", "Correction locale"), Item("LocalSeulement", "Préserver")], [], default);
        var patch = WritePatch(fixture.Root, Item("Editeur", "Libellé ' +33 | ;"), Item("Nouveau", "Nouveau logiciel"));
        var preview = service.PreviewPatch(patch, default);
        Check.Equal(1, preview.Changes.Count(change => change.Disposition == "conflict"), "conflit décrit");
        var applied = service.ApplyPatch(preview.Id, false, true, default);
        Check.Equal(3, applied.State.AppData.Count, "ajout sans suppression des absents");
        Check.Equal("Correction locale", applied.State.AppData.Single(item => item.Rel == "Editeur").Name, "conflit préservé par défaut");
        Check.True(File.Exists(applied.BackupPath), "sauvegarde réelle de l'ancien modèle");
        var before = File.ReadAllBytes(applied.WrittenPath!);
        var again = service.ApplyPatch(service.PreviewPatch(patch, default).Id, true, true, default);
        Check.True(again.AlreadyApplied, "empreinte appliquée reconnue");
        Check.True(before.SequenceEqual(File.ReadAllBytes(applied.WrittenPath!)), "aucune réécriture au rejeu");
        service.Save([Item("Editeur", "Nouvelle correction locale")], [], default);
        var replace = service.ApplyPatch(service.PreviewPatch(patch, default).Id, true, true, default);
        Check.Equal("Libellé ' +33 | ;", replace.State.AppData.Single(item => item.Rel == "Editeur").Name, "remplacement explicite et valeurs JSON conservées");
    }

    [Test] public static void PatchAltereContexteChangeEtAnnulationRefusentTouteMutation()
    {
        using var fixture = new PackTestFixture(); var service = new LocalCommonSelectionService(fixture.Data, TimeProvider.System);
        var patch = WritePatch(fixture.Root, Item("Editeur", "Modèle"));
        var first = service.PreviewPatch(patch, default);
        Check.Throws<FilproOperationException>(() => service.ApplyPatch(first.Id, false, false, default), "confirmation requise");
        Check.False(service.Read(default).HasSelection, "pas d'écriture après refus");
        service.Save([Item("Autre", "Local")], [], default);
        Check.Throws<FilproOperationException>(() => service.ApplyPatch(first.Id, true, true, default), "contexte concurrent");
        var fresh = service.PreviewPatch(patch, default); File.AppendAllText(patch, " ");
        Check.Throws<FilproOperationException>(() => service.ApplyPatch(fresh.Id, true, true, default), "empreinte altérée");
        Check.Equal("Autre", service.Read(default).AppData.Single().Rel, "choix local intact");
    }

    [Test] public static void ExportReimportEtPatchVideSansSuppression()
    {
        using var fixture = new PackTestFixture(); var source = new LocalCommonSelectionService(fixture.Data, TimeProvider.System);
        source.Save([Item("Logiciel", "Exemple")], [], default);
        var export = Path.Combine(fixture.Root, "export.json"); source.Export(export, true, default);
        var target = new LocalCommonSelectionService(Path.Combine(fixture.Root, "second"), TimeProvider.System);
        var applied = target.ApplyPatch(target.PreviewPatch(export, default).Id, false, true, default);
        Check.Equal("Logiciel", applied.State.AppData.Single().Rel, "modèle importé depuis l'export");
        var empty = WritePatch(fixture.Root);
        var unchanged = target.ApplyPatch(target.PreviewPatch(empty, default).Id, true, true, default);
        Check.Equal(1, unchanged.State.AppData.Count, "un patch vide ne supprime rien");
        Check.Throws<FilproOperationException>(() => source.Export(export, true, default), "export existant préservé");
    }

    [Test] public static void AutreApplicationEtOptionsActivesRefusees()
    {
        var bytes = CommonSelectionPatch.Encode(new(false, true, [Item("Logiciel", "Exemple")], []));
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Check.Throws<FilproOperationException>(() => CommonSelectionPatch.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace("FILPRO", "Autre", StringComparison.Ordinal)), false), "application différente");
        Check.Throws<FilproOperationException>(() => CommonSelectionPatch.Decode(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"vss\":false", "\"vss\":true", StringComparison.Ordinal)), false), "pas de consentement importé");
        var demo = new SimulationCommonSelectionService(TimeProvider.System);
        Check.Throws<FilproOperationException>(() => demo.PreviewPatch(@"C:\Users\PersonneReelle\patch.json", default), "fichier réel interdit en démo");
        Check.False(demo.Read(default).HasSelection, "démo intacte après refus");
    }
}
