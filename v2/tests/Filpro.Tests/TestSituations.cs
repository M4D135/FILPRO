using Filpro.Core;

namespace Filpro.Tests;

/// <summary>
/// Situations de l'affichage pour les contextes de poste des tests : sans situation enregistrée, le pont applique les règles
/// les plus strictes (compte indéterminé).
/// </summary>
internal static class TestSituations
{
    public const string HostSid = "S-1-5-21-1000-2000-3000-500";
    public const string SessionSid = "S-1-5-21-1000-2000-3000-1001";

    public static HostSituation SameAccount { get; } =
        new(SessionSituation.SameAccount, HostSid, @"POSTE-01\Administrateur", HostSid, @"POSTE-01\Administrateur");

    public static HostSituation CrossAccount { get; } =
        new(SessionSituation.CrossAccount, HostSid, @"POSTE-01\Technicien", SessionSid, @"POSTE-01\Utilisateur");

    public static HostSituation Unknown { get; } =
        new(SessionSituation.Unknown, HostSid, @"POSTE-01\Technicien", null, null);

    /// <summary>Contexte de poste dont l'affichage tourne sous le compte élevé, comme sur le poste de l'administrateur.</summary>
    public static EngineContext WithSameAccount(this EngineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Register(SameAccount);
        return context;
    }
}
