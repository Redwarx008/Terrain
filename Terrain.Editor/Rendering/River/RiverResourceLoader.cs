#nullable enable

using System;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;

namespace Terrain.Editor.Rendering.River;

public sealed class RiverResourceLoader : IDisposable
{
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
    public Texture? ReflectionSpecular { get; private set; }

    public void Load(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        BottomDiffuse = LoadOptionalTexture(content, BottomDiffuseUrl);
        BottomNormal = LoadOptionalTexture(content, BottomNormalUrl);
        BottomProperties = LoadOptionalTexture(content, BottomPropertiesUrl);
        BottomDepth = LoadOptionalTexture(content, BottomDepthUrl);
        AmbientNormal = LoadOptionalTexture(content, AmbientNormalUrl);
        FlowNormal = LoadOptionalTexture(content, FlowNormalUrl);
        Foam = LoadOptionalTexture(content, FoamUrl);
        FoamRamp = LoadOptionalTexture(content, FoamRampUrl);
        FoamMap = LoadOptionalTexture(content, FoamMapUrl);
        FoamNoise = LoadOptionalTexture(content, FoamNoiseUrl);
        ReflectionSpecular = LoadOptionalTexture(content, ReflectionSpecularUrl);
    }

    public void Unload(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

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
        UnloadTexture(content, ReflectionSpecular);
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
        ReflectionSpecular = null;
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
