#nullable enable

using System;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverResourceLoader : IDisposable
{
    private bool bottomEnvironmentLoadAttempted;

    public const string BottomEnvironmentUrl = "Skybox texture";
    public const string BottomDiffuseUrl = "River/Bottom/bottom-diffuse";
    public const string BottomNormalUrl = "River/Bottom/bottom-normal";
    public const string BottomPropertiesUrl = "River/Bottom/bottom-properties";
    public const string BottomDepthUrl = "River/Bottom/bottom-depth";
    public const string AmbientNormalUrl = "River/Water/ambient-normal";
    public const string FlowNormalUrl = "River/Water/flow-normal";
    public const string FoamUrl = "River/Water/foam";
    public const string FoamRampUrl = "River/Water/foam-ramp";
    public const string FoamMapUrl = "River/Water/foam-map";
    public const string FoamNoiseUrl = "River/Water/foam-noise";
    public const string WaterColorUrl = "River/Water/water-color";
    public const string ReflectionSpecularUrl = "River/Environment/reflection-specular";

    public Texture? BottomEnvironment { get; private set; }
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

    public void Load(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        BottomDiffuse = LoadRequiredTexture(content, BottomDiffuseUrl);
        BottomNormal = LoadRequiredTexture(content, BottomNormalUrl);
        BottomProperties = LoadRequiredTexture(content, BottomPropertiesUrl);
        BottomDepth = LoadRequiredTexture(content, BottomDepthUrl);
        AmbientNormal = LoadRequiredTexture(content, AmbientNormalUrl);
        FlowNormal = LoadRequiredTexture(content, FlowNormalUrl);
        Foam = LoadRequiredTexture(content, FoamUrl);
        FoamRamp = LoadRequiredTexture(content, FoamRampUrl);
        FoamMap = LoadRequiredTexture(content, FoamMapUrl);
        FoamNoise = LoadRequiredTexture(content, FoamNoiseUrl);
        WaterColor = LoadRequiredTexture(content, WaterColorUrl);
        ReflectionSpecular = LoadRequiredTexture(content, ReflectionSpecularUrl);
    }

    public Texture? EnsureBottomEnvironment(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (bottomEnvironmentLoadAttempted)
        {
            return BottomEnvironment;
        }

        bottomEnvironmentLoadAttempted = true;
        BottomEnvironment = LoadOptionalTexture(content, BottomEnvironmentUrl);
        return BottomEnvironment;
    }

    public void Unload(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        UnloadTexture(content, BottomEnvironment);
        UnloadTexture(content, BottomDiffuse);
        UnloadTexture(content, BottomNormal);
        UnloadTexture(content, BottomProperties);
        UnloadTexture(content, BottomDepth);
        UnloadTexture(content, AmbientNormal);
        UnloadTexture(content, FlowNormal);
        UnloadTexture(content, Foam);
        UnloadTexture(content, FoamRamp);
        UnloadTexture(content, FoamMap);
        UnloadTexture(content, FoamNoise);
        UnloadTexture(content, WaterColor);
        UnloadTexture(content, ReflectionSpecular);
        Dispose();
    }

    public void Dispose()
    {
        bottomEnvironmentLoadAttempted = false;
        BottomEnvironment = null;
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

    private static Texture LoadRequiredTexture(ContentManager content, string url)
    {
        try
        {
            return content.Load<Texture>(url);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"River texture asset '{url}' could not be loaded. Ensure the .sdtex is included as a RootAsset in Terrain.Editor.sdpkg.", exception);
        }
    }

    private static Texture? LoadOptionalTexture(ContentManager content, string url)
    {
        try
        {
            return content.Load<Texture>(url);
        }
        catch
        {
            return null;
        }
    }

    private static void UnloadTexture(ContentManager content, Texture? texture)
    {
        if (texture == null) return;
        content.Unload(texture);
    }
}
