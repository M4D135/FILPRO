using System.Text.Json;
using Filpro.Core;

namespace Filpro.App;

/// <summary>Le paramètre de relais ne remplace jamais le contrôle du jeton Windows.</summary>
internal static class StartupPolicy
{
    internal const string ElevatedArgument = "--filpro-elevated";

    internal static bool IsElevatedRelay(string[] args)
    {
        if (!args.Contains(ElevatedArgument, StringComparer.Ordinal)) return false;
        if (!args.SequenceEqual(new[] { ModeResolver.PosteArgument, ElevatedArgument }, StringComparer.Ordinal))
            throw new FilproOperationException("startup.arguments", "Arguments de relancement administrateur invalides.");
        return true;
    }

    /// <summary>Un jeton réellement administrateur ouvre FILPRO directement : l'UAC tient lieu de connexion.</summary>
    internal static bool CanEnterDirectly(bool elevated) => elevated;

    internal static string? ReadAction(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 256) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var fields = document.RootElement.EnumerateObject().ToArray();
            if (fields.Length != 2 || fields.Count(p => p.Name == "type") != 1 || fields.Count(p => p.Name == "action") != 1) return null;
            var type = document.RootElement.GetProperty("type");
            var action = document.RootElement.GetProperty("action");
            if (type.ValueKind != JsonValueKind.String || type.GetString() != "filpro.startup" || action.ValueKind != JsonValueKind.String) return null;
            return action.GetString() is "info" or "open" or "close" or "layout" ? action.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
