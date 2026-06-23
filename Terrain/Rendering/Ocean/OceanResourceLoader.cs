#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Terrain.Resources;

namespace Terrain.Rendering.Ocean;

public sealed class OceanResourceLoader : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");

    private const string WaterColorFileName = "water_color.dds";
    private const string AmbientNormalFileName = "ambient_normal.dds";
    private const string FlowMapFileName = "flowmap.dds";
    private const string FlowNormalFileName = "flow_normal.dds";
    private const string FoamFileName = "foam.dds";
    private const string FoamRampFileName = "foam_ramp.dds";
    private const string FoamMapFileName = "foam_map.dds";
    private const string FoamNoiseFileName = "foam_noise.dds";

    public static IReadOnlyList<string> RequiredFileNames { get; } =
    [
        WaterColorFileName,
        AmbientNormalFileName,
        FlowMapFileName,
        FlowNormalFileName,
        FoamFileName,
        FoamRampFileName,
        FoamMapFileName,
        FoamNoiseFileName,
    ];

    public Texture? WaterColor { get; private set; }
    public Texture? AmbientNormal { get; private set; }
    public Texture? FlowMap { get; private set; }
    public Texture? FlowNormal { get; private set; }
    public Texture? Foam { get; private set; }
    public Texture? FoamRamp { get; private set; }
    public Texture? FoamMap { get; private set; }
    public Texture? FoamNoise { get; private set; }

    public bool IsLoaded =>
        WaterColor != null &&
        AmbientNormal != null &&
        FlowMap != null &&
        FlowNormal != null &&
        Foam != null &&
        FoamRamp != null &&
        FoamMap != null &&
        FoamNoise != null;

    public void Load(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        Dispose();

        string gameRoot = GameResourceRootLocator.FindFromTerrainAssembly();
        string waterDirectory = Path.Combine(gameRoot, "map", "water");

        WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, WaterColorFileName);
        AmbientNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, AmbientNormalFileName);
        FlowMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowMapFileName);
        FlowNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowNormalFileName);
        Foam = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamFileName);
        FoamRamp = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamRampFileName);
        FoamMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamMapFileName);
        FoamNoise = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamNoiseFileName);
    }

    public void Dispose()
    {
        WaterColor?.Dispose();
        AmbientNormal?.Dispose();
        FlowMap?.Dispose();
        FlowNormal?.Dispose();
        Foam?.Dispose();
        FoamRamp?.Dispose();
        FoamMap?.Dispose();
        FoamNoise?.Dispose();

        WaterColor = null;
        AmbientNormal = null;
        FlowMap = null;
        FlowNormal = null;
        Foam = null;
        FoamRamp = null;
        FoamMap = null;
        FoamNoise = null;
    }

    private static Texture LoadRequiredLocalTexture(GraphicsDevice graphicsDevice, string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            Log.Error($"Ocean local texture file '{path}' is missing from game/map/water.");
        }

        using var stream = File.OpenRead(path);
        return Texture.Load(
            graphicsDevice,
            stream,
            TextureFlags.ShaderResource,
            GraphicsResourceUsage.Immutable,
            loadAsSrgb: false);
    }

}
