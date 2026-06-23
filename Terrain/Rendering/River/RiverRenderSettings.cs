#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Rendering.River;

public sealed class RiverRenderSettings
{
    public bool Visible { get; set; } = true;
    public bool ShowBottom { get; set; } = true;
    public bool ShowSurface { get; set; } = true;
    public float RiverMaxVisibleCameraHeight { get; set; } = 3000.0f;

    public float TextureUvScale { get; set; } = 1.0f;
    public float FlowNormalUvScale { get; set; } = 0.4f;
    public float FlowNormalSpeed { get; set; } = 0.075f;
    public float RiverFoamFactor { get; set; } = 0.5f;
    public float NoiseScale { get; set; } = 0.25f;
    public float NoiseSpeed { get; set; } = 2.0f;
    public float FlattenMultiplier { get; set; } = 1.0f;
    public float OceanFadeRate { get; set; } = 0.8f;
    public float BankAmount { get; set; } = 0.0f;
    public float BankFade { get; set; } = 0.025f;
    public float Depth { get; set; } = 0.15f;
    public float DepthWidthPower { get; set; } = 2.0f;
    public float DepthFakeFactor { get; set; } = 2.0f;
    public int ParallaxIterations { get; set; } = 10;
    public float BottomNormalStrength { get; set; } = 1.0f;
    public float BottomEnvironmentIntensity { get; set; } = 1.0f;

    public float FlatMapLerp { get; set; } = 0.0f;

    public float WaterRefractionScale { get; set; } = 500.0f;
    public float WaterRefractionShoreMaskDepth { get; set; } = 3.0f;
    public float WaterRefractionShoreMaskSharpness { get; set; } = 1.0f;
    public float WaterRefractionFade { get; set; } = 1.0f;
    public Vector4 WaterColorShallow { get; set; } = new(0.0055146287f, 0.0078107193f, 0.0120865023f, 1.0f);
    public Vector4 WaterColorDeep { get; set; } = new(0.0001385075f, 0.0001974951f, 0.0002262951f, 1.0f);
}
