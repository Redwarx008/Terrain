#nullable enable

using System;
using System.Diagnostics;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.ComputeEffect;
using Terrain.Editor.Services;

namespace Terrain.Editor.Rendering;

internal sealed class EditorTerrainSplatMapComputeDispatcher : IDisposable
{
    private const int ThreadCountX = 8;
    private const int ThreadCountY = 8;
    private static readonly ProfilingKey BuildSplatMapKey = new("EditorTerrain.BuildSplatMap");

    private ComputeEffectShader? buildSplatMapEffect;

    public void Initialize(RenderContext renderContext)
    {
        buildSplatMapEffect ??= new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "EditorTerrainBuildSplatMap",
            ThreadNumbers = new Int3(ThreadCountX, ThreadCountY, 1),
        };
    }

    public void Dispatch(RenderDrawContext drawContext, EditorTerrainRenderObject renderObject)
    {
        Debug.Assert(buildSplatMapEffect != null);

        EditorTerrainEntity? entity = renderObject.TerrainEntity;
        if (entity == null || !entity.HasDirtyBiomeSplatMap)
            return;
        if (entity.BiomeMaskTexture == null || entity.BiomeBuffer == null || entity.LayerBuffer == null || entity.ModifierBuffer == null)
            return;

        CommandList commandList = drawContext.CommandList;

        commandList.ResourceBarrierTransition(entity.BiomeMaskTexture, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(entity.BiomeBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(entity.LayerBuffer, GraphicsResourceState.NonPixelShaderResource);
        commandList.ResourceBarrierTransition(entity.ModifierBuffer, GraphicsResourceState.NonPixelShaderResource);

        for (int i = 0; i < entity.Slices.Count; i++)
        {
            EditorTerrainSlice slice = entity.Slices[i];
            Texture? outputIndexTexture = entity.DetailIndexMapTextures[i];
            Texture? outputWeightTexture = entity.DetailWeightMapTextures[i];
            if (!slice.BiomeSplatDirty || outputIndexTexture == null || outputWeightTexture == null)
                continue;

            int groupCountX = (outputIndexTexture.Width + ThreadCountX - 1) / ThreadCountX;
            int groupCountY = (outputIndexTexture.Height + ThreadCountY - 1) / ThreadCountY;

            commandList.ResourceBarrierTransition(outputIndexTexture, GraphicsResourceState.UnorderedAccess);
            commandList.ResourceBarrierTransition(outputWeightTexture, GraphicsResourceState.UnorderedAccess);

            using (Profiler.Begin(BuildSplatMapKey))
            {
                buildSplatMapEffect!.ThreadGroupCounts = new Int3(groupCountX, groupCountY, 1);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice0, renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice1, renderObject.HeightmapSliceTextures[1] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice2, renderObject.HeightmapSliceTextures[2] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice3, renderObject.HeightmapSliceTextures[3] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice4, renderObject.HeightmapSliceTextures[4] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice5, renderObject.HeightmapSliceTextures[5] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice6, renderObject.HeightmapSliceTextures[6] ?? renderObject.HeightmapSliceTextures[0]!);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlice7, renderObject.HeightmapSliceTextures[7] ?? renderObject.HeightmapSliceTextures[0]!);

                SetSliceBounds(buildSplatMapEffect.Parameters, entity);

                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightScale, entity.HeightScale);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.BaseChunkSize, entity.BaseChunkSize);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.SliceCount, entity.Slices.Count);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSlicePadding, 0);

                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.BiomeMaskTexture, entity.BiomeMaskTexture);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.Biomes, entity.BiomeBuffer);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.Layers, entity.LayerBuffer);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.Modifiers, entity.ModifierBuffer);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.OutputIndexMap, outputIndexTexture);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.OutputWeightMap, outputWeightTexture);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.CurrentSliceIndex, i);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.OutputWidth, outputIndexTexture.Width);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.OutputHeight, outputIndexTexture.Height);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.BiomeMaskWidth, entity.BiomeMaskTexture.Width);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.BiomeMaskHeight, entity.BiomeMaskTexture.Height);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.BiomeCount, entity.BiomeCount);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.LayerCount, entity.LayerCount);
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.ModifierCount, entity.ModifierCount);

                // Set TextureMask resource placeholder (white texture as default)
                // A proper implementation would load and bind the actual texture from BiomeModifier.TextureMaskPath
                // For now, we use the entity's existing texture or a fallback white texture
                if (entity.TextureMaskResource != null)
                {
                    buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.TextureMaskResource, entity.TextureMaskResource);
                    buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.TextureMaskSampler, drawContext.CommandList.GraphicsDevice.SamplerStates.LinearWrap);
                }

                // Heatmap preview parameters
                buildSplatMapEffect.Parameters.Set(Editor.EditorTerrainBuildSplatMapKeys.HeatmapLayerIndex, EditorState.Instance.SelectedRuleIndex);
                buildSplatMapEffect.Parameters.Set(
                    Editor.EditorTerrainBuildSplatMapKeys.HeatmapEnabled,
                    EditorState.Instance.CurrentDebugViewMode == SceneDebugViewMode.LayerHeatmap ? 1 : 0);

                buildSplatMapEffect.Draw(drawContext);
            }

            commandList.ResourceBarrierTransition(outputIndexTexture, GraphicsResourceState.PixelShaderResource);
            commandList.ResourceBarrierTransition(outputWeightTexture, GraphicsResourceState.PixelShaderResource);
            entity.ClearBiomeSplatDirty(i);
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

        EditorTerrainSlice slice = entity.Slices[sliceIndex];
        // HeightmapSliceBounds* always uses full-resolution heightmap space.
        // The shader converts to splatmap space later via GetIndexMapSliceBounds().
        return new Int4(slice.StartSampleX, slice.StartSampleZ, slice.Width, slice.Height);
    }

    private static void SetSliceBounds(ParameterCollection parameters, int sliceIndex, Int4 bounds)
    {
        switch (sliceIndex)
        {
            case 0: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds0, bounds); break;
            case 1: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds1, bounds); break;
            case 2: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds2, bounds); break;
            case 3: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds3, bounds); break;
            case 4: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds4, bounds); break;
            case 5: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds5, bounds); break;
            case 6: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds6, bounds); break;
            case 7: parameters.Set(Editor.EditorTerrainHeightParametersKeys.HeightmapSliceBounds7, bounds); break;
        }
    }

    public void Dispose()
    {
        buildSplatMapEffect?.Dispose();
        buildSplatMapEffect = null;
    }
}
