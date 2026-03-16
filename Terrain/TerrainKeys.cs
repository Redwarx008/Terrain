using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

public static class TerrainKeys
{
    public static readonly ObjectParameterKey<Texture> HeightTexture = ParameterKeys.NewObject<Texture>(null, nameof(HeightTexture));
    public static readonly ObjectParameterKey<Buffer> ChunkBuffer = ParameterKeys.NewObject<Buffer>(null, nameof(ChunkBuffer));
    public static readonly ObjectParameterKey<Texture> DefaultDiffuseTexture = ParameterKeys.NewObject<Texture>(null, nameof(DefaultDiffuseTexture));

    public static readonly ValueParameterKey<Vector2> HeightTextureTexelSize = ParameterKeys.NewValue(Vector2.One, nameof(HeightTextureTexelSize));
    public static readonly ValueParameterKey<Vector2> HeightmapDimensionsInSamples = ParameterKeys.NewValue(Vector2.One, nameof(HeightmapDimensionsInSamples));
    public static readonly ValueParameterKey<float> HeightScale = ParameterKeys.NewValue(1.0f, nameof(HeightScale));
    public static readonly ValueParameterKey<int> BaseChunkSize = ParameterKeys.NewValue(32, nameof(BaseChunkSize));
    public static readonly ValueParameterKey<float> DiffuseWorldRepeatSize = ParameterKeys.NewValue(8.0f, nameof(DiffuseWorldRepeatSize));
    public static readonly ValueParameterKey<Color4> BaseColor = ParameterKeys.NewValue(Color4.White, nameof(BaseColor));
}
