#nullable enable

using System.Linq;
using Stride.Rendering.Compositing;
using Terrain.Editor.Models;
using Terrain.Editor.Rendering.River;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Applies editor river debug rendering modes to the component-backed river render feature.
/// </summary>
public sealed class RiverWireframeModeController
{
    public void Apply(SceneViewMode mode, GraphicsCompositor graphicsCompositor)
    {
        var riverRenderFeature = graphicsCompositor.RenderFeatures.OfType<RiverRenderFeature>().FirstOrDefault();
        if (riverRenderFeature == null)
        {
            return;
        }

        riverRenderFeature.DebugMode = mode == SceneViewMode.Wireframe
            ? RiverRenderDebugMode.Wireframe
            : RiverRenderDebugMode.Normal;
    }
}
