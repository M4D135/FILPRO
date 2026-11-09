namespace Filpro.Core;

/// <summary>Actions fermées de l'hôte. Aucun chemin d'exécutable ni argument arbitraire ne vient de la page.</summary>
public interface IApplicationSessionControl
{
    Task RequestRestartAsync(AppMode mode, CancellationToken cancellationToken);
    Task RequestExitAsync(CancellationToken cancellationToken);
    Task AcknowledgeReadyAsync(string generation, CancellationToken cancellationToken);
    Task<string?> PickPathAsync(ApplicationPathKind kind, CancellationToken cancellationToken);
}

public enum ApplicationPathKind { Folder, OpenConfiguration, SaveConfiguration, UpdateZip, EncryptionKey, RepairReceipt }
