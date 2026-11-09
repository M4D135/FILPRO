using System.Security.Principal;
using Filpro.Core;

namespace Filpro.App;

/// <summary>Emplacements utilisés par l'application portable : tout à côté de l'exe, données utilisateur sous LOCALAPPDATA.</summary>
internal static class AppPaths
{
    public static string AppDirectory { get; } = AppContext.BaseDirectory;

    public static string WebRoot { get; } = Path.Combine(AppDirectory, "wad", "web");

    /// <summary>
    /// %LOCALAPPDATA%\FILPRO quand il est réellement inscriptible ; sinon, pour un processus élevé, le dossier protégé
    /// de ProgramData décrit par <see cref="UserDataLocation"/>. Nul sinon : rien n'est alors écrit nulle part (jamais un
    /// chemin relatif au dossier courant, qui peut être le partage ou la clé dont le pack est lancé).
    /// </summary>
    public static string? UserData => userData.Value;

    // Résolu au premier besoin seulement : le worker de mise à jour lit AppDirectory sans toucher aux données.
    private static readonly Lazy<string?> userData = new(() => UserDataLocation.Resolve(WindowsElevation.IsElevated()));

    /// <summary>
    /// Nom du profil WebView2 du mode : la démonstration ne partage jamais le stockage web ni le cache avec le mode poste
    /// réel, et chaque génération fictive a le sien.
    /// </summary>
    public static string WebViewProfile(AppMode mode, string? generation)
        => mode == AppMode.Simulation ? Path.Combine("WebView2-Simulation", generation ?? "initial") : "WebView2";

    /// <summary>
    /// Crée le profil WebView2 là où le compte qui exécutera WebView2 peut écrire (voir <see cref="WebViewDataLocation"/>) :
    /// sous le dossier de données de FILPRO dans le cas courant, sous ProgramData quand FILPRO est élevé avec un autre
    /// compte que celui de la session Windows. Le compte de la session n'est rendu qu'avec un dossier prêt pour lui.
    /// </summary>
    public static WebViewDataLocation.ProfileFolder PrepareWebView(string profile)
    {
        var elevated = WindowsElevation.IsElevated();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException("Identité Windows sans SID.");
        var session = elevated ? WebViewDataLocation.SessionAccountSid() : null;
        if (elevated && session is null)
            TechnicalLog.Instance.Warning("Compte de la session Windows introuvable : profil d’affichage laissé dans le dossier de FILPRO.");
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        var local = WebViewDataLocation.NeedsSessionFolder(elevated, user.Value, session) ? null : UserData;
        var folder = WebViewDataLocation.PrepareProfile(profile, elevated, user, session, programData, local);
        if (folder.SessionAccount is { } account)
            TechnicalLog.Instance.Info("Affichage WebView2 exécuté par Windows sous le compte de la session " + account
                + " (FILPRO élevé sous " + identity.Name + ") : profil d’affichage " + folder.Path + ".");
        return folder;
    }
}
