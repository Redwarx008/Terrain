using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorMaterialRecoveryTests
{
    public static void RunAll()
    {
        TestHarness.Run("recovery creates dedicated runtime fallback slots for missing material ids", RecoveryCreatesDedicatedRuntimeFallbackSlotsForMissingMaterialIds);
        TestHarness.Run("recovery marks missing albedo as non-blocking texture fallback", RecoveryMarksMissingAlbedoAsNonBlockingTextureFallback);
        TestHarness.Run("recovery resolves material textures through virtual resource lookup", RecoveryResolvesMaterialTexturesThroughVirtualResourceLookup);
        TestHarness.Run("recovery throws when runtime fallback slots are exhausted before reserved sentinel", RecoveryThrowsWhenRuntimeFallbackSlotsAreExhaustedBeforeReservedSentinel);
    }

    private static void RecoveryCreatesDedicatedRuntimeFallbackSlotsForMissingMaterialIds()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "grass",
            Index = 5,
            Name = "Grass",
            AlbedoPath = "grass.png",
        });

        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 10,
            BiomeId = 1,
            Name = "Grass Layer",
            MaterialId = "grass",
            Priority = 0,
            Enabled = true,
            Visible = true,
        });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 11,
            BiomeId = 1,
            Name = "Rock Layer",
            MaterialId = "rock",
            Priority = 1,
            Enabled = true,
            Visible = true,
        });

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            ResolveMissingMaterialTexture);

        TestHarness.AssertEqual(5, result.MaterialIndicesById["grass"], "existing material should keep its original slot index");
        TestHarness.AssertEqual(0, result.MaterialIndicesById["rock"], "missing material should claim the first free runtime slot");
        EditorResolvedMaterialSlot missingSlot = result.Slots.Single(slot => slot.MaterialId == "rock");
        TestHarness.Assert(missingSlot.IsRuntimeFallbackPlaceholder, "missing material should produce a runtime placeholder slot");
        TestHarness.Assert(missingSlot.UsesFallbackAlbedo, "missing material placeholder should use fallback albedo");
        TestHarness.Assert(result.LoadState.HasBlockingMissingMaterialIds, "missing material ids should be blocking");
    }

    private static void RecoveryMarksMissingAlbedoAsNonBlockingTextureFallback()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "grass",
            Index = 5,
            Name = "Grass",
            AlbedoPath = "missing_grass.png",
        });

        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 10,
            BiomeId = 1,
            Name = "Grass Layer",
            MaterialId = "grass",
            Priority = 0,
            Enabled = true,
            Visible = true,
        });

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            ResolveMissingMaterialTexture);

        EditorResolvedMaterialSlot grassSlot = result.Slots.Single(slot => slot.MaterialId == "grass");
        TestHarness.Assert(grassSlot.UsesFallbackAlbedo, "missing albedo should trigger fallback albedo");
        TestHarness.Assert(!grassSlot.IsRuntimeFallbackPlaceholder, "real descriptor slot should remain a real slot");
        TestHarness.Assert(!result.LoadState.HasBlockingMissingMaterialIds, "missing albedo should not block save/export");
        TestHarness.Assert(result.LoadState.HasTextureFallbacks, "missing albedo should count as texture fallback");
    }

    private static void RecoveryResolvesMaterialTexturesThroughVirtualResourceLookup()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "base", "map", "materials", "descriptor.toml");
        string resolvedTexturePath = Path.Combine(root, "mod", "map", "materials", "grass.png");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedTexturePath)!);
        File.WriteAllText(resolvedTexturePath, "texture");

        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "grass",
            Index = 5,
            Name = "Grass",
            AlbedoPath = "grass.png",
        });

        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 10,
            BiomeId = 1,
            Name = "Grass Layer",
            MaterialId = "grass",
            Priority = 0,
            Enabled = true,
            Visible = true,
        });

        EditorMaterialRecoveryResult result = new EditorMaterialRecoveryService().Recover(
            descriptor,
            descriptorPath,
            settings,
            virtualPath => virtualPath == "map/materials/grass.png" ? resolvedTexturePath : null);

        EditorResolvedMaterialSlot grassSlot = result.Slots.Single(slot => slot.MaterialId == "grass");
        TestHarness.AssertEqual(resolvedTexturePath, grassSlot.AlbedoTexturePath, "albedo should come from the resolved winning layer");
        TestHarness.Assert(!grassSlot.UsesFallbackAlbedo, "resolved winning-layer texture should not fallback");
        TestHarness.Assert(!result.LoadState.HasTextureFallbacks, "winning-layer texture should not produce fallback diagnostics");
    }

    private static void RecoveryThrowsWhenRuntimeFallbackSlotsAreExhaustedBeforeReservedSentinel()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "game", "map", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);

        var descriptor = new RuntimeMaterialDescriptor();
        for (int index = 0; index <= 254; index++)
        {
            descriptor.Materials.Add(new RuntimeMaterialEntry
            {
                Id = $"material-{index}",
                Index = index,
                Name = $"Material {index}",
            });
        }

        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 999,
            BiomeId = 1,
            Name = "Overflow Layer",
            MaterialId = "overflow",
            Priority = 0,
            Enabled = true,
            Visible = true,
        });

        InvalidDataException exception = TestHarness.AssertThrows<InvalidDataException>(
            () => new EditorMaterialRecoveryService().Recover(
                descriptor,
                descriptorPath,
                settings,
                ResolveMissingMaterialTexture),
            "recovery should fail once all non-sentinel fallback slots are exhausted");

        TestHarness.Assert(
            exception.Message.Contains("0..254", StringComparison.Ordinal),
            "exhausted-slot error should describe the valid fallback slot range");
    }

    private static string? ResolveMissingMaterialTexture(string virtualPath)
    {
        return null;
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-material-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
