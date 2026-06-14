using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class GameResourceRootLocatorTests
{
    public static void RunAll()
    {
        TestHarness.Run("game resource root locator finds repository game directory without launch settings file", FindsRepositoryGameDirectoryWithoutLaunchSettingsFile);
        TestHarness.Run("game resource root locator accepts direct-hit legal game root", AcceptsDirectHitLegalGameRoot);
        TestHarness.Run("game resource root locator ignores binary-local game copies", IgnoresBinaryLocalGameCopies);
        TestHarness.Run("resolver returns writable target for missing top layer file", ResolverReturnsWritableTargetForMissingTopLayerFile);
    }

    private static void FindsRepositoryGameDirectoryWithoutLaunchSettingsFile()
    {
        string root = CreateWorkspace();
        string gameRoot = Path.Combine(root, "game");
        string binaryRoot = Path.Combine(root, "Bin", "Editor", "Debug", "win-x64");
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data"));
        Directory.CreateDirectory(binaryRoot);

        string resolved = GameResourceRootLocator.FindFrom(binaryRoot);

        TestHarness.AssertEqual(Path.GetFullPath(gameRoot), resolved, "locator should resolve workspace game directory");
    }

    private static void IgnoresBinaryLocalGameCopies()
    {
        string root = CreateWorkspace();
        string gameRoot = Path.Combine(root, "game");
        string binaryRoot = Path.Combine(root, "Bin", "Editor", "Debug", "win-x64");
        string binaryGameRoot = Path.Combine(binaryRoot, "game");
        File.WriteAllText(Path.Combine(root, "Terrain.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(gameRoot, "map_data"));
        Directory.CreateDirectory(Path.Combine(binaryGameRoot, "map_data"));

        string resolved = GameResourceRootLocator.FindFrom(binaryRoot);

        TestHarness.AssertEqual(
            Path.GetFullPath(gameRoot),
            resolved,
            "locator should ignore binary-local game roots and keep using workspace game");
    }

    private static void AcceptsDirectHitLegalGameRoot()
    {
        string root = CreateWorkspace();
        string gameRoot = Path.Combine(root, "game");
        string nestedDirectory = Path.Combine(gameRoot, "map_data", "materials");
        Directory.CreateDirectory(nestedDirectory);

        string resolvedFromRoot = GameResourceRootLocator.FindFrom(gameRoot);
        string resolvedFromChild = GameResourceRootLocator.FindFrom(nestedDirectory);

        TestHarness.AssertEqual(Path.GetFullPath(gameRoot), resolvedFromRoot, "locator should accept direct-hit game root");
        TestHarness.AssertEqual(Path.GetFullPath(gameRoot), resolvedFromChild, "locator should accept child path under direct-hit game root");
    }

    private static void ResolverReturnsWritableTargetForMissingTopLayerFile()
    {
        string root = CreateWorkspace();
        string baseRoot = Path.Combine(root, "game");
        string modRoot = Path.Combine(root, "mods", "example_mod");
        Directory.CreateDirectory(Path.Combine(baseRoot, "map_data"));
        Directory.CreateDirectory(Path.Combine(modRoot, "map_data"));

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", baseRoot, isBaseLayer: true),
            new GameResourceLayer("example_mod", modRoot, isBaseLayer: false),
        });

        ResolvedGameResource target = resolver.ResolveWritableTarget("map_data/terrain.terrain");

        TestHarness.AssertEqual(
            Path.GetFullPath(Path.Combine(modRoot, "map_data", "terrain.terrain")),
            target.ResolvedPath,
            "missing file should target top writable layer");
        TestHarness.AssertEqual("example_mod", target.SourceLayerId, "writable target should point at highest priority layer");
        TestHarness.Assert(target.IsWritable, "writable target should be writable");
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-game-root-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
