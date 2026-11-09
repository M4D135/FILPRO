using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Correctif des conflits : coexistence par défaut, décision obligatoire, refus des chevauchements contradictoires.</summary>
internal sealed class ConflictPolicyTests
{
    private static readonly SoftwareSizeSummary NoSize = new(SoftwareSizeState.Aucun, null, false, false, "Aucun dossier", 0, 0, 0, "");

    private static SoftwareItem Reg(string rel) => new(SoftwareScope.Hkcu, rel, rel, SoftwareCategory.Autre, false, false, false, null, false, false);
    private static SoftwareItem File(string rel) => new(SoftwareScope.Roaming, rel, rel, SoftwareCategory.Donnees, false, false, false, null, false, false);
    private static SoftwareGroup Group(string id, string name, string[] files, string[] registry) =>
        new(id, name, true, [.. files.Select(File)], [.. registry.Select(Reg)], null, 0, "", NoSize);

    [Test]
    public void SansDecisionLaCoexistenceEstLeDefaut()
    {
        var choices = new[] { Group("a", "Alpha", ["App"], ["Software\\A"]), Group("b", "Beta", ["App"], ["Software\\B"]) };
        var policies = RestoreConflictPolicies.Resolve(choices, null);
        Check.Equal(2, policies.Count, "une décision par groupe affiché");
        Check.True(policies.All(p => p.Mode == RestorePolicyMode.KeepBoth), "coexistence par défaut");
    }

    [Test]
    public void DecisionManquanteEstRefusee()
    {
        var choices = new[] { Group("a", "Alpha", ["App"], ["Software\\A"]), Group("b", "Beta", ["App"], ["Software\\B"]) };
        var ex = Check.Throws<FilproOperationException>(
            () => RestoreConflictPolicies.Resolve(choices, [new RestorePolicy("a", RestorePolicyMode.UseSaved)]),
            "tous les groupes doivent être décidés");
        Check.Equal(RestoreConflictPolicies.ConflictCode, ex.Code, "code conflict.policy");
    }

    [Test]
    public void ChevauchementDePolitiquesDifferentesEstRefuse()
    {
        var choices = new[] { Group("a", "Alpha", [], ["Software\\Shared"]), Group("b", "Beta", [], ["Software\\Shared"]) };
        Check.Throws<FilproOperationException>(
            () => RestoreConflictPolicies.Resolve(choices,
            [
                new RestorePolicy("a", RestorePolicyMode.UseSaved),
                new RestorePolicy("b", RestorePolicyMode.KeepCurrent),
            ]),
            "deux logiciels ne peuvent pas imposer des politiques différentes à la même clé");
    }

    [Test]
    public void ResumeAnnonceLaCoexistence()
    {
        var choices = new[] { Group("a", "Alpha", ["App"], ["Software\\A"]) };
        var policies = RestoreConflictPolicies.Resolve(choices, null);
        var summary = RestoreConflictPolicies.Summarize(choices, policies);
        Check.Contains("Coexistence", summary, "le résumé mentionne la coexistence");
        Check.Contains("Validation utilisateur restante", summary, "la validation cible reste à faire");
        var lines = RestoreConflictPolicies.SummaryLines(choices, policies);
        Check.Equal(2, lines.Count, "une ligne de décision puis la validation restante");
        Check.Equal(summary, string.Join("\r\n", lines), "le résumé joint ses lignes par CR LF");
        Check.Equal("Aucun réglage spécifique à restaurer.", string.Join("|", RestoreConflictPolicies.SummaryLines([], [])), "sans groupe, une seule ligne");
    }
}
