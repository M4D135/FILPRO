namespace Filpro.Core;

/// <summary>
/// Dérivation pure des preuves de session et du tri des profils, reprise des fonctions « pures » de la V1
/// (Get-FilproProfileSessionEvidence, Sort-FilproProfileCandidates). Aucune lecture système : les lignes WTS sont fournies
/// par le fournisseur. Un état inconnu n'est jamais converti en « aucune session » et un compte homonyme ne remplace jamais
/// le SID exact.
/// </summary>
public static class SessionEvidenceReader
{
    /// <summary>
    /// Dérive l'état de session d'un SID à partir d'un instantané WTS. Règles : lecture indisponible → inconnu ; lignes du
    /// SID → active si une session est connectée (0), déconnectée si une est en 4, sinon inconnu ; aucune ligne et lecture
    /// complète → aucune session ; aucune ligne et lecture partielle → inconnu (absence non prouvée).
    /// </summary>
    public static SessionEvidence Derive(string sid, WindowsSessionInventory? inventory, string source = "WTS")
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        var readStatus = inventory?.ReadStatus ?? SessionReadStatus.Unavailable;
        var reason = inventory is null ? "État des sessions non lu." : inventory.Error ?? string.Empty;
        if (readStatus == SessionReadStatus.Unavailable)
        {
            return new SessionEvidence(SessionState.Unknown, null, [], source, readStatus, reason);
        }

        var rows = new List<WindowsSession>();
        foreach (var session in inventory!.Sessions)
        {
            if (session is not null && string.Equals(session.Sid, sid, StringComparison.Ordinal))
            {
                rows.Add(session);
            }
        }

        if (rows.Count > 0)
        {
            var ids = rows.Select(r => r.SessionId).Distinct().OrderBy(id => id).ToList();
            var state = rows.Any(r => r.ConnectState == 0)
                ? SessionState.Active
                : rows.Any(r => r.ConnectState == 4) ? SessionState.Disconnected : SessionState.Unknown;
            return new SessionEvidence(state, ids.Count, ids, source, readStatus, reason);
        }

        return readStatus == SessionReadStatus.Complete
            ? new SessionEvidence(SessionState.None, 0, [], source, readStatus, reason)
            : new SessionEvidence(SessionState.Unknown, null, [], source, readStatus, reason);
    }

    /// <summary>Ordre d'affichage stable : session active d'abord, puis compte, chemin, SID (aucune date technique).</summary>
    public static IReadOnlyList<ProfileCandidate> Sort(IReadOnlyList<ProfileCandidate> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return
        [
            .. profiles.Where(p => p is not null)
                .OrderBy(p => p.Session.State == SessionState.Active ? 0 : 1)
                .ThenBy(p => p.Account, StringComparer.Ordinal)
                .ThenBy(p => p.LocalPath, StringComparer.Ordinal)
                .ThenBy(p => p.Sid, StringComparer.Ordinal),
        ];
    }
}
