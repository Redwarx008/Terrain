#nullable enable

using System.Collections.Generic;

namespace Terrain.Editor.Services.Resources;

public enum EditorMaterialLoadIssueKind
{
    MissingMaterialId = 0,
    MissingAlbedoTexture = 1,
    MissingNormalTexture = 2,
    MissingPropertiesTexture = 3,
}

public sealed record EditorMaterialLoadIssue(
    EditorMaterialLoadIssueKind Kind,
    string MaterialId,
    string Message,
    string? Path);

public sealed class EditorMaterialLoadState
{
    public static EditorMaterialLoadState Empty { get; } = new([], false, false);

    public EditorMaterialLoadState(
        IReadOnlyList<EditorMaterialLoadIssue> issues,
        bool hasBlockingMissingMaterialIds,
        bool hasTextureFallbacks)
    {
        Issues = issues;
        HasBlockingMissingMaterialIds = hasBlockingMissingMaterialIds;
        HasTextureFallbacks = hasTextureFallbacks;
    }

    public IReadOnlyList<EditorMaterialLoadIssue> Issues { get; }
    public bool HasBlockingMissingMaterialIds { get; }
    public bool HasTextureFallbacks { get; }
    public bool HasIssues => Issues.Count > 0;
}
