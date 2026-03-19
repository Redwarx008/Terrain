#nullable enable

using Stride.Graphics;
using Stride.Rendering;

namespace Terrain;

public static partial class MaterialTerrainDiffuseKeys
{
    public static readonly ObjectParameterKey<SamplerState> TerrainDiffuseRepeatSampler = ParameterKeys.NewObject<SamplerState>();
}
