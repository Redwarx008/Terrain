#nullable enable

using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Editor.Rendering;

public sealed class PathDepthBiasPipelineProcessor : PipelineProcessor
{
    public RenderGroup TargetRenderGroup { get; set; } = RenderGroup.Group1;

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (renderObject.RenderGroup != TargetRenderGroup)
            return;

        pipelineState.DepthStencilState = DepthStencilStates.None;
    }
}
