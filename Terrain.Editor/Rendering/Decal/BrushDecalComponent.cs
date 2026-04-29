#nullable enable

using System.ComponentModel;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Rendering;

namespace Terrain.Editor.Rendering.Decal;

/// <summary>
/// Entity component for the brush decal overlay.
/// Stores the decal color and scale for screen-space decal rendering.
/// </summary>
[DataContract]
[DefaultEntityComponentRenderer(typeof(BrushDecalProcessor))]
public class BrushDecalComponent : ActivableEntityComponent
{
    [DataMember(10)]
    [Display("Color")]
    public Color4 Color { get; set; } = Color4.White;

    [DataMember(20)]
    [DefaultValue(1f)]
    [Display("Texture Scale")]
    public float TextureScale { get; set; } = 1f;

    [DataMember(30)]
    [Display("Render Group")]
    public RenderGroup RenderGroup { get; set; } = RenderGroup.Group0;
}