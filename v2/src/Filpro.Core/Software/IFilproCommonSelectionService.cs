namespace Filpro.Core;

/// <summary>Choix communs aux profils de l'opérateur. Jamais inclus dans la configuration partageable du pack.</summary>
public sealed record CommonSelectionState(bool Demonstration, bool HasSelection,
    IReadOnlyList<SoftwareItem> AppData, IReadOnlyList<SoftwareItem> RegistryKeys);

public interface IFilproCommonSelectionService
{
    bool Demonstration { get; }
    CommonSelectionState Read(CancellationToken cancellationToken);
    CommonSelectionState Save(IReadOnlyList<SoftwareItem> files, IReadOnlyList<SoftwareItem> registry, CancellationToken cancellationToken);
    CommonSelectionState Clear(CancellationToken cancellationToken);
    CommonPatchResult Export(string destination, bool confirmed, CancellationToken cancellationToken);
    CommonPatchPreview PreviewPatch(string source, CancellationToken cancellationToken);
    CommonPatchResult ApplyPatch(string previewId, bool replaceConflicts, bool confirmed, CancellationToken cancellationToken);
}
