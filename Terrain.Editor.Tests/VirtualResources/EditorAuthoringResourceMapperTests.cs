using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorAuthoringResourceMapperTests
{
    public static void RunAll()
    {
        TestHarness.Run("authoring mapper converts material slots to descriptor slots", ConvertsMaterialSlotsToDescriptorSlots);
        TestHarness.Run("authoring mapper does not mutate material slots when generating ids", DoesNotMutateMaterialSlotsWhenGeneratingIds);
        TestHarness.Run("committed descriptor ids update live material slots after save", CommittedDescriptorIdsUpdateLiveMaterialSlotsAfterSave);
        TestHarness.Run("existing material ids win over generated duplicate names", ExistingMaterialIdsWinOverGeneratedDuplicateNames);
        TestHarness.Run("authoring mapper preserves existing material ids when slot names change", PreservesExistingMaterialIdsWhenSlotNamesChange);
        TestHarness.Run("authoring mapper converts biome layers to material ids", ConvertsBiomeLayersToMaterialIds);
        TestHarness.Run("thumbnail provider resolves short material paths from resource session", ThumbnailProviderResolvesShortMaterialPathsFromSession);
        TestHarness.Run("authoring state loads material descriptor into slots", LoadsMaterialDescriptorIntoSlots);
        TestHarness.Run("authoring state loads biome settings with material ids", LoadsBiomeSettingsWithMaterialIds);
    }

    private static void ConvertsMaterialSlotsToDescriptorSlots()
    {
        string root = CreateWorkspace();
        var slots = new[]
        {
            new MaterialSlot
            {
                Index = 5,
                Name = "Soft Grass",
                AlbedoTexturePath = Path.Combine(root, "gfx", "map", "materials", "grass.png"),
                NormalTexturePath = Path.Combine(root, "gfx", "map", "materials", "grass_n.png"),
            },
            new MaterialSlot { Index = 6, Name = "Empty" },
        };

        IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots = EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(slots);

        TestHarness.AssertEqual(1, descriptorSlots.Count, "only active material slots should be exported");
        TestHarness.AssertEqual("soft_grass", descriptorSlots[0].Id, "material id should be derived from display name");
        TestHarness.AssertEqual(5, descriptorSlots[0].Index, "material index");
        TestHarness.AssertEqual("grass.png", descriptorSlots[0].Albedo, "albedo path should be a file name");
        TestHarness.AssertEqual("grass_n.png", descriptorSlots[0].Normal, "normal path should be a file name");
    }

    private static void DoesNotMutateMaterialSlotsWhenGeneratingIds()
    {
        var slot = new MaterialSlot
        {
            Index = 5,
            Name = "Soft Grass",
            AlbedoTexturePath = "grass.png",
        };

        IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots = EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots([slot]);

        TestHarness.AssertEqual("soft_grass", descriptorSlots[0].Id, "generated material id");
        TestHarness.Assert(slot.MaterialId == null, "descriptor mapping should not mutate live material slot ids");
    }

    private static void CommittedDescriptorIdsUpdateLiveMaterialSlotsAfterSave()
    {
        MaterialSlotManager manager = MaterialSlotManager.Instance;
        manager.ClearAll();

        try
        {
            manager[5].Name = "Soft Grass";
            manager[5].AlbedoTexturePath = "grass.png";

            IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots =
                EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(manager.GetActiveSlots());

            TestHarness.Assert(manager[5].MaterialId == null, "snapshot mapping should leave live id unset before save commits");

            manager.ApplyCommittedDescriptorIds(descriptorSlots);

            TestHarness.AssertEqual("soft_grass", manager[5].MaterialId, "committed descriptor id should update the live slot after save");
        }
        finally
        {
            manager.ClearAll();
        }
    }

    private static void ExistingMaterialIdsWinOverGeneratedDuplicateNames()
    {
        MaterialSlotManager manager = MaterialSlotManager.Instance;
        manager.ClearAll();

        try
        {
            manager[5].Name = "Soft Grass";
            manager[5].AlbedoTexturePath = "grass.png";
            manager.ApplyCommittedDescriptorIds(
            [
                new EditorMaterialDescriptorSlot("soft_grass", 5, "Soft Grass", "grass.png", null, null),
            ]);

            manager[2].Name = "Soft Grass";
            manager[2].AlbedoTexturePath = "grass_alt.png";

            IReadOnlyList<EditorMaterialDescriptorSlot> descriptorSlots =
                EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(manager.GetActiveSlots());

            EditorMaterialDescriptorSlot lowerSlot = descriptorSlots.Single(slot => slot.Index == 2);
            EditorMaterialDescriptorSlot existingSlot = descriptorSlots.Single(slot => slot.Index == 5);
            TestHarness.AssertEqual("soft_grass_2", lowerSlot.Id, "new duplicate name should receive a suffix");
            TestHarness.AssertEqual("soft_grass", existingSlot.Id, "existing committed id should not drift");
        }
        finally
        {
            manager.ClearAll();
        }
    }

    private static void ConvertsBiomeLayersToMaterialIds()
    {
        BiomeRuleService service = BiomeRuleService.Instance;
        service.ClearAll();
        service.AddBiomeFromConfig(1, "Temperate", new System.Numerics.Vector4(0, 1, 0, 1));
        service.AddLayerFromConfig(
            biomeId: 1,
            name: "Grass Layer",
            enabled: true,
            minAltitude: 0.0f,
            maxAltitude: 100.0f,
            minSlopeDegrees: 0.0f,
            maxSlopeDegrees: 45.0f,
            blendRange: 1.0f,
            materialSlotIndex: 5);
        service.Layers[0].Modifiers[0].Radius = 2.5f;
        service.Layers[0].Modifiers[0].AngleDegrees = 45.0f;
        service.Layers[0].Modifiers[0].TextureMaskPath = "masks/height.png";
        service.Layers[0].Modifiers[0].TextureMaskChannel = 1;

        var materialIdsByIndex = new Dictionary<int, string>
        {
            [5] = "soft_grass",
        };

        EditorBiomeSettingsSnapshot snapshot = EditorAuthoringResourceMapper.CreateBiomeSettingsSnapshot(service, materialIdsByIndex);

        TestHarness.AssertEqual(1, snapshot.Biomes.Count, "biome count");
        TestHarness.AssertEqual(1, snapshot.Layers.Count, "layer count");
        TestHarness.AssertEqual("soft_grass", snapshot.Layers[0].MaterialId, "layer should reference material id");
        TestHarness.Assert(snapshot.Modifiers.Count >= 2, "legacy height and slope modifiers should be exported");
        TestHarness.Assert(snapshot.Modifiers.All(modifier => modifier.Type != "material_slot"), "modifiers should not encode old material slot fields");
        TestHarness.AssertEqual(2.5f, snapshot.Modifiers[0].Radius, "modifier radius should be preserved");
        TestHarness.AssertEqual(45.0f, snapshot.Modifiers[0].AngleDegrees, "modifier angle should be preserved");
        TestHarness.AssertEqual("masks/height.png", snapshot.Modifiers[0].TextureMask, "modifier texture mask should be preserved");
        TestHarness.AssertEqual(1, snapshot.Modifiers[0].TextureMaskChannel, "modifier texture mask channel should be preserved");
    }

    private static void PreservesExistingMaterialIdsWhenSlotNamesChange()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "soft_grass",
            Index = 5,
            Name = "Soft Grass",
            AlbedoPath = "grass.png",
        });

        MaterialSlotManager manager = MaterialSlotManager.Instance;
        manager.ApplyDescriptor(descriptor, descriptorPath);
        manager[5].Name = "Renamed Grass";

        IReadOnlyList<EditorMaterialDescriptorSlot> exported =
            EditorAuthoringResourceMapper.CreateMaterialDescriptorSlots(manager.GetActiveSlots());

        TestHarness.AssertEqual(1, exported.Count, "exported material count");
        TestHarness.AssertEqual("soft_grass", exported[0].Id, "existing material id should survive display name edits");
    }

    private static void ThumbnailProviderResolvesShortMaterialPathsFromSession()
    {
        string root = CreateWorkspace();
        string materialsDirectory = Path.Combine(root, "mod", "map", "materials");
        Directory.CreateDirectory(materialsDirectory);
        string texturePath = Path.Combine(materialsDirectory, "grass.png");
        File.WriteAllText(texturePath, "placeholder");

        EditorResourceSession session = CreateSession(root);

        string? resolvedPath = TextureThumbnailProvider.ResolveTextureThumbnailPath("grass.png", session);

        TestHarness.AssertEqual(texturePath, resolvedPath, "thumbnail path should resolve from material descriptor directory");
    }

    private static void LoadsMaterialDescriptorIntoSlots()
    {
        string root = CreateWorkspace();
        string descriptorPath = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
        var descriptor = new RuntimeMaterialDescriptor();
        descriptor.Materials.Add(new RuntimeMaterialEntry
        {
            Id = "soft_grass",
            Index = 5,
            Name = "Soft Grass",
            AlbedoPath = "grass.png",
            NormalPath = "grass_n.png",
        });

        MaterialSlotManager manager = MaterialSlotManager.Instance;
        manager.ApplyDescriptor(descriptor, descriptorPath);

        TestHarness.AssertEqual("Soft Grass", manager[5].Name, "material slot name");
        TestHarness.AssertEqual(Path.Combine(root, "mod", "map", "materials", "grass.png"), manager[5].AlbedoTexturePath, "albedo should resolve beside descriptor");
        TestHarness.AssertEqual(Path.Combine(root, "mod", "map", "materials", "grass_n.png"), manager[5].NormalTexturePath, "normal should resolve beside descriptor");
        TestHarness.AssertEqual(5, manager.SelectedSlotIndex, "first active material slot should be selected");
    }

    private static void LoadsBiomeSettingsWithMaterialIds()
    {
        var settings = new RuntimeBiomeSettings();
        settings.Biomes.Add(new RuntimeBiomeEntry { Id = 1, Name = "Temperate" });
        settings.Layers.Add(new RuntimeBiomeLayerEntry
        {
            Id = 10,
            BiomeId = 1,
            Name = "Grass Layer",
            MaterialId = "soft_grass",
            Priority = 3,
            Enabled = true,
            Visible = false,
        });
        settings.Modifiers.Add(new RuntimeBiomeModifierEntry
        {
            Id = 20,
            LayerId = 10,
            Name = "Height range",
            Type = "HeightRange",
            BlendMode = "Multiply",
            Min = 0.1f,
            Max = 0.8f,
            MinFalloff = 0.05f,
            MaxFalloff = 0.1f,
            Opacity = 0.75f,
            Enabled = true,
            Visible = true,
        });

        BiomeRuleService service = BiomeRuleService.Instance;
        service.ApplyRuntimeSettings(settings, new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["soft_grass"] = 5,
        });

        TestHarness.AssertEqual(1, service.Biomes.Count, "loaded biome count");
        TestHarness.AssertEqual(1, service.Layers.Count, "loaded layer count");
        TestHarness.AssertEqual(5, service.Layers[0].MaterialSlotIndex, "material id should map to slot index");
        TestHarness.AssertEqual(false, service.Layers[0].Visible, "layer visibility should be restored");
        TestHarness.AssertEqual(20, service.Layers[0].Modifiers[0].Id, "modifier id should be restored");
    }

    private static EditorResourceSession CreateSession(string root)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: true);
        }

        return new EditorResourceSession(
            Resource("map/default.toml", Path.Combine(root, "mod", "map", "default.toml")),
            Resource("map/heightmap.png", Path.Combine(root, "mod", "map", "heightmap.png")),
            Resource("map/terrain.terrain", Path.Combine(root, "mod", "map", "terrain.terrain")),
            Resource("map/biome_mask.png", Path.Combine(root, "mod", "map", "biome_mask.png")),
            Resource("map/biome_settings.toml", Path.Combine(root, "mod", "map", "biome_settings.toml")),
            Resource("map/materials/descriptor.toml", Path.Combine(root, "mod", "map", "materials", "descriptor.toml")),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-authoring-mapper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
