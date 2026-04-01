#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Processor that manages EditorTerrainEntity rendering.
/// Creates and registers EditorTerrainRenderObject instances with the visibility group.
/// </summary>
public sealed class EditorTerrainProcessor : EntityProcessor<EditorTerrainComponent, EditorTerrainRenderObject>, IEntityComponentRenderProcessor
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override EditorTerrainRenderObject GenerateComponentData([NotNull] Entity entity, [NotNull] EditorTerrainComponent component)
    {
        return new EditorTerrainRenderObject();
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] EditorTerrainComponent component, [NotNull] EditorTerrainRenderObject renderObject)
    {
        if (component.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Remove(renderObject);
            component.IsRegisteredWithVisibilityGroup = false;
        }

        renderObject.Dispose();
        base.OnEntityComponentRemoved(entity, component, renderObject);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()?.GraphicsDevice;
        if (graphicsDevice == null)
        {
            return;
        }

        foreach (var pair in ComponentDatas)
        {
            UpdateRenderObject(pair.Key, pair.Value, graphicsDevice);
        }
    }

    private void UpdateRenderObject(EditorTerrainComponent component, EditorTerrainRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        var entity = component.TerrainEntity;
        if (entity == null)
        {
            renderObject.Enabled = false;
            return;
        }

        // Initialize render object from entity if needed
        if (renderObject.Source == null)
        {
            renderObject.InitializeFromEntity(graphicsDevice, entity);
        }

        // Update render object state
        renderObject.Enabled = component.Enabled;
        renderObject.World = Matrix.Translation(entity.WorldOffset);
        renderObject.BoundingBox = (BoundingBoxExt)entity.Bounds;

        // Register with visibility group if not already registered
        if (!component.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Add(renderObject);
            component.IsRegisteredWithVisibilityGroup = true;
        }
    }
}

/// <summary>
/// Component that wraps an EditorTerrainEntity for the Stride entity system.
/// Attach this to an entity to make it renderable through EditorTerrainRenderFeature.
/// </summary>
public sealed class EditorTerrainComponent : EntityComponent
{
    /// <summary>
    /// The terrain entity to render.
    /// </summary>
    public EditorTerrainEntity? TerrainEntity { get; set; }

    /// <summary>
    /// Whether the terrain is enabled for rendering.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether this component has been registered with the visibility group.
    /// </summary>
    internal bool IsRegisteredWithVisibilityGroup { get; set; }
}
