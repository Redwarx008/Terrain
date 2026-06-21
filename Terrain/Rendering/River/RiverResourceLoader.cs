#nullable enable

using System;
using System.IO;
using Stride.Core.Diagnostics;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Terrain.Resources;

namespace Terrain.Rendering.River;

public sealed class RiverResourceLoader : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain.Editor");

    private const string BottomDiffuseFileName = "bottom_diffuse.dds";
    private const string BottomNormalFileName = "bottom_normal.dds";
    private const string BottomPropertiesFileName = "bottom_properties.dds";
    private const string BottomDepthFileName = "bottom_depth.dds";
    private const string AmbientNormalFileName = "ambient_normal.dds";
    private const string FlowNormalFileName = "flow_normal.dds";
    private const string FoamFileName = "foam.dds";
    private const string FoamRampFileName = "foam_ramp.dds";
    private const string FoamMapFileName = "foam_map.dds";
    private const string FoamNoiseFileName = "foam_noise.dds";
    private const string WaterColorFileName = "water_color.dds";
    public const string ReflectionSpecularUrl = "River/Environment/reflection-specular";

    public Texture? BottomDiffuse { get; private set; }
    public Texture? BottomNormal { get; private set; }
    public Texture? BottomProperties { get; private set; }
    public Texture? BottomDepth { get; private set; }
    public Texture? AmbientNormal { get; private set; }
    public Texture? FlowNormal { get; private set; }
    public Texture? Foam { get; private set; }
    public Texture? FoamRamp { get; private set; }
    public Texture? FoamMap { get; private set; }
    public Texture? FoamNoise { get; private set; }
    public Texture? WaterColor { get; private set; }
    public Texture? ReflectionSpecular { get; private set; }

    public void Load(GraphicsDevice graphicsDevice, ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(content);

        string gameRoot = GameResourceRootLocator.FindFromTerrainAssembly();
        string waterDirectory = Path.Combine(gameRoot, "map", "water");

        BottomDiffuse = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomDiffuseFileName, loadAsSrgb: true);
        BottomNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomNormalFileName, loadAsSrgb: false);
        BottomProperties = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomPropertiesFileName, loadAsSrgb: false);
        BottomDepth = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomDepthFileName, loadAsSrgb: false);
        AmbientNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, AmbientNormalFileName, loadAsSrgb: false);
        FlowNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowNormalFileName, loadAsSrgb: false);
        Foam = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamFileName, loadAsSrgb: false);
        FoamRamp = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamRampFileName, loadAsSrgb: false);
        FoamMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamMapFileName, loadAsSrgb: false);
        FoamNoise = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamNoiseFileName, loadAsSrgb: false);
        WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, WaterColorFileName, loadAsSrgb: false);
        ReflectionSpecular = LoadRequiredContentTexture(content, ReflectionSpecularUrl);
    }

    public void Unload(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        DisposeLocalTexture(BottomDiffuse);
        DisposeLocalTexture(BottomNormal);
        DisposeLocalTexture(BottomProperties);
        DisposeLocalTexture(BottomDepth);
        DisposeLocalTexture(AmbientNormal);
        DisposeLocalTexture(FlowNormal);
        DisposeLocalTexture(Foam);
        DisposeLocalTexture(FoamRamp);
        DisposeLocalTexture(FoamMap);
        DisposeLocalTexture(FoamNoise);
        DisposeLocalTexture(WaterColor);
        UnloadContentTexture(content, ReflectionSpecular);
        Dispose();
    }

    public void Dispose()
    {
        BottomDiffuse = null;
        BottomNormal = null;
        BottomProperties = null;
        BottomDepth = null;
        AmbientNormal = null;
        FlowNormal = null;
        Foam = null;
        FoamRamp = null;
        FoamMap = null;
        FoamNoise = null;
        WaterColor = null;
        ReflectionSpecular = null;
    }

    private static Texture LoadRequiredLocalTexture(GraphicsDevice graphicsDevice, string directory, string fileName, bool loadAsSrgb)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            Log.Error($"River local texture file '{path}' is missing from game/map/water.");
        }

        using var stream = File.OpenRead(path);
        return Texture.Load(
            graphicsDevice,
            stream,
            TextureFlags.ShaderResource,
            GraphicsResourceUsage.Immutable,
            loadAsSrgb);
    }

    private static Texture LoadRequiredContentTexture(ContentManager content, string url)
    {
        return content.Load<Texture>(url);
    }

    private static void DisposeLocalTexture(Texture? texture)
    {
        texture?.Dispose();
    }

    private static void UnloadContentTexture(ContentManager content, Texture? texture)
    {
        if (texture == null) return;
        content.Unload(texture);
    }
}
