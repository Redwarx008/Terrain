#nullable enable

using System;
using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace Terrain.Rendering.Ocean;

[DataContract("OceanComponent")]
[DefaultEntityComponentRenderer(typeof(OceanProcessor))]
public sealed class OceanComponent : ActivableEntityComponent
{
    [DataMember(10)]
    public bool Visible { get; set; } = true;

    [DataMember(20)]
    public OceanMaterialSettings Material { get; set; } = new();

    [DataMemberIgnore]
    public OceanRuntimeInput? RuntimeInput { get; private set; }

    public void ApplyRuntimeInput(OceanRuntimeInput input)
    {
        if (input.MapWorldSize.X <= 0.0f || input.MapWorldSize.Y <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(input), "Ocean map world size must be positive.");

        RuntimeInput = input;
    }

    public void ClearRuntimeInput()
    {
        RuntimeInput = null;
    }
}
