#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace Terrain.Resources;

public sealed class GameResourceResolver
{
    private readonly IReadOnlyList<GameResourceLayer> layers;

    public GameResourceResolver(IReadOnlyList<GameResourceLayer> layers)
    {
        this.layers = layers;
    }

    public ResolvedGameResource ResolveRequiredFile(string virtualPath)
    {
        string normalized = NormalizeVirtualPath(virtualPath);
        if (TryResolveExistingFile(normalized, out ResolvedGameResource resolved))
            return resolved;

        throw new FileNotFoundException($"Virtual resource was not found: {normalized}", normalized);
    }

    public ResolvedGameResource ResolveWritableTarget(string virtualPath)
    {
        string normalized = NormalizeVirtualPath(virtualPath);
        if (TryResolveExistingFile(normalized, out ResolvedGameResource resolved))
            return resolved;

        if (layers.Count == 0)
            throw new InvalidOperationException("No resource layers are configured.");

        GameResourceLayer targetLayer = layers[^1];
        string nativeRelativePath = normalized.Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.Combine(targetLayer.RootPath, nativeRelativePath);
        return new ResolvedGameResource(
            normalized,
            Path.GetFullPath(candidate),
            targetLayer.Id,
            IsWritablePath(candidate),
            false);
    }

    private bool TryResolveExistingFile(string normalizedVirtualPath, out ResolvedGameResource resolved)
    {
        string nativeRelativePath = normalizedVirtualPath.Replace('/', Path.DirectorySeparatorChar);
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            GameResourceLayer layer = layers[i];
            string candidate = Path.Combine(layer.RootPath, nativeRelativePath);
            if (!File.Exists(candidate))
                continue;

            bool hasLowerPriorityFallback = false;
            for (int lower = i - 1; lower >= 0; lower--)
            {
                string lowerCandidate = Path.Combine(layers[lower].RootPath, nativeRelativePath);
                if (File.Exists(lowerCandidate))
                {
                    hasLowerPriorityFallback = true;
                    break;
                }
            }

            resolved = new ResolvedGameResource(
                normalizedVirtualPath,
                Path.GetFullPath(candidate),
                layer.Id,
                IsWritablePath(candidate),
                hasLowerPriorityFallback);
            return true;
        }

        resolved = default;
        return false;
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            throw new InvalidDataException("Virtual path is empty.");

        string trimmed = virtualPath.Trim();
        if (IsRootedVirtualPath(trimmed))
            throw new InvalidDataException($"Rooted virtual path is not allowed: {virtualPath}");

        string normalized = trimmed.Replace('\\', '/').TrimStart('/');
        var segments = new List<string>();
        foreach (string segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
                throw new InvalidDataException($"Parent traversal is not allowed: {virtualPath}");
            segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new InvalidDataException("Virtual path is empty.");

        return string.Join("/", segments);
    }

    private static bool IsRootedVirtualPath(string virtualPath)
    {
        if (virtualPath.StartsWith("/", StringComparison.Ordinal) || virtualPath.StartsWith("\\", StringComparison.Ordinal))
            return true;

        if (virtualPath.Length >= 2 && char.IsLetter(virtualPath[0]) && virtualPath[1] == ':')
            return true;

        return false;
    }

    private static bool IsWritablePath(string path)
    {
        if (File.Exists(path))
        {
            FileAttributes fileAttributes = File.GetAttributes(path);
            return (fileAttributes & FileAttributes.ReadOnly) == 0;
        }

        string? existingDirectory = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(existingDirectory) && !Directory.Exists(existingDirectory))
            existingDirectory = Path.GetDirectoryName(existingDirectory);

        if (string.IsNullOrWhiteSpace(existingDirectory))
            return false;

        FileAttributes directoryAttributes = File.GetAttributes(existingDirectory);
        return (directoryAttributes & FileAttributes.ReadOnly) == 0;
    }
}
