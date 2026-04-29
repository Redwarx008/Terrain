#nullable enable

using Stride.Core.Annotations;
using Stride.Core.Threading;
using Stride.Engine;
using Stride.Rendering;

namespace Terrain.Editor.Rendering.Decal;

/// <summary>
/// Entity processor that synchronizes BrushDecalComponent data to BrushDecalRenderObject
/// each frame. Manages render object registration with the VisibilityGroup.
/// </summary>
internal class BrushDecalProcessor : EntityProcessor<BrushDecalComponent, BrushDecalProcessor.RenderDecalData>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    public BrushDecalProcessor()
        : base(typeof(TransformComponent))
    {
    }

    protected override RenderDecalData GenerateComponentData(Entity entity, BrushDecalComponent component)
    {
        return new RenderDecalData();
    }

    protected override bool IsAssociatedDataValid(Entity entity, BrushDecalComponent component, RenderDecalData data)
    {
        return true;
    }

    protected override void OnEntityComponentAdding(Entity entity, [NotNull] BrushDecalComponent component, [NotNull] RenderDecalData data)
    {
        data.RenderObject = new BrushDecalRenderObject();
        data.RenderObject.RenderGroup = component.RenderGroup;

        VisibilityGroup.RenderObjects.Add(data.RenderObject);
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] BrushDecalComponent component, [NotNull] RenderDecalData data)
    {
        if (data.RenderObject != null)
        {
            VisibilityGroup.RenderObjects.Remove(data.RenderObject);
            data.RenderObject.Dispose();
        }
    }

    public override void Draw(RenderContext context)
    {
        Dispatcher.ForEach(ComponentDatas, entry =>
        {
            var component = entry.Key;
            var data = entry.Value;
            data.RenderObject.Enabled = component.Enabled;

            if (component.Enabled)
            {
                UpdateRenderObject(component, data);
            }
        });
    }

    private static void UpdateRenderObject(BrushDecalComponent component, RenderDecalData data)
    {
        var renderObject = data.RenderObject;
        renderObject.Color = component.Color;
        renderObject.TextureScale = component.TextureScale;
        renderObject.RenderGroup = component.RenderGroup;
        renderObject.WorldMatrix = component.Entity.Transform.WorldMatrix;
    }

    public sealed class RenderDecalData
    {
        public BrushDecalRenderObject RenderObject = null!;
    }
}
