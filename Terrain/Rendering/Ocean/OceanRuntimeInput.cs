#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Rendering.Ocean;

public readonly record struct OceanRuntimeInput(float SeaLevel, Vector2 MapWorldSize);
