#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Terrain.Resources;
using Terrain.Rendering.Ocean;

namespace Terrain.MapSurface;

public sealed class MapSurfaceProcessor : EntityProcessor<MapSurfaceComponent>
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");

    public override void Update(GameTime time)
    {
        base.Update(time);

        foreach (MapSurfaceComponent component in ComponentDatas.Values)
        {
            UpdateMapSurface(component);
        }
    }

    private static void UpdateMapSurface(MapSurfaceComponent component)
    {
        MapSurfaceRuntimeState state = component.RuntimeState;
        if (component.TerrainEntity?.Get<TerrainComponent>() is not { } terrain)
        {
            ClearRuntimeContext(state);
            ClearOceanRuntimeInputIfPresent(component);
            LogMissingReferencesOnce(state);
            return;
        }

        if (!TryEnsureResources(
            state,
            LoadRuntimeResourceBundle,
            message => Log.Warning(message),
            out TerrainRuntimeResourceBundle? resources))
        {
            ClearRuntimeContext(state);
            ClearOceanRuntimeInputIfPresent(component);
            return;
        }

        terrain.ApplyRuntimeResourceBundle(resources);

        if (!terrain.IsInitialized || terrain.HeightmapWidth <= 0 || terrain.HeightmapHeight <= 0)
        {
            ClearRuntimeContext(state);
            ClearOceanRuntimeInputIfPresent(component);
            return;
        }

        var mapWorldSize = new Vector2(terrain.HeightmapWidth - 1, terrain.HeightmapHeight - 1);
        state.Context = new MapSurfaceRuntimeContext(resources, terrain, mapWorldSize, resources.SeaLevel);
        state.ContextApplied = true;

        if (component.OceanEntity?.Get<OceanComponent>() is { } ocean)
        {
            ocean.ApplyRuntimeInput(new OceanRuntimeInput(resources.SeaLevel, mapWorldSize));
        }
    }

    private static void ClearRuntimeContext(MapSurfaceRuntimeState state)
    {
        state.ContextApplied = false;
        state.Context = null;
    }

    private static void ClearOceanRuntimeInputIfPresent(MapSurfaceComponent component)
    {
        component.OceanEntity?.Get<OceanComponent>()?.ClearRuntimeInput();
    }

    private static void LogMissingReferencesOnce(MapSurfaceRuntimeState state)
    {
        if (state.MissingReferencesLogged)
            return;

        Log.Warning("MapSurfaceComponent is waiting for a TerrainEntity with TerrainComponent.");
        state.MissingReferencesLogged = true;
    }

    internal static bool TryEnsureResources(
        MapSurfaceRuntimeState state,
        Func<TerrainRuntimeResourceBundle> resourceLoader,
        Action<string> logDiagnostic,
        [NotNullWhen(true)]
        out TerrainRuntimeResourceBundle? resources)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(resourceLoader);
        ArgumentNullException.ThrowIfNull(logDiagnostic);

        if (state.ResourcesLoaded && state.Resources != null)
        {
            resources = state.Resources;
            return true;
        }

        if (state.ResourceLoadFailed)
        {
            resources = null;
            return false;
        }

        try
        {
            resources = resourceLoader();
            foreach (string diagnostic in resources.Diagnostics)
            {
                logDiagnostic(diagnostic);
            }

            state.Resources = resources;
            state.ResourcesLoaded = true;
            return true;
        }
        catch (Exception exception)
        {
            resources = null;
            state.ResourceLoadFailed = true;
            state.ResourceLoadFailureDiagnostic = FormatRuntimeLoadFailure(exception);
            logDiagnostic($"MapSurface runtime resources could not be read: {state.ResourceLoadFailureDiagnostic}");
            return false;
        }
    }

    private static TerrainRuntimeResourceBundle LoadRuntimeResourceBundle()
    {
        var resolver = GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory();
        return new GameRuntimeResourceBootstrap(resolver).Load();
    }

    private static string FormatRuntimeLoadFailure(Exception exception)
    {
        if (exception is FileNotFoundException { FileName: { Length: > 0 } fileName })
        {
            return $"{exception.Message} ({fileName})";
        }

        return exception.Message;
    }
}
