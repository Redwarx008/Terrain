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
}
