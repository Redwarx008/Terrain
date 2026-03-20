#nullable enable

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

[RequireComponent(typeof(TransformComponent))]
[DataContract("TerrainComponent")]
[Display("Terrain", Expand = ExpandRule.Once)]
[DefaultEntityComponentRenderer(typeof(TerrainProcessor))]
public sealed class TerrainComponent : ActivableEntityComponent
{
    internal const float HeightSampleNormalization = 1.0f / ushort.MaxValue;

    [DataMember(10)]
    public string? TerrainDataPath { get; set; }

    [DataMember(20)]
    public float HeightScale { get; set; } = 24.0f;

    [DataMember(40)]
    public float MaxScreenSpaceErrorPixels { get; set; } = 8.0f;

    [DataMember(45)]
    [DefaultValue(65536)]
    public int MaxVisibleChunkInstances { get; set; } = 65536;

    [DataMember(47)]
    [DefaultValue(1024)]
    public int MaxResidentChunks { get; set; } = 1024;

    [DataMember(49)]
    [DefaultValue(8)]
    public int MaxStreamingUploadsPerFrame { get; set; } = 8;

    [DataMember(50)]
    public Texture? DefaultDiffuseTexture { get; set; }

    [DataMember(60)]
    public Color4 BaseColor { get; set; } = Color4.White;

    [DataMember(70)]
    [DefaultValue(RenderGroup.Group0)]
    public RenderGroup RenderGroup { get; set; } = RenderGroup.Group0;

    [DataMember(80)]
    [DefaultValue(true)]
    public bool CastShadows { get; set; } = true;

    [DataMemberIgnore]
    internal TerrainChunkInstance[] InstanceData = Array.Empty<TerrainChunkInstance>();

    [DataMemberIgnore]
    internal int MaxLeafChunkCount;

    [DataMemberIgnore]
    internal int InstanceCapacity;

    [DataMemberIgnore]
    internal TerrainMinMaxErrorMap[]? MinMaxErrorMaps;

    [DataMemberIgnore]
    internal int HeightmapWidth;

    [DataMemberIgnore]
    internal int HeightmapHeight;

    [DataMemberIgnore]
    internal int MaxLod;

    [DataMemberIgnore]
    internal int BaseChunkSize = 32;

    [DataMemberIgnore]
    internal int HeightmapTileSize;

    [DataMemberIgnore]
    internal int HeightmapTilePadding;

    [DataMemberIgnore]
    internal float MinHeight;

    [DataMemberIgnore]
    internal float MaxHeight;

    [DataMemberIgnore]
    internal bool IsInitialized;

    [DataMemberIgnore]
    internal TerrainConfig LoadedConfig;

    [DataMemberIgnore]
    internal Texture? LoadedDiffuseTexture;

    [DataMemberIgnore]
    internal bool IsRegisteredWithVisibilityGroup;

    [DataMemberIgnore]
    internal TerrainStreamingManager? StreamingManager;
}

/// <summary>
/// 地形配置，用于检测需要触发重构建的配置变化。
/// </summary>
internal struct TerrainConfig : IEquatable<TerrainConfig>
{
    public string? TerrainDataPath;
    public int MaxVisibleChunkInstances;
    public int MaxResidentChunks;

    public static TerrainConfig Capture(TerrainComponent component)
    {
        return new TerrainConfig
        {
            TerrainDataPath = component.TerrainDataPath,
            MaxVisibleChunkInstances = component.MaxVisibleChunkInstances,
            MaxResidentChunks = component.MaxResidentChunks
        };
    }

    public bool Equals(TerrainConfig other)
    {
        return string.Equals(TerrainDataPath, other.TerrainDataPath, StringComparison.OrdinalIgnoreCase)
            && MaxVisibleChunkInstances == other.MaxVisibleChunkInstances
            && MaxResidentChunks == other.MaxResidentChunks;
    }

    public override bool Equals(object? obj)
        => obj is TerrainConfig other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(TerrainDataPath, MaxVisibleChunkInstances, MaxResidentChunks);

    public static bool operator ==(TerrainConfig left, TerrainConfig right)
        => left.Equals(right);

    public static bool operator !=(TerrainConfig left, TerrainConfig right)
        => !left.Equals(right);
}

internal sealed class TerrainMinMaxErrorMap
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

    public void GetGlobalMinMax(out float minHeight, out float maxHeight)
    {
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Get(x, y, out var min, out var max, out _);
                minHeight = MathF.Min(minHeight, min);
                maxHeight = MathF.Max(maxHeight, max);
            }
        }

        if (minHeight == float.MaxValue)
        {
            minHeight = 0.0f;
            maxHeight = 0.0f;
        }
    }

    public static TerrainMinMaxErrorMap ReadFrom(BinaryReader reader)
    {
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        var map = new TerrainMinMaxErrorMap(width, height);

        reader.BaseStream.ReadExactly(map.GetByteView());
        return map;
    }

    internal Span<byte> GetByteView()
        => MemoryMarshal.AsBytes(data.AsSpan());
}
