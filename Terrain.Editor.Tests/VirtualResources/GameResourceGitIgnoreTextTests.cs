namespace Terrain.Editor.Tests.VirtualResources;

internal static class GameResourceGitIgnoreTextTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static void RunAll()
    {
        TestHarness.Run("repository gitignore excludes game directory", RepositoryGitignoreExcludesGameDirectory);
        TestHarness.Run("repository only tracks allowed game files", RepositoryOnlyTracksAllowedGameFiles);
        TestHarness.Run("repository gitignore preserves adjacent rules", RepositoryGitignorePreservesAdjacentRules);
        TestHarness.Run("git command failures surface stderr", GitCommandFailuresSurfaceStderr);
    }

    private static void RepositoryGitignoreExcludesGameDirectory()
    {
        string gitignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));
        TestHarness.Assert(gitignore.Contains("/game/", StringComparison.Ordinal), ".gitignore should ignore the game directory");
        GitCommandResult ignoredGame = RunGit("check-ignore -v --no-index -- game");
        TestHarness.AssertEqual(0, ignoredGame.ExitCode, $"git check-ignore should confirm that game is ignored. stderr: {ignoredGame.StandardError}");
        TestHarness.Assert(
            ignoredGame.StandardOutput.Contains("/game/", StringComparison.Ordinal),
            $"git check-ignore should report the /game/ rule. stdout: {ignoredGame.StandardOutput}");
    }

    private static void RepositoryOnlyTracksAllowedGameFiles()
    {
        GitCommandResult trackedFiles = RunGit("ls-files game");
        TestHarness.AssertEqual(0, trackedFiles.ExitCode, $"git ls-files should succeed in the repository root. stderr: {trackedFiles.StandardError}");

        string[] allowedGameFiles = ["game/map/water/flowmap.dds"];
        string[] actual = trackedFiles.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        TestHarness.AssertEqual(string.Join(Environment.NewLine, allowedGameFiles), string.Join(Environment.NewLine, actual), "git should only track explicitly allowed files under game/");
    }

    private static void RepositoryGitignorePreservesAdjacentRules()
    {
        string gitignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));
        TestHarness.Assert(gitignore.Contains("!Terrain.Editor/app.manifest", StringComparison.Ordinal), ".gitignore should keep the app manifest negation rule");
        TestHarness.Assert(gitignore.Contains(".worktrees/", StringComparison.Ordinal), ".gitignore should keep the worktree ignore rule");

        GitCommandResult ignoredWorktree = RunGit("check-ignore -v --no-index -- .worktrees/dummy");
        TestHarness.AssertEqual(0, ignoredWorktree.ExitCode, $"git check-ignore should confirm that .worktrees stays ignored. stderr: {ignoredWorktree.StandardError}");
        TestHarness.Assert(
            ignoredWorktree.StandardOutput.Contains(".worktrees/", StringComparison.Ordinal),
            $"git check-ignore should report the .worktrees/ rule. stdout: {ignoredWorktree.StandardOutput}");

        GitCommandResult appManifestIgnoreCheck = RunGit("check-ignore -v --no-index -- Terrain.Editor/app.manifest");
        TestHarness.AssertEqual(0, appManifestIgnoreCheck.ExitCode, $"git check-ignore should confirm the app manifest negation rule. stderr: {appManifestIgnoreCheck.StandardError}");
        TestHarness.Assert(
            appManifestIgnoreCheck.StandardOutput.Contains("!Terrain.Editor/app.manifest", StringComparison.Ordinal),
            $"git check-ignore should report the app manifest negation rule. stdout: {appManifestIgnoreCheck.StandardOutput}");

        GitCommandResult trackedManifest = RunGit("ls-files --error-unmatch -- Terrain.Editor/app.manifest");
        TestHarness.AssertEqual(0, trackedManifest.ExitCode, $"Terrain.Editor/app.manifest should remain tracked. stderr: {trackedManifest.StandardError}");
        TestHarness.Assert(
            trackedManifest.StandardOutput.Contains("Terrain.Editor/app.manifest", StringComparison.Ordinal),
            $"git ls-files should report Terrain.Editor/app.manifest as tracked. stdout: {trackedManifest.StandardOutput}");
    }

    private static void GitCommandFailuresSurfaceStderr()
    {
        string invalidDirectory = Path.Combine(Path.GetTempPath(), $"terrain-gitignore-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(invalidDirectory);
        GitCommandResult result = RunGit("ls-files game", invalidDirectory);
        TestHarness.Assert(result.ExitCode != 0, "git command should fail outside the repository");
        TestHarness.Assert(result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase), "git failures should surface stderr");
    }

    private static GitCommandResult RunGit(string arguments, string? workingDirectory = null)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory ?? RepositoryRoot,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitCommandResult(process.ExitCode, stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Terrain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private readonly record struct GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
