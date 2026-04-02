#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Annotations;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Terrain.Editor.Rendering.Materials;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Processor that manages EditorTerrainEntity rendering.
/// Creates and registers EditorTerrainRenderObject instances with the visibility group.
/// </summary>
public sealed class EditorTerrainProcessor : EntityProcessor<EditorTerrainComponent, EditorTerrainRenderObject>, IEntityComponentRenderProcessor
{
    private const float DiffuseWorldRepeatSize = 8.0f;
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override EditorTerrainRenderObject GenerateComponentData([NotNull] Entity entity, [NotNull] EditorTerrainComponent component)
    {
        return new EditorTerrainRenderObject
        {
            Source = component,
        };
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

        // VisibilityGroup is set by the rendering system after the processor is registered.
        // If it's null, the render system hasn't initialized yet - skip this update.
        if (VisibilityGroup == null)
        {
            return;
        }

        // Initialize render object from entity if needed
        if (renderObject.TerrainEntity == null)
        {
            renderObject.InitializeFromEntity(graphicsDevice, entity);
        }

        // Ensure material is set up
        if (!EnsureMaterial(graphicsDevice, component, renderObject))
        {
            renderObject.Enabled = false;
            return;
        }

        // Update material parameters
        UpdateMaterialParameters(component, entity, renderObject, graphicsDevice);

        // Update render object state
        renderObject.Enabled = component.Enabled;
        renderObject.RenderGroup = RenderGroup.Group0;
        renderObject.World = Matrix.Translation(entity.WorldOffset);
        renderObject.BoundingBox = (BoundingBoxExt)entity.Bounds;
        renderObject.IsScalingNegative = false;
        renderObject.IsShadowCaster = false;

        // Register with visibility group if not already registered
        if (!component.IsRegisteredWithVisibilityGroup)
        {
            VisibilityGroup.RenderObjects.Add(renderObject);
            component.IsRegisteredWithVisibilityGroup = true;
        }
    }

    private bool EnsureMaterial(GraphicsDevice graphicsDevice, EditorTerrainComponent component, EditorTerrainRenderObject renderObject)
    {
        // MaterialPass is required for Stride's rendering pipeline
        if (renderObject.MaterialPass != null)
        {
            return true;
        }

        // Create terrain material using editor-specific shader features
        var descriptor = new MaterialDescriptor();
        descriptor.Attributes.Diffuse = new MaterialEditorTerrainDiffuseFeature();
        descriptor.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
        descriptor.Attributes.Displacement = new MaterialEditorTerrainDisplacementFeature();
        descriptor.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.12f));
        descriptor.Attributes.Specular = new MaterialMetalnessMapFeature(new ComputeFloat(0.0f));
        descriptor.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();

        var material = Material.New(graphicsDevice, descriptor);
        renderObject.MaterialPass = material.Passes[0];
        return true;
    }

    private void UpdateMaterialParameters(EditorTerrainComponent component, EditorTerrainEntity entity, EditorTerrainRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        var materialPass = renderObject.MaterialPass;
        if (materialPass == null)
        {
            return;
        }

        Debug.Assert(renderObject.HeightmapTexture != null);
        Debug.Assert(renderObject.ChunkNodeBuffer != null);

        var parameters = materialPass.Parameters;
        var dimensionsInSamples = new Vector2(entity.HeightmapWidth - 1, entity.HeightmapHeight - 1);

        // Set heightmap parameters (EditorTerrainHeightParameters)
        parameters.Set(EditorTerrainHeightParametersKeys.HeightmapTexture, renderObject.HeightmapTexture!);
        parameters.Set(EditorTerrainHeightParametersKeys.HeightScale, entity.HeightScale);
        parameters.Set(EditorTerrainHeightParametersKeys.BaseChunkSize, entity.BaseChunkSize);

        // Set dimension parameters (shared between displacement and diffuse shaders)
        parameters.Set(EditorTerrainDisplacementKeys.HeightmapDimensionsInSamples, dimensionsInSamples);
        parameters.Set(EditorTerrainDiffuseKeys.HeightmapDimensionsInSamples, dimensionsInSamples);

        // Set displacement parameters (EditorTerrainDisplacement)
        parameters.Set(EditorTerrainDisplacementKeys.InstanceBuffer, renderObject.ChunkNodeBuffer!);

        // Set diffuse parameters (EditorTerrainDiffuse)
        var defaultTexture = component.DefaultDiffuseTexture;
        if (defaultTexture == null)
        {
            Log.Warning("Editor terrain component is missing DefaultDiffuseTexture.");
            return;
        }

        parameters.Set(EditorTerrainDiffuseKeys.DefaultDiffuseTexture, defaultTexture);
        parameters.Set(EditorTerrainDiffuseKeys.TerrainDiffuseRepeatSampler, graphicsDevice.SamplerStates.LinearWrap);
        parameters.Set(EditorTerrainDiffuseKeys.DiffuseWorldRepeatSize, DiffuseWorldRepeatSize);
        parameters.Set(EditorTerrainDiffuseKeys.BaseColor, new Color4(1.0f, 1.0f, 1.0f, 1.0f));
    }
}

/// <summary>
/// Component that wraps an EditorTerrainEntity for the Stride entity system.
/// Attach this to an entity to make it renderable through EditorTerrainRenderFeature.
/// </summary>
[DataContract("EditorTerrainComponent")]
[DefaultEntityComponentRenderer(typeof(EditorTerrainProcessor))]
public sealed class EditorTerrainComponent : EntityComponent
{
    /// <summary>
    /// The terrain entity to render.
    /// </summary>
    public EditorTerrainEntity? TerrainEntity { get; set; }

    /// <summary>
    /// Diffuse texture used by the editor terrain material.
    /// </summary>
    public Texture? DefaultDiffuseTexture { get; set; }

    /// <summary>
    /// Whether the terrain is enabled for rendering.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether this component has been registered with the visibility group.
    /// </summary>
    internal bool IsRegisteredWithVisibilityGroup { get; set; }
}
