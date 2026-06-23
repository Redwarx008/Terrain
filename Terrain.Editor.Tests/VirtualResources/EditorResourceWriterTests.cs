using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorResourceWriterTests
{
    private static readonly string[] MapDefinitionTemplateLines =
    [
        "# Optional terrain companion resources:",
        "# rivers = \"rivers.png\"",
        "# provinces = \"provinces.png\"",
    ];

    private static readonly string[] MaterialDescriptorTemplateLines =
    [
        "# Example material:",
        "# [[materials]]",
        "# id = \"plains\"",
        "# index = 0",
        "# name = \"Plains\"",
        "# albedo = \"plains_01_diffuse.dds\"",
        "# normal = \"plains_01_normal.dds\"",
        "# properties = \"plains_01_properties.dds\"",
    ];

    private static readonly string[] BiomeSettingsTemplateLines =
    [
        "# Example biome:",
        "# [[biomes]]",
        "# id = 1",
        "# name = \"Default\"",
        "#",
        "# Example layer:",
        "# [[layers]]",
        "# id = 1",
        "# biome_id = 1",
        "# name = \"Base\"",
        "# material_id = \"plains\"",
        "# priority = 0",
        "# enabled = true",
        "# visible = true",
        "#",
        "# Example modifier:",
        "# [[modifiers]]",
        "# id = 1",
        "# layer_id = 1",
        "# name = \"Slope\"",
        "# type = \"slope\"",
        "# blend_mode = \"add\"",
        "# min = 0.2",
        "# max = 0.8",
        "# min_falloff = 0.1",
        "# max_falloff = 0.1",
        "# opacity = 1.0",
        "# enabled = true",
        "# visible = true",
    ];

    public static void RunAll()
    {
        TestHarness.Run("heightmap writer saves directly to resolved target", HeightmapWriterSavesToResolvedTarget);
        TestHarness.Run("biome mask writer saves directly to resolved target", BiomeMaskWriterSavesToResolvedTarget);
        TestHarness.Run("heightmap writer fails on read-only target without touching fallback", HeightmapWriterFailsOnReadOnlyTargetWithoutTouchingFallback);
        TestHarness.Run("map definition writer preserves map data entries and height scale", MapDefinitionWriterPreservesMapDataEntriesAndHeightScale);
        TestHarness.Run("map definition writer rewrites existing header with fixed template", MapDefinitionWriterRewritesExistingHeaderWithFixedTemplate);
        TestHarness.Run("map definition reader defaults river width range", MapDefinitionReaderDefaultsRiverWidthRange);
        TestHarness.Run("map definition reader reads explicit river width range", MapDefinitionReaderReadsExplicitRiverWidthRange);
        TestHarness.Run("map definition reader defaults river camera visibility height", MapDefinitionReaderDefaultsRiverCameraVisibilityHeight);
        TestHarness.Run("map definition reader reads explicit river camera visibility height", MapDefinitionReaderReadsExplicitRiverCameraVisibilityHeight);
        TestHarness.Run("map definition reader rejects invalid river camera visibility height", MapDefinitionReaderRejectsInvalidRiverCameraVisibilityHeight);
        TestHarness.Run("map definition reader defaults sea level", MapDefinitionReaderDefaultsSeaLevel);
        TestHarness.Run("map definition reader reads explicit sea level", MapDefinitionReaderReadsExplicitSeaLevel);
        TestHarness.Run("map definition reader rejects invalid sea level", MapDefinitionReaderRejectsInvalidSeaLevel);
        TestHarness.Run("map definition reader rejects out-of-range sea level", MapDefinitionReaderRejectsOutOfRangeSeaLevel);
        TestHarness.Run("map definition writer rejects invalid sea level", MapDefinitionWriterRejectsInvalidSeaLevel);
        TestHarness.Run("map definition reader rejects invalid river width range", MapDefinitionReaderRejectsInvalidRiverWidthRange);
        TestHarness.Run("map definition reader rejects non-finite river width range", MapDefinitionReaderRejectsNonFiniteRiverWidthRange);
        TestHarness.Run("map definition writer rejects non-finite river width range", MapDefinitionWriterRejectsNonFiniteRiverWidthRange);
        TestHarness.Run("map definition writer rejects invalid river camera visibility height", MapDefinitionWriterRejectsInvalidRiverCameraVisibilityHeight);
        TestHarness.Run("material descriptor writer preserves short relative texture paths", MaterialDescriptorWriterPreservesRelativeTexturePaths);
        TestHarness.Run("material descriptor writer rewrites existing header with fixed template", MaterialDescriptorWriterRewritesExistingHeaderWithFixedTemplate);
        TestHarness.Run("material descriptor writer fails on read-only target without touching fallback", MaterialDescriptorWriterFailsOnReadOnlyTargetWithoutTouchingFallback);
        TestHarness.Run("biome settings writer persists material id references", BiomeSettingsWriterPersistsMaterialIdReferences);
        TestHarness.Run("biome settings writer rewrites existing header with fixed template", BiomeSettingsWriterRewritesExistingHeaderWithFixedTemplate);
        TestHarness.Run("biome settings writer preserves advanced modifier fields", BiomeSettingsWriterPreservesAdvancedModifierFields);
    }

    private static void HeightmapWriterSavesToResolvedTarget()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "heightmap.png");
        var session = CreateSession(root, heightmapPath: output);

        new HeightmapWriter().Write(session, [0, ushort.MaxValue, 123, 456], width: 2, height: 2);

        TestHarness.Assert(File.Exists(output), "heightmap writer should write the resolved heightmap path");
        using var image = Image.Load<L16>(output);
        TestHarness.AssertEqual(2, image.Width, "heightmap width");
        TestHarness.AssertEqual(2, image.Height, "heightmap height");
        TestHarness.AssertEqual(ushort.MaxValue, image[1, 0].PackedValue, "heightmap pixel");
    }

    private static void BiomeMaskWriterSavesToResolvedTarget()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "biome_mask.png");
        var session = CreateSession(root, biomeMaskPath: output);
        var mask = new BiomeMask(2, 2);
        mask.SetValue(0, 0, 7);
        mask.SetValue(1, 1, 9);

        new BiomeMaskWriter().Write(session, mask);

        TestHarness.Assert(File.Exists(output), "biome mask writer should write the resolved biome mask path");
        using var image = Image.Load<L8>(output);
        TestHarness.AssertEqual((byte)7, image[0, 0].PackedValue, "biome mask first pixel");
        TestHarness.AssertEqual((byte)9, image[1, 1].PackedValue, "biome mask last pixel");
    }

    private static void HeightmapWriterFailsOnReadOnlyTargetWithoutTouchingFallback()
    {
        string root = CreateWorkspace();
        string baseFallback = Path.Combine(root, "base", "map", "heightmap.png");
        string modTarget = Path.Combine(root, "mod", "map", "heightmap.png");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFallback)!);
        File.WriteAllText(baseFallback, "base heightmap fallback");

        EditorResourceSession session = CreateSession(
            root,
            heightmap: new ResolvedGameResource("map/heightmap.png", modTarget, "mod", IsWritable: false, HasLowerPriorityFallback: true));

        TestHarness.AssertThrows<InvalidOperationException>(
            () => new HeightmapWriter().Write(session, [1, 2, 3, 4], width: 2, height: 2),
            "read-only heightmap target should throw");

        TestHarness.AssertEqual("base heightmap fallback", File.ReadAllText(baseFallback), "fallback heightmap should stay untouched");
        TestHarness.Assert(!File.Exists(modTarget), "writer should not create a writable override target when the resolved target is read-only");
    }

    private static void MaterialDescriptorWriterPreservesRelativeTexturePaths()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
        var session = CreateSession(root, materialDescriptorPath: output);

        new MaterialDescriptorWriter().Write(session,
        [
            new EditorMaterialDescriptorSlot("grass", 1, "Grass", "grass.png", "grass_n.png", null),
        ]);

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, MaterialDescriptorTemplateLines, "descriptor writer output");
        TestHarness.Assert(text.Contains("albedo = \"grass.png\"", StringComparison.Ordinal), "writer should keep short albedo path");
        TestHarness.Assert(!text.Contains(root, StringComparison.OrdinalIgnoreCase), "writer should not persist absolute workspace paths");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(output);
        TestHarness.AssertEqual("grass", descriptor.Materials[0].Id, "material id");
        TestHarness.AssertEqual("grass.png", descriptor.Materials[0].AlbedoPath, "material albedo path");
    }

    private static void MaterialDescriptorWriterRewritesExistingHeaderWithFixedTemplate()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
        var session = CreateSession(root, materialDescriptorPath: output);
        WriteExistingFile(output, "# legacy descriptor header\n# keep-me-out\nversion = 99\n");

        new MaterialDescriptorWriter().Write(session,
        [
            new EditorMaterialDescriptorSlot("grass", 1, "Grass", "grass.png", "grass_n.png", null),
        ]);

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, MaterialDescriptorTemplateLines, "descriptor writer rewrite output");
        TestHarness.Assert(!text.Contains("# keep-me-out", StringComparison.Ordinal), "writer should replace the previous descriptor header block");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(output);
        TestHarness.AssertEqual("grass", descriptor.Materials[0].Id, "rewritten descriptor material id");
        TestHarness.AssertEqual("grass.png", descriptor.Materials[0].AlbedoPath, "rewritten descriptor albedo path");
    }

    private static void MaterialDescriptorWriterFailsOnReadOnlyTargetWithoutTouchingFallback()
    {
        string root = CreateWorkspace();
        string baseFallback = Path.Combine(root, "base", "map", "materials", "descriptor.toml");
        string modTarget = Path.Combine(root, "mod", "map", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFallback)!);
        File.WriteAllText(baseFallback, "base descriptor fallback");

        EditorResourceSession session = CreateSession(
            root,
            materialDescriptor: new ResolvedGameResource("map/materials/descriptor.toml", modTarget, "mod", IsWritable: false, HasLowerPriorityFallback: true));

        TestHarness.AssertThrows<InvalidOperationException>(
            () => new MaterialDescriptorWriter().Write(session,
            [
                new EditorMaterialDescriptorSlot("grass", 1, "Grass", "grass.png", null, null),
            ]),
            "read-only material descriptor target should throw");

        TestHarness.AssertEqual("base descriptor fallback", File.ReadAllText(baseFallback), "fallback descriptor should stay untouched");
        TestHarness.Assert(!File.Exists(modTarget), "writer should not create a writable override descriptor when the resolved target is read-only");
    }

    private static void MapDefinitionWriterPreservesMapDataEntriesAndHeightScale()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        var session = CreateSession(root, mapDefinitionPath: output);

        new MapDefinitionWriter().Write(session, new RuntimeMapDefinition
        {
            HeightmapPath = "heightmap.png",
            TerrainDataPath = "terrain.terrain",
            RiversPath = "rivers.png",
            ProvincesPath = "provinces.png",
            HeightScale = 250.0f,
            RiverMinWidth = 2.0f,
            RiverMaxWidth = 6.0f,
            RiverMaxVisibleCameraHeight = 750.0f,
            SeaLevel = 12.5f,
        });

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, MapDefinitionTemplateLines, "map definition writer output");
        TestHarness.Assert(text.Contains("[terrain]", StringComparison.Ordinal), "writer should still emit the terrain table");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);
        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "terrain data path");
        TestHarness.AssertEqual("rivers.png", map.RiversPath, "rivers path");
        TestHarness.AssertEqual("provinces.png", map.ProvincesPath, "provinces path");
        TestHarness.AssertEqual(250.0f, map.HeightScale, "height scale");
        TestHarness.AssertEqual(2.0f, map.RiverMinWidth, "river min full width");
        TestHarness.AssertEqual(6.0f, map.RiverMaxWidth, "river max full width");
        TestHarness.AssertEqual(750.0f, map.RiverMaxVisibleCameraHeight, "river max visible camera height");
        TestHarness.AssertEqual(12.5f, map.SeaLevel, "sea level");
    }

    private static void MapDefinitionWriterRewritesExistingHeaderWithFixedTemplate()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        var session = CreateSession(root, mapDefinitionPath: output);
        WriteExistingFile(output, "# legacy map header\n# stale-map-header\nversion = 99\n");

        new MapDefinitionWriter().Write(session, new RuntimeMapDefinition
        {
            HeightmapPath = "heightmap.png",
            TerrainDataPath = "terrain.terrain",
            RiversPath = "rivers.png",
            ProvincesPath = "provinces.png",
            HeightScale = 250.0f,
        });

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, MapDefinitionTemplateLines, "map definition writer rewrite output");
        TestHarness.Assert(!text.Contains("# stale-map-header", StringComparison.Ordinal), "writer should replace the previous map definition header block");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);
        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "rewritten map definition heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "rewritten map definition terrain path");
        TestHarness.AssertEqual(250.0f, map.HeightScale, "rewritten map definition height scale");
    }

    private static void MapDefinitionReaderDefaultsRiverWidthRange()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(1.0f, map.RiverMinWidth, "default river min full width");
        TestHarness.AssertEqual(4.0f, map.RiverMaxWidth, "default river max full width");
    }

    private static void MapDefinitionReaderDefaultsRiverCameraVisibilityHeight()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(3000.0f, map.RiverMaxVisibleCameraHeight, "default river max visible camera height");
    }

    private static void MapDefinitionReaderReadsExplicitRiverWidthRange()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = 2
river_max_width = 6
river_max_visible_camera_height = 750
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(2.0f, map.RiverMinWidth, "explicit river min full width");
        TestHarness.AssertEqual(6.0f, map.RiverMaxWidth, "explicit river max full width");
        TestHarness.AssertEqual(750.0f, map.RiverMaxVisibleCameraHeight, "explicit river max visible camera height");
    }

    private static void MapDefinitionReaderReadsExplicitRiverCameraVisibilityHeight()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_max_visible_camera_height = 875
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(875.0f, map.RiverMaxVisibleCameraHeight, "explicit river max visible camera height");
    }

    private static void MapDefinitionReaderRejectsInvalidRiverCameraVisibilityHeight()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_max_visible_camera_height = 0
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(output),
            "non-positive river_max_visible_camera_height should be rejected");
    }

    private static void MapDefinitionReaderDefaultsSeaLevel()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(3.8f, map.SeaLevel, "default sea level");
    }

    private static void MapDefinitionReaderReadsExplicitSeaLevel()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
sea_level = 12.5
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

        TestHarness.AssertEqual(12.5f, map.SeaLevel, "explicit sea level");
    }

    private static void MapDefinitionReaderRejectsInvalidSeaLevel()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
sea_level = nan
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(output),
            "non-finite sea_level should be rejected");
    }

    private static void MapDefinitionReaderRejectsOutOfRangeSeaLevel()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
sea_level = 1000000000000
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(output),
            "out-of-range sea_level should be rejected");
    }

    private static void MapDefinitionReaderRejectsInvalidRiverWidthRange()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "default.toml");
        WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = 5
river_max_width = 4
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(output),
            "river_max_width below river_min_width should be rejected");
    }

    private static void MapDefinitionReaderRejectsNonFiniteRiverWidthRange()
    {
        string root = CreateWorkspace();
        string minOutput = Path.Combine(root, "mod", "map", "default-min-nan.toml");
        WriteExistingFile(minOutput, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = nan
river_max_width = 4
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(minOutput),
            "nan river_min_width should be rejected");

        string maxOutput = Path.Combine(root, "mod", "map", "default-max-inf.toml");
        WriteExistingFile(maxOutput, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
river_min_width = 1
river_max_width = inf
""");

        TestHarness.AssertThrows<InvalidDataException>(
            () => RuntimeMapDefinitionReader.ReadFrom(maxOutput),
            "infinite river_max_width should be rejected");
    }

    private static void MapDefinitionWriterRejectsNonFiniteRiverWidthRange()
    {
        string root = CreateWorkspace();
        var session = CreateSession(root);
        var writer = new MapDefinitionWriter();

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                RiverMinWidth = float.NaN,
                RiverMaxWidth = 4.0f,
            }),
            "nan river_min_width should be rejected by writer");

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                RiverMinWidth = 1.0f,
                RiverMaxWidth = float.PositiveInfinity,
            }),
            "infinite river_max_width should be rejected by writer");
    }

    private static void MapDefinitionWriterRejectsInvalidRiverCameraVisibilityHeight()
    {
        string root = CreateWorkspace();
        var session = CreateSession(root);
        var writer = new MapDefinitionWriter();

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                RiverMaxVisibleCameraHeight = float.PositiveInfinity,
            }),
            "infinite river_max_visible_camera_height should be rejected by writer");

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                RiverMaxVisibleCameraHeight = 0.0f,
            }),
            "non-positive river_max_visible_camera_height should be rejected by writer");
    }

    private static void MapDefinitionWriterRejectsInvalidSeaLevel()
    {
        string root = CreateWorkspace();
        var session = CreateSession(root);
        var writer = new MapDefinitionWriter();

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                SeaLevel = float.PositiveInfinity,
            }),
            "infinite sea_level should be rejected by writer");

        TestHarness.AssertThrows<InvalidDataException>(
            () => writer.Write(session, new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 200.0f,
                SeaLevel = float.MaxValue,
            }),
            "out-of-range sea_level should be rejected by writer");
    }

    private static void BiomeSettingsWriterPersistsMaterialIdReferences()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "biome_settings.toml");
        var session = CreateSession(root, biomeSettingsPath: output);

        new BiomeSettingsWriter().Write(
            session,
            [new EditorBiomeDefinition(1, "Temperate")],
            [new EditorBiomeLayerDefinition(10, 1, "Grass Layer", "grass", 5, Enabled: true, Visible: true)],
            [new EditorBiomeModifierDefinition(20, 10, "Height range", "HeightRange", "Multiply", 0.1f, 0.9f, 0.05f, 0.1f, 1.0f, 0.0f, 180.0f, 1.0f, 0.0f, 0.0f, 0.0f, 4.0f, 0.0f, null, 0, 0.75f, Enabled: true, Visible: true)]);

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, BiomeSettingsTemplateLines, "biome settings writer output");
        TestHarness.Assert(text.Contains("material_id = \"grass\"", StringComparison.Ordinal), "writer should persist material_id references");
        TestHarness.Assert(!text.Contains("material_slot", StringComparison.Ordinal), "writer should not persist old material_slot fields");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(output, new HashSet<string>(StringComparer.Ordinal) { "grass" });
        TestHarness.AssertEqual("grass", settings.Layers[0].MaterialId, "layer material id");
        TestHarness.AssertEqual("HeightRange", settings.Modifiers[0].Type, "modifier type");
    }

    private static void BiomeSettingsWriterRewritesExistingHeaderWithFixedTemplate()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "biome_settings.toml");
        var session = CreateSession(root, biomeSettingsPath: output);
        WriteExistingFile(output, "# legacy biome header\n# stale-biome-header\nversion = 99\n");

        new BiomeSettingsWriter().Write(
            session,
            [new EditorBiomeDefinition(1, "Temperate")],
            [new EditorBiomeLayerDefinition(10, 1, "Grass Layer", "grass", 5, Enabled: true, Visible: true)],
            [new EditorBiomeModifierDefinition(20, 10, "Height range", "HeightRange", "Multiply", 0.1f, 0.9f, 0.05f, 0.1f, 1.0f, 0.0f, 180.0f, 1.0f, 0.0f, 0.0f, 0.0f, 4.0f, 0.0f, null, 0, 0.75f, Enabled: true, Visible: true)]);

        string text = File.ReadAllText(output);
        AssertStartsWithTemplateBlock(text, BiomeSettingsTemplateLines, "biome settings writer rewrite output");
        TestHarness.Assert(!text.Contains("# stale-biome-header", StringComparison.Ordinal), "writer should replace the previous biome settings header block");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(output, new HashSet<string>(StringComparer.Ordinal) { "grass" });
        TestHarness.AssertEqual("grass", settings.Layers[0].MaterialId, "rewritten biome settings layer material id");
        TestHarness.AssertEqual("HeightRange", settings.Modifiers[0].Type, "rewritten biome settings modifier type");
    }

    private static void BiomeSettingsWriterPreservesAdvancedModifierFields()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map", "biome_settings.toml");
        var session = CreateSession(root, biomeSettingsPath: output);

        new BiomeSettingsWriter().Write(
            session,
            [new EditorBiomeDefinition(1, "Temperate")],
            [new EditorBiomeLayerDefinition(10, 1, "Grass Layer", "grass", 5, Enabled: true, Visible: true)],
            [new EditorBiomeModifierDefinition(20, 10, "Noise Detail", "Noise", "Add", 0.1f, 0.9f, 0.05f, 0.1f, 2.5f, 45.0f, 90.0f, 0.25f, 12.0f, 34.0f, 56.0f, 6.0f, 1.0f, "masks/noise.png", 2, 0.75f, Enabled: true, Visible: false)]);

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(output, new HashSet<string>(StringComparer.Ordinal) { "grass" });
        RuntimeBiomeModifierEntry modifier = settings.Modifiers[0];
        TestHarness.AssertEqual("Noise Detail", modifier.Name, "modifier name");
        TestHarness.AssertEqual("Noise", modifier.Type, "modifier type");
        TestHarness.AssertEqual("Add", modifier.BlendMode, "modifier blend mode");
        TestHarness.AssertEqual(2.5f, modifier.Radius, "modifier radius");
        TestHarness.AssertEqual(45.0f, modifier.AngleDegrees, "modifier angle");
        TestHarness.AssertEqual(90.0f, modifier.AngleRangeDegrees, "modifier angle range");
        TestHarness.AssertEqual(0.25f, modifier.Scale, "modifier scale");
        TestHarness.AssertEqual(12.0f, modifier.OffsetX, "modifier offset x");
        TestHarness.AssertEqual(34.0f, modifier.OffsetY, "modifier offset y");
        TestHarness.AssertEqual(56.0f, modifier.Seed, "modifier seed");
        TestHarness.AssertEqual(6.0f, modifier.Octaves, "modifier octaves");
        TestHarness.AssertEqual(1.0f, modifier.Invert, "modifier invert");
        TestHarness.AssertEqual("masks/noise.png", modifier.TextureMaskPath, "modifier texture mask path");
        TestHarness.AssertEqual(2, modifier.TextureMaskChannel, "modifier texture mask channel");
        TestHarness.AssertEqual(false, modifier.Visible, "modifier visible flag");
    }

    private static EditorResourceSession CreateSession(
        string root,
        string? mapDefinitionPath = null,
        string? heightmapPath = null,
        string? biomeMaskPath = null,
        string? biomeSettingsPath = null,
        string? materialDescriptorPath = null,
        ResolvedGameResource? heightmap = null,
        ResolvedGameResource? materialDescriptor = null)
    {
        static ResolvedGameResource Resource(string virtualPath, string path)
        {
            return new ResolvedGameResource(virtualPath, path, "mod", IsWritable: true, HasLowerPriorityFallback: true);
        }

        return new EditorResourceSession(
            Resource("map/default.toml", mapDefinitionPath ?? Path.Combine(root, "mod", "map", "default.toml")),
            heightmap ?? Resource("map/heightmap.png", heightmapPath ?? Path.Combine(root, "mod", "map", "heightmap.png")),
            Resource("map/terrain.terrain", Path.Combine(root, "mod", "map", "terrain.terrain")),
            Resource("map/biome_mask.png", biomeMaskPath ?? Path.Combine(root, "mod", "map", "biome_mask.png")),
            Resource("map/biome_settings.toml", biomeSettingsPath ?? Path.Combine(root, "mod", "map", "biome_settings.toml")),
            materialDescriptor ?? Resource("map/materials/descriptor.toml", materialDescriptorPath ?? Path.Combine(root, "mod", "map", "materials", "descriptor.toml")),
            new RuntimeMapDefinition
            {
                HeightmapPath = "heightmap.png",
                TerrainDataPath = "terrain.terrain",
                HeightScale = 100.0f,
            });
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-resource-writer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertStartsWithTemplateBlock(string text, IReadOnlyList<string> templateLines, string fileLabel)
    {
        string normalized = NormalizeLineEndings(text);
        string expectedPrefix = string.Join('\n', templateLines) + "\n\n";
        TestHarness.Assert(normalized.StartsWith(expectedPrefix, StringComparison.Ordinal), $"{fileLabel} should begin with the full template block followed by a blank line");
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void WriteExistingFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
