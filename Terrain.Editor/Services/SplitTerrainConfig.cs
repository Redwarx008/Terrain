#nullable enable
using Stride.Core.Mathematics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Terrain.Editor.Services;

/// <summary>
/// Configuration for editor terrain heightmap slicing.
/// Large terrains stay logically single, but their height textures are split into slices that respect
/// the GPU maximum texture size and stay aligned to BaseChunkSize cell boundaries.
/// </summary>
public sealed class SplitTerrainConfig
{
    public const int MaxTextureSize = 16384;
    public const int DefaultBaseChunkSize = 32;

    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int BaseChunkSize { get; init; }
    public int SliceCountX { get; init; }
    public int SliceCountZ { get; init; }
    public ReadOnlyCollection<SplitTerrainSliceInfo> Slices { get; init; } = Array.Empty<SplitTerrainSliceInfo>().AsReadOnly();

    public int TotalSliceCount => Slices.Count;
    public bool IsSliced => Slices.Count > 1;
    public int MaxSliceCount => Math.Max(SliceCountX, SliceCountZ);

    public static SplitTerrainConfig Compute(int sourceWidth, int sourceHeight, int baseChunkSize = DefaultBaseChunkSize)
    {
        if (sourceWidth <= 1)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 1)
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (baseChunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseChunkSize));

        var xSegments = ComputeAxisSegments(sourceWidth, baseChunkSize);
        var zSegments = ComputeAxisSegments(sourceHeight, baseChunkSize);
        var slices = new List<SplitTerrainSliceInfo>(xSegments.Count * zSegments.Count);

        int index = 0;
        for (int sliceZ = 0; sliceZ < zSegments.Count; sliceZ++)
        {
            var z = zSegments[sliceZ];
            for (int sliceX = 0; sliceX < xSegments.Count; sliceX++)
            {
                var x = xSegments[sliceX];
                slices.Add(new SplitTerrainSliceInfo(
                    Index: index++,
                    SliceX: sliceX,
                    SliceZ: sliceZ,
                    StartSampleX: x.StartSample,
                    StartSampleZ: z.StartSample,
                    Width: x.SampleCount,
                    Height: z.SampleCount));
            }
        }

        return new SplitTerrainConfig
        {
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            BaseChunkSize = baseChunkSize,
            SliceCountX = xSegments.Count,
            SliceCountZ = zSegments.Count,
            Slices = new ReadOnlyCollection<SplitTerrainSliceInfo>(slices),
        };
    }

    public bool TryGetOwningSliceForNode(int originSampleX, int originSampleZ, int sizeInSamples, out SplitTerrainSliceInfo slice)
    {
        int endSampleX = originSampleX + sizeInSamples;
        int endSampleZ = originSampleZ + sizeInSamples;

        foreach (var candidate in Slices)
        {
            if (originSampleX < candidate.StartSampleX || originSampleZ < candidate.StartSampleZ)
                continue;

            if (endSampleX > candidate.EndSampleX || endSampleZ > candidate.EndSampleZ)
                continue;

            slice = candidate;
            return true;
        }

        slice = default;
        return false;
    }

    public static bool RequiresSplit(int width, int height)
    {
        return width > MaxTextureSize || height > MaxTextureSize;
    }

    private static List<AxisSegment> ComputeAxisSegments(int sampleCount, int baseChunkSize)
    {
        int totalCells = sampleCount - 1;
        int maxCellsPerSlice = Math.Max(baseChunkSize, ((MaxTextureSize - 1) / baseChunkSize) * baseChunkSize);
        var segments = new List<AxisSegment>();

        int startCell = 0;
        while (startCell < totalCells)
        {
            int remainingCells = totalCells - startCell;
            int cellCount = Math.Min(maxCellsPerSlice, remainingCells);

            // Non-terminal slices must end on a BaseChunk boundary so that leaf chunks never straddle slices.
            if (remainingCells > maxCellsPerSlice)
            {
                cellCount = maxCellsPerSlice;
            }

            segments.Add(new AxisSegment(startCell, cellCount + 1));
            startCell += cellCount;
        }

        if (segments.Count == 0)
        {
            segments.Add(new AxisSegment(0, sampleCount));
        }

        return segments;
    }

    private readonly record struct AxisSegment(int StartSample, int SampleCount);
}

public readonly record struct SplitTerrainSliceInfo(
    int Index,
    int SliceX,
    int SliceZ,
    int StartSampleX,
    int StartSampleZ,
    int Width,
    int Height)
{
    public int EndSampleX => StartSampleX + Width - 1;
    public int EndSampleZ => StartSampleZ + Height - 1;
    public int CellWidth => Width - 1;
    public int CellHeight => Height - 1;
    public Vector3 WorldOffset => new(StartSampleX, 0.0f, StartSampleZ);
}
