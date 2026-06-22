#nullable enable

using System;
using System.Collections.Generic;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorAuthoringSaveSnapshot
{
    public EditorAuthoringSaveSnapshot(
        ushort[] heightData,
        int width,
        int height,
        BiomeMask biomeMask,
        float heightScale,
        float riverMaxVisibleCameraHeight,
        IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots,
        EditorBiomeSettingsSnapshot biomeSnapshot)
    {
        HeightData = heightData ?? throw new ArgumentNullException(nameof(heightData));
        Width = width;
        Height = height;
        BiomeMask = biomeMask ?? throw new ArgumentNullException(nameof(biomeMask));
        HeightScale = heightScale;
        RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight;
        DescriptorSlots = descriptorSlots ?? throw new ArgumentNullException(nameof(descriptorSlots));
        BiomeSnapshot = biomeSnapshot ?? throw new ArgumentNullException(nameof(biomeSnapshot));
    }

    public ushort[] HeightData { get; }
    public int Width { get; }
    public int Height { get; }
    public BiomeMask BiomeMask { get; }
    public float HeightScale { get; }
    public float RiverMaxVisibleCameraHeight { get; }
    public IReadOnlyList<EditorMaterialDescriptorSlot> DescriptorSlots { get; }
    public EditorBiomeSettingsSnapshot BiomeSnapshot { get; }
}
