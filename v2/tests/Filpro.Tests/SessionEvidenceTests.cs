using Filpro.Core;

namespace Filpro.Tests;

/// <summary>Dérivation pure des preuves de session WTS et tri des profils (règles du correctif V1).</summary>
internal sealed class SessionEvidenceTests
{
    private static WindowsSession Row(string sid, int id, int state) => new(id, state, sid, "LAB\\u", "WTS token SID");
    private static WindowsSessionInventory Inv(SessionReadStatus status, params WindowsSession[] rows) => new(status, rows, status == SessionReadStatus.Complete ? null : "fixture");

    [Test]
    public void LectureCompleteVideDonneAucuneSession()
    {
        var e = SessionEvidenceReader.Derive("S-1-5-21-1-2-3-1001", Inv(SessionReadStatus.Complete));
        Check.Equal(SessionState.None, e.State, "aucune session prouvée");
        Check.Equal(0, e.Count!.Value, "compteur zéro");
        Check.Equal(0, e.Ids.Count, "aucun identifiant");
    }

    [Test]
    public void IndisponibleNeConvertitJamaisEnAucuneSession()
    {
        var e = SessionEvidenceReader.Derive("S-1-5-21-1-2-3-1001", Inv(SessionReadStatus.Unavailable, Row("S-1-5-21-1-2-3-1001", 2, 0)));
        Check.Equal(SessionState.Unknown, e.State, "indisponible → inconnu");
        Check.Null(e.Count, "aucun compteur fictif");
    }

    [Test]
    public void PartielVideInterditAbsenceAffirmee()
    {
        var e = SessionEvidenceReader.Derive("S-1-5-21-1-2-3-1003", Inv(SessionReadStatus.Partial));
        Check.Equal(SessionState.Unknown, e.State, "partiel sans ligne → inconnu, pas aucune");
        Check.Null(e.Count, "compteur nul");
    }

    [Test]
    public void EtatWTS0Actif4DeconnecteAutreInconnu()
    {
        var a = SessionEvidenceReader.Derive("S", Inv(SessionReadStatus.Complete, Row("S", 2, 0)));
        Check.Equal(SessionState.Active, a.State, "0 = active");
        Check.Equal(1, a.Count!.Value, "une session");
        var d = SessionEvidenceReader.Derive("S", Inv(SessionReadStatus.Complete, Row("S", 2, 4)));
        Check.Equal(SessionState.Disconnected, d.State, "4 = déconnectée");
        var u = SessionEvidenceReader.Derive("S", Inv(SessionReadStatus.Complete, Row("S", 2, 1)));
        Check.Equal(SessionState.Unknown, u.State, "1 = inconnu");
        Check.Equal(1, u.Count!.Value, "la session reste comptée");
    }

    [Test]
    public void MemeSidPlusieursSessionsIdentifiantsUniques()
    {
        var inv = Inv(SessionReadStatus.Complete, Row("A", 2, 4), Row("A", 7, 0), Row("A", 7, 0), Row("B", 8, 0));
        var e = SessionEvidenceReader.Derive("A", inv);
        Check.Equal(SessionState.Active, e.State, "une session connectée rend actif");
        Check.Equal(2, e.Count!.Value, "identifiants uniques 2 et 7");
        Check.Equal("2,7", string.Join(",", e.Ids), "tri des identifiants");
        Check.Equal(SessionState.None, SessionEvidenceReader.Derive("C", inv).State, "un SID absent = aucune (lecture complète)");
    }

    [Test]
    public void TriMetLaSessionActiveEnTetePuisCompteCheminSid()
    {
        SessionEvidence Ev(SessionState s) => new(s, 0, [], "WTS", SessionReadStatus.Complete, null);
        ProfileCandidate P(string sid, string account, string path, SessionState s) =>
            new(sid, ProfileIdentityKind.CompteLocalOuDomaine, true, account, path, false, Ev(s), true, [], false, false, false, false, null);
        var sorted = SessionEvidenceReader.Sort([
            P("b", "LAB\\alpha", "/profiles/alpha", SessionState.None),
            P("c", "LAB\\bravo", "/profiles/bravo", SessionState.Disconnected),
            P("a", "LAB\\zeta", "/profiles/zeta", SessionState.Active),
        ]);
        Check.Equal("a,b,c", string.Join(",", sorted.Select(p => p.Sid)), "actif d’abord, puis compte, chemin, SID");
    }
}
