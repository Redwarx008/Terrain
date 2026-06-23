#nullable enable

using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Terrain.Resources;

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
            LogMissingReferencesOnce(state);
            return;
        }

        TerrainRuntimeResourceBundle resources = EnsureResources(state);
        terrain.ApplyRuntimeResourceBundle(resources);

        if (!terrain.IsInitialized || terrain.HeightmapWidth <= 0 || terrain.HeightmapHeight <= 0)
        {
            state.ContextApplied = false;
            state.Context = null;
            return;
        }

        var mapWorldSize = new Vector2(terrain.HeightmapWidth - 1, terrain.HeightmapHeight - 1);
        state.Context = new MapSurfaceRuntimeContext(resources, terrain, mapWorldSize, resources.SeaLevel);
        state.ContextApplied = true;
    }

    private static void LogMissingReferencesOnce(MapSurfaceRuntimeState state)
    {
        if (state.MissingReferencesLogged)
            return;

        Log.Warning("MapSurfaceComponent is waiting for a TerrainEntity with TerrainComponent.");
        state.MissingReferencesLogged = true;
    }

    private static TerrainRuntimeResourceBundle EnsureResources(MapSurfaceRuntimeState state)
    {
        if (state.ResourcesLoaded && state.Resources != null)
            return state.Resources;

        var resolver = GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory();
        TerrainRuntimeResourceBundle resources = new GameRuntimeResourceBootstrap(resolver).Load();
        foreach (string diagnostic in resources.Diagnostics)
        {
            Log.Warning(diagnostic);
        }

        state.Resources = resources;
        state.ResourcesLoaded = true;
        return resources;
    }
}
