using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;
using Terrain.Editor.Tests;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorResourceWriterTests
{
    public static void RunAll()
    {
        TestHarness.Run("heightmap writer saves directly to resolved target", HeightmapWriterSavesToResolvedTarget);
        TestHarness.Run("biome mask writer saves directly to resolved target", BiomeMaskWriterSavesToResolvedTarget);
        TestHarness.Run("heightmap writer fails on read-only target without touching fallback", HeightmapWriterFailsOnReadOnlyTargetWithoutTouchingFallback);
        TestHarness.Run("map definition writer preserves map data entries and height scale", MapDefinitionWriterPreservesMapDataEntriesAndHeightScale);
        TestHarness.Run("material descriptor writer preserves short relative texture paths", MaterialDescriptorWriterPreservesRelativeTexturePaths);
        TestHarness.Run("material descriptor writer fails on read-only target without touching fallback", MaterialDescriptorWriterFailsOnReadOnlyTargetWithoutTouchingFallback);
        TestHarness.Run("biome settings writer persists material id references", BiomeSettingsWriterPersistsMaterialIdReferences);
        TestHarness.Run("biome settings writer preserves advanced modifier fields", BiomeSettingsWriterPreservesAdvancedModifierFields);
    }

    private static void HeightmapWriterSavesToResolvedTarget()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map_data", "heightmap.png");
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
        string output = Path.Combine(root, "mod", "map_data", "biome_mask.png");
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
        string baseFallback = Path.Combine(root, "base", "map_data", "heightmap.png");
        string modTarget = Path.Combine(root, "mod", "map_data", "heightmap.png");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFallback)!);
        File.WriteAllText(baseFallback, "base heightmap fallback");

        EditorResourceSession session = CreateSession(
            root,
            heightmap: new ResolvedGameResource("map_data/heightmap.png", modTarget, "mod", IsWritable: false, HasLowerPriorityFallback: true));

        TestHarness.AssertThrows<InvalidOperationException>(
            () => new HeightmapWriter().Write(session, [1, 2, 3, 4], width: 2, height: 2),
            "read-only heightmap target should throw");

        TestHarness.AssertEqual("base heightmap fallback", File.ReadAllText(baseFallback), "fallback heightmap should stay untouched");
        TestHarness.Assert(!File.Exists(modTarget), "writer should not create a writable override target when the resolved target is read-only");
    }

    private static void MaterialDescriptorWriterPreservesRelativeTexturePaths()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml");
        var session = CreateSession(root, materialDescriptorPath: output);

        new MaterialDescriptorWriter().Write(session,
        [
            new EditorMaterialDescriptorSlot("grass", 1, "Grass", "grass.png", "grass_n.png", null),
        ]);

        string text = File.ReadAllText(output);
        TestHarness.Assert(text.StartsWith("# Example material:", StringComparison.Ordinal), "writer should preserve the material comment template");
        TestHarness.Assert(text.Contains("# [[materials]]", StringComparison.Ordinal), "writer should preserve the materials example comment");
        TestHarness.Assert(text.Contains("# id = \"plains\"", StringComparison.Ordinal), "writer should preserve the material id example comment");
        TestHarness.Assert(text.Contains("# index = 0", StringComparison.Ordinal), "writer should preserve the material index example comment");
        TestHarness.Assert(text.Contains("# name = \"Plains\"", StringComparison.Ordinal), "writer should preserve the material name example comment");
        TestHarness.Assert(text.Contains("# albedo = \"plains_01_diffuse.dds\"", StringComparison.Ordinal), "writer should preserve the material albedo example comment");
        TestHarness.Assert(text.Contains("albedo = \"grass.png\"", StringComparison.Ordinal), "writer should keep short albedo path");
        TestHarness.Assert(!text.Contains(root, StringComparison.OrdinalIgnoreCase), "writer should not persist absolute workspace paths");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(output);
        TestHarness.AssertEqual("grass", descriptor.Materials[0].Id, "material id");
        TestHarness.AssertEqual("grass.png", descriptor.Materials[0].AlbedoPath, "material albedo path");
    }

    private static void MaterialDescriptorWriterFailsOnReadOnlyTargetWithoutTouchingFallback()
    {
        string root = CreateWorkspace();
        string baseFallback = Path.Combine(root, "base", "map_data", "materials", "descriptor.toml");
        string modTarget = Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFallback)!);
        File.WriteAllText(baseFallback, "base descriptor fallback");

        EditorResourceSession session = CreateSession(
            root,
            materialDescriptor: new ResolvedGameResource("map_data/materials/descriptor.toml", modTarget, "mod", IsWritable: false, HasLowerPriorityFallback: true));

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
        string output = Path.Combine(root, "mod", "map_data", "default.toml");
        var session = CreateSession(root, mapDefinitionPath: output);

        new MapDefinitionWriter().Write(session, new RuntimeMapDefinition
        {
            HeightmapPath = "heightmap.png",
            TerrainDataPath = "terrain.terrain",
            RiversPath = "rivers.png",
            ProvincesPath = "provinces.png",
            HeightScale = 250.0f,
        });

        string text = File.ReadAllText(output);
        TestHarness.Assert(text.StartsWith("# Optional terrain companion resources:", StringComparison.Ordinal), "writer should preserve the terrain comment template");
        TestHarness.Assert(text.Contains("# rivers = \"rivers.png\"", StringComparison.Ordinal), "writer should preserve rivers example comment");
        TestHarness.Assert(text.Contains("# provinces = \"provinces.png\"", StringComparison.Ordinal), "writer should preserve provinces example comment");
        TestHarness.Assert(text.Contains("[terrain]", StringComparison.Ordinal), "writer should still emit the terrain table");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);
        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "terrain data path");
        TestHarness.AssertEqual("rivers.png", map.RiversPath, "rivers path");
        TestHarness.AssertEqual("provinces.png", map.ProvincesPath, "provinces path");
        TestHarness.AssertEqual(250.0f, map.HeightScale, "height scale");
    }

    private static void BiomeSettingsWriterPersistsMaterialIdReferences()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map_data", "biome_settings.toml");
        var session = CreateSession(root, biomeSettingsPath: output);

        new BiomeSettingsWriter().Write(
            session,
            [new EditorBiomeDefinition(1, "Temperate")],
            [new EditorBiomeLayerDefinition(10, 1, "Grass Layer", "grass", 5, Enabled: true, Visible: true)],
            [new EditorBiomeModifierDefinition(20, 10, "Height range", "HeightRange", "Multiply", 0.1f, 0.9f, 0.05f, 0.1f, 1.0f, 0.0f, 180.0f, 1.0f, 0.0f, 0.0f, 0.0f, 4.0f, 0.0f, null, 0, 0.75f, Enabled: true, Visible: true)]);

        string text = File.ReadAllText(output);
        TestHarness.Assert(text.StartsWith("# Example biome:", StringComparison.Ordinal), "writer should preserve the biome comment template");
        TestHarness.Assert(text.Contains("# [[biomes]]", StringComparison.Ordinal), "writer should preserve the biomes example comment");
        TestHarness.Assert(text.Contains("# [[layers]]", StringComparison.Ordinal), "writer should preserve the layers example comment");
        TestHarness.Assert(text.Contains("# [[modifiers]]", StringComparison.Ordinal), "writer should preserve the modifiers example comment");
        TestHarness.Assert(text.Contains("# id = 1", StringComparison.Ordinal), "writer should preserve the biome id example comment");
        TestHarness.Assert(text.Contains("# name = \"Default\"", StringComparison.Ordinal), "writer should preserve the biome name example comment");
        TestHarness.Assert(text.Contains("# biome_id = 1", StringComparison.Ordinal), "writer should preserve the layer biome id example comment");
        TestHarness.Assert(text.Contains("# material_id = \"plains\"", StringComparison.Ordinal), "writer should preserve the layer material id example comment");
        TestHarness.Assert(text.Contains("# enabled = true", StringComparison.Ordinal), "writer should preserve the enabled example comment");
        TestHarness.Assert(text.Contains("# type = \"slope\"", StringComparison.Ordinal), "writer should preserve the modifier type example comment");
        TestHarness.Assert(text.Contains("# blend_mode = \"add\"", StringComparison.Ordinal), "writer should preserve the modifier blend mode example comment");
        TestHarness.Assert(text.Contains("# opacity = 1.0", StringComparison.Ordinal), "writer should preserve the modifier opacity example comment");
        TestHarness.Assert(text.Contains("material_id = \"grass\"", StringComparison.Ordinal), "writer should persist material_id references");
        TestHarness.Assert(!text.Contains("material_slot", StringComparison.Ordinal), "writer should not persist old material_slot fields");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(output, new HashSet<string>(StringComparer.Ordinal) { "grass" });
        TestHarness.AssertEqual("grass", settings.Layers[0].MaterialId, "layer material id");
        TestHarness.AssertEqual("HeightRange", settings.Modifiers[0].Type, "modifier type");
    }

    private static void BiomeSettingsWriterPreservesAdvancedModifierFields()
    {
        string root = CreateWorkspace();
        string output = Path.Combine(root, "mod", "map_data", "biome_settings.toml");
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
            Resource("map_data/default.toml", mapDefinitionPath ?? Path.Combine(root, "mod", "map_data", "default.toml")),
            heightmap ?? Resource("map_data/heightmap.png", heightmapPath ?? Path.Combine(root, "mod", "map_data", "heightmap.png")),
            Resource("map_data/terrain.terrain", Path.Combine(root, "mod", "map_data", "terrain.terrain")),
            Resource("map_data/biome_mask.png", biomeMaskPath ?? Path.Combine(root, "mod", "map_data", "biome_mask.png")),
            Resource("map_data/biome_settings.toml", biomeSettingsPath ?? Path.Combine(root, "mod", "map_data", "biome_settings.toml")),
            materialDescriptor ?? Resource("map_data/materials/descriptor.toml", materialDescriptorPath ?? Path.Combine(root, "mod", "map_data", "materials", "descriptor.toml")),
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
}
