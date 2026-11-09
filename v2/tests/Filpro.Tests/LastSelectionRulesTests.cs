using Filpro.Core;

namespace Filpro.Tests;

internal sealed class LastSelectionRulesTests
{
    private const string Sid = "S-1-5-21-111222333-444555666-777888999-1001";
    private static readonly SoftwareItem FileItem = new(SoftwareScope.Roaming, "Notepad++", "Notepad++",
        SoftwareCategory.Donnees, false, true, true, 42, true, true);
    private static readonly SoftwareItem RegistryItem = new(SoftwareScope.Hkcu, @"Software\Notepad++", "Notepad++",
        SoftwareCategory.Autre, false, false, true, null, false, false);

    [Test]
    public void AbsenceDeMemoireEtChoixVideNeSontPasConfondus()
    {
        var recommended = LastSelectionRules.Apply(Sid, [FileItem], [RegistryItem], null).Single();
        Check.True(recommended.Selected == true, "sans choix enregistré, fichiers et clé sont recommandés ensemble");
        var empty = new LastSelection(Sid, [], [], DateTimeOffset.UnixEpoch);
        var kept = LastSelectionRules.Apply(Sid, [FileItem], [RegistryItem], empty).Single();
        Check.True(kept.Selected == false, "un choix vide volontaire ne réactive pas les recommandations");
    }

    [Test]
    public void LaMemoireNeRecreePasUnElementAbsentEtGardeLaNouvelleMesure()
    {
        var absent = FileItem with { Rel = "Absent", Name = "Absent", Size = null, SizeMeasured = false };
        var last = new LastSelection(Sid, [FileItem with { Size = 1 }, absent], [RegistryItem], DateTimeOffset.UnixEpoch);
        var kept = LastSelectionRules.Apply(Sid, [FileItem], [RegistryItem], last);
        Check.Equal(1, kept.Count, "le dossier absent n'est pas ajouté à l'inventaire");
        Check.True(kept[0].Selected == true, "les éléments encore présents retrouvent le choix");
        Check.Equal((long?)42, kept[0].Files.Single().Size, "la mesure fraîche remplace l'ancienne mesure");
        Check.Equal(1, kept[0].Registry.Count, "la clé fait partie du même groupe");
    }

    [Test]
    public void LaSelectionDunAutreSidEstRefusee()
    {
        var error = Check.Throws<FilproOperationException>(() => LastSelectionRules.Apply(Sid, [FileItem], [],
            new LastSelection("S-1-5-21-111222333-444555666-777888999-1002", [FileItem], [], DateTimeOffset.UnixEpoch)),
            "aucune substitution de compte");
        Check.Equal("selection.profile", error.Code, "refus explicite de l'identité différente");
    }
}
