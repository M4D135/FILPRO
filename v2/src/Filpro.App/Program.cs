using System.Text;
using Filpro.Core;
using Filpro.Platform;

namespace Filpro.App;

internal static class Program
{
    private const string FatalMessage = "FILPRO a rencontré une erreur imprévue et va se fermer. Le détail technique est dans le journal de l’application.";

    private static int failed;

    [STAThread]
    private static int Main(string[] args)
    {
        // Avant tout appel à WebView2 et tout processus lancé : aucune variable WEBVIEW2_* de la session ne choisit le moteur.
        var ignoredVariables = EnvironmentGuard.RemoveWebView2Overrides();
        // Traces de dernier recours : un plantage ou une tâche oubliée ne laisse jamais le journal vide.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
            TechnicalLog.Instance.Error("Erreur fatale imprévue.", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TechnicalLog.Instance.Error("Tâche en échec jamais observée.", e.Exception);
            e.SetObserved();
        };
#pragma warning disable CA1031 // Les échecs du démarrage sont tous tracés et dits à l'opérateur en français.
        try
        {
            // Le processus de remplacement n'initialise ni WebView2, ni la configuration utilisateur.
            var workerExit = WindowsUpdateWorker.TryRunAsync(args).GetAwaiter().GetResult();
            if (workerExit is not null) return workerExit.Value;
            // Noms seulement : une valeur peut désigner un dossier ou des arguments posés par un autre programme.
            if (ignoredVariables.Count > 0)
                TechnicalLog.Instance.Warning("Variables d’environnement WebView2 ignorées : " + EnvironmentGuard.Describe(ignoredVariables) + ".");
            // Le nonce de santé reste réservé au worker ; il ne traverse ni ModeResolver, ni le journal.
            var healthStartup = args.Length == 3 && args[0] == "--filpro-update-health";
            if (args.Any(argument => argument.StartsWith("--filpro-update-", StringComparison.Ordinal)) && !healthStartup)
                throw new FilproOperationException("update.arguments", "Arguments réservés de mise à jour invalides.");
            var elevatedRelay = StartupPolicy.IsElevatedRelay(args);
            var elevated = WindowsElevation.IsElevated();
            if ((elevatedRelay || healthStartup) && !elevated)
                throw new FilproOperationException("startup.elevation", "Le relancement doit disposer des droits administrateur Windows.");
            var decision = ResolveMode(healthStartup ? [] : elevatedRelay ? [ModeResolver.PosteArgument] : args);
            using var lease = TryAcquireInstallationLease();
            Run(decision, healthStartup ? args : [], StartupPolicy.CanEnterDirectly(elevated));
        }
        catch (Exception ex)
        {
            Fail("Échec du démarrage.", ex);
        }
#pragma warning restore CA1031
        return Volatile.Read(ref failed) != 0 ? 1 : 0;
    }

    /// <summary>
    /// Le mode est décidé avant la composition : argument explicite, variable de simulation, puis type du pack.
    /// PROD demande le poste ; les sources et DEV ouvrent la démonstration. Les capacités restent vérifiées ensuite.
    /// </summary>
    private static ModeDecision ResolveMode(string[] args)
    {
        var decision = ModeResolver.Resolve(args, Environment.GetEnvironmentVariable(ModeResolver.SimulationVariable), IsDeclaredProductionPack());
        TechnicalLog.Instance.Info($"Démarrage de {ProductInfo.Name} {ProductInfo.Version} : mode {decision.Mode}, raison {decision.Reason}.");
        foreach (var warning in decision.Warnings)
        {
            TechnicalLog.Instance.Warning(warning);
        }
        return decision;
    }

    private static bool IsDeclaredProductionPack()
    {
        var path = Path.Combine(AppPaths.AppDirectory, PackManifestCodec.ManifestPath);
        if (!File.Exists(path)) return false;
        // Ce choix de contexte n'accorde aucune capacité native : Composition vérifie ensuite le pack complet.
        return PackManifestCodec.IsProductionFlavor(PackIntegrityVerifier.ReadManifest(AppPaths.AppDirectory).Flavor);
    }

    private static IDisposable? TryAcquireInstallationLease()
    {
        try { return WindowsUpdateWorker.AcquireInstallationLease(AppPaths.AppDirectory); }
        catch (Exception ex) when (ex is FilproOperationException or IOException or UnauthorizedAccessException)
        {
            TechnicalLog.Instance.Warning("Verrou d’installation indisponible : remplacement automatique du dossier désactivé pour cette session.");
            return null;
        }
    }

    private static void Run(ModeDecision decision, string[] startupArguments, bool nativeAccessGranted)
    {
        ApplicationConfiguration.Initialize();
        // Les exceptions du fil de l'interface (gestionnaires d'événements asynchrones compris) sont tracées, puis l'application se ferme proprement.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Fail("Erreur imprévue sur le fil de l’interface.", e.Exception);
            Application.Exit();
        };
        // Une seule présence par processus élevé : elle survit aux passages par la démonstration, et seule une relance la renouvelle.
        // Les durées suivent le compteur de Windows (veille comprise), jamais l'heure du poste.
        IOperatorPresence presence;
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
            presence = new OperatorPresence(() => Environment.TickCount64, DateTime.UtcNow - self.StartTime.ToUniversalTime());
        while (true)
        {
            if (decision.Mode == AppMode.Poste && !nativeAccessGranted)
            {
                // L'UAC est demandée d'emblée ; l'écran d'entrée ne sert qu'à réessayer après une annulation ou une erreur.
                var refusal = WindowsElevation.TryRelaunch();
                if (refusal is null) return;
                using var welcome = new WelcomeForm(refusal);
                Application.Run(welcome);
                if (!welcome.ContinueHere) return;
                nativeAccessGranted = WindowsElevation.IsElevated();
                if (!nativeAccessGranted) throw new FilproOperationException("startup.elevation", "Les droits administrateur ne sont plus disponibles.");
            }
            using var composition = Composition.Create(decision, presence);
            using var form = new MainForm(composition, startupArguments, presence);
            Application.Run(form);
            if (form.RequestedMode is not { } target) break;
            decision = ModeResolver.Resolve([target == AppMode.Simulation ? ModeResolver.SimulationArgument : ModeResolver.PosteArgument], null);
            startupArguments = [];
        }
    }

    private static void Fail(string message, Exception exception)
    {
        TechnicalLog.Instance.Error(message, exception);
        // Un seul boîte de message, même si plusieurs échecs arrivent pendant la fermeture.
        if (Interlocked.Exchange(ref failed, 1) == 0)
        {
            MessageBox.Show(FatalMessage, ProductInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Message français sur la sortie d'erreur standard, en UTF-8 : une application fenêtrée n'a pas de page de codes console à suivre.</summary>
    internal static void WriteStandardError(string message)
    {
        try
        {
            using var writer = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: -1, leaveOpen: true);
            writer.WriteLine(message);
        }
        catch (IOException ex)
        {
            TechnicalLog.Instance.Warning($"Sortie d’erreur non écrite : {ex.GetType().Name}.");
        }
    }
}
