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

        // Get CommandList for GPU data upload
        var graphicsContext = Services.GetService<GraphicsContext>();
        var commandList = graphicsContext?.CommandList;

        foreach (var pair in ComponentDatas)
        {
            var entity = pair.Key.TerrainEntity;

            // Sync dirty height data to GPU
            if (entity != null && commandList != null)
            {
                entity.SyncToGpu(commandList);
            }

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

        Debug.Assert(renderObject.ChunkNodeBuffer != null);
        Debug.Assert(renderObject.HeightmapSliceTextures[0] != null);

        var parameters = materialPass.Parameters;
        var dimensionsInSamples = new Vector2(entity.HeightmapWidth - 1, entity.HeightmapHeight - 1);

        SetSliceTextures(parameters, entity, renderObject);
        SetSliceBounds(parameters, entity);
        parameters.Set(EditorTerrainHeightParametersKeys.SliceCount, entity.Slices.Count);
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

    private static void SetSliceTextures(ParameterCollection parameters, EditorTerrainEntity entity, EditorTerrainRenderObject renderObject)
    {
        var fallback = renderObject.HeightmapSliceTextures[0] ?? entity.Slices[0].Texture;
        SetSliceTexture(parameters, 0, renderObject.HeightmapSliceTextures[0] ?? fallback);
        SetSliceTexture(parameters, 1, renderObject.HeightmapSliceTextures[1] ?? fallback);
        SetSliceTexture(parameters, 2, renderObject.HeightmapSliceTextures[2] ?? fallback);
        SetSliceTexture(parameters, 3, renderObject.HeightmapSliceTextures[3] ?? fallback);
        SetSliceTexture(parameters, 4, renderObject.HeightmapSliceTextures[4] ?? fallback);
        SetSliceTexture(parameters, 5, renderObject.HeightmapSliceTextures[5] ?? fallback);
        SetSliceTexture(parameters, 6, renderObject.HeightmapSliceTextures[6] ?? fallback);
        SetSliceTexture(parameters, 7, renderObject.HeightmapSliceTextures[7] ?? fallback);
    }

    private static void SetSliceTexture(ParameterCollection parameters, int sliceIndex, Texture texture)
    {
        switch (sliceIndex)
        {
            case 0: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice0, texture); break;
            case 1: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice1, texture); break;
            case 2: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice2, texture); break;
            case 3: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice3, texture); break;
            case 4: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice4, texture); break;
            case 5: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice5, texture); break;
            case 6: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice6, texture); break;
            case 7: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSlice7, texture); break;
        }
    }

    private static void SetSliceBounds(ParameterCollection parameters, EditorTerrainEntity entity)
    {
        SetSliceBounds(parameters, 0, GetSliceBounds(entity, 0));
        SetSliceBounds(parameters, 1, GetSliceBounds(entity, 1));
        SetSliceBounds(parameters, 2, GetSliceBounds(entity, 2));
        SetSliceBounds(parameters, 3, GetSliceBounds(entity, 3));
        SetSliceBounds(parameters, 4, GetSliceBounds(entity, 4));
        SetSliceBounds(parameters, 5, GetSliceBounds(entity, 5));
        SetSliceBounds(parameters, 6, GetSliceBounds(entity, 6));
        SetSliceBounds(parameters, 7, GetSliceBounds(entity, 7));
    }

    private static Int4 GetSliceBounds(EditorTerrainEntity entity, int sliceIndex)
    {
        if (sliceIndex >= entity.Slices.Count)
            return new Int4(0, 0, 1, 1);

        var slice = entity.Slices[sliceIndex];
        return new Int4(slice.StartSampleX, slice.StartSampleZ, slice.Width, slice.Height);
    }

    private static void SetSliceBounds(ParameterCollection parameters, int sliceIndex, Int4 bounds)
    {
        switch (sliceIndex)
        {
            case 0: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds0, bounds); break;
            case 1: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds1, bounds); break;
            case 2: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds2, bounds); break;
            case 3: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds3, bounds); break;
            case 4: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds4, bounds); break;
            case 5: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds5, bounds); break;
            case 6: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds6, bounds); break;
            case 7: parameters.Set(EditorTerrainHeightParametersKeys.HeightmapSliceBounds7, bounds); break;
        }
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
