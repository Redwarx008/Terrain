#nullable enable

using System;
using Stride.Core.Mathematics;
using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Services;

public sealed class OceanRenderingService
{
    private readonly OceanComponent oceanComponent;
    private bool isVisible = true;
    private float seaLevel = 3.8f;
    private Vector2? mapWorldSize;

    public OceanRenderingService(OceanComponent oceanComponent)
    {
        this.oceanComponent = oceanComponent ?? throw new ArgumentNullException(nameof(oceanComponent));
        ApplyVisibility();
    }

    public OceanComponent OceanComponent => oceanComponent;

    public void SetVisible(bool visible)
    {
        isVisible = visible;
        ApplyVisibility();
    }

    public void SetSeaLevel(float value)
    {
        if (!float.IsFinite(value))
            return;

        seaLevel = value;
        ApplyRuntimeInputIfReady();
    }

    public void SetMapWorldSize(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0.0f || height <= 0.0f)
        {
            mapWorldSize = null;
            oceanComponent.ClearRuntimeInput();
            return;
        }

        mapWorldSize = new Vector2(width, height);
        ApplyRuntimeInputIfReady();
    }

    private void ApplyVisibility()
    {
        oceanComponent.Visible = isVisible;
        oceanComponent.Enabled = isVisible;
    }

    private void ApplyRuntimeInputIfReady()
    {
        if (mapWorldSize is not { } size)
            return;

        oceanComponent.ApplyRuntimeInput(new OceanRuntimeInput(seaLevel, size));
    }
}
