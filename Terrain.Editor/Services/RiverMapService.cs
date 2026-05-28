#nullable enable

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

public sealed class RiverMapService
{
    public RiverCell[,]? Cells { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<string> Errors { get; } = new();
    public int SystemCount { get; private set; }

    public bool Load(string pngPath)
    {
        Errors.Clear();
        Cells = null;

        if (!System.IO.File.Exists(pngPath))
        {
            Errors.Add($"File not found: {pngPath}");
            return false;
        }

        try
        {
            using var image = Image.Load<Rgba32>(pngPath);
            Width = image.Width;
            Height = image.Height;
            Cells = new RiverCell[Width, Height];

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    Cells[x, y] = RiverCell.FromRgba32(image[x, y]);

            return Validate();
        }
        catch (Exception ex)
        {
            Errors.Add($"Load error: {ex.Message}");
            return false;
        }
    }

    public bool Validate()
    {
        if (Cells == null) return false;
        Errors.Clear();

        int w = Width, h = Height;

        // Check orthogonal adjacency for all river pixels
        int[] dx = [0, 1, 0, -1];
        int[] dy = [-1, 0, 1, 0];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Cells[x, y].IsFilled) continue;

                int orthoCount = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && Cells[nx, ny].IsFilled)
                        orthoCount++;
                }
                if (orthoCount > 2)
                    Errors.Add($"Pixel ({x},{y}): {orthoCount} orthogonal neighbors (max 2)");
            }

        // Check each river system has exactly 1 source
        var visited = new bool[w, h];
        SystemCount = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!Cells[x, y].IsFilled || visited[x, y]) continue;

                // Flood fill to find connected component
                var component = new List<(int X, int Y)>();
                FloodFill(x, y, Cells, w, h, visited, component);
                SystemCount++;

                int sourceCount = 0;
                foreach (var (cx, cy) in component)
                    if (Cells[cx, cy].Type == RiverPixelType.Source)
                        sourceCount++;

                if (sourceCount == 0)
                    Errors.Add($"River system at ({x},{y}): no source pixel");
                else if (sourceCount > 1)
                    Errors.Add($"River system at ({x},{y}): {sourceCount} source pixels (max 1)");
            }

        return Errors.Count == 0;
    }

    private static void FloodFill(int sx, int sy, RiverCell[,] cells, int w, int h, bool[,] visited, List<(int, int)> result)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= w || y < 0 || y >= h) continue;
            if (visited[x, y] || !cells[x, y].IsFilled) continue;
            visited[x, y] = true;
            result.Add((x, y));
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }
}
