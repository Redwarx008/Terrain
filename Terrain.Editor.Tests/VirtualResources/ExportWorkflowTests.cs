using Terrain.Editor.Services.Export;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class ExportWorkflowTests
{
    public static void RunAll()
    {
        TestHarness.Run("export manager rolls back failed export without touching fallback", ExportManagerRollsBackFailedExportWithoutTouchingFallback);
    }

    private static void ExportManagerRollsBackFailedExportWithoutTouchingFallback()
    {
        string root = CreateWorkspace();
        string baseFallback = Path.Combine(root, "base", "map", "terrain.terrain");
        string modTarget = Path.Combine(root, "mod", "map", "terrain.terrain");
        Directory.CreateDirectory(Path.GetDirectoryName(baseFallback)!);
        File.WriteAllText(baseFallback, "base terrain fallback");

        var manager = new ExportManager();
        manager.Register(new ThrowingExporter("Terrain"));

        TestHarness.AssertThrows<InvalidOperationException>(
            () => manager.ExecuteAsync("Terrain", modTarget, new Progress<ExportProgress>(), CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "failed export should be surfaced");

        TestHarness.AssertEqual("base terrain fallback", File.ReadAllText(baseFallback), "fallback terrain file should stay untouched");
        TestHarness.Assert(!File.Exists(modTarget), "failed export should roll back the resolved target file");
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-export-workflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ThrowingExporter(string name) : IExporter
    {
        public string Name { get; } = name;
        public string FileFilter => "Terrain Files (*.terrain)|*.terrain";
        public string DefaultExtension => "terrain";

        public Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "partial terrain");
            throw new InvalidOperationException("export failed");
        }
    }
}
