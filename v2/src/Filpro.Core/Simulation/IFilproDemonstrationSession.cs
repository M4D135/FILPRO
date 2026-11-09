namespace Filpro.Core;

public sealed record DemonstrationDestination(string Id, string Label, string Path, string Kind);
public sealed record DemonstrationStore(string Id, string Bundle, string Label, DateTimeOffset CreatedUtc,
    IReadOnlyList<string> Accounts, string State, bool Partial, bool Encrypted);
public sealed record DemonstrationScenario(string Id, string Label, string Description);
public sealed record DemonstrationProfileState(string Sid, string Account, string State, string? ReceiptPath, string? SourceStoreId);
public sealed record DemonstrationRepairReceipt(string ReceiptPath, string Account, string Sid);
public sealed record DemonstrationAssets(string ImportConfigurationPath, string ExportConfigurationPath,
    string UpdateFolderPath, string UpdateZipPath, string RecoveryDestination, string SafetyBackupRoot);
public sealed record DemonstrationCatalog(bool Demonstration, string Generation,
    IReadOnlyList<DemonstrationDestination> Destinations, IReadOnlyList<DemonstrationStore> Stores,
    IReadOnlyList<DemonstrationScenario> Scenarios, string SelectedScenario, DemonstrationAssets Assets,
    IReadOnlyList<DemonstrationProfileState>? ProfileStates = null,
    IReadOnlyList<DemonstrationRepairReceipt>? RepairReceipts = null);

/// <summary>Monde fictif uniquement. Aucun chemin fourni par ce catalogue ne désigne une ressource à ouvrir réellement.</summary>
public interface IFilproDemonstrationSession
{
    DemonstrationCatalog ReadCatalog(CancellationToken cancellationToken);
    /// <summary>Choisit une issue fermée pour la prochaine action fictive ; interdit pendant une opération active.</summary>
    DemonstrationCatalog SetScenario(string outcome, CancellationToken cancellationToken);
}
