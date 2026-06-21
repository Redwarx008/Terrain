using System;
using System.IO;
using System.Reflection;

namespace Terrain.Resources;

public static class GameResourceRootLocator
{
    private const string GameDirectoryName = "game";
    private const string MapDataDirectoryName = "map";
    private const string TerrainWorkspaceRootMetadataKey = "TerrainWorkspaceRoot";

    public static string TerrainAssemblyDirectory
    {
        get
        {
            string assemblyLocation = typeof(GameResourceRootLocator).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyLocation))
                return AppContext.BaseDirectory;

            return Path.GetDirectoryName(assemblyLocation)
                ?? AppContext.BaseDirectory;
        }
    }

    public static string FindFromTerrainAssembly()
    {
        return FindFromTerrainAssemblyContext(TerrainAssemblyDirectory, TerrainBuildWorkspaceRoot);
    }

    public static string TerrainResourceAppDirectory
    {
        get
        {
            return ResolveTerrainAssemblyAppDirectory(TerrainAssemblyDirectory, TerrainBuildWorkspaceRoot);
        }
    }

    internal static string FindFromTerrainAssemblyContext(string assemblyDirectory, string buildWorkspaceRoot)
    {
        return FindFrom(ResolveTerrainAssemblyAppDirectory(assemblyDirectory, buildWorkspaceRoot));
    }

    internal static string ResolveTerrainAssemblyAppDirectory(string assemblyDirectory, string buildWorkspaceRoot)
    {
        if (!string.IsNullOrWhiteSpace(buildWorkspaceRoot))
        {
            string normalizedBuildWorkspaceRoot = Path.GetFullPath(buildWorkspaceRoot);
            if (LooksLikeWorkspaceRoot(normalizedBuildWorkspaceRoot))
                return normalizedBuildWorkspaceRoot;

            if (IsDirectGameRoot(normalizedBuildWorkspaceRoot))
                return normalizedBuildWorkspaceRoot;
        }

        return assemblyDirectory;
    }

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

    private static string TerrainBuildWorkspaceRoot
    {
        get
        {
            foreach (AssemblyMetadataAttribute attribute in typeof(GameResourceRootLocator).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attribute.Key, TerrainWorkspaceRootMetadataKey, StringComparison.Ordinal))
                    return attribute.Value ?? string.Empty;
            }

            return string.Empty;
        }
    }
}
