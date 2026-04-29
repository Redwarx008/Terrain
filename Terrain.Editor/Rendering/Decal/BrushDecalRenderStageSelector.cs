#nullable enable

using System.ComponentModel;
using Stride.Rendering;

namespace Terrain.Editor.Rendering.Decal;

/// <summary>
/// Render stage selector for brush decal objects.
/// Assigns decal render objects to the Transparent render stage.
/// </summary>
public class BrushDecalRenderStageSelector : RenderStageSelector
{
    [DefaultValue(RenderGroupMask.All)]
    public RenderGroupMask RenderGroup { get; set; } = RenderGroupMask.All;

    public RenderStage? RenderStage { get; set; }

    public string? EffectName { get; set; }

    public override void Process(RenderObject renderObject)
    {
        if (RenderStage != null && ((RenderGroupMask)(1U << (int)renderObject.RenderGroup) & RenderGroup) != 0)
        {
            renderObject.ActiveRenderStages[RenderStage.Index] = new ActiveRenderStage(EffectName);
        }
    }
}