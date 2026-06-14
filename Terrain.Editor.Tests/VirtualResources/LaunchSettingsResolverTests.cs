using System.Text.Json;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class LaunchSettingsResolverTests
{
    public static void RunAll()
    {
        TestHarness.Run("launch settings loads enabled mods in declared order", LoadsEnabledModsInDeclaredOrder);
        TestHarness.Run("launch settings auto-create next to app directory", LaunchSettingsAutoCreateNextToAppDirectory);
        TestHarness.Run("enabled mod root must be absolute", EnabledModRootMustBeAbsolute);
        TestHarness.Run("enabled mod root rejects drive-relative rooted path", EnabledModRootRejectsDriveRelativeRootedPath);
        TestHarness.Run("enabled mod root must exist", EnabledModRootMustExist);
        TestHarness.Run("resolver prefers highest priority layer", ResolverPrefersHighestPriorityLayer);
        TestHarness.Run("resolver reports writable hit and fallback state", ResolverReportsWritableHitAndFallbackState);
        TestHarness.Run("resolver rejects rooted virtual paths", ResolverRejectsRootedVirtualPaths);
        TestHarness.Run("resolver allows file names containing double dots", ResolverAllowsFileNamesContainingDoubleDots);
        TestHarness.Run("resolver rejects parent traversal path segments", ResolverRejectsParentTraversalPathSegments);
        TestHarness.Run("resolver canonicalizes empty and dot path segments", ResolverCanonicalizesEmptyAndDotPathSegments);
        TestHarness.Run("resolver canonicalizes backslash path separators", ResolverCanonicalizesBackslashPathSeparators);
        TestHarness.Run("resolver rejects virtual paths that normalize to empty", ResolverRejectsVirtualPathsThatNormalizeToEmpty);
        TestHarness.Run("launch settings validates enabled mod identity fields", LaunchSettingsValidatesEnabledModIdentityFields);
    }

    private static void LoadsEnabledModsInDeclaredOrder()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "mod_a"));
        Directory.CreateDirectory(Path.Combine(root, "mod_b"));
        Directory.CreateDirectory(Path.Combine(root, "mod_c"));
        File.WriteAllText(Path.Combine(root, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new object[]
            {
                new { id = "mod_a", root = Path.Combine(root, "mod_a"), enabled = true },
                new { id = "mod_b", root = Path.Combine(root, "mod_b"), enabled = false },
                new { id = "mod_c", root = Path.Combine(root, "mod_c"), enabled = true },
            }
        }));

        LaunchSettings settings = LaunchSettingsService.Load(Path.Combine(root, "LaunchSetting.json"));

        TestHarness.AssertEqual(1, settings.Version, "version");
        TestHarness.AssertEqual(3, settings.Mods.Count, "mods count");
        TestHarness.AssertEqual("mod_a", settings.Mods[0].Id, "first mod id");
        TestHarness.AssertEqual("mod_b", settings.Mods[1].Id, "disabled mod id");
        TestHarness.AssertEqual("mod_c", settings.Mods[2].Id, "third mod id");

        IReadOnlyList<LaunchSettingsMod> enabledMods = LaunchSettingsService.GetEnabledMods(settings);

        TestHarness.AssertEqual(2, enabledMods.Count, "enabled mods count");
        TestHarness.AssertEqual("mod_a", enabledMods[0].Id, "first enabled mod id");
        TestHarness.AssertEqual("mod_c", enabledMods[1].Id, "second enabled mod id");
    }

    private static void LaunchSettingsAutoCreateNextToAppDirectory()
    {
        string appRoot = CreateWorkspace();

        LaunchSettings settings = LaunchSettingsService.LoadOrCreateForAppDirectory(appRoot);

        string filePath = Path.Combine(appRoot, "LaunchSetting.json");
        TestHarness.Assert(File.Exists(filePath), "LaunchSetting.json should be created next to the exe");
        TestHarness.AssertEqual(1, settings.Version, "default launch settings version");
        TestHarness.AssertEqual(0, settings.Mods.Count, "default launch settings mod count");
    }

    private static void EnabledModRootMustBeAbsolute()
    {
        AssertThrowsInvalidData(
            () => LaunchSettingsService.GetEnabledMods(new LaunchSettings
            {
                Mods =
                {
                    new LaunchSettingsMod { Id = "mod_a", Root = "mods/mod_a", Enabled = true },
                },
            }),
            "enabled mod with relative root should be rejected");
    }

    private static void EnabledModRootMustExist()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), "terrain-missing-mod", Guid.NewGuid().ToString("N"));

        AssertThrowsInvalidData(
            () => LaunchSettingsService.GetEnabledMods(new LaunchSettings
            {
                Mods =
                {
                    new LaunchSettingsMod { Id = "mod_a", Root = missingRoot, Enabled = true },
                },
            }),
            "enabled mod with missing root should be rejected");
    }

    private static void EnabledModRootRejectsDriveRelativeRootedPath()
    {
        AssertThrowsInvalidDataContaining(
            () => LaunchSettingsService.GetEnabledMods(new LaunchSettings
            {
                Mods =
                {
                    new LaunchSettingsMod { Id = "mod_a", Root = "C:mods\\mod_a", Enabled = true },
                },
            }),
            "absolute path",
            "enabled mod with drive-relative rooted path should be rejected as non-absolute");
    }

    private static void ResolverPrefersHighestPriorityLayer()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_a", "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_b", "map_data"));

        File.WriteAllText(Path.Combine(root, "map_data", "default.toml"), "base");
        File.WriteAllText(Path.Combine(root, "mod_a", "map_data", "default.toml"), "mod_a");
        File.WriteAllText(Path.Combine(root, "mod_b", "map_data", "default.toml"), "mod_b");

        var layers = new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
            new GameResourceLayer("mod_a", Path.Combine(root, "mod_a"), isBaseLayer: false),
            new GameResourceLayer("mod_b", Path.Combine(root, "mod_b"), isBaseLayer: false),
        };

        var resolver = new GameResourceResolver(layers);
        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data/default.toml");

        TestHarness.AssertEqual("mod_b", resolved.SourceLayerId, "highest priority layer");
        TestHarness.AssertEqual("mod_b", File.ReadAllText(resolved.ResolvedPath), "resolved file contents");
    }

    private static void ResolverReportsWritableHitAndFallbackState()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_a", "map_data"));

        string baseFile = Path.Combine(root, "map_data", "heightmap.png");
        string modFile = Path.Combine(root, "mod_a", "map_data", "heightmap.png");
        File.WriteAllText(baseFile, "base");
        File.WriteAllText(modFile, "mod");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
            new GameResourceLayer("mod_a", Path.Combine(root, "mod_a"), isBaseLayer: false),
        });

        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data/heightmap.png");

        TestHarness.Assert(resolved.IsWritable, "resolved hit should be writable in temp workspace");
        TestHarness.Assert(resolved.HasLowerPriorityFallback, "resolved mod file should report covered base fallback");
    }

    private static void ResolverRejectsRootedVirtualPaths()
    {
        string root = CreateWorkspace();
        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile(Path.Combine(root, "map_data", "heightmap.png")), "absolute virtual path should be rejected");
        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("C:\\map_data\\heightmap.png"), "drive-rooted virtual path should be rejected");
        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("\\\\server\\share\\map_data\\heightmap.png"), "UNC virtual path should be rejected");
        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("\\map_data\\heightmap.png"), "root-relative virtual path should be rejected");
        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("/map_data/heightmap.png"), "slash-rooted virtual path should be rejected");
    }

    private static void ResolverAllowsFileNamesContainingDoubleDots()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        string file = Path.Combine(root, "map_data", "biome..png");
        File.WriteAllText(file, "biome");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data/biome..png");

        TestHarness.AssertEqual(Path.GetFullPath(file), resolved.ResolvedPath, "double dot file name should resolve");
    }

    private static void ResolverRejectsParentTraversalPathSegments()
    {
        string root = CreateWorkspace();
        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("map_data/../heightmap.png"), "parent traversal segment should be rejected");
    }

    private static void ResolverCanonicalizesEmptyAndDotPathSegments()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        string file = Path.Combine(root, "map_data", "heightmap.png");
        File.WriteAllText(file, "height");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data//./heightmap.png");

        TestHarness.AssertEqual("map_data/heightmap.png", resolved.VirtualPath, "canonical virtual path");
        TestHarness.AssertEqual(Path.GetFullPath(file), resolved.ResolvedPath, "canonicalized path should resolve");
    }

    private static void ResolverCanonicalizesBackslashPathSeparators()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        string file = Path.Combine(root, "map_data", "heightmap.png");
        File.WriteAllText(file, "height");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data\\heightmap.png");

        TestHarness.AssertEqual("map_data/heightmap.png", resolved.VirtualPath, "backslash virtual path should be canonicalized");
        TestHarness.AssertEqual(Path.GetFullPath(file), resolved.ResolvedPath, "backslash path should resolve");
    }

    private static void ResolverRejectsVirtualPathsThatNormalizeToEmpty()
    {
        string root = CreateWorkspace();
        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        });

        AssertThrowsInvalidData(() => resolver.ResolveRequiredFile("./"), "dot-only virtual path should be rejected");
    }

    private static void LaunchSettingsValidatesEnabledModIdentityFields()
    {
        string root = CreateWorkspace();
        string validRoot = Path.Combine(root, "mod_a");
        Directory.CreateDirectory(validRoot);

        var disabledInvalid = new LaunchSettings
        {
            Mods =
            {
                new LaunchSettingsMod { Id = string.Empty, Root = string.Empty, Enabled = false },
                new LaunchSettingsMod { Id = "mod_a", Root = validRoot, Enabled = true },
            },
        };

        IReadOnlyList<LaunchSettingsMod> enabledMods = LaunchSettingsService.GetEnabledMods(disabledInvalid);

        TestHarness.AssertEqual(1, enabledMods.Count, "disabled invalid mod should not block enabled mod selection");
        TestHarness.AssertEqual("mod_a", enabledMods[0].Id, "valid enabled mod id");

        AssertThrowsInvalidData(
            () => LaunchSettingsService.GetEnabledMods(new LaunchSettings
            {
                Mods =
                {
                    new LaunchSettingsMod { Id = string.Empty, Root = "mod_a", Enabled = true },
                },
            }),
            "enabled mod with empty id should be rejected");

        AssertThrowsInvalidData(
            () => LaunchSettingsService.GetEnabledMods(new LaunchSettings
            {
                Mods =
                {
                    new LaunchSettingsMod { Id = "mod_a", Root = string.Empty, Enabled = true },
                },
            }),
            "enabled mod with empty root should be rejected");
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-virtual-resource-tests", Guid.NewGuid().ToString("N"));
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

    private static void AssertThrowsInvalidDataContaining(Action action, string expectedMessagePart, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message.Contains(expectedMessagePart, StringComparison.Ordinal))
        {
            return;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"{message}: expected message containing '{expectedMessagePart}', actual '{ex.Message}'");
        }

        throw new InvalidOperationException(message);
    }
}
