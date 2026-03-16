#nullable enable

using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

public sealed class TerrainRenderObject : RenderMesh
{
    public Texture? HeightTextureAsset;
    public Buffer? ChunkBufferAsset;
}
