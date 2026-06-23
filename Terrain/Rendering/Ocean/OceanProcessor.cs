#nullable enable

using Stride.Core.Annotations;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Rendering.Ocean;

public sealed class OceanProcessor : EntityProcessor<OceanComponent, OceanRenderObject>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override OceanRenderObject GenerateComponentData([NotNull] Entity entity, [NotNull] OceanComponent component)
    {
        return new OceanRenderObject
        {
            Source = component,
            RenderGroup = OceanRenderGroups.OceanRenderGroup,
        };
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] OceanComponent component, [NotNull] OceanRenderObject renderObject)
    {
        RemoveFromVisibilityGroup(renderObject);
        renderObject.Dispose();
        base.OnEntityComponentRemoved(entity, component, renderObject);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()?.GraphicsDevice;
        if (graphicsDevice == null || VisibilityGroup == null)
        {
            return;
        }

        foreach (var pair in ComponentDatas)
        {
            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private void UpdateRenderObject(Entity entity, OceanComponent component, OceanRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        if (component.RuntimeInput is not { } input)
        {
            renderObject.Enabled = false;
            return;
        }

        if (!renderObject.Matches(input) || renderObject.MeshDraw == null)
        {
            renderObject.Rebuild(graphicsDevice, input);
        }

        if (!renderObject.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Add(renderObject);
            renderObject.IsRegisteredWithVisibilityGroup = true;
        }

        entity.Transform.UpdateWorldMatrix();
        renderObject.Enabled = component.Enabled && component.Visible;
        renderObject.RenderGroup = OceanRenderGroups.OceanRenderGroup;
        renderObject.World = entity.Transform.WorldMatrix;
    }

    private void RemoveFromVisibilityGroup(OceanRenderObject renderObject)
    {
        if (!renderObject.IsRegisteredWithVisibilityGroup)
            return;

        VisibilityGroup?.RenderObjects.Remove(renderObject);
        renderObject.IsRegisteredWithVisibilityGroup = false;
    }
}
