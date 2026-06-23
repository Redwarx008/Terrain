#nullable enable

using Stride.Core;
using Stride.Core.Mathematics;

namespace Terrain.Rendering.Ocean;

[DataContract("OceanMaterialSettings")]
public sealed class OceanMaterialSettings
{
    [DataMember(10)]
    public Color3 ShallowColor { get; set; } = new(0.08f, 0.32f, 0.42f);

    [DataMember(20)]
    public Color3 DeepColor { get; set; } = new(0.01f, 0.08f, 0.16f);

    [DataMember(30)]
    public float Roughness { get; set; } = 0.08f;

    [DataMember(40)]
    public float WaveScale { get; set; } = 1.0f;
}
