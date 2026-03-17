#nullable enable

using System.ComponentModel;
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
    [DataMember(10)]
    public string HeightmapPath { get; set; } = "Resources/terrain_heightmap.png";

    [DataMember(20)]
    public float HeightScale { get; set; } = 24.0f;

    [DataMember(30)]
    public int BaseChunkSize { get; set; } = 32;

    [DataMember(40)]
    public float MaxScreenSpaceErrorPixels { get; set; } = 8.0f;

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

    public const int MaxInstanceCount = 200;

    [DataMemberIgnore]
    internal readonly Int4[] InstanceData = new Int4[MaxInstanceCount];

    [DataMemberIgnore]
    internal TerrainMinMaxErrorMap[]? MinMaxErrorMaps;

    [DataMemberIgnore]
    internal int HeightmapWidth;

    [DataMemberIgnore]
    internal int HeightmapHeight;

    [DataMemberIgnore]
    internal int MaxLod;

    [DataMemberIgnore]
    internal float MinHeight;

    [DataMemberIgnore]
    internal float MaxHeight;

    [DataMemberIgnore]
    internal bool IsInitialized;

    [DataMemberIgnore]
    internal string? LoadedPath;

    [DataMemberIgnore]
    internal int LoadedBaseChunkSize;

    [DataMemberIgnore]
    internal Texture? LoadedDiffuseTexture;

    [DataMemberIgnore]
    internal bool IsRegisteredWithVisibilityGroup;
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
}
