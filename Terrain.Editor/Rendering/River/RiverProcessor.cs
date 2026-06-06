#nullable enable

using System.Collections.Generic;
using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverProcessor : EntityProcessor<RiverComponent, RiverProcessor.RenderData>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override RenderData GenerateComponentData([NotNull] Entity entity, [NotNull] RiverComponent component)
    {
        return new RenderData(component);
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] RiverComponent component, [NotNull] RenderData data)
    {
        ReleaseRenderObjects(data);
        base.OnEntityComponentRemoved(entity, component, data);
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
            UpdateRenderObjects(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private void UpdateRenderObjects(Entity entity, RiverComponent component, RenderData data, GraphicsDevice graphicsDevice)
    {
        if (data.SynchronizedVersion != component.Version)
        {
            RebuildRenderObjects(component, data, graphicsDevice);
        }

        entity.Transform.UpdateWorldMatrix();
        bool enabled = component.Enabled && component.Settings.Visible;
        foreach (var renderObject in data.RenderObjects)
        {
            renderObject.Enabled = enabled;
            renderObject.RenderGroup = RiverRenderingService.RiverRenderGroup;
            renderObject.World = entity.Transform.WorldMatrix;
        }
    }

    private void RebuildRenderObjects(RiverComponent component, RenderData data, GraphicsDevice graphicsDevice)
    {
        ReleaseRenderObjects(data);

        var meshes = component.Meshes;
        foreach (var mesh in meshes)
        {
            if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0) continue;

            var renderObject = new RiverRenderObject
            {
                Source = component,
                RenderGroup = RiverRenderingService.RiverRenderGroup,
            };
            renderObject.Rebuild(graphicsDevice, mesh, component.Version);
            renderObject.Enabled = component.Enabled && component.Settings.Visible;
            data.RenderObjects.Add(renderObject);
            VisibilityGroup.RenderObjects.Add(renderObject);
        }

        data.SynchronizedVersion = component.Version;
    }

    private void ReleaseRenderObjects(RenderData data)
    {
        foreach (var renderObject in data.RenderObjects)
        {
            VisibilityGroup?.RenderObjects.Remove(renderObject);
            renderObject.Dispose();
        }

        data.RenderObjects.Clear();
        data.SynchronizedVersion = -1;
    }

    public sealed class RenderData
    {
        public RenderData(RiverComponent component)
        {
            Component = component;
        }

        public RiverComponent Component { get; }
        public List<RiverRenderObject> RenderObjects { get; } = new();
        public int SynchronizedVersion { get; set; } = -1;
    }
}
