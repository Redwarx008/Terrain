#nullable enable

using System;
using System.Collections.Generic;
using Stride.Engine;
using Stride.Graphics;
using Terrain.Rivers;
using Terrain.Rendering.River;

namespace Terrain.Editor.Services;

public sealed class RiverRenderingService : IDisposable
{
    private readonly RiverComponent riverComponent;
    private bool isVisible = true;

    public RiverRenderingService(GraphicsDevice graphicsDevice, Scene scene, RiverComponent riverComponent)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(scene);
        this.riverComponent = riverComponent ?? throw new ArgumentNullException(nameof(riverComponent));
    }

    public RiverRenderingService(GraphicsDevice graphicsDevice, Scene scene)
        : this(graphicsDevice, scene, FindOrCreateRiverComponent(scene))
    {
    }

    public RiverRenderingService(RiverComponent riverComponent)
    {
        this.riverComponent = riverComponent ?? throw new ArgumentNullException(nameof(riverComponent));
    }

    public RiverComponent RiverComponent => riverComponent;

    public void UpdateMeshes(List<RiverSegment> segments, RiverMeshService meshService, float widthScale)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(meshService);

        var meshes = new List<RiverMeshData>();
        foreach (var seg in segments)
        {
            var mesh = meshService.BuildRiverMesh(seg, widthScale);
            if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0) continue;
            meshes.Add(mesh);
        }

        riverComponent.SetMeshes(meshes);
        riverComponent.Enabled = isVisible;
        riverComponent.Settings.Visible = isVisible;
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;
        riverComponent.Enabled = visible;
        riverComponent.Settings.Visible = visible;
    }

    public void SetMaxVisibleCameraHeight(float value)
    {
        riverComponent.Settings.RiverMaxVisibleCameraHeight = value;
    }

    public void SetSeaLevel(float value)
    {
        if (!float.IsFinite(value))
            return;

        riverComponent.Settings.SeaLevel = value;
    }

    public void ClearMeshes()
    {
        riverComponent.Clear();
    }

    public void Dispose()
    {
        ClearMeshes();
    }

    private static RiverComponent FindOrCreateRiverComponent(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        foreach (var entity in scene.Entities)
        {
            var component = entity.Get<RiverComponent>();
            if (component != null) return component;
        }

        var riverEntity = new Entity("RiverSystem");
        var riverComponent = new RiverComponent();
        riverEntity.Add(riverComponent);
        scene.Entities.Add(riverEntity);
        return riverComponent;
    }
}
