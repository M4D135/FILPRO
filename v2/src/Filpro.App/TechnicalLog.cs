using System.Globalization;
using Filpro.Core;

namespace Filpro.App;

/// <summary>
/// Journal technique de l'hôte : logs\filpro.log dans le dossier de données de <see cref="AppPaths.UserData"/>. N'écrit que des codes, compteurs et échecs, jamais
/// la saisie de la page ni de secret. N'élève jamais d'exception : une panne du journal ne doit jamais empêcher l'application
/// de démarrer ou de se fermer.
/// </summary>
internal sealed class TechnicalLog : ITechnicalJournal
{
    public static TechnicalLog Instance { get; } = new();

    private readonly object gate = new();
    private readonly string? path;

    private TechnicalLog()
    {
        try
        {
            if (AppPaths.UserData is string data)
            {
                var folder = Path.Combine(data, "logs");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, "filpro.log");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            path = null;
        }
        // Constats de choix du dossier de données, connus avant l'ouverture du journal.
        foreach (var note in UserDataLocation.Notes) Write("INFO", note, null);
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warning(string message) => Write("WARN", message, null);

    public void Error(string message, Exception exception) => Write("ERREUR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        if (path is null)
        {
            return;
        }
        try
        {
            var line = $"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z {level} {message}";
            if (exception is not null)
            {
                // Type et message courts seulement : la pile complète reste hors du journal partagé.
                line += $" [{exception.GetType().Name}: {exception.Message}]";
            }
            lock (gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le journal ne doit jamais faire échouer l'application.
        }
    }
}
