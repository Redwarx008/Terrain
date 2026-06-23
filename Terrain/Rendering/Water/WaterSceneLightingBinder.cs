#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Shadows;
using Stride.Rendering.Skyboxes;
using Terrain;

namespace Terrain.Rendering.Water;

public sealed class WaterSceneLightingBinder
{
    private static readonly FieldInfo? RenderViewDatasField = typeof(ForwardLightingRenderFeature).GetField("renderViewDatas", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? OwnerContextProperty = typeof(RenderFeature).GetProperty("Context", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly RootRenderFeature owner;
    private readonly ForwardLightingRenderFeature? forwardLightingFeature;
    private readonly IShadowMapRenderer? shadowMapRenderer;
    private readonly List<RenderLight> fallbackVisibleLights = new(8);

    public WaterSceneLightingBinder(
        RootRenderFeature owner,
        ForwardLightingRenderFeature? forwardLightingFeature,
        IShadowMapRenderer? shadowMapRenderer)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this.forwardLightingFeature = forwardLightingFeature;
        this.shadowMapRenderer = shadowMapRenderer;
    }

    public void Bind(RenderDrawContext context, RenderView renderView, params DynamicEffectInstance?[] effects)
    {
        if (effects.Length == 0 || !HasAnyEffect(effects))
        {
            return;
        }

        var lightingView = renderView.LightingView ?? renderView;
        var renderViewLightData = TryGetRenderViewLightData(lightingView);
        var lights = renderViewLightData?.VisibleLights ?? CollectFallbackVisibleLights(context, lightingView);
        var (directionalLight, shadowMapTexture) = SelectDirectionalLight(lights, renderViewLightData, lightingView);
        RenderLight? skyboxLight = SelectSkyboxLight(lights);

        BindDirectionalLightFromScene(effects, directionalLight, shadowMapTexture);
        BindEnvironmentFromScene(effects, skyboxLight);

        foreach (var effect in effects)
        {
            effect?.UpdateEffect(context.GraphicsDevice);
        }
    }

    private static bool HasAnyEffect(DynamicEffectInstance?[] effects)
    {
        foreach (var effect in effects)
        {
            if (effect != null)
            {
                return true;
            }
        }

        return false;
    }

    private ForwardLightingRenderFeature.RenderViewLightData? TryGetRenderViewLightData(RenderView lightingView)
    {
        if (forwardLightingFeature != null
            && RenderViewDatasField?.GetValue(forwardLightingFeature) is Dictionary<RenderView, ForwardLightingRenderFeature.RenderViewLightData> renderViewDatas
            && renderViewDatas.TryGetValue(lightingView, out var renderViewLightData))
        {
            return renderViewLightData;
        }

        return null;
    }

    private IReadOnlyList<RenderLight>? CollectFallbackVisibleLights(RenderDrawContext context, RenderView lightingView)
    {
        fallbackVisibleLights.Clear();

        var lights = GetOwnerContext(context).VisibilityGroup.Tags.Get(ForwardLightingRenderFeature.CurrentLights);
        if (lights == null)
        {
            return null;
        }

        var frustum = lightingView.Frustum;
        foreach (var light in lights)
        {
            if (light.Type is IDirectLight directLight
                && directLight.HasBoundingBox
                && !frustum.Contains(ref light.BoundingBoxExt))
            {
                continue;
            }

            fallbackVisibleLights.Add(light);
        }

        return fallbackVisibleLights;
    }

    private RenderContext GetOwnerContext(RenderDrawContext fallbackContext)
    {
        if (OwnerContextProperty?.GetValue(owner) is RenderContext renderContext)
        {
            return renderContext;
        }

        return fallbackContext.RenderContext;
    }

    private (RenderLight? DirectionalLight, LightShadowMapTexture? ShadowMapTexture) SelectDirectionalLight(
        IReadOnlyList<RenderLight>? lights,
        ForwardLightingRenderFeature.RenderViewLightData? renderViewLightData,
        RenderView lightingView)
    {
        RenderLight? bestDirectionalLight = null;
        LightShadowMapTexture? bestShadowMapTexture = null;

        if (renderViewLightData != null)
        {
            foreach (var light in renderViewLightData.VisibleLightsWithShadows)
            {
                if (light.Type is not LightDirectional)
                {
                    continue;
                }

                var shadowMapTexture = TryGetSceneShadowMapTexture(renderViewLightData, lightingView, light);
                if (shadowMapTexture == null)
                {
                    continue;
                }

                if (bestDirectionalLight == null || light.Intensity > bestDirectionalLight.Intensity)
                {
                    bestDirectionalLight = light;
                    bestShadowMapTexture = shadowMapTexture;
                }
            }
        }

        if (bestDirectionalLight != null)
        {
            return (bestDirectionalLight, bestShadowMapTexture);
        }

        if (lights != null)
        {
            foreach (var light in lights)
            {
                if (light.Type is not LightDirectional)
                {
                    continue;
                }

                if (bestDirectionalLight == null || light.Intensity > bestDirectionalLight.Intensity)
                {
                    bestDirectionalLight = light;
                }
            }
        }

        return (bestDirectionalLight, bestDirectionalLight != null ? TryGetSceneShadowMapTexture(renderViewLightData, lightingView, bestDirectionalLight) : null);
    }

    private static RenderLight? SelectSkyboxLight(IReadOnlyList<RenderLight>? lights)
    {
        if (lights == null)
        {
            return null;
        }

        RenderLight? bestSkyboxLight = null;
        RenderLight? bestSkyboxWithCubemap = null;
        foreach (var light in lights)
        {
            if (light.Type is not LightSkybox)
            {
                continue;
            }

            if (bestSkyboxLight == null || light.Intensity > bestSkyboxLight.Intensity)
            {
                bestSkyboxLight = light;
            }

            if (TryGetSceneEnvironmentTexture(light) != null
                && (bestSkyboxWithCubemap == null || light.Intensity > bestSkyboxWithCubemap.Intensity))
            {
                bestSkyboxWithCubemap = light;
            }
        }

        return bestSkyboxWithCubemap ?? bestSkyboxLight;
    }

    private LightShadowMapTexture? TryGetSceneShadowMapTexture(
        ForwardLightingRenderFeature.RenderViewLightData? renderViewLightData,
        RenderView lightingView,
        RenderLight directionalLight)
    {
        if (renderViewLightData?.RenderLightsWithShadows.TryGetValue(directionalLight, out var shadowMapTexture) == true)
        {
            return shadowMapTexture;
        }

        return shadowMapRenderer?.FindShadowMap(lightingView, directionalLight);
    }

    private static void BindDirectionalLightFromScene(DynamicEffectInstance?[] effects, RenderLight? directionalLight, LightShadowMapTexture? shadowMapTexture)
    {
        Vector3 sceneSunDirection = directionalLight?.Direction ?? new Vector3(0.0f, -1.0f, 0.0f);
        Vector3 sceneSunColor = directionalLight?.Color.ToVector3() ?? Vector3.Zero;
        int cascadeCount = 0;
        float shadowBlendCascades = 0.0f;
        float sceneShadowDepthBias = 0.01f;
        float[] shadowCascadeSplits = [0.0f, 0.0f, 0.0f, 0.0f];
        Matrix[] worldToShadowCascadeUv =
        [
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
            Matrix.Identity,
        ];
        Texture? sceneShadowMapTexture = null;

        if (shadowMapTexture?.ShaderData is LightDirectionalShadowMapRenderer.ShaderData shaderData)
        {
            cascadeCount = Math.Min(Math.Min(shadowMapTexture.CascadeCount, shaderData.CascadeSplits.Length), worldToShadowCascadeUv.Length);
            Array.Copy(shaderData.CascadeSplits, shadowCascadeSplits, cascadeCount);
            Array.Copy(shaderData.WorldToShadowCascadeUV, worldToShadowCascadeUv, cascadeCount);
            shadowBlendCascades = (shadowMapTexture.ShadowType & LightShadowType.BlendCascade) != 0 ? 1.0f : 0.0f;
            sceneShadowDepthBias = shaderData.DepthBias;
            sceneShadowMapTexture = shaderData.Texture;
        }

        foreach (var effect in effects)
        {
            BindDirectionalLightToEffect(effect?.Parameters, sceneSunDirection, sceneSunColor, cascadeCount, shadowBlendCascades, sceneShadowDepthBias, shadowCascadeSplits, worldToShadowCascadeUv, sceneShadowMapTexture);
        }
    }

    private static void BindDirectionalLightToEffect(
        ParameterCollection? parameters,
        Vector3 sceneSunDirection,
        Vector3 sceneSunColor,
        int cascadeCount,
        float shadowBlendCascades,
        float sceneShadowDepthBias,
        float[] shadowCascadeSplits,
        Matrix[] worldToShadowCascadeUv,
        Texture? sceneShadowMapTexture)
    {
        if (parameters == null)
        {
            return;
        }

        parameters.Set(RiverStrideLightingKeys._SceneSunDirection, sceneSunDirection);
        parameters.Set(RiverStrideLightingKeys._SceneSunColor, sceneSunColor);
        parameters.Set(RiverStrideLightingKeys._SceneShadowCascadeCount, cascadeCount);
        parameters.Set(RiverStrideLightingKeys._SceneShadowBlendCascades, shadowBlendCascades);
        parameters.Set(RiverStrideLightingKeys._SceneShadowDepthBias, sceneShadowDepthBias);
        parameters.Set(RiverStrideLightingKeys._SceneShadowCascadeSplits, shadowCascadeSplits);
        parameters.Set(RiverStrideLightingKeys._SceneWorldToShadowCascadeUV, worldToShadowCascadeUv);
        parameters.SetObject(RiverStrideLightingKeys.SceneShadowMapTexture, sceneShadowMapTexture);
    }

    private static void BindEnvironmentFromScene(DynamicEffectInstance?[] effects, RenderLight? skyboxLight)
    {
        Texture? environmentTexture = TryGetSceneEnvironmentTexture(skyboxLight);
        Debug.Assert(environmentTexture != null, "River bottom requires a real scene skybox cubemap.");
        if (environmentTexture == null)
        {
            throw new InvalidOperationException("River bottom requires a real scene skybox cubemap.");
        }

        Matrix skyMatrix = Matrix.Identity;
        float intensity = 1.0f;
        if (skyboxLight != null)
        {
            var rotation = Quaternion.RotationMatrix(skyboxLight.WorldMatrix);
            skyMatrix = Matrix.Invert(Matrix.RotationQuaternion(rotation));
            intensity = skyboxLight.Intensity;
        }

        foreach (var effect in effects)
        {
            BindEnvironmentToEffect(effect?.Parameters, environmentTexture, skyMatrix, intensity);
        }
    }

    private static void BindEnvironmentToEffect(ParameterCollection? parameters, Texture? environmentTexture, Matrix skyMatrix, float intensity)
    {
        if (parameters == null)
        {
            return;
        }

        parameters.SetObject(RiverStrideLightingKeys.EnvironmentMapTexture, environmentTexture);
        parameters.Set(RiverStrideLightingKeys._EnvironmentSkyMatrix, skyMatrix);
        parameters.Set(RiverStrideLightingKeys._EnvironmentIntensity, intensity);
        parameters.Set(RiverStrideLightingKeys._EnvironmentMipCount, (float)Math.Max(environmentTexture?.MipLevelCount ?? 1, 1));
    }

    private static Texture? TryGetSceneEnvironmentTexture(RenderLight? skyboxLight)
    {
        if (skyboxLight?.Type is not LightSkybox lightSkybox)
        {
            return null;
        }

        return lightSkybox.Skybox?.SpecularLightingParameters.Get(SkyboxKeys.CubeMap);
    }
}
