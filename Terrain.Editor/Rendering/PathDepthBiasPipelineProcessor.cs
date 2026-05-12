#nullable enable

using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Editor.Rendering;

public sealed class PathDepthBiasPipelineProcessor : PipelineProcessor
{
    public RenderGroup TargetRenderGroup { get; set; } = RenderGroup.Group1;

    public int DepthBias { get; set; } = -2000;

    public float SlopeScaleDepthBias { get; set; } = -10.0f;

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (renderObject.RenderGroup != TargetRenderGroup)
            return;

        pipelineState.DepthStencilState = DepthStencilStates.DepthRead;
        pipelineState.RasterizerState.DepthBias = DepthBias;
        pipelineState.RasterizerState.SlopeScaleDepthBias = SlopeScaleDepthBias;
    }
}
