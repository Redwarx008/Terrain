#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain;

public sealed class TerrainRuntimeData : IDisposable
{
    public TerrainRenderObject? RenderObject;
    public Material? RuntimeMaterial;
    public Texture? HeightTexture;
    public Buffer? ChunkBuffer;
    public Buffer? PatchVertexBuffer;
    public Buffer? PatchIndexBuffer;
    public MeshDraw? PatchDraw;

    public TerrainMinMaxErrorMap[]? MinMaxErrorMaps;
    public int HeightmapWidth;
    public int HeightmapHeight;
    public int MaxLod;
    public float MinHeight;
    public float MaxHeight;

    public readonly List<TerrainSelectedChunk> SelectedChunks = new();

    public bool IsInitialized;
    public string? LoadedPath;
    public int LoadedBaseChunkSize;
    public Texture? LoadedDiffuseTexture;

    public void Dispose()
    {
        RenderObject = null;

        RuntimeMaterial = null;

        ChunkBuffer?.Dispose();
        ChunkBuffer = null;

        HeightTexture?.Dispose();
        HeightTexture = null;

        MinMaxErrorMaps = null;
        SelectedChunks.Clear();

        PatchIndexBuffer?.Dispose();
        PatchIndexBuffer = null;

        PatchVertexBuffer?.Dispose();
        PatchVertexBuffer = null;

        PatchDraw = null;
        LoadedDiffuseTexture = null;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct TerrainPatchVertex
{
    public Vector3 Position;

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector3>());
}

public struct TerrainSelectedChunk
{
    public int ChunkX;
    public int ChunkY;
    public int LodLevel;
    public float MinHeight;
    public float MaxHeight;
    public BoundingBox WorldBounds;
}

public sealed class TerrainMinMaxErrorMap
{
    private readonly float[] data;

    public TerrainMinMaxErrorMap(int width, int height)
    {
        Width = width;
        Height = height;
        data = new float[width * height * 3];
    }

    public int Width { get; }
    public int Height { get; }

    public void Set(int x, int y, float min, float max, float error)
    {
        int index = (x + y * Width) * 3;
        data[index] = min;
        data[index + 1] = max;
        data[index + 2] = error;
    }

    public void Get(int x, int y, out float min, out float max, out float error)
    {
        int index = (x + y * Width) * 3;
        min = data[index];
        max = data[index + 1];
        error = data[index + 2];
    }

    public void GetSubNodesExist(int parentX, int parentY, out bool subTLExist, out bool subTRExist, out bool subBLExist, out bool subBRExist)
    {
        int x = parentX * 2;
        int y = parentY * 2;
        subTLExist = x < Width && y < Height;
        subTRExist = x + 1 < Width && y < Height;
        subBLExist = x < Width && y + 1 < Height;
        subBRExist = x + 1 < Width && y + 1 < Height;
    }
}
