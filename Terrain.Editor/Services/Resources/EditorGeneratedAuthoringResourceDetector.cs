#nullable enable

using System.IO;
using System;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services.Resources;

public static class EditorGeneratedAuthoringResourceDetector
{
    public static EditorDirtyResource DetectMissingGeneratedResources(
        EditorResourceSession session,
        bool hasBiomeMaskData)
    {
        ArgumentNullException.ThrowIfNull(session);

        EditorDirtyResource resources = EditorDirtyResource.None;
        if (hasBiomeMaskData && !File.Exists(session.BiomeMask.ResolvedPath))
            resources |= EditorDirtyResource.BiomeMask;

        return resources;
    }
}
