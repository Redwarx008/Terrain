#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Terrain.Resources;
using Terrain.Rivers;

namespace Terrain.Rendering.River;

public sealed class RiverProcessor : EntityProcessor<RiverComponent, RiverProcessor.RenderData>, IEntityComponentRenderProcessor
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");

    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override RenderData GenerateComponentData([NotNull] Entity entity, [NotNull] RiverComponent component)
    {
        return new RenderData(component);
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] RiverComponent component, [NotNull] RenderData data)
    {
        ReleaseRenderObjects(data);
        base.OnEntityComponentRemoved(entity, component, data);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()?.GraphicsDevice;
        if (graphicsDevice == null || VisibilityGroup == null)
        {
            return;
        }

        foreach (var pair in ComponentDatas)
        {
            UpdateRenderObjects(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private void UpdateRenderObjects(Entity entity, RiverComponent component, RenderData data, GraphicsDevice graphicsDevice)
    {
        TryEnsureRuntimeMeshes(component);

        if (data.SynchronizedVersion != component.Version)
        {
            RebuildRenderObjects(component, data, graphicsDevice);
        }

        entity.Transform.UpdateWorldMatrix();
        bool enabled = component.Enabled && component.Settings.Visible;
        foreach (var renderObject in data.RenderObjects)
        {
            renderObject.ApplySettings(component.Settings);
            renderObject.Enabled = enabled;
            renderObject.RenderGroup = RiverRenderGroups.RiverRenderGroup;
            renderObject.World = entity.Transform.WorldMatrix;
        }
    }

    private void RebuildRenderObjects(RiverComponent component, RenderData data, GraphicsDevice graphicsDevice)
    {
        ReleaseRenderObjects(data);

        var meshes = component.Meshes;
        foreach (var mesh in meshes)
        {
            if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0) continue;

            var renderObject = new RiverRenderObject
            {
                Source = component,
                RenderGroup = RiverRenderGroups.RiverRenderGroup,
            };
            renderObject.Rebuild(graphicsDevice, mesh, component.Version);
            renderObject.Enabled = component.Enabled && component.Settings.Visible;
            data.RenderObjects.Add(renderObject);
            VisibilityGroup.RenderObjects.Add(renderObject);
        }

        data.SynchronizedVersion = component.Version;
    }

    private void ReleaseRenderObjects(RenderData data)
    {
        foreach (var renderObject in data.RenderObjects)
        {
            VisibilityGroup?.RenderObjects.Remove(renderObject);
            renderObject.Dispose();
        }

        data.RenderObjects.Clear();
        data.SynchronizedVersion = -1;
    }

    private void TryEnsureRuntimeMeshes(RiverComponent component)
    {
        if (component.RuntimeLoadState is RiverRuntimeLoadState.Loaded or RiverRuntimeLoadState.NoRiverResource
            || component.MeshCount > 0)
        {
            return;
        }

        TerrainComponent? terrainComponent = FindInitializedTerrainComponent();
        if (terrainComponent == null
            || terrainComponent.HeightmapWidth <= 0
            || terrainComponent.HeightmapHeight <= 0)
        {
            return;
        }

        var unresolvedConfig = new RiverRuntimeLoadConfig(
            null,
            1.0f,
            4.0f,
            terrainComponent.HeightScale,
            terrainComponent.HeightmapWidth,
            terrainComponent.HeightmapHeight);
        if (!component.ShouldAttemptRuntimeLoad(unresolvedConfig))
        {
            return;
        }

        TerrainRuntimeResourceBundle bundle;
        try
        {
            var resolver = GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory();
            bundle = new GameRuntimeResourceBootstrap(resolver).Load();
        }
        catch (Exception exception)
        {
            component.MarkRuntimeLoadFailure(unresolvedConfig);
            Log.Error($"River runtime resources could not be read: {exception.Message}");
            return;
        }

        var config = new RiverRuntimeLoadConfig(
            bundle.RiversPath,
            bundle.RiverMinWidth,
            bundle.RiverMaxWidth,
            bundle.HeightScale,
            terrainComponent.HeightmapWidth,
            terrainComponent.HeightmapHeight);

        if (!component.ShouldAttemptRuntimeLoad(config))
        {
            return;
        }

        foreach (string diagnostic in bundle.Diagnostics)
        {
            Log.Warning(diagnostic);
        }

        if (bundle.RiversPath == null)
        {
            component.MarkRuntimeNoRiverResource();
            Log.Warning("River runtime resource is not available; river rendering is disabled.");
            return;
        }

        try
        {
            var mapService = new RiverMapService(bundle.RiverMinWidth, bundle.RiverMaxWidth);
            if (!mapService.Load(bundle.RiversPath) || mapService.Cells == null)
                throw new InvalidDataException($"River map load failed: {string.Join("; ", mapService.Errors)}");

            foreach (string error in mapService.Errors)
            {
                Log.Warning(error);
            }

            var segments = mapService.ExtractSegments();
            foreach (var segment in segments)
            {
                segment.TaperStart = segment.StartKind is SegmentEndKind.Source or SegmentEndKind.None;
                segment.TaperEnd = segment.EndKind is SegmentEndKind.Confluence or SegmentEndKind.Bifurcation;
            }

            var meshService = new RiverMeshService(
                terrainComponent.GetHeight,
                terrainComponent.HeightmapWidth,
                terrainComponent.HeightmapHeight,
                terrainComponent.HeightScale);
            meshService.BuildCenterlines(segments, mapService.Width, mapService.Height);
            RiverMeshData[] meshes = segments
                .Select(segment => meshService.BuildRiverMesh(segment, 1.0f))
                .Where(mesh => mesh.Vertices.Length > 0 && mesh.Indices.Length > 0)
                .ToArray();

            component.SetMeshes(meshes);
            component.MarkRuntimeLoadSuccess();
        }
        catch (Exception exception)
        {
            component.MarkRuntimeLoadFailure(config);
            Log.Error($"River runtime meshes could not be generated: {exception.Message}");
        }
    }

    private TerrainComponent? FindInitializedTerrainComponent()
    {
        if (VisibilityGroup == null)
        {
            return null;
        }

        foreach (RenderObject renderObject in VisibilityGroup.RenderObjects)
        {
            if (renderObject is TerrainRenderObject { Source: TerrainComponent { IsInitialized: true } terrainComponent })
                return terrainComponent;
        }

        return null;
    }

    public sealed class RenderData
    {
        public RenderData(RiverComponent component)
        {
            Component = component;
        }

        public RiverComponent Component { get; }
        public List<RiverRenderObject> RenderObjects { get; } = new();
        public int SynchronizedVersion { get; set; } = -1;
    }
}
