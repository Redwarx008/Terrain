#nullable enable
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Editor.Rendering;

/// <summary>
/// Global LOD map shared across all terrain entities.
/// Per CONTEXT.md: All entities share the same global LOD map for seamless chunk boundaries.
/// </summary>
public sealed class EditorGlobalLodMap : IDisposable
{
    private readonly GraphicsDevice graphicsDevice;
    private Texture? lodMapTexture;
    private Buffer? lodLookupBuffer;
    private Buffer? lodLookupLayoutBuffer;

    /// <summary>
    /// The global LOD map texture covering all terrain entities.
    /// </summary>
    public Texture? LodMapTexture => lodMapTexture;

    /// <summary>
    /// Global LOD lookup buffer for all entities.
    /// </summary>
    public Buffer? LodLookupBuffer => lodLookupBuffer;

    /// <summary>
    /// Layout buffer describing LOD hierarchy structure.
    /// </summary>
    public Buffer? LodLookupLayoutBuffer => lodLookupLayoutBuffer;

    public EditorGlobalLodMap(GraphicsDevice graphicsDevice)
    {
        this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    }

    /// <summary>
    /// Rebuilds the global LOD map from all terrain entities.
    /// Called after terrain load or when LOD selection changes.
    /// </summary>
    public void RebuildFromEntities(IEnumerable<EditorTerrainEntity> entities)
    {
        // Compute combined dimensions
        int totalWidth = 0;
        int totalHeight = 0;
        int maxLod = 0;

        foreach (var entity in entities)
        {
            // Expand bounds to cover all entities
            totalWidth = Math.Max(totalWidth, (int)entity.WorldOffset.X + entity.HeightmapWidth);
            totalHeight = Math.Max(totalHeight, (int)entity.WorldOffset.Z + entity.HeightmapHeight);
            maxLod = Math.Max(maxLod, entity.MaxLod);
        }

        if (totalWidth == 0 || totalHeight == 0)
        {
            // No entities, nothing to build
            return;
        }

        // Create or resize LOD map texture
        // LOD map resolution = (totalWidth / baseChunkSize) x (totalHeight / baseChunkSize)
        int baseChunkSize = 32;  // Should match EditorTerrainEntity.BaseChunkSize
        int lodMapWidth = Math.Max(1, (totalWidth + baseChunkSize - 1) / baseChunkSize);
        int lodMapHeight = Math.Max(1, (totalHeight + baseChunkSize - 1) / baseChunkSize);

        if (lodMapTexture == null ||
            lodMapTexture.Width != lodMapWidth ||
            lodMapTexture.Height != lodMapHeight)
        {
            lodMapTexture?.Dispose();
            lodMapTexture = Texture.New2D(
                graphicsDevice,
                lodMapWidth,
                lodMapHeight,
                1,
                PixelFormat.R8_UInt,
                TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
        }

        // Note: For proper LOD map building, we would need to:
        // 1. Combine MinMaxErrorMaps from all entities
        // 2. Run the TerrainBuildLodMap compute shader
        // This is a placeholder for the full implementation
        // The existing EditorTerrainRenderFeature handles per-entity LOD selection
    }

    /// <summary>
    /// Dispatches compute shader to build LOD map from lookup buffers.
    /// Uses TerrainBuildLodMap.sdsl shader.
    /// </summary>
    public void DispatchBuildLodMap(CommandList commandList, int instanceCount)
    {
        // This would dispatch the TerrainBuildLodMap compute shader
        // For now, the per-entity LOD map in EditorTerrainRenderFeature handles this
    }

    public void Dispose()
    {
        lodMapTexture?.Dispose();
        lodLookupBuffer?.Dispose();
        lodLookupLayoutBuffer?.Dispose();
    }
}
