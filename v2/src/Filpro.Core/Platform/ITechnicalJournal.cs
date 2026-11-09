namespace Filpro.Core;

/// <summary>
/// Journal technique de l'application (%LOCALAPPDATA%\FILPRO\logs dans l'hôte). Ne reçoit jamais la saisie de la page,
/// des secrets ni des données en masse ; les implémentations ne lèvent jamais d'exception.
/// </summary>
public interface ITechnicalJournal
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception exception);
}

/// <summary>Journal qui ne conserve rien (tests purs, outils).</summary>
public sealed class SilentTechnicalJournal : ITechnicalJournal
{
    public static SilentTechnicalJournal Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception exception)
    {
    }
}
