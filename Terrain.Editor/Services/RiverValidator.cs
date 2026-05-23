#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

/// <summary>
/// 河流地图像素约束验证器。
/// 检查 CK3 规范要求的拓扑合法性。
/// </summary>
public static class RiverValidator
{
    private static readonly (int Dx, int Dy)[] OrthoOffsets = [(0, -1), (1, 0), (0, 1), (-1, 0)];
    private static readonly (int Dx, int Dy)[] DiagOffsets = [(1, -1), (1, 1), (-1, 1), (-1, -1)];

    public readonly record struct ValidationError(int X, int Y, string Code, string Message);

    /// <summary>全量验证整个地图，返回所有错误</summary>
    public static List<ValidationError> ValidateAll(RiverMap map)
    {
        var errors = new List<ValidationError>();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                ValidatePixel(map, x, y, errors);
            }
        }

        // 额外：检查每个连通分量是否 ≤ 1 个 Source
        CheckSourceCount(map, errors);

        return errors;
    }

    /// <summary>增量验证单个像素及其邻居</summary>
    public static void ValidatePixel(RiverMap map, int x, int y, List<ValidationError> errors)
    {
        RiverPixelType type = map.GetType(x, y);
        if (type == RiverPixelType.Land || type == RiverPixelType.Ocean)
            return;

        int orthoCount = CountOrthogonalNeighbors(map, x, y);
        bool hasDiag = HasDiagonalOnlyConnection(map, x, y);

        switch (type)
        {
            case RiverPixelType.River:
                if (orthoCount > 2)
                    errors.Add(new ValidationError(x, y, "TooManyNeighbors",
                        $"River pixel ({x},{y}) 有 {orthoCount} 个正交邻居（允许 ≤ 2）"));
                break;

            case RiverPixelType.Source:
                if (orthoCount > 1)
                    errors.Add(new ValidationError(x, y, "SourceTooManyNeighbors",
                        $"Source pixel ({x},{y}) 有 {orthoCount} 个正交邻居（允许 ≤ 1）"));
                break;

            case RiverPixelType.Confluence:
            case RiverPixelType.Bifurcation:
                if (orthoCount > 3)
                    errors.Add(new ValidationError(x, y, "JunctionTooManyNeighbors",
                        $"Junction pixel ({x},{y}) 有 {orthoCount} 个正交邻居（允许 ≤ 3）"));
                break;
        }

        if (orthoCount == 0 && hasDiag)
            errors.Add(new ValidationError(x, y, "DiagonalOnly",
                $"River pixel ({x},{y}) 仅通过对角邻居连接"));
    }

    /// <summary>检查是否有 2 像素宽的结构</summary>
    public static bool HasTwoPixelWideParallel(RiverMap map, int x, int y)
    {
        if (map.GetType(x, y) == RiverPixelType.Land)
            return false;

        // 检查是否有并行的 River 像素（同一行相邻且同一列相邻）
        if (IsRiver(map, x + 1, y) && IsRiver(map, x, y + 1) && IsRiver(map, x + 1, y + 1))
            return true;
        if (IsRiver(map, x + 1, y) && IsRiver(map, x, y - 1) && IsRiver(map, x + 1, y - 1))
            return true;

        return false;
    }

    private static void CheckSourceCount(RiverMap map, List<ValidationError> errors)
    {
        var visited = new HashSet<(int, int)>();
        int sourceCount = 0;

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (map.GetType(x, y) != RiverPixelType.Source)
                    continue;
                visited.Clear();
                FloodFillConnected(map, x, y, visited);
                int sources = 0;
                foreach (var (px, py) in visited)
                {
                    if (map.GetType(px, py) == RiverPixelType.Source)
                        sources++;
                }
                if (sources > 1)
                {
                    sourceCount++;
                    // 只报告第一次发现
                }
            }
        }

        if (sourceCount > 0)
            errors.Add(new ValidationError(0, 0, "MultipleSources",
                $"检测到 {sourceCount} 个连通分量包含多个 Source 像素"));
    }

    private static int CountOrthogonalNeighbors(RiverMap map, int x, int y)
    {
        int count = 0;
        foreach (var (dx, dy) in OrthoOffsets)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                && map.GetType(nx, ny) != RiverPixelType.Land
                && map.GetType(nx, ny) != RiverPixelType.Ocean)
                count++;
        }
        return count;
    }

    private static bool HasDiagonalOnlyConnection(RiverMap map, int x, int y)
    {
        // 检查是否有对角邻居，但没有正交邻居
        foreach (var (dx, dy) in DiagOffsets)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                && map.GetType(nx, ny) != RiverPixelType.Land
                && map.GetType(nx, ny) != RiverPixelType.Ocean)
                return true;
        }
        return false;
    }

    private static bool IsRiver(RiverMap map, int x, int y)
    {
        if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height)
            return false;
        var t = map.GetType(x, y);
        return t == RiverPixelType.River || t == RiverPixelType.Source
            || t == RiverPixelType.Confluence || t == RiverPixelType.Bifurcation;
    }

    private static void FloodFillConnected(RiverMap map, int sx, int sy, HashSet<(int, int)> visited)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (!visited.Add((x, y)))
                continue;

            foreach (var (dx, dy) in OrthoOffsets)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx < (uint)map.Width && (uint)ny < (uint)map.Height
                    && IsRiver(map, nx, ny)
                    && !visited.Contains((nx, ny)))
                    stack.Push((nx, ny));
            }
        }
    }
}
