namespace Filpro.Core;

/// <summary>Textes français opérateur, indépendants de la culture du poste (repris du kit DOMENIA, réduit au strict besoin).</summary>
public static class FrenchText
{
    /// <summary>Espace insécable, avant « : », « % » et dans « ».</summary>
    public const string Nbsp = " ";

    /// <summary>« : » avec son espace insécable.</summary>
    public const string Colon = " : ";

    /// <summary>Texte entre guillemets français avec espaces insécables : « texte ».</summary>
    public static string Quote(string text) => "«" + Nbsp + text + Nbsp + "»";
}
