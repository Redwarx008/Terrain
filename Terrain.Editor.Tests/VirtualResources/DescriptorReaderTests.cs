using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class DescriptorReaderTests
{
    public static void RunAll()
    {
        TestHarness.Run("map definition reads required and optional paths", MapDefinitionReadsRequiredAndOptionalPaths);
        TestHarness.Run("map definition requires heightmap and terrain data", MapDefinitionRequiresHeightmapAndTerrainData);
        TestHarness.Run("map definition rejects non-positive height scale", MapDefinitionRejectsNonPositiveHeightScale);
        TestHarness.Run("map definition requires supported version", MapDefinitionRequiresSupportedVersion);
        TestHarness.Run("map definition rejects unknown root fields", MapDefinitionRejectsUnknownRootFields);
        TestHarness.Run("map definition rejects unknown terrain fields", MapDefinitionRejectsUnknownTerrainFields);
        TestHarness.Run("map definition rejects unsafe relative paths", MapDefinitionRejectsUnsafeRelativePaths);
        TestHarness.Run("map definition rejects invalid optional path types", MapDefinitionRejectsInvalidOptionalPathTypes);
        TestHarness.Run("map definition rejects empty optional paths", MapDefinitionRejectsEmptyOptionalPaths);
        TestHarness.Run("material descriptor preserves short relative texture paths", MaterialDescriptorPreservesShortRelativePaths);
        TestHarness.Run("material descriptor does not resolve texture paths", MaterialDescriptorDoesNotResolveTexturePaths);
        TestHarness.Run("material descriptor normalizes safe relative texture paths", MaterialDescriptorNormalizesSafeRelativeTexturePaths);
        TestHarness.Run("material descriptor requires supported version", MaterialDescriptorRequiresSupportedVersion);
        TestHarness.Run("material descriptor rejects old root fields", MaterialDescriptorRejectsOldRootFields);
        TestHarness.Run("material descriptor rejects unknown material fields", MaterialDescriptorRejectsUnknownMaterialFields);
        TestHarness.Run("material descriptor rejects unsafe texture paths", MaterialDescriptorRejectsUnsafeTexturePaths);
        TestHarness.Run("material descriptor rejects nested texture paths", MaterialDescriptorRejectsNestedTexturePaths);
        TestHarness.Run("material descriptor rejects shader sentinel material index", MaterialDescriptorRejectsShaderSentinelMaterialIndex);
        TestHarness.Run("material descriptor requires materials array", MaterialDescriptorRequiresMaterialsArray);
        TestHarness.Run("material descriptor requires non-empty material id", MaterialDescriptorRequiresNonEmptyMaterialId);
        TestHarness.Run("material descriptor rejects out-of-range material index", MaterialDescriptorRejectsOutOfRangeMaterialIndex);
        TestHarness.Run("material descriptor rejects duplicate ids and indices", MaterialDescriptorRejectsDuplicateIdsAndIndices);
        TestHarness.Run("biome settings keep material_id references", BiomeSettingsKeepMaterialIdReferences);
        TestHarness.Run("biome settings do not require biome mask field", BiomeSettingsDoNotRequireBiomeMaskField);
        TestHarness.Run("biome settings requires supported version", BiomeSettingsRequiresSupportedVersion);
        TestHarness.Run("biome settings rejects old root fields", BiomeSettingsRejectsOldRootFields);
        TestHarness.Run("biome settings rejects old layer material slot field", BiomeSettingsRejectsOldLayerMaterialSlotField);
        TestHarness.Run("biome settings validates known material ids when provided", BiomeSettingsValidatesKnownMaterialIdsWhenProvided);
        TestHarness.Run("biome settings rejects out-of-range integer fields", BiomeSettingsRejectsOutOfRangeIntegerFields);
        TestHarness.Run("biome settings reject empty material id", BiomeSettingsRejectEmptyMaterialId);
        TestHarness.Run("biome settings reject empty modifier type", BiomeSettingsRejectEmptyModifierType);
        TestHarness.Run("biome settings reject missing biome references", BiomeSettingsRejectMissingBiomeReferences);
        TestHarness.Run("biome settings reject missing layer references", BiomeSettingsRejectMissingLayerReferences);
        TestHarness.Run("biome settings preserves advanced modifier parameters", BiomeSettingsPreservesAdvancedModifierParameters);
    }

    private static void MapDefinitionReadsRequiredAndOptionalPaths()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = "rivers.png"

[settings]
height_scale = 200.0
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml"));

        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "terrain data path");
        TestHarness.AssertEqual("rivers.png", map.RiversPath, "rivers path");
        TestHarness.AssertEqual(null, map.ProvincesPath, "provinces path");
        TestHarness.AssertEqual(200.0f, map.HeightScale, "height scale");
    }

    private static void MapDefinitionRequiresHeightmapAndTerrainData()
    {
        string missingHeightmapRoot = CreateMapData();
        File.WriteAllText(Path.Combine(missingHeightmapRoot, "default.toml"), """
version = 1

[terrain]
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(missingHeightmapRoot, "default.toml")),
            "missing heightmap should throw InvalidDataException");

        string missingTerrainDataRoot = CreateMapData();
        File.WriteAllText(Path.Combine(missingTerrainDataRoot, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(missingTerrainDataRoot, "default.toml")),
            "missing terrain_data should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsNonPositiveHeightScale()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "height_scale <= 0 should throw InvalidDataException");
    }

    private static void MapDefinitionRequiresSupportedVersion()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "missing version should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 2

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "unsupported version should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 4294967297

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "oversized unsupported version should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsUnknownRootFields()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1
biome_mask = "biome_mask.png"

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "unknown root field should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsUnknownTerrainFields()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
biome_mask = "biome_mask.png"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "unknown terrain field should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsUnsafeRelativePaths()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "../heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "parent traversal should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "default.toml"), $$"""
version = 1

[terrain]
heightmap = "{{Path.GetFullPath(Path.Combine(root, "heightmap.png")).Replace("\\", "\\\\")}}"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "absolute path should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsInvalidOptionalPathTypes()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = 123
provinces = true

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "rivers/provinces with non-string types should throw InvalidDataException");
    }

    private static void MapDefinitionRejectsEmptyOptionalPaths()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = ""

[settings]
height_scale = 100.0
""");

        AssertThrowsInvalidData(
            () => RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml")),
            "explicit empty rivers path should throw InvalidDataException");
    }

    private static void MaterialDescriptorPreservesShortRelativePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
normal = "grass_n.png"
properties = "grass_p.png"
""");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml"));

        TestHarness.AssertEqual(1, descriptor.Materials.Count, "material count");
        TestHarness.AssertEqual("grassland", descriptor.Materials[0].Id, "material id");
        TestHarness.AssertEqual("grass_a.png", descriptor.Materials[0].AlbedoPath, "albedo path");
    }

    private static void MaterialDescriptorDoesNotResolveTexturePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "cliff"
index = 1
name = "Cliff"
albedo = "cliff_a.png"
""");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml"));

        TestHarness.AssertEqual("cliff_a.png", descriptor.Materials[0].AlbedoPath, "texture path should stay exactly as declared");
        TestHarness.Assert(!Path.IsPathRooted(descriptor.Materials[0].AlbedoPath!), "texture path should not be expanded to an absolute path");
    }

    private static void MaterialDescriptorNormalizesSafeRelativeTexturePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "cliff"
index = 1
name = "Cliff"
albedo = "./cliff_a.png"
""");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml"));

        TestHarness.AssertEqual("cliff_a.png", descriptor.Materials[0].AlbedoPath, "texture path should be normalized but remain relative");
    }

    private static void MaterialDescriptorRequiresSupportedVersion()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 2

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "unsupported material descriptor version should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 4294967297

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "oversized unsupported material descriptor version should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsOldRootFields()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[material_slots]]
index = 0
name = "Grassland"

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "old root field material_slots should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsUnknownMaterialFields()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
texture = "x.png"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "unknown material field should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsUnsafeTexturePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "../grass_a.png"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "unsafe material texture path should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsNestedTexturePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass/grass_a.png"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "nested material texture path should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsShaderSentinelMaterialIndex()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "sentinel"
index = 255
name = "Sentinel"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "material index 255 should throw InvalidDataException because it is the detail-map sentinel");
    }

    private static void MaterialDescriptorRequiresMaterialsArray()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "missing materials array should throw InvalidDataException");
    }

    private static void MaterialDescriptorRequiresNonEmptyMaterialId()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = ""
index = 0
name = "Grassland"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "empty material id should throw InvalidDataException");
    }

    private static void MaterialDescriptorRejectsOutOfRangeMaterialIndex()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 4294967297
name = "Grassland"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml")),
            "out-of-range material index should throw InvalidDataException");
    }


    private static void MaterialDescriptorRejectsDuplicateIdsAndIndices()
    {
        string duplicateIdRoot = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(duplicateIdRoot);
        File.WriteAllText(Path.Combine(duplicateIdRoot, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"

[[materials]]
id = "grassland"
index = 1
name = "Grassland Variant"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(duplicateIdRoot, "descriptor.toml")),
            "duplicate material id should throw InvalidDataException");

        string duplicateIndexRoot = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(duplicateIndexRoot);
        File.WriteAllText(Path.Combine(duplicateIndexRoot, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"

[[materials]]
id = "cliff"
index = 0
name = "Cliff"
""");

        AssertThrowsInvalidData(
            () => RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(duplicateIndexRoot, "descriptor.toml")),
            "duplicate material index should throw InvalidDataException");
    }

    private static void BiomeSettingsKeepMaterialIdReferences()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
type = "HeightRange"
blend_mode = "Multiply"
min = 0
max = 1
enabled = true
visible = true
opacity = 1
min_falloff = 0.1
max_falloff = 0.1
""");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"));

        TestHarness.AssertEqual(1, settings.Layers.Count, "layer count");
        TestHarness.AssertEqual("grassland", settings.Layers[0].MaterialId, "material id binding");
        TestHarness.AssertEqual(1, settings.Modifiers.Count, "modifier count");
    }

    private static void BiomeSettingsDoNotRequireBiomeMaskField()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"));

        TestHarness.AssertEqual(1, settings.Biomes.Count, "biome count");
        TestHarness.Assert(
            typeof(RuntimeBiomeSettings).GetProperty("BiomeMaskPath") == null,
            "biome settings should not model biome_mask");
    }

    private static void BiomeSettingsRequiresSupportedVersion()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = "1"

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "non-integer biome settings version should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 4294967297

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "oversized unsupported biome settings version should throw InvalidDataException");
    }

    private static void BiomeSettingsRejectsOldRootFields()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1
biome_mask = "biome_mask.png"

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "old root field biome_mask should throw InvalidDataException");
    }

    private static void BiomeSettingsRejectsOldLayerMaterialSlotField()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
material_slot = 0
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "old layer material_slot field should throw InvalidDataException");
    }

    private static void BiomeSettingsValidatesKnownMaterialIdsWhenProvided()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"));
        TestHarness.AssertEqual("grassland", settings.Layers[0].MaterialId, "base reader should keep material id without validating external descriptor");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"), new HashSet<string> { "cliff" }),
            "unknown material_id should throw InvalidDataException when known ids are provided");
    }

    private static void BiomeSettingsRejectsOutOfRangeIntegerFields()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 4294967297
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "out-of-range biome id should throw InvalidDataException");

        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 4294967297
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "out-of-range layer biome_id should throw InvalidDataException");
    }


    private static void BiomeSettingsRejectEmptyMaterialId()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = ""
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "empty material_id should throw InvalidDataException");
    }

    private static void BiomeSettingsRejectEmptyModifierType()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
type = ""
blend_mode = "Multiply"
min = 0
max = 1
enabled = true
visible = true
opacity = 1
min_falloff = 0.1
max_falloff = 0.1
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "empty modifier type should throw InvalidDataException");
    }

    private static void BiomeSettingsRejectMissingBiomeReferences()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 99
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "layer biome_id should reference an existing biome");
    }

    private static void BiomeSettingsRejectMissingLayerReferences()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 99
type = "HeightRange"
blend_mode = "Multiply"
min = 0
max = 1
enabled = true
visible = true
opacity = 1
min_falloff = 0.1
max_falloff = 0.1
""");

        AssertThrowsInvalidData(
            () => RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml")),
            "modifier layer_id should reference an existing layer");
    }

    private static void BiomeSettingsPreservesAdvancedModifierParameters()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
name = "Noise mask"
type = "Noise"
blend_mode = "Add"
min = 0.2
max = 0.8
enabled = true
visible = true
opacity = 0.75
min_falloff = 0.05
max_falloff = 0.15
radius = 3
angle_degrees = 45
angle_range_degrees = 90
scale = 0.025
offset_x = 10
offset_y = 20
seed = 123
octaves = 6
invert = 1
texture_mask = "masks/noise.png"
texture_mask_channel = 2
""");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"));
        RuntimeBiomeModifierEntry modifier = settings.Modifiers[0];

        TestHarness.AssertEqual("Noise mask", modifier.Name, "modifier name");
        TestHarness.AssertEqual(3.0f, modifier.Radius, "modifier radius");
        TestHarness.AssertEqual(45.0f, modifier.AngleDegrees, "modifier angle");
        TestHarness.AssertEqual(90.0f, modifier.AngleRangeDegrees, "modifier angle range");
        TestHarness.AssertEqual(0.025f, modifier.Scale, "modifier scale");
        TestHarness.AssertEqual(10.0f, modifier.OffsetX, "modifier offset x");
        TestHarness.AssertEqual(20.0f, modifier.OffsetY, "modifier offset y");
        TestHarness.AssertEqual(123.0f, modifier.Seed, "modifier seed");
        TestHarness.AssertEqual(6.0f, modifier.Octaves, "modifier octaves");
        TestHarness.AssertEqual(1.0f, modifier.Invert, "modifier invert");
        TestHarness.AssertEqual("masks/noise.png", modifier.TextureMaskPath, "modifier texture mask");
        TestHarness.AssertEqual(2, modifier.TextureMaskChannel, "modifier texture mask channel");
    }

    private static string CreateMapData()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-map-definition-tests", Guid.NewGuid().ToString("N"), "map_data");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertThrowsInvalidData(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
