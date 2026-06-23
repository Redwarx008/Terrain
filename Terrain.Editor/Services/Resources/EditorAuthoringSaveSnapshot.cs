#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Services;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorAuthoringSaveSnapshot
{
    public EditorAuthoringSaveSnapshot(
        ushort[]? heightData,
        int width,
        int height,
        BiomeMask? biomeMask,
        float heightScale,
        float riverMaxVisibleCameraHeight,
        float seaLevel,
        IReadOnlyList<EditorMaterialDescriptorSlot>? descriptorSlots,
        EditorBiomeSettingsSnapshot? biomeSnapshot,
        EditorDirtyResource dirtyResources = EditorDirtyResource.All)
        : this(
            heightData,
            width,
            height,
            biomeMask,
            heightScale,
            riverMaxVisibleCameraHeight,
            seaLevel,
            descriptorSlots,
            biomeSnapshot,
            EditorDirtySnapshot.Unversioned(dirtyResources))
    {
    }

    public EditorAuthoringSaveSnapshot(
        ushort[]? heightData,
        int width,
        int height,
        BiomeMask? biomeMask,
        float heightScale,
        float riverMaxVisibleCameraHeight,
        float seaLevel,
        IReadOnlyList<EditorMaterialDescriptorSlot>? descriptorSlots,
        EditorBiomeSettingsSnapshot? biomeSnapshot,
        EditorDirtySnapshot dirtySnapshot)
    {
        HeightData = heightData;
        Width = width;
        Height = height;
        BiomeMask = biomeMask;
        HeightScale = heightScale;
        RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight;
        SeaLevel = seaLevel;
        DescriptorSlots = descriptorSlots;
        BiomeSnapshot = biomeSnapshot;
        DirtySnapshot = dirtySnapshot;
    }

    public ushort[]? HeightData { get; }
    public int Width { get; }
    public int Height { get; }
    public BiomeMask? BiomeMask { get; }
    public float HeightScale { get; }
    public float RiverMaxVisibleCameraHeight { get; }
    public float SeaLevel { get; }
    public IReadOnlyList<EditorMaterialDescriptorSlot>? DescriptorSlots { get; }
    public EditorBiomeSettingsSnapshot? BiomeSnapshot { get; }
    public EditorDirtySnapshot DirtySnapshot { get; }
    public EditorDirtyResource DirtyResources => DirtySnapshot.Resources;
}
