#nullable enable
using System;

namespace Terrain.Editor.Services;

[Flags]
public enum EditorDirtyResource
{
    None = 0,
    MapDefinition = 1 << 0,
    Heightmap = 1 << 1,
    BiomeMask = 1 << 2,
    MaterialDescriptor = 1 << 3,
    BiomeSettings = 1 << 4,
    All = MapDefinition | Heightmap | BiomeMask | MaterialDescriptor | BiomeSettings,
}

public readonly struct EditorDirtySnapshot
{
    internal EditorDirtySnapshot(
        EditorDirtyResource resources,
        ulong mapDefinitionVersion,
        ulong heightmapVersion,
        ulong biomeMaskVersion,
        ulong materialDescriptorVersion,
        ulong biomeSettingsVersion)
    {
        Resources = resources;
        MapDefinitionVersion = mapDefinitionVersion;
        HeightmapVersion = heightmapVersion;
        BiomeMaskVersion = biomeMaskVersion;
        MaterialDescriptorVersion = materialDescriptorVersion;
        BiomeSettingsVersion = biomeSettingsVersion;
    }

    public EditorDirtyResource Resources { get; }
    internal ulong MapDefinitionVersion { get; }
    internal ulong HeightmapVersion { get; }
    internal ulong BiomeMaskVersion { get; }
    internal ulong MaterialDescriptorVersion { get; }
    internal ulong BiomeSettingsVersion { get; }

    public static EditorDirtySnapshot Unversioned(EditorDirtyResource resources)
    {
        return new EditorDirtySnapshot(resources, 0, 0, 0, 0, 0);
    }

    public EditorDirtySnapshot WithAdditionalResources(EditorDirtyResource resources)
    {
        return new EditorDirtySnapshot(
            Resources | resources,
            MapDefinitionVersion,
            HeightmapVersion,
            BiomeMaskVersion,
            MaterialDescriptorVersion,
            BiomeSettingsVersion);
    }
}

public sealed class EditorDirtyState
{
    private static readonly Lazy<EditorDirtyState> InstanceFactory = new(() => new EditorDirtyState());

    public static EditorDirtyState Instance => InstanceFactory.Value;

    private EditorDirtyState()
    {
    }

    private readonly object syncRoot = new();
    private ulong mapDefinitionVersion;
    private ulong heightmapVersion;
    private ulong biomeMaskVersion;
    private ulong materialDescriptorVersion;
    private ulong biomeSettingsVersion;
    private EditorDirtyResource dirtyResources;

    public EditorDirtyResource DirtyResources
    {
        get
        {
            lock (syncRoot)
            {
                return dirtyResources;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (syncRoot)
            {
                return dirtyResources != EditorDirtyResource.None;
            }
        }
    }

    public event EventHandler? DirtyChanged;

    public void MarkDirty(EditorDirtyResource resources = EditorDirtyResource.All)
    {
        if (resources == EditorDirtyResource.None)
            return;

        bool changed;
        lock (syncRoot)
        {
            IncrementVersions(resources);
            EditorDirtyResource newResources = dirtyResources | resources;
            changed = newResources != dirtyResources;
            dirtyResources = newResources;
        }

        if (changed)
            DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public EditorDirtySnapshot CaptureSnapshot()
    {
        lock (syncRoot)
        {
            return new EditorDirtySnapshot(
                dirtyResources,
                mapDefinitionVersion,
                heightmapVersion,
                biomeMaskVersion,
                materialDescriptorVersion,
                biomeSettingsVersion);
        }
    }

    public void ClearDirty(EditorDirtyResource resources = EditorDirtyResource.All)
    {
        bool changed;
        lock (syncRoot)
        {
            EditorDirtyResource newResources = resources == EditorDirtyResource.All
                ? EditorDirtyResource.None
                : dirtyResources & ~resources;
            changed = newResources != dirtyResources;
            dirtyResources = newResources;
        }

        if (changed)
            DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearDirty(EditorDirtySnapshot snapshot)
    {
        bool changed;
        lock (syncRoot)
        {
            EditorDirtyResource resourcesToClear = EditorDirtyResource.None;
            if (CanClear(snapshot.Resources, EditorDirtyResource.MapDefinition, snapshot.MapDefinitionVersion, mapDefinitionVersion))
                resourcesToClear |= EditorDirtyResource.MapDefinition;
            if (CanClear(snapshot.Resources, EditorDirtyResource.Heightmap, snapshot.HeightmapVersion, heightmapVersion))
                resourcesToClear |= EditorDirtyResource.Heightmap;
            if (CanClear(snapshot.Resources, EditorDirtyResource.BiomeMask, snapshot.BiomeMaskVersion, biomeMaskVersion))
                resourcesToClear |= EditorDirtyResource.BiomeMask;
            if (CanClear(snapshot.Resources, EditorDirtyResource.MaterialDescriptor, snapshot.MaterialDescriptorVersion, materialDescriptorVersion))
                resourcesToClear |= EditorDirtyResource.MaterialDescriptor;
            if (CanClear(snapshot.Resources, EditorDirtyResource.BiomeSettings, snapshot.BiomeSettingsVersion, biomeSettingsVersion))
                resourcesToClear |= EditorDirtyResource.BiomeSettings;

            if (resourcesToClear == EditorDirtyResource.None)
                return;

            EditorDirtyResource newResources = dirtyResources & ~resourcesToClear;
            changed = newResources != dirtyResources;
            dirtyResources = newResources;
        }

        if (changed)
            DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool CanClear(EditorDirtyResource capturedResources, EditorDirtyResource resource, ulong capturedVersion, ulong currentVersion)
    {
        return (capturedResources & resource) != 0 && capturedVersion == currentVersion;
    }

    private void IncrementVersions(EditorDirtyResource resources)
    {
        if ((resources & EditorDirtyResource.MapDefinition) != 0)
            mapDefinitionVersion++;
        if ((resources & EditorDirtyResource.Heightmap) != 0)
            heightmapVersion++;
        if ((resources & EditorDirtyResource.BiomeMask) != 0)
            biomeMaskVersion++;
        if ((resources & EditorDirtyResource.MaterialDescriptor) != 0)
            materialDescriptorVersion++;
        if ((resources & EditorDirtyResource.BiomeSettings) != 0)
            biomeSettingsVersion++;
    }
}
