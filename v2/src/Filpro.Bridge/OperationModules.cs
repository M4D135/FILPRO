using System.Collections.Immutable;
using System.Reflection;
using Filpro.Core;

namespace Filpro.Bridge;

/// <summary>
/// Trouve les modules d'opérations des assemblages FILPRO et construit le registre fermé du pont. Le registre reste fermé :
/// seuls les modules publics des assemblages nommés « Filpro » ou « Filpro.* » que l'hôte de l'application approuve (sa liste
/// de dépendances) sont considérés, et chaque opération garde un nom valide et unique dans son écran.
/// </summary>
public static class OperationModules
{
    /// <summary>
    /// Assemblages de l'application nommés « Filpro » ou « Filpro.* », chargés depuis la liste approuvée de l'hôte (les
    /// fichiers listés par le deps.json de l'application), afin qu'un DLL égaré déposé à côté de l'exécutable ne soit jamais
    /// considéré.
    /// </summary>
    public static ImmutableArray<Assembly> LoadFilproAssemblies()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
        {
            foreach (var file in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (IsFilproName(name))
                {
                    names.Add(name);
                }
            }
        }
        // Les assemblages déjà chargés (et l'hôte lui-même) en font toujours partie.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name is string name && IsFilproName(name))
            {
                names.Add(name);
            }
        }
        var result = ImmutableArray.CreateBuilder<Assembly>(names.Count);
        foreach (var name in names)
        {
            result.Add(Assembly.Load(new AssemblyName(name)));
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// Classes publiques, non abstraites, implémentant <see cref="IOperationModule"/> avec un constructeur public sans
    /// paramètre, ordonnées par nom complet (composition stable).
    /// </summary>
    public static ImmutableArray<IOperationModule> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return
        [
            .. assemblies.Distinct()
                .SelectMany(ExportedTypes)
                .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && typeof(IOperationModule).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IOperationModule)Activator.CreateInstance(t)!),
        ];
    }

    /// <summary>
    /// Registre de toutes les opérations des modules. Un module dont le nom d'écran est invalide, une opération hors de son
    /// écran, un nom invalide ou dupliqué arrête le démarrage (erreurs de programmation, jamais un choix silencieux).
    /// </summary>
    public static OperationRegistry CreateRegistry(EngineContext context, IEnumerable<IOperationModule> modules)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(modules);
        var definitions = new List<OperationDefinition>();
        foreach (var module in modules)
        {
            var screen = module.Screen;
            if (!IsValidScreen(screen))
            {
                throw new InvalidOperationException($"Invalid screen name in {module.GetType().FullName}: {screen}");
            }
            foreach (var definition in module.CreateOperations(context))
            {
                if (!definition.Name.StartsWith(screen + ".", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Operation {definition.Name} is outside the screen {screen} of {module.GetType().FullName}.");
                }
                definitions.Add(definition);
            }
        }
        return new OperationRegistry(definitions);
    }

    /// <summary>Découverte et registre en un seul appel (l'hôte de l'application).</summary>
    public static OperationRegistry CreateRegistry(EngineContext context, IEnumerable<Assembly> assemblies) =>
        CreateRegistry(context, Discover(assemblies));

    /// <summary>Noms d'écran : un segment lower camel case (« accueil », « journal »).</summary>
    public static bool IsValidScreen(string? screen) =>
        !string.IsNullOrEmpty(screen) && screen.Length <= 32 && char.IsAsciiLetterLower(screen[0]) && screen.All(char.IsAsciiLetterOrDigit);

    private static bool IsFilproName(string name) =>
        string.Equals(name, "Filpro", StringComparison.Ordinal) || name.StartsWith("Filpro.", StringComparison.Ordinal);

    private static IEnumerable<Type> ExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().Where(t => t.IsPublic);
        }
    }
}
