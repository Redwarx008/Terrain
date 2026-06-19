using System;
using System.IO;

namespace Terrain.Resources;

public static class GameResourceRootLocator
{
    private const string GameDirectoryName = "game";
    private const string MapDataDirectoryName = "map";

    public static string FindFrom(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            throw new ArgumentException("Start path is required.", nameof(startPath));

        string current = NormalizeToDirectory(startPath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (IsDirectGameRoot(current))
                return Path.GetFullPath(current);

            if (LooksLikeWorkspaceRoot(current))
            {
                string nestedGame = Path.Combine(current, GameDirectoryName);
                if (IsDirectGameRoot(nestedGame))
                    return Path.GetFullPath(nestedGame);
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException($"Could not locate game resource root from '{startPath}'.");
    }

    private static bool IsDirectGameRoot(string path)
    {
        return Directory.Exists(path)
            && string.Equals(
                Path.GetFileName(Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))),
                GameDirectoryName,
                StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(path, MapDataDirectoryName));
    }

    private static bool LooksLikeWorkspaceRoot(string path)
    {
        return Directory.Exists(path)
            && (File.Exists(Path.Combine(path, "Terrain.sln"))
                || Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly).Length > 0);
    }

    private static string NormalizeToDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            return Path.GetDirectoryName(fullPath)
                ?? throw new DirectoryNotFoundException($"File path has no containing directory: {path}");

        return fullPath;
    }
}
