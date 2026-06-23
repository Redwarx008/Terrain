# Ocean System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a full-map horizontal ocean that shares editable sea level with river under-ocean fade and is coordinated through a thin `MapSurfaceComponent`.

**Architecture:** Treat `sea_level` as map settings data loaded from `game/map/default.toml`, not as an `OceanComponent` property. Add a `MapSurfaceRoot` entity with `MapSurfaceComponent` that references independent Terrain, River, and Ocean entities, loads shared map settings, waits for terrain readiness, and pushes explicit runtime input to river and ocean. Ocean rendering uses its own component, processor, render object, render feature, and SDSL shader while reusing the existing Stride scene-light/skybox binding model from river.

**Tech Stack:** C#/.NET, Stride ECS processors/render features/SDSL, Avalonia AXAML, Tommy TOML, existing `Terrain.Editor.Tests` harness, Stride asset compiler workflow.

---

## Scope Check

This plan is one vertical feature, not independent projects: sea-level persistence, map-surface coordination, river fade synchronization, and ocean rendering are required together for the editor-visible ocean to work without initialization-order coupling.

The plan deliberately lands the ocean as a full-map plane first. Heightmap clipping, coastline mesh generation, and terrain-intersection foam are excluded by the approved spec.

---

## File Structure

- Modify `Terrain/Resources/RuntimeMapDefinition.cs`: add `SeaLevel`.
- Modify `Terrain/Resources/RuntimeMapDefinitionReader.cs`: allow/read/validate `[settings].sea_level` with default `3.8`.
- Modify `Terrain/Resources/TerrainRuntimeResourceBundle.cs`: carry `SeaLevel`.
- Modify `Terrain/Resources/GameRuntimeResourceBootstrap.cs`: copy sea level into the runtime bundle.
- Modify `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`: validate/write `sea_level`.
- Modify `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`: scaffold default `sea_level = 3.8`.
- Modify `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`: carry sea level through background save.
- Modify `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`: save sea level.
- Modify `Terrain.Editor/ViewModels/SettingsViewModel.cs`: expose `ShowOcean` and `SeaLevel`.
- Modify `Terrain.Editor/ViewModels/EditorShellViewModel.cs`: sync sea level load/change/save.
- Modify `Terrain.Editor/Views/MainWindow.axaml`: add Show Ocean and Sea Level controls.
- Create `Terrain/MapSurface/MapSurfaceComponent.cs`: thin coordinator component with entity references.
- Create `Terrain/MapSurface/MapSurfaceRuntimeState.cs`: private coordinator state.
- Create `Terrain/MapSurface/MapSurfaceRuntimeContext.cs`: explicit runtime input snapshot.
- Create `Terrain/MapSurface/MapSurfaceProcessor.cs`: loads map resources once, waits for terrain, applies inputs to river/ocean.
- Modify `Terrain/Core/TerrainComponent.cs`: accept coordinator-provided runtime bundle.
- Modify `Terrain/Core/TerrainProcessor.cs`: prefer component-provided bundle over self-bootstrap.
- Modify `Terrain/Rendering/River/RiverRenderSettings.cs`: add `OceanWaterHeight`.
- Modify `Terrain/Rendering/River/RiverRuntimeLoadState.cs`: add coordinator runtime input config.
- Modify `Terrain/Rendering/River/RiverComponent.cs`: accept explicit runtime input from coordinator.
- Modify `Terrain/Rendering/River/RiverProcessor.cs`: use explicit runtime input before old render-object scan fallback.
- Modify `Terrain/Rendering/River/RiverRenderFeature.cs`: bind `_WaterHeight` from `RiverRenderSettings.OceanWaterHeight`.
- Create `Terrain/Rendering/Water/WaterSceneLightingBinder.cs`: shared Stride scene light/environment binder.
- Modify `Terrain/Rendering/River/RiverRenderFeature.cs`: call `WaterSceneLightingBinder` instead of keeping river-only private binding code.
- Create `Terrain/Rendering/Ocean/OceanComponent.cs`: scene hook and material/runtime input container.
- Create `Terrain/Rendering/Ocean/OceanMaterialSettings.cs`: ocean tuning defaults.
- Create `Terrain/Rendering/Ocean/OceanRuntimeInput.cs`: sea level and map dimensions from coordinator.
- Create `Terrain/Rendering/Ocean/OceanVertex.cs`: full-map plane vertex format.
- Create `Terrain/Rendering/Ocean/OceanRenderObject.cs`: GPU buffers and ocean draw metadata.
- Create `Terrain/Rendering/Ocean/OceanProcessor.cs`: builds/updates ocean render object from runtime input.
- Create `Terrain/Rendering/Ocean/OceanRenderGroups.cs`: render group constants.
- Create `Terrain/Rendering/Ocean/OceanResourceLoader.cs`: loads `game/map/water` DDS textures needed by ocean.
- Create `Terrain/Rendering/Ocean/OceanRenderFeature.cs`: binds ocean shader, textures, scene lighting, and draws plane.
- Create `Terrain/Effects/Ocean/OceanVertexStreams.sdsl`: ocean vertex streams.
- Create `Terrain/Effects/Ocean/OceanSurface.sdsl`: ocean shader.
- Generate `Terrain/Effects/Ocean/OceanVertexStreams.sdsl.cs` and `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`.
- Modify `Terrain/Terrain.csproj`: include generated ocean shader key files.
- Modify `Terrain/Terrain.sdpkg`: effects folder already included; verify no extra folder registration is needed.
- Modify `Terrain/Assets/MainScene.sdscene`: add `MapSurfaceRoot` and `Ocean` entities and references.
- Modify `Terrain/Assets/GraphicsCompositor.sdgfxcomp`: add `OceanRenderFeature` selector.
- Modify `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`: create matching editor-only MapSurface/Ocean entities and ensure ocean render feature in fallback compositor.
- Modify `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`: expose an ocean service or coordinator hook if needed for UI.
- Modify `Terrain.Editor/Services/RiverRenderingService.cs`: expose sea-level setter through river settings.
- Create `Terrain.Editor/Services/OceanRenderingService.cs`: editor facade for ocean visibility and sea level.
- Copy resource `game/map/water/flowmap.dds` from CK3 if missing.
- Modify `game/map/default.toml`: add `sea_level = 3.8`.
- Add tests under `Terrain.Editor.Tests/VirtualResources`, `Terrain.Editor.Tests`, and shader text tests.
- Update `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, and a session log after implementation verification.

---

## Task 1: Sea-Level Configuration Model

**Files:**
- Modify: `Terrain/Resources/RuntimeMapDefinition.cs`
- Modify: `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- Modify: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Modify: `Terrain/Resources/GameRuntimeResourceBootstrap.cs`
- Modify: `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorMapDataScaffoldTests.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`

- [ ] **Step 1: Add failing reader/writer/scaffold/bootstrap tests**

In `EditorResourceWriterTests.RunAll`, add:

```csharp
TestHarness.Run("map definition reader defaults sea level", MapDefinitionReaderDefaultsSeaLevel);
TestHarness.Run("map definition reader reads explicit sea level", MapDefinitionReaderReadsExplicitSeaLevel);
TestHarness.Run("map definition reader rejects invalid sea level", MapDefinitionReaderRejectsInvalidSeaLevel);
TestHarness.Run("map definition writer rejects invalid sea level", MapDefinitionWriterRejectsInvalidSeaLevel);
```

Add test bodies:

```csharp
private static void MapDefinitionReaderDefaultsSeaLevel()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
""");

    RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

    TestHarness.AssertEqual(3.8f, map.SeaLevel, "default sea level");
}

private static void MapDefinitionReaderReadsExplicitSeaLevel()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
sea_level = 12.5
""");

    RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(output);

    TestHarness.AssertEqual(12.5f, map.SeaLevel, "explicit sea level");
}

private static void MapDefinitionReaderRejectsInvalidSeaLevel()
{
    string root = CreateWorkspace();
    string output = Path.Combine(root, "mod", "map", "default.toml");
    WriteExistingFile(output, """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 200
sea_level = nan
""");

    TestHarness.AssertThrows<InvalidDataException>(
        () => RuntimeMapDefinitionReader.ReadFrom(output),
        "non-finite sea_level should be rejected");
}

private static void MapDefinitionWriterRejectsInvalidSeaLevel()
{
    string root = CreateWorkspace();
    var session = CreateSession(root);
    var writer = new MapDefinitionWriter();

    TestHarness.AssertThrows<InvalidDataException>(
        () => writer.Write(session, new RuntimeMapDefinition
        {
            HeightmapPath = "heightmap.png",
            TerrainDataPath = "terrain.terrain",
            HeightScale = 200.0f,
            SeaLevel = float.PositiveInfinity,
        }),
        "infinite sea_level should be rejected by writer");
}
```

In `MapDefinitionWriterPreservesMapDataEntriesAndHeightScale`, set and assert:

```csharp
SeaLevel = 12.5f,
...
TestHarness.AssertEqual(12.5f, map.SeaLevel, "sea level");
```

In `EditorMapDataScaffoldTests`, assert generated defaults:

```csharp
TestHarness.Assert(defaultText.Contains("sea_level = 3.8", StringComparison.Ordinal), "generated default.toml should write sea level");
TestHarness.AssertEqual(3.8f, map.SeaLevel, "generated default sea level");
```

In `GameRuntimeResourceBootstrapTests.BootstrapLoadsFixedCompanionResources`, pass a non-default sea level from `WriteResourceBundle` and assert:

```csharp
TestHarness.AssertEqual(9.25f, bundle.SeaLevel, "sea level");
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failures for missing `SeaLevel` or assertion failures for missing `sea_level` support.

- [ ] **Step 3: Add model fields**

In `RuntimeMapDefinition`, add:

```csharp
public float SeaLevel { get; init; } = 3.8f;
```

In `TerrainRuntimeResourceBundle`, add:

```csharp
public float SeaLevel { get; init; } = 3.8f;
```

- [ ] **Step 4: Read and validate TOML**

In `RuntimeMapDefinitionReader.ReadFrom`, extend allowed settings:

```csharp
ValidateTableKeys(settings, filePath, "settings", "height_scale", "river_min_width", "river_max_width", "river_max_visible_camera_height", "sea_level");
```

After river camera height validation, add:

```csharp
float seaLevel = ReadOptionalFloat(settings, "sea_level", filePath, 3.8f);
ValidateSeaLevel(seaLevel, filePath);
```

Add validation:

```csharp
private static void ValidateSeaLevel(float value, string filePath)
{
    if (!float.IsFinite(value))
        throw new InvalidDataException($"sea_level must be finite: {filePath}");
}
```

Set the returned model property:

```csharp
SeaLevel = seaLevel,
```

- [ ] **Step 5: Write TOML**

In `MapDefinitionWriter.Write`, call validation:

```csharp
ValidateSeaLevel(mapDefinition.SeaLevel);
```

Write the field:

```csharp
["sea_level"] = mapDefinition.SeaLevel,
```

Add writer validation:

```csharp
private static void ValidateSeaLevel(float value)
{
    if (!float.IsFinite(value))
        throw new InvalidDataException("Map definition sea_level must be finite.");
}
```

- [ ] **Step 6: Bootstrap sea level**

In `GameRuntimeResourceBootstrap.Load`, copy:

```csharp
SeaLevel = mapDefinition.SeaLevel,
```

Update the `WriteResourceBundle` helper in `GameRuntimeResourceBootstrapTests` to accept:

```csharp
float seaLevel = 3.8f
```

and emit:

```csharp
sea_level = {{seaLevel}}
```

- [ ] **Step 7: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Resources/RuntimeMapDefinition.cs Terrain/Resources/RuntimeMapDefinitionReader.cs Terrain/Resources/TerrainRuntimeResourceBundle.cs Terrain/Resources/GameRuntimeResourceBootstrap.cs Terrain.Editor/Services/Resources/MapDefinitionWriter.cs Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs Terrain.Editor.Tests/VirtualResources/EditorMapDataScaffoldTests.cs Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs
git commit -m "feat: add sea level map setting"
```

---

## Task 2: Editor Sea-Level Save and UI

**Files:**
- Modify: `Terrain.Editor/ViewModels/SettingsViewModel.cs`
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
- Modify: `Terrain.Editor/Views/MainWindow.axaml`
- Modify: `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`

- [ ] **Step 1: Add failing save tests**

In `EditorResourceSaveServiceTests.RunAll`, add:

```csharp
TestHarness.Run("authoring save persists sea level", AuthoringSavePersistsSeaLevel);
TestHarness.Run("authoring save preserves loaded sea level by default", AuthoringSavePreservesLoadedSeaLevelByDefault);
```

Add:

```csharp
private static void AuthoringSavePersistsSeaLevel()
{
    SaveFixture fixture = CreatePopulatedSaveFixture();
    var biomeMask = new BiomeMask(2, 2);

    EditorResourceSaveService.Save(
        fixture.Session,
        [1, 2, 3, 4],
        width: 2,
        height: 2,
        biomeMask,
        heightScale: 222.0f,
        descriptorSlots:
        [
            new EditorMaterialDescriptorSlot("grass", 0, "Grass", "grass.png", null, null),
        ],
        biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []),
        seaLevel: 9.25f);

    RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

    TestHarness.AssertEqual(9.25f, saved.SeaLevel, "save should persist sea level");
}

private static void AuthoringSavePreservesLoadedSeaLevelByDefault()
{
    SaveFixture fixture = CreatePopulatedSaveFixture(seaLevel: 9.25f);
    var biomeMask = new BiomeMask(2, 2);

    EditorResourceSaveService.Save(
        fixture.Session,
        [1, 2, 3, 4],
        width: 2,
        height: 2,
        biomeMask,
        heightScale: 222.0f,
        descriptorSlots:
        [
            new EditorMaterialDescriptorSlot("grass", 0, "Grass", "grass.png", null, null),
        ],
        biomeSnapshot: new EditorBiomeSettingsSnapshot([], [], []));

    RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(fixture.MapDefinitionPath);

    TestHarness.AssertEqual(9.25f, saved.SeaLevel, "save should preserve loaded sea level when caller omits the parameter");
}
```

Update `CreatePopulatedSaveFixture` to accept `float seaLevel = 3.8f` and seed `RuntimeMapDefinition.SeaLevel`.

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failures for missing `seaLevel` parameters or failing preservation assertions.

- [ ] **Step 3: Carry sea level through save snapshot**

In `EditorAuthoringSaveSnapshot`, add a constructor parameter after `riverMaxVisibleCameraHeight`:

```csharp
float seaLevel,
```

Store:

```csharp
SeaLevel = seaLevel;
```

Expose:

```csharp
public float SeaLevel { get; }
```

In `TerrainManager.CreateAuthoringSaveSnapshot`, pass the method argument through. Change overload signatures to:

```csharp
public EditorAuthoringSaveSnapshot CreateAuthoringSaveSnapshot(
    float riverMaxVisibleCameraHeight = 3000.0f,
    float seaLevel = 3.8f,
    IProgress<AuthoringSaveProgress>? progress = null,
    EditorDirtyResource dirtyResources = EditorDirtyResource.All)
```

and:

```csharp
public EditorAuthoringSaveSnapshot CreateAuthoringSaveSnapshot(
    float riverMaxVisibleCameraHeight,
    float seaLevel,
    IProgress<AuthoringSaveProgress>? progress,
    EditorDirtySnapshot dirtySnapshot)
```

- [ ] **Step 4: Save sea level**

In `EditorResourceSaveService.Save`, add optional parameter:

```csharp
float? seaLevel = null,
```

Set the saved map model:

```csharp
SeaLevel = seaLevel ?? session.MapDefinitionModel.SeaLevel,
```

In `TerrainManager.SaveAuthoringResources`, pass:

```csharp
snapshot.SeaLevel,
```

- [ ] **Step 5: Expose editor settings**

In `SettingsViewModel`, add:

```csharp
[ObservableProperty]
private bool _showOcean = true;

[ObservableProperty]
private float _seaLevel = 3.8f;
```

In `MainWindow.axaml`, under the settings inspector after `Show Rivers`, add:

```xml
<CheckBox Content="Show Ocean" IsChecked="{Binding Settings.ShowOcean}" />
<Grid ColumnDefinitions="*,72" ColumnSpacing="8">
  <StackPanel Spacing="2">
    <TextBlock Classes="fieldLabel" Text="Sea Level" />
    <Slider Minimum="-100" Maximum="300" Value="{Binding Settings.SeaLevel}" />
  </StackPanel>
  <TextBox Grid.Column="1" Classes="valueBox" IsReadOnly="True"
           Text="{Binding Settings.SeaLevel, StringFormat='{}{0:F1}'}"
           VerticalAlignment="Bottom" />
</Grid>
```

- [ ] **Step 6: Sync editor setting changes**

In `EditorShellViewModel.Save`, change snapshot creation to:

```csharp
var snapshot = terrainManager.CreateAuthoringSaveSnapshot(
    Settings.RiverMaxVisibleCameraHeight,
    Settings.SeaLevel,
    progress,
    dirtySnapshot);
```

In `OnSettingsPropertyChanged`, add:

```csharp
else if (e.PropertyName == nameof(SettingsViewModel.ShowOcean))
{
    _viewportHost.OceanRenderingService?.SetVisible(Settings.ShowOcean);
}
else if (e.PropertyName == nameof(SettingsViewModel.SeaLevel))
{
    _viewportHost.OceanRenderingService?.SetSeaLevel(Settings.SeaLevel);
    _viewportHost.RiverRenderingService?.SetSeaLevel(Settings.SeaLevel);
    EditorDirtyState.Instance.MarkDirty(EditorDirtyResource.MapDefinition);
}
```

In `SyncSettingsFromTerrainManager`, inside `_resourceSession != null`:

```csharp
Settings.SeaLevel = _resourceSession.MapDefinitionModel.SeaLevel;
```

Then apply:

```csharp
_viewportHost.OceanRenderingService?.SetSeaLevel(Settings.SeaLevel);
_viewportHost.RiverRenderingService?.SetSeaLevel(Settings.SeaLevel);
_viewportHost.OceanRenderingService?.SetVisible(Settings.ShowOcean);
```

- [ ] **Step 7: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain.Editor/ViewModels/SettingsViewModel.cs Terrain.Editor/ViewModels/EditorShellViewModel.cs Terrain.Editor/Views/MainWindow.axaml Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs Terrain.Editor/Services/Resources/EditorResourceSaveService.cs Terrain.Editor/Services/TerrainManager.cs Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs
git commit -m "feat: expose sea level editor setting"
```

---

## Task 3: Map Surface Coordinator

**Files:**
- Create: `Terrain/MapSurface/MapSurfaceComponent.cs`
- Create: `Terrain/MapSurface/MapSurfaceRuntimeState.cs`
- Create: `Terrain/MapSurface/MapSurfaceRuntimeContext.cs`
- Create: `Terrain/MapSurface/MapSurfaceProcessor.cs`
- Modify: `Terrain/Core/TerrainComponent.cs`
- Modify: `Terrain/Core/TerrainProcessor.cs`
- Test: `Terrain.Editor.Tests/MapSurfaceCoordinatorTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add failing coordinator tests**

Create `Terrain.Editor.Tests/MapSurfaceCoordinatorTests.cs`:

```csharp
using System.Reflection;
using Stride.Engine.Design;
using Terrain.MapSurface;

namespace Terrain.Editor.Tests;

internal static class MapSurfaceCoordinatorTests
{
    public static void RunAll()
    {
        TestHarness.Run("map surface component uses map surface processor", MapSurfaceComponentUsesProcessor);
        TestHarness.Run("map surface component does not expose sea level property", MapSurfaceComponentDoesNotExposeSeaLevelProperty);
    }

    private static void MapSurfaceComponentUsesProcessor()
    {
        var attribute = typeof(MapSurfaceComponent)
            .GetCustomAttributes()
            .OfType<DefaultEntityComponentProcessorAttribute>()
            .FirstOrDefault();

        TestHarness.Assert(attribute != null, "MapSurfaceComponent should register a non-render entity processor");
        TestHarness.AssertEqual(typeof(MapSurfaceProcessor).AssemblyQualifiedName, attribute!.TypeName, "processor type");
    }

    private static void MapSurfaceComponentDoesNotExposeSeaLevelProperty()
    {
        PropertyInfo? seaLevel = typeof(MapSurfaceComponent).GetProperty("SeaLevel", BindingFlags.Instance | BindingFlags.Public);

        TestHarness.Assert(seaLevel == null, "SeaLevel should stay in RuntimeMapDefinition settings, not MapSurfaceComponent");
    }
}
```

In `Program.cs`, add:

```csharp
MapSurfaceCoordinatorTests.RunAll();
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failure because `Terrain.MapSurface` types do not exist.

- [ ] **Step 3: Add component and state types**

Create `MapSurfaceComponent.cs`:

```csharp
#nullable enable

using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace Terrain.MapSurface;

[DataContract("MapSurfaceComponent")]
[DefaultEntityComponentProcessor(typeof(MapSurfaceProcessor))]
public sealed class MapSurfaceComponent : EntityComponent
{
    [DataMember(10)]
    public Entity? TerrainEntity { get; set; }

    [DataMember(20)]
    public Entity? RiverEntity { get; set; }

    [DataMember(30)]
    public Entity? OceanEntity { get; set; }

    [DataMemberIgnore]
    internal MapSurfaceRuntimeState RuntimeState { get; } = new();
}
```

Create `MapSurfaceRuntimeState.cs`:

```csharp
#nullable enable

using Terrain.Resources;

namespace Terrain.MapSurface;

internal sealed class MapSurfaceRuntimeState
{
    public TerrainRuntimeResourceBundle? Resources { get; set; }
    public bool ResourcesLoaded { get; set; }
    public bool ContextApplied { get; set; }
}
```

Create `MapSurfaceRuntimeContext.cs`:

```csharp
#nullable enable

using Stride.Core.Mathematics;
using Terrain.Resources;

namespace Terrain.MapSurface;

internal readonly record struct MapSurfaceRuntimeContext(
    TerrainRuntimeResourceBundle Resources,
    TerrainComponent Terrain,
    Vector2 MapWorldSize,
    float SeaLevel);
```

- [ ] **Step 4: Let TerrainComponent accept coordinator bundle**

In `TerrainComponent`, add:

```csharp
[DataMemberIgnore]
internal TerrainRuntimeResourceBundle? RuntimeResourceBundle { get; private set; }

internal void ApplyRuntimeResourceBundle(TerrainRuntimeResourceBundle bundle)
{
    ArgumentNullException.ThrowIfNull(bundle);
    RuntimeResourceBundle = bundle;
}
```

Add `using Terrain.Resources;` if needed.

In `TerrainProcessor.TryLoadTerrainData`, replace:

```csharp
TerrainRuntimeResourceBundle bundle = LoadRuntimeResourceBundle();
```

with:

```csharp
TerrainRuntimeResourceBundle bundle = component.RuntimeResourceBundle ?? LoadRuntimeResourceBundle();
```

- [ ] **Step 5: Add processor**

Create `MapSurfaceProcessor.cs`:

```csharp
#nullable enable

using System;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Terrain.Rendering.Ocean;
using Terrain.Rendering.River;
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

    private void UpdateMapSurface(MapSurfaceComponent component)
    {
        if (!TryResolveComponents(component, out TerrainComponent? terrain, out RiverComponent? river, out OceanComponent? ocean))
        {
            Log.Warning("MapSurfaceComponent is waiting for Terrain/River/Ocean entity references.");
            return;
        }

        TerrainRuntimeResourceBundle resources = EnsureResources(component.RuntimeState);
        terrain.ApplyRuntimeResourceBundle(resources);

        if (!terrain.IsInitialized || terrain.HeightmapWidth <= 0 || terrain.HeightmapHeight <= 0)
        {
            return;
        }

        var mapWorldSize = new Vector2(terrain.HeightmapWidth - 1, terrain.HeightmapHeight - 1);
        var context = new MapSurfaceRuntimeContext(resources, terrain, mapWorldSize, resources.SeaLevel);

        ocean.ApplyRuntimeInput(new OceanRuntimeInput(context.SeaLevel, context.MapWorldSize));
        river.ApplyRuntimeInput(new RiverRuntimeInput(
            resources.RiversPath,
            resources.RiverMinWidth,
            resources.RiverMaxWidth,
            resources.RiverMaxVisibleCameraHeight,
            context.SeaLevel,
            terrain));
        component.RuntimeState.ContextApplied = true;
    }

    internal static bool TryResolveComponents(
        MapSurfaceComponent component,
        out TerrainComponent? terrain,
        out RiverComponent? river,
        out OceanComponent? ocean)
    {
        terrain = component.TerrainEntity?.Get<TerrainComponent>();
        river = component.RiverEntity?.Get<RiverComponent>();
        ocean = component.OceanEntity?.Get<OceanComponent>();
        return terrain != null && river != null && ocean != null;
    }

    private static TerrainRuntimeResourceBundle EnsureResources(MapSurfaceRuntimeState state)
    {
        if (state.ResourcesLoaded && state.Resources != null)
            return state.Resources;

        var resolver = GameResourceResolverBootstrap.CreateForTerrainAssemblyDirectory();
        TerrainRuntimeResourceBundle resources = new GameRuntimeResourceBootstrap(resolver).Load();
        state.Resources = resources;
        state.ResourcesLoaded = true;
        return resources;
    }
}
```

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/MapSurface/MapSurfaceComponent.cs Terrain/MapSurface/MapSurfaceRuntimeState.cs Terrain/MapSurface/MapSurfaceRuntimeContext.cs Terrain/MapSurface/MapSurfaceProcessor.cs Terrain/Core/TerrainComponent.cs Terrain/Core/TerrainProcessor.cs Terrain.Editor.Tests/MapSurfaceCoordinatorTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add map surface coordinator"
```

---

## Task 4: River Runtime Input and Shared Sea Level

**Files:**
- Modify: `Terrain/Rendering/River/RiverRenderSettings.cs`
- Modify: `Terrain/Rendering/River/RiverRuntimeLoadState.cs`
- Modify: `Terrain/Rendering/River/RiverComponent.cs`
- Modify: `Terrain/Rendering/River/RiverProcessor.cs`
- Modify: `Terrain/Rendering/River/RiverRenderFeature.cs`
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs`
- Test: `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`

- [ ] **Step 1: Add failing river tests**

In `RiverRenderFeatureRuntimeTests.RunAll`, add:

```csharp
TestHarness.Run("river render settings carries ocean water height", RiverRenderSettingsCarriesOceanWaterHeight);
```

Add:

```csharp
private static void RiverRenderSettingsCarriesOceanWaterHeight()
{
    var settings = new RiverRenderSettings();

    TestHarness.AssertEqual(3.8f, settings.OceanWaterHeight, "default ocean water height should match map sea level default");

    settings.OceanWaterHeight = 9.25f;

    TestHarness.AssertEqual(9.25f, settings.OceanWaterHeight, "ocean water height should be mutable from coordinator/editor service");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failure for missing `OceanWaterHeight`.

- [ ] **Step 3: Add river sea-level setting**

In `RiverRenderSettings`, add:

```csharp
public float OceanWaterHeight { get; set; } = 3.8f;
```

In `RiverRenderingService`, add:

```csharp
public void SetSeaLevel(float value)
{
    riverComponent.Settings.OceanWaterHeight = value;
}
```

In `RiverRenderFeature.RiverParametersMatch`, include:

```csharp
&& sourceSettings.OceanWaterHeight == candidateSettings.OceanWaterHeight
```

In `ApplyBottomParameters`, bind:

```csharp
effect.Parameters.Set(RiverBottomKeys._WaterHeight, settings.OceanWaterHeight);
```

In `ApplySurfaceParameters`, bind:

```csharp
effect.Parameters.Set(RiverSurfaceKeys._WaterHeight, settings.OceanWaterHeight);
```

Remove the static binding:

```csharp
bottomEffect.Parameters.Set(RiverBottomKeys._WaterHeight, 3.0f);
```

- [ ] **Step 4: Add explicit river runtime input**

In `RiverRuntimeLoadState.cs`, add:

```csharp
public readonly record struct RiverRuntimeInput(
    string? RiversPath,
    float RiverMinWidth,
    float RiverMaxWidth,
    float RiverMaxVisibleCameraHeight,
    float SeaLevel,
    TerrainComponent Terrain);
```

In `RiverComponent`, add:

```csharp
public RiverRuntimeInput? RuntimeInput { get; private set; }

public void ApplyRuntimeInput(RiverRuntimeInput input)
{
    RuntimeInput = input;
    Settings.RiverMaxVisibleCameraHeight = input.RiverMaxVisibleCameraHeight;
    Settings.OceanWaterHeight = input.SeaLevel;
}
```

- [ ] **Step 5: Prefer explicit input in RiverProcessor**

In `TryEnsureRuntimeMeshes`, before `FindInitializedTerrainComponent`, add:

```csharp
if (component.RuntimeInput is { } input)
{
    TryEnsureRuntimeMeshesFromInput(component, input);
    return;
}
```

Add helper:

```csharp
private void TryEnsureRuntimeMeshesFromInput(RiverComponent component, RiverRuntimeInput input)
{
    TerrainComponent terrainComponent = input.Terrain;
    if (!terrainComponent.IsInitialized || terrainComponent.HeightmapWidth <= 0 || terrainComponent.HeightmapHeight <= 0)
        return;

    var config = new RiverRuntimeLoadConfig(
        input.RiversPath,
        input.RiverMinWidth,
        input.RiverMaxWidth,
        input.RiverMaxVisibleCameraHeight,
        terrainComponent.HeightScale,
        terrainComponent.HeightmapWidth,
        terrainComponent.HeightmapHeight);

    if (!component.ShouldAttemptRuntimeLoad(config))
        return;

    if (input.RiversPath == null)
    {
        component.MarkRuntimeNoRiverResource();
        Log.Warning("River runtime resource is not available; river rendering is disabled.");
        return;
    }

    TryGenerateMeshes(component, input.RiversPath, input.RiverMinWidth, input.RiverMaxWidth, input.RiverMaxVisibleCameraHeight, terrainComponent, config);
}
```

Extract the existing mesh-generation body into:

```csharp
private void TryGenerateMeshes(
    RiverComponent component,
    string riversPath,
    float riverMinWidth,
    float riverMaxWidth,
    float riverMaxVisibleCameraHeight,
    TerrainComponent terrainComponent,
    RiverRuntimeLoadConfig config)
```

Inside it, use `riversPath`, `riverMinWidth`, `riverMaxWidth`, and `riverMaxVisibleCameraHeight` instead of `bundle.*`.

Keep `FindInitializedTerrainComponent` fallback for one migration slice, but leave a comment:

```csharp
// Compatibility path for scenes without MapSurfaceComponent. The coordinator path above is authoritative.
```

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Rendering/River/RiverRenderSettings.cs Terrain/Rendering/River/RiverRuntimeLoadState.cs Terrain/Rendering/River/RiverComponent.cs Terrain/Rendering/River/RiverProcessor.cs Terrain/Rendering/River/RiverRenderFeature.cs Terrain.Editor/Services/RiverRenderingService.cs Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs
git commit -m "feat: drive river sea level from map settings"
```

---

## Task 5: Ocean Component, Processor, and Render Object

**Files:**
- Create: `Terrain/Rendering/Ocean/OceanComponent.cs`
- Create: `Terrain/Rendering/Ocean/OceanMaterialSettings.cs`
- Create: `Terrain/Rendering/Ocean/OceanRuntimeInput.cs`
- Create: `Terrain/Rendering/Ocean/OceanVertex.cs`
- Create: `Terrain/Rendering/Ocean/OceanRenderObject.cs`
- Create: `Terrain/Rendering/Ocean/OceanProcessor.cs`
- Create: `Terrain/Rendering/Ocean/OceanRenderGroups.cs`
- Create: `Terrain.Editor.Tests/OceanRenderingTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add failing ocean component tests**

Create `OceanRenderingTests.cs`:

```csharp
using System.Reflection;
using Stride.Engine.Design;
using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Tests;

internal static class OceanRenderingTests
{
    public static void RunAll()
    {
        TestHarness.Run("ocean component uses ocean processor", OceanComponentUsesOceanProcessor);
        TestHarness.Run("ocean component does not expose sea level as public setting", OceanComponentDoesNotExposeSeaLevelAsPublicSetting);
        TestHarness.Run("ocean vertex layout exposes position and uv", OceanVertexLayoutExposesPositionAndUv);
    }

    private static void OceanComponentUsesOceanProcessor()
    {
        var attribute = typeof(OceanComponent)
            .GetCustomAttributes()
            .OfType<DefaultEntityComponentRendererAttribute>()
            .FirstOrDefault();

        TestHarness.Assert(attribute != null, "OceanComponent should register a render processor");
        TestHarness.AssertEqual(typeof(OceanProcessor).AssemblyQualifiedName, attribute!.TypeName, "processor type");
    }

    private static void OceanComponentDoesNotExposeSeaLevelAsPublicSetting()
    {
        PropertyInfo? seaLevel = typeof(OceanComponent).GetProperty("SeaLevel", BindingFlags.Instance | BindingFlags.Public);

        TestHarness.Assert(seaLevel == null, "OceanComponent should consume runtime input instead of owning persisted sea level");
    }

    private static void OceanVertexLayoutExposesPositionAndUv()
    {
        TestHarness.AssertEqual(2, OceanVertex.Layout.VertexElements.Length, "ocean vertex element count");
    }
}
```

In `Program.cs`, add:

```csharp
OceanRenderingTests.RunAll();
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failure because ocean types do not exist.

- [ ] **Step 3: Add ocean data types**

Create `OceanRuntimeInput.cs`:

```csharp
#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Rendering.Ocean;

public readonly record struct OceanRuntimeInput(float SeaLevel, Vector2 MapWorldSize);
```

Create `OceanMaterialSettings.cs`:

```csharp
#nullable enable

namespace Terrain.Rendering.Ocean;

public sealed class OceanMaterialSettings
{
    public float NormalStrength { get; set; } = 1.0f;
    public float FoamIntensity { get; set; } = 0.6f;
    public float ReflectionIntensity { get; set; } = 0.1f;
    public float WaterDiffuseMultiplier { get; set; } = 0.4f;
}
```

Create `OceanComponent.cs`:

```csharp
#nullable enable

using Stride.Core;
using Stride.Engine;
using Stride.Engine.Design;

namespace Terrain.Rendering.Ocean;

[DataContract("OceanComponent")]
[DefaultEntityComponentRenderer(typeof(OceanProcessor))]
public sealed class OceanComponent : ActivableEntityComponent
{
    [DataMember(10)]
    public bool Visible { get; set; } = true;

    [DataMember(20)]
    public OceanMaterialSettings Material { get; set; } = new();

    [DataMemberIgnore]
    public OceanRuntimeInput? RuntimeInput { get; private set; }

    public void ApplyRuntimeInput(OceanRuntimeInput input)
    {
        RuntimeInput = input;
    }
}
```

Create `OceanRenderGroups.cs`:

```csharp
#nullable enable

using Stride.Rendering;

namespace Terrain.Rendering.Ocean;

public static class OceanRenderGroups
{
    public const RenderGroup OceanRenderGroup = RenderGroup.Group1;
    public const RenderGroupMask OceanRenderGroupMask = RenderGroupMask.Group1;
}
```

Create `OceanVertex.cs`:

```csharp
#nullable enable

using System.Runtime.InteropServices;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Terrain.Rendering.Ocean;

[StructLayout(LayoutKind.Sequential)]
public struct OceanVertex
{
    public Vector4 Position;
    public Vector2 UV;

    public OceanVertex(Vector3 position, Vector2 uv)
    {
        Position = new Vector4(position, 1.0f);
        UV = uv;
    }

    public static readonly VertexDeclaration Layout = new(
        VertexElement.Position<Vector4>(),
        VertexElement.TextureCoordinate<Vector2>(0));
}
```

- [ ] **Step 4: Add render object**

Create `OceanRenderObject.cs`:

```csharp
#nullable enable

using System;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace Terrain.Rendering.Ocean;

public sealed class OceanRenderObject : RenderObject, IDisposable
{
    public OceanComponent Source { get; init; } = null!;
    public float SeaLevel { get; private set; }
    public Vector2 MapWorldSize { get; private set; }
    public Buffer? VertexBuffer { get; private set; }
    public Buffer? IndexBuffer { get; private set; }
    public int IndexCount { get; private set; }
    public Matrix World { get; set; } = Matrix.Identity;

    public OceanRenderObject()
    {
        BoundingBox = (BoundingBoxExt)new BoundingBox(Vector3.Zero, Vector3.One);
    }

    public void Rebuild(GraphicsDevice graphicsDevice, OceanRuntimeInput input)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        ReleaseGpuResources();
        SeaLevel = input.SeaLevel;
        MapWorldSize = input.MapWorldSize;

        float width = MathF.Max(input.MapWorldSize.X, 1.0f);
        float height = MathF.Max(input.MapWorldSize.Y, 1.0f);
        var vertices = new[]
        {
            new OceanVertex(new Vector3(0.0f, input.SeaLevel, 0.0f), new Vector2(0.0f, 1.0f)),
            new OceanVertex(new Vector3(width, input.SeaLevel, 0.0f), new Vector2(1.0f, 1.0f)),
            new OceanVertex(new Vector3(0.0f, input.SeaLevel, height), new Vector2(0.0f, 0.0f)),
            new OceanVertex(new Vector3(width, input.SeaLevel, height), new Vector2(1.0f, 0.0f)),
        };
        ushort[] indices = [0, 1, 2, 2, 1, 3];

        VertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices, GraphicsResourceUsage.Dynamic);
        IndexBuffer = Buffer.Index.New(graphicsDevice, indices);
        IndexCount = indices.Length;
        BoundingBox = (BoundingBoxExt)new BoundingBox(
            new Vector3(0.0f, input.SeaLevel - 0.1f, 0.0f),
            new Vector3(width, input.SeaLevel + 0.1f, height));
    }

    public bool Matches(OceanRuntimeInput input)
    {
        return SeaLevel == input.SeaLevel && MapWorldSize == input.MapWorldSize && VertexBuffer != null && IndexBuffer != null;
    }

    public void ReleaseGpuResources()
    {
        VertexBuffer?.Dispose();
        VertexBuffer = null;
        IndexBuffer?.Dispose();
        IndexBuffer = null;
        IndexCount = 0;
    }

    public void Dispose()
    {
        ReleaseGpuResources();
    }
}
```

- [ ] **Step 5: Add processor**

Create `OceanProcessor.cs`:

```csharp
#nullable enable

using Stride.Core.Annotations;
using Stride.Engine;
using Stride.Engine.Processors;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;

namespace Terrain.Rendering.Ocean;

public sealed class OceanProcessor : EntityProcessor<OceanComponent, OceanRenderObject>, IEntityComponentRenderProcessor
{
    public VisibilityGroup VisibilityGroup { get; set; } = null!;

    protected override OceanRenderObject GenerateComponentData([NotNull] Entity entity, [NotNull] OceanComponent component)
    {
        return new OceanRenderObject
        {
            Source = component,
            RenderGroup = OceanRenderGroups.OceanRenderGroup,
        };
    }

    protected override void OnEntityComponentRemoved(Entity entity, [NotNull] OceanComponent component, [NotNull] OceanRenderObject renderObject)
    {
        VisibilityGroup?.RenderObjects.Remove(renderObject);
        renderObject.Dispose();
        base.OnEntityComponentRemoved(entity, component, renderObject);
    }

    public override void Draw(RenderContext context)
    {
        base.Draw(context);

        var graphicsDevice = Services.GetService<IGraphicsDeviceService>()?.GraphicsDevice;
        if (graphicsDevice == null || VisibilityGroup == null)
            return;

        foreach (var pair in ComponentDatas)
        {
            UpdateRenderObject(pair.Key.Entity, pair.Key, pair.Value, graphicsDevice);
        }
    }

    private void UpdateRenderObject(Entity entity, OceanComponent component, OceanRenderObject renderObject, GraphicsDevice graphicsDevice)
    {
        if (component.RuntimeInput is not { } input)
        {
            renderObject.Enabled = false;
            return;
        }

        if (!renderObject.Matches(input))
        {
            renderObject.Rebuild(graphicsDevice, input);
            if (!VisibilityGroup.RenderObjects.Contains(renderObject))
                VisibilityGroup.RenderObjects.Add(renderObject);
        }

        entity.Transform.UpdateWorldMatrix();
        renderObject.World = entity.Transform.WorldMatrix;
        renderObject.Enabled = component.Enabled && component.Visible;
        renderObject.RenderGroup = OceanRenderGroups.OceanRenderGroup;
    }
}
```

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Rendering/Ocean/OceanComponent.cs Terrain/Rendering/Ocean/OceanMaterialSettings.cs Terrain/Rendering/Ocean/OceanRuntimeInput.cs Terrain/Rendering/Ocean/OceanVertex.cs Terrain/Rendering/Ocean/OceanRenderObject.cs Terrain/Rendering/Ocean/OceanProcessor.cs Terrain/Rendering/Ocean/OceanRenderGroups.cs Terrain.Editor.Tests/OceanRenderingTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add ocean render component"
```

---

## Task 6: Ocean Resources and CK3 Flowmap

**Files:**
- Create: `Terrain/Rendering/Ocean/OceanResourceLoader.cs`
- Modify: `Terrain/Rendering/River/RiverResourceLoader.cs`
- Copy: `game/map/water/flowmap.dds`
- Test: `Terrain.Editor.Tests/OceanResourceTextTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add failing resource tests**

Create `OceanResourceTextTests.cs`:

```csharp
using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Tests;

internal static class OceanResourceTextTests
{
    public static void RunAll()
    {
        TestHarness.Run("ocean resource manifest includes ck3 flowmap", OceanResourceManifestIncludesCk3Flowmap);
        TestHarness.Run("local flowmap resource exists", LocalFlowmapResourceExists);
    }

    private static void OceanResourceManifestIncludesCk3Flowmap()
    {
        TestHarness.Assert(
            OceanResourceLoader.RequiredFileNames.Contains("flowmap.dds"),
            "ocean resource loader should require flowmap.dds");
    }

    private static void LocalFlowmapResourceExists()
    {
        string path = Path.GetFullPath(Path.Combine("game", "map", "water", "flowmap.dds"));

        TestHarness.Assert(File.Exists(path), $"flowmap.dds should exist at {path}");
    }
}
```

In `Program.cs`, add:

```csharp
OceanResourceTextTests.RunAll();
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile failure for missing loader or failing `flowmap.dds` file check.

- [ ] **Step 3: Copy flowmap resource**

Run:

```powershell
Copy-Item -LiteralPath 'E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\water\flowmap.dds' -Destination 'game\map\water\flowmap.dds' -Force
```

Expected: `game\map\water\flowmap.dds` exists.

- [ ] **Step 4: Add ocean resource loader**

Create `OceanResourceLoader.cs`:

```csharp
#nullable enable

using System;
using System.IO;
using Stride.Core.Diagnostics;
using Stride.Graphics;
using Terrain.Resources;

namespace Terrain.Rendering.Ocean;

public sealed class OceanResourceLoader : IDisposable
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");

    public static readonly string[] RequiredFileNames =
    [
        "water_color.dds",
        "ambient_normal.dds",
        "flowmap.dds",
        "flow_normal.dds",
        "foam.dds",
        "foam_ramp.dds",
        "foam_map.dds",
        "foam_noise.dds",
    ];

    public Texture? WaterColor { get; private set; }
    public Texture? AmbientNormal { get; private set; }
    public Texture? FlowMap { get; private set; }
    public Texture? FlowNormal { get; private set; }
    public Texture? Foam { get; private set; }
    public Texture? FoamRamp { get; private set; }
    public Texture? FoamMap { get; private set; }
    public Texture? FoamNoise { get; private set; }

    public bool IsLoaded => WaterColor != null
        && AmbientNormal != null
        && FlowMap != null
        && FlowNormal != null
        && Foam != null
        && FoamRamp != null
        && FoamMap != null
        && FoamNoise != null;

    public void Load(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        string gameRoot = GameResourceRootLocator.FindFromTerrainAssembly();
        string waterDirectory = Path.Combine(gameRoot, "map", "water");

        WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "water_color.dds", loadAsSrgb: false);
        AmbientNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "ambient_normal.dds", loadAsSrgb: false);
        FlowMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "flowmap.dds", loadAsSrgb: false);
        FlowNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "flow_normal.dds", loadAsSrgb: false);
        Foam = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "foam.dds", loadAsSrgb: false);
        FoamRamp = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "foam_ramp.dds", loadAsSrgb: false);
        FoamMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "foam_map.dds", loadAsSrgb: false);
        FoamNoise = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, "foam_noise.dds", loadAsSrgb: false);
    }

    public void Dispose()
    {
        DisposeLocalTexture(WaterColor);
        DisposeLocalTexture(AmbientNormal);
        DisposeLocalTexture(FlowMap);
        DisposeLocalTexture(FlowNormal);
        DisposeLocalTexture(Foam);
        DisposeLocalTexture(FoamRamp);
        DisposeLocalTexture(FoamMap);
        DisposeLocalTexture(FoamNoise);
        WaterColor = null;
        AmbientNormal = null;
        FlowMap = null;
        FlowNormal = null;
        Foam = null;
        FoamRamp = null;
        FoamMap = null;
        FoamNoise = null;
    }

    private static Texture LoadRequiredLocalTexture(GraphicsDevice graphicsDevice, string directory, string fileName, bool loadAsSrgb)
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
            loadAsSrgb);
    }

    private static void DisposeLocalTexture(Texture? texture)
    {
        texture?.Dispose();
    }
}
```

- [ ] **Step 5: Add flowmap to RiverResourceLoader for shared water parity**

In `RiverResourceLoader`, add:

```csharp
private const string FlowMapFileName = "flowmap.dds";
public Texture? FlowMap { get; private set; }
```

Load:

```csharp
FlowMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowMapFileName, loadAsSrgb: false);
```

Dispose/reset:

```csharp
DisposeLocalTexture(FlowMap);
FlowMap = null;
```

This does not force river shader usage in this task; it makes the shared water directory complete.

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
git diff --check
```

Expected: tests pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Rendering/Ocean/OceanResourceLoader.cs Terrain/Rendering/River/RiverResourceLoader.cs Terrain.Editor.Tests/OceanResourceTextTests.cs Terrain.Editor.Tests/Program.cs game/map/water/flowmap.dds
git commit -m "feat: add ocean water resources"
```

---

## Task 7: Shared Scene Lighting Binder

**Files:**
- Create: `Terrain/Rendering/Water/WaterSceneLightingBinder.cs`
- Modify: `Terrain/Rendering/River/RiverRenderFeature.cs`
- Test: `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- [ ] **Step 1: Add failing text test**

In `RiverShaderTextTests.RunAll`, add:

```csharp
TestHarness.Run("river render feature uses shared water lighting binder", RiverRenderFeatureUsesSharedWaterLightingBinder);
```

Add:

```csharp
private static void RiverRenderFeatureUsesSharedWaterLightingBinder()
{
    string text = File.ReadAllText(Path.Combine("Terrain", "Rendering", "River", "RiverRenderFeature.cs"));

    TestHarness.Assert(text.Contains("WaterSceneLightingBinder", StringComparison.Ordinal), "RiverRenderFeature should share lighting binding with ocean");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: text test fails because river still owns private lighting binding.

- [ ] **Step 3: Create binder by moving existing logic**

Create `WaterSceneLightingBinder.cs` and move the following river private responsibilities into it without changing semantics:

```csharp
public sealed class WaterSceneLightingBinder
{
    public WaterSceneLightingBinder(
        RootRenderFeature owner,
        ForwardLightingRenderFeature? forwardLightingFeature,
        IShadowMapRenderer? shadowMapRenderer)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ForwardLightingFeature = forwardLightingFeature;
        ShadowMapRenderer = shadowMapRenderer;
    }

    public RootRenderFeature Owner { get; }
    public ForwardLightingRenderFeature? ForwardLightingFeature { get; }
    public IShadowMapRenderer? ShadowMapRenderer { get; }

    public void Bind(RenderDrawContext context, RenderView renderView, params DynamicEffectInstance?[] effects)
    {
        // Copy the existing RiverRenderFeature logic:
        // - resolve lighting view
        // - get ForwardLightingRenderFeature.RenderViewLightData via reflection
        // - choose strongest directional light and shadow map
        // - choose strongest skybox with cubemap
        // - bind RiverStrideLightingKeys to every non-null effect
        // - call UpdateEffect(context.GraphicsDevice) for every non-null effect
    }
}
```

The method body must be the existing code from `PrepareRiverSceneLighting`, `TryGetRenderViewLightData`, `CollectFallbackVisibleLights`, `SelectBottomDirectionalLight`, `SelectBottomSkyboxLight`, `TryGetSceneShadowMapTexture`, `BindDirectionalLightToEffect`, `BindEnvironmentToEffect`, and `TryGetSceneEnvironmentTexture`, adapted so it iterates over `effects`.

- [ ] **Step 4: Update RiverRenderFeature**

In `RiverRenderFeature`, add field:

```csharp
private WaterSceneLightingBinder? sceneLightingBinder;
```

In `InitializeCore`, after `bottomShadowMapRenderer` is set:

```csharp
sceneLightingBinder = new WaterSceneLightingBinder(this, forwardLightingFeature, bottomShadowMapRenderer);
```

In `Destroy`, set:

```csharp
sceneLightingBinder = null;
```

Replace:

```csharp
PrepareRiverSceneLighting(context, renderView);
```

with:

```csharp
sceneLightingBinder?.Bind(context, renderView, bottomEffect, surfaceEffect);
```

Remove the old private lighting-binding methods from `RiverRenderFeature`.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests and build pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Rendering/Water/WaterSceneLightingBinder.cs Terrain/Rendering/River/RiverRenderFeature.cs Terrain.Editor.Tests/RiverShaderTextTests.cs
git commit -m "refactor: share water scene lighting binding"
```

---

## Task 8: Ocean Shader and Render Feature

**Files:**
- Create: `Terrain/Effects/Ocean/OceanVertexStreams.sdsl`
- Create: `Terrain/Effects/Ocean/OceanSurface.sdsl`
- Generate: `Terrain/Effects/Ocean/OceanVertexStreams.sdsl.cs`
- Generate: `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- Modify: `Terrain/Terrain.csproj`
- Create: `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
- Test: `Terrain.Editor.Tests/OceanShaderTextTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add failing shader text tests**

Create `OceanShaderTextTests.cs`:

```csharp
namespace Terrain.Editor.Tests;

internal static class OceanShaderTextTests
{
    public static void RunAll()
    {
        TestHarness.Run("ocean shader samples required water textures", OceanShaderSamplesRequiredWaterTextures);
        TestHarness.Run("ocean shader uses stride lighting helper", OceanShaderUsesStrideLightingHelper);
        TestHarness.Run("ocean shader avoids ck3-only map inputs", OceanShaderAvoidsCk3OnlyMapInputs);
    }

    private static void OceanShaderSamplesRequiredWaterTextures()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Effects", "Ocean", "OceanSurface.sdsl"));

        string[] required =
        [
            "WaterColorTexture",
            "AmbientNormalTexture",
            "FlowMapTexture",
            "FlowNormalTexture",
            "FoamTexture",
            "FoamRampTexture",
            "FoamMapTexture",
            "FoamNoiseTexture",
        ];

        foreach (string token in required)
            TestHarness.Assert(text.Contains(token, StringComparison.Ordinal), $"OceanSurface should reference {token}");
    }

    private static void OceanShaderUsesStrideLightingHelper()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Effects", "Ocean", "OceanSurface.sdsl"));

        TestHarness.Assert(text.Contains("RiverStrideLighting", StringComparison.Ordinal), "OceanSurface should reuse Stride-style scene lighting helper");
        TestHarness.Assert(text.Contains("RiverStrideComputeLighting", StringComparison.Ordinal), "OceanSurface should call Stride-style lighting computation");
    }

    private static void OceanShaderAvoidsCk3OnlyMapInputs()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Effects", "Ocean", "OceanSurface.sdsl"));

        string[] banned =
        [
            "ProvinceColorTexture",
            "BorderDistanceField",
            "PatternTexture",
            "FogOfWarAlpha",
            "FlatMapTexture",
            "_WaterToSunDir",
            "_DefaultEnvironmentSun",
        ];

        foreach (string token in banned)
            TestHarness.Assert(!text.Contains(token, StringComparison.Ordinal), $"OceanSurface should not reference {token}");
    }
}
```

In `Program.cs`, add:

```csharp
OceanShaderTextTests.RunAll();
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: file-not-found failure for ocean shader files.

- [ ] **Step 3: Add OceanVertexStreams.sdsl**

Create:

```c
namespace Terrain
{
    shader OceanVertexStreams
    {
        stage stream float2 OceanUV : TEXCOORD0;
    };
}
```

- [ ] **Step 4: Add OceanSurface.sdsl**

Create:

```c
namespace Terrain
{
    shader OceanSurface : ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting
    {
        stage float _SeaLevel = 3.8f;
        stage float2 _MapWorldSize = float2(4096.0f, 4096.0f);
        stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);
        stage float _GlobalTime = 0.0f;
        stage float _NormalStrength = 1.0f;
        stage float _FoamIntensity = 0.6f;
        stage float _ReflectionIntensity = 0.1f;
        stage float _WaterDiffuseMultiplier = 0.4f;

        stage Texture2D<float4> WaterColorTexture;
        stage Texture2D<float4> AmbientNormalTexture;
        stage Texture2D<float4> FlowMapTexture;
        stage Texture2D<float4> FlowNormalTexture;
        stage Texture2D<float4> FoamTexture;
        stage Texture2D<float4> FoamRampTexture;
        stage Texture2D<float4> FoamMapTexture;
        stage Texture2D<float4> FoamNoiseTexture;
        stage SamplerState OceanTextureSampler;

        float3 DecodeOceanNormal(float4 packedNormal)
        {
            return normalize(packedNormal.xyz * 2.0f - 1.0f);
        }

        float2 FlowOffset(float2 uv, float2 flowVector, float speed)
        {
            return uv + flowVector * (_GlobalTime * speed);
        }

        stage override void PSMain()
        {
            float2 uv = saturate(streams.OceanUV);
            float4 waterColorAndSpec = WaterColorTexture.Sample(OceanTextureSampler, uv);
            float4 flowMap = FlowMapTexture.Sample(OceanTextureSampler, uv);
            float2 flowVector = flowMap.xy * 2.0f - 1.0f;
            float2 flowUv = FlowOffset(uv, flowVector, 0.025f);

            float3 ambientNormal = DecodeOceanNormal(AmbientNormalTexture.Sample(OceanTextureSampler, uv)).xzy;
            float3 flowNormal = DecodeOceanNormal(FlowNormalTexture.Sample(OceanTextureSampler, flowUv)).xzy;
            float3 normal = normalize(lerp(float3(0.0f, 1.0f, 0.0f), normalize(ambientNormal + flowNormal), saturate(_NormalStrength)));

            float foamMask = FoamMapTexture.Sample(OceanTextureSampler, uv).r;
            float foamNoise = FoamNoiseTexture.Sample(OceanTextureSampler, flowUv).r;
            float foam = FoamTexture.Sample(OceanTextureSampler, uv * 16.0f + flowVector * foamNoise).r * foamMask * _FoamIntensity;
            float3 foamColor = FoamRampTexture.Sample(OceanTextureSampler, float2(saturate(foam), 0.5f)).rgb;

            float3 viewDir = normalize(_CameraWorldPosition - streams.PositionWS.xyz);
            float3 diffuse = waterColorAndSpec.rgb * _WaterDiffuseMultiplier + foamColor * foam;
            float glossiness = saturate(0.7f + waterColorAndSpec.a * 0.25f);
            float3 specular = float3(0.02f, 0.02f, 0.02f) + waterColorAndSpec.a * _ReflectionIntensity;
            float3 lit = RiverStrideComputeLighting(diffuse, specular, glossiness, normal, viewDir, 1.0f, 1.0f);

            streams.ColorTarget = float4(lit, 0.92f);
        }
    };
}
```

- [ ] **Step 5: Add csproj shader key entries**

In `Terrain.csproj`, add compile entries:

```xml
<Compile Update="Effects\Ocean\OceanVertexStreams.sdsl.cs">
  <DesignTime>True</DesignTime>
  <DesignTimeSharedInput>True</DesignTimeSharedInput>
  <AutoGen>True</AutoGen>
</Compile>
<Compile Update="Effects\Ocean\OceanSurface.sdsl.cs">
  <DesignTime>True</DesignTime>
  <DesignTimeSharedInput>True</DesignTimeSharedInput>
  <AutoGen>True</AutoGen>
</Compile>
```

Add none entries:

```xml
<None Update="Effects\Ocean\OceanVertexStreams.sdsl">
  <LastGenOutput>OceanVertexStreams.sdsl.cs</LastGenOutput>
</None>
<None Update="Effects\Ocean\OceanSurface.sdsl">
  <LastGenOutput>OceanSurface.sdsl.cs</LastGenOutput>
</None>
```

- [ ] **Step 6: Add OceanRenderFeature**

Create `OceanRenderFeature.cs`:

```csharp
#nullable enable

using System;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Terrain.Rendering.Water;

namespace Terrain.Rendering.Ocean;

public sealed class OceanRenderFeature : RootRenderFeature
{
    private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");
    private static readonly InputElementDescription[] OceanInputElements = OceanVertex.Layout.CreateInputElements();
    private readonly OceanResourceLoader oceanResources = new();
    private DynamicEffectInstance? oceanEffect;
    private MutablePipelineState? pipelineState;
    private WaterSceneLightingBinder? sceneLightingBinder;
    private ForwardLightingRenderFeature? forwardLightingFeature;

    public override Type SupportedRenderObjectType => typeof(OceanRenderObject);

    public OceanRenderFeature()
    {
        SortKey = 185;
    }

    protected override void InitializeCore()
    {
        base.InitializeCore();

        oceanEffect = new DynamicEffectInstance("OceanSurface");
        oceanEffect.Initialize(Context.Services);

        try
        {
            oceanResources.Load(Context.GraphicsDevice);
        }
        catch (Exception exception)
        {
            Log.Error($"Ocean render resources could not be loaded: {exception.Message}");
            oceanResources.Dispose();
        }

        var meshRenderFeature = RenderSystem?.RenderFeatures.OfType<MeshRenderFeature>().FirstOrDefault();
        forwardLightingFeature = meshRenderFeature?.RenderFeatures.OfType<ForwardLightingRenderFeature>().FirstOrDefault();
        sceneLightingBinder = new WaterSceneLightingBinder(this, forwardLightingFeature, forwardLightingFeature?.ShadowMapRenderer);

        pipelineState = CreatePipelineState(Context.GraphicsDevice);
        BindStaticResources(Context.GraphicsDevice);
    }

    protected override void Destroy()
    {
        oceanResources.Dispose();
        oceanEffect?.Dispose();
        oceanEffect = null;
        pipelineState = null;
        sceneLightingBinder = null;
        forwardLightingFeature = null;
        base.Destroy();
    }

    public override void Prepare(RenderDrawContext context)
    {
        base.Prepare(context);
        if (oceanEffect == null)
            return;

        OceanRenderObject? ocean = RenderObjects.OfType<OceanRenderObject>().FirstOrDefault(static obj => obj.Enabled && obj.VertexBuffer != null && obj.IndexBuffer != null);
        if (ocean == null)
            return;

        oceanEffect.Parameters.Set(OceanSurfaceKeys._SeaLevel, ocean.SeaLevel);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._MapWorldSize, ocean.MapWorldSize);
        if (ocean.Source.Material is { } material)
        {
            oceanEffect.Parameters.Set(OceanSurfaceKeys._NormalStrength, material.NormalStrength);
            oceanEffect.Parameters.Set(OceanSurfaceKeys._FoamIntensity, material.FoamIntensity);
            oceanEffect.Parameters.Set(OceanSurfaceKeys._ReflectionIntensity, material.ReflectionIntensity);
            oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterDiffuseMultiplier, material.WaterDiffuseMultiplier);
        }
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
    {
        if (oceanEffect == null || pipelineState == null || startIndex >= endIndex || !oceanResources.IsLoaded)
            return;

        Matrix.Invert(ref renderView.View, out Matrix viewInverse);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._CameraWorldPosition, viewInverse.TranslationVector);
        oceanEffect.Parameters.Set(OceanSurfaceKeys._GlobalTime, (float)context.RenderContext.Time.Total.TotalSeconds);
        oceanEffect.Parameters.Set(TransformationKeys.ViewProjection, renderView.ViewProjection);
        sceneLightingBinder?.Bind(context, renderView, oceanEffect);

        pipelineState.State.RootSignature = oceanEffect.RootSignature;
        pipelineState.State.EffectBytecode = oceanEffect.Effect.Bytecode;
        pipelineState.State.InputElements = OceanInputElements;
        pipelineState.State.Output.CaptureState(context.CommandList);
        pipelineState.Update();
        if (pipelineState.CurrentState == null)
            return;

        context.CommandList.SetPipelineState(pipelineState.CurrentState);

        for (int index = startIndex; index < endIndex; index++)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            if (renderNode.RenderObject is not OceanRenderObject ocean
                || !ocean.Enabled
                || ocean.VertexBuffer == null
                || ocean.IndexBuffer == null)
            {
                continue;
            }

            oceanEffect.Parameters.Set(TransformationKeys.World, ocean.World);
            oceanEffect.Parameters.Set(TransformationKeys.WorldViewProjection, ocean.World * renderView.ViewProjection);
            oceanEffect.Apply(context.GraphicsContext);
            context.CommandList.SetVertexBuffer(0, ocean.VertexBuffer, 0, OceanVertex.Layout.VertexStride);
            context.CommandList.SetIndexBuffer(ocean.IndexBuffer, 0, true);
            context.CommandList.DrawIndexed(ocean.IndexCount);
        }
    }

    private void BindStaticResources(GraphicsDevice graphicsDevice)
    {
        if (oceanEffect == null)
            return;

        oceanEffect.Parameters.Set(OceanSurfaceKeys.OceanTextureSampler, graphicsDevice.SamplerStates.LinearWrap);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.WaterColorTexture, oceanResources.WaterColor);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.AmbientNormalTexture, oceanResources.AmbientNormal);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FlowMapTexture, oceanResources.FlowMap);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FlowNormalTexture, oceanResources.FlowNormal);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FoamTexture, oceanResources.Foam);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FoamRampTexture, oceanResources.FoamRamp);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FoamMapTexture, oceanResources.FoamMap);
        oceanEffect.Parameters.SetObject(OceanSurfaceKeys.FoamNoiseTexture, oceanResources.FoamNoise);
        oceanEffect.Parameters.Set(RiverStrideLightingKeys.EnvironmentMapSampler, graphicsDevice.SamplerStates.LinearClamp);
    }

    private static MutablePipelineState CreatePipelineState(GraphicsDevice graphicsDevice)
    {
        var pipeline = new MutablePipelineState(graphicsDevice);
        pipeline.State.PrimitiveType = PrimitiveType.TriangleList;
        pipeline.State.BlendState = BlendStateDescription.AlphaBlend;
        pipeline.State.DepthStencilState = DepthStencilStateDescription.DepthRead;
        pipeline.State.RasterizerState = RasterizerStateDescription.CullBack;
        return pipeline;
    }
}
```

- [ ] **Step 7: Generate shader keys and compile assets**

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Expected:

- generated `OceanVertexStreams.sdsl.cs`
- generated `OceanSurface.sdsl.cs`
- Stride asset compile succeeds

- [ ] **Step 8: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests/build pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Effects/Ocean/OceanVertexStreams.sdsl Terrain/Effects/Ocean/OceanSurface.sdsl Terrain/Effects/Ocean/OceanVertexStreams.sdsl.cs Terrain/Effects/Ocean/OceanSurface.sdsl.cs Terrain/Terrain.csproj Terrain/Rendering/Ocean/OceanRenderFeature.cs Terrain.Editor.Tests/OceanShaderTextTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add ocean shader render feature"
```

---

## Task 9: Scene, Compositor, and Editor Runtime Wiring

**Files:**
- Modify: `Terrain/Assets/MainScene.sdscene`
- Modify: `Terrain/Assets/GraphicsCompositor.sdgfxcomp`
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- Modify: `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs`
- Create: `Terrain.Editor/Services/OceanRenderingService.cs`
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs`
- Test: `Terrain.Editor.Tests/RuntimeOceanAssetTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`
- Modify: `game/map/default.toml`

- [ ] **Step 1: Add failing asset text tests**

Create `RuntimeOceanAssetTests.cs`:

```csharp
namespace Terrain.Editor.Tests;

internal static class RuntimeOceanAssetTests
{
    public static void RunAll()
    {
        TestHarness.Run("main scene contains map surface and ocean entities", MainSceneContainsMapSurfaceAndOceanEntities);
        TestHarness.Run("graphics compositor contains ocean render feature", GraphicsCompositorContainsOceanRenderFeature);
        TestHarness.Run("default map writes sea level", DefaultMapWritesSeaLevel);
    }

    private static void MainSceneContainsMapSurfaceAndOceanEntities()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Assets", "MainScene.sdscene"));

        TestHarness.Assert(text.Contains("Name: MapSurfaceRoot", StringComparison.Ordinal), "MainScene should contain MapSurfaceRoot entity");
        TestHarness.Assert(text.Contains("!Terrain.MapSurface.MapSurfaceComponent,Terrain", StringComparison.Ordinal), "MainScene should contain MapSurfaceComponent");
        TestHarness.Assert(text.Contains("Name: Ocean", StringComparison.Ordinal), "MainScene should contain Ocean entity");
        TestHarness.Assert(text.Contains("!Terrain.Rendering.Ocean.OceanComponent,Terrain", StringComparison.Ordinal), "MainScene should contain OceanComponent");
    }

    private static void GraphicsCompositorContainsOceanRenderFeature()
    {
        string text = File.ReadAllText(Path.Combine("Terrain", "Assets", "GraphicsCompositor.sdgfxcomp"));

        TestHarness.Assert(text.Contains("!Terrain.Rendering.Ocean.OceanRenderFeature,Terrain", StringComparison.Ordinal), "GraphicsCompositor should register OceanRenderFeature");
        TestHarness.Assert(text.Contains("EffectName: OceanSurface", StringComparison.Ordinal), "GraphicsCompositor should route OceanSurface");
    }

    private static void DefaultMapWritesSeaLevel()
    {
        string text = File.ReadAllText(Path.Combine("game", "map", "default.toml"));

        TestHarness.Assert(text.Contains("sea_level = 3.8", StringComparison.Ordinal), "default map should persist sea level");
    }
}
```

In `Program.cs`, add:

```csharp
RuntimeOceanAssetTests.RunAll();
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: asset text tests fail until scene/compositor/default TOML are updated.

- [ ] **Step 3: Add editor ocean service**

Create `OceanRenderingService.cs`:

```csharp
#nullable enable

using System;
using Terrain.Rendering.Ocean;

namespace Terrain.Editor.Services;

public sealed class OceanRenderingService
{
    private readonly OceanComponent oceanComponent;

    public OceanRenderingService(OceanComponent oceanComponent)
    {
        this.oceanComponent = oceanComponent ?? throw new ArgumentNullException(nameof(oceanComponent));
    }

    public OceanComponent OceanComponent => oceanComponent;

    public void SetVisible(bool visible)
    {
        oceanComponent.Visible = visible;
        oceanComponent.Enabled = visible;
    }

    public void SetSeaLevel(float value)
    {
        if (oceanComponent.RuntimeInput is { } input)
            oceanComponent.ApplyRuntimeInput(input with { SeaLevel = value });
    }
}
```

In `NativeStrideViewportHost`, expose:

```csharp
public OceanRenderingService? OceanRenderingService => _game?.OceanRenderingService;
```

In `EmbeddedStrideViewportGame`, add fields:

```csharp
private Entity? _mapSurfaceEntity;
private Entity? _oceanEntity;
private OceanComponent? _oceanComponent;
```

Add property:

```csharp
public OceanRenderingService? OceanRenderingService { get; private set; }
```

- [ ] **Step 4: Create editor MapSurface/Ocean entities**

In `InitializeTerrainManager`, after river entity creation, add:

```csharp
_oceanEntity = new Entity("Ocean");
_oceanComponent = new OceanComponent();
_oceanEntity.Add(_oceanComponent);
_scene!.Entities.Add(_oceanEntity);

_mapSurfaceEntity = new Entity("MapSurfaceRoot");
_mapSurfaceEntity.Add(new MapSurfaceComponent
{
    TerrainEntity = null,
    RiverEntity = _riverEntity,
    OceanEntity = _oceanEntity,
});
_scene.Entities.Add(_mapSurfaceEntity);

OceanRenderingService = new OceanRenderingService(_oceanComponent);
```

When the editor terrain entity is created after loading, assign the map-surface terrain reference. Add a helper:

```csharp
private void UpdateMapSurfaceTerrainEntity(Entity? terrainEntity)
{
    var component = _mapSurfaceEntity?.Get<MapSurfaceComponent>();
    if (component != null)
        component.TerrainEntity = terrainEntity;
}
```

Call it from `OnTerrainLoaded` after editor terrain scene entity exists. If the editor uses `EditorTerrainComponent` instead of runtime `TerrainComponent`, keep the runtime scene `TerrainComponent` path for `MainScene` and use the existing editor river generation path until the editor terrain runtime component is available. Do not force ocean to depend on editor CPU terrain beyond map dimensions.

- [ ] **Step 5: Ensure ocean render feature in editor compositor**

Add:

```csharp
private static void EnsureOceanRenderFeature(GraphicsCompositor graphicsCompositor)
{
    if (graphicsCompositor.RenderFeatures.OfType<OceanRenderFeature>().Any())
        return;

    var oceanRenderFeature = new OceanRenderFeature();
    var transparentStage = graphicsCompositor.RenderStages.FirstOrDefault(stage =>
        string.Equals(stage.Name, "Transparent", StringComparison.Ordinal));
    if (transparentStage == null)
    {
        transparentStage = new RenderStage("OceanTransparent", "Main")
        {
            SortMode = new BackToFrontSortMode(),
        };
        graphicsCompositor.RenderStages.Add(transparentStage);
    }

    oceanRenderFeature.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector
    {
        EffectName = "OceanSurface",
        RenderGroup = OceanRenderGroups.OceanRenderGroupMask,
        RenderStage = transparentStage,
    });
    graphicsCompositor.RenderFeatures.Add(oceanRenderFeature);
}
```

Call it in both scene initialization paths after `EnsureRiverRenderFeature`.

- [ ] **Step 6: Update runtime assets and default map**

In `game/map/default.toml`, add:

```toml
sea_level = 3.8
```

In `MainScene.sdscene`, add an `Ocean` entity with `OceanComponent` and a `MapSurfaceRoot` entity with `MapSurfaceComponent` that references the existing `Terrain`, existing `RiverSystem`, and new `Ocean` entities.

In `GraphicsCompositor.sdgfxcomp`, add an `OceanRenderFeature` block with selector:

```yaml
!Terrain.Rendering.Ocean.OceanRenderFeature,Terrain
    RenderStageSelectors:
        <new-id>: !Stride.Rendering.SimpleGroupToRenderStageSelector,Stride.Rendering
            RenderStage: ref!! 0fbd7f2d-8037-4033-9616-14d59c88b1fd
            EffectName: OceanSurface
            RenderGroup: Group1
```

Generate new GUIDs for scene/compositor YAML entries. Keep existing terrain, camera, light, skybox, and river entries unchanged.

- [ ] **Step 7: Run tests and commit**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests/build pass; diff check prints no output.

Commit:

```powershell
git add -- Terrain/Assets/MainScene.sdscene Terrain/Assets/GraphicsCompositor.sdgfxcomp Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs Terrain.Editor/Services/OceanRenderingService.cs Terrain.Editor/Services/RiverRenderingService.cs Terrain.Editor.Tests/RuntimeOceanAssetTests.cs Terrain.Editor.Tests/Program.cs game/map/default.toml
git commit -m "feat: wire ocean scene entities"
```

---

## Task 10: Shader Asset Verification and Runtime Smoke

**Files:**
- Verify: `Terrain/Effects/Ocean/OceanSurface.sdsl`
- Verify: `Terrain/Effects/Ocean/OceanSurface.sdsl.cs`
- Verify: `Terrain/Assets/GraphicsCompositor.sdgfxcomp`
- Verify: `Terrain/Assets/MainScene.sdscene`

- [ ] **Step 1: Run Stride generated-file update**

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
```

Expected: command exits 0 and generated ocean shader key files exist.

- [ ] **Step 2: Run Stride asset clean/compile**

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Expected: both commands exit 0. If `OceanSurface` fails, fix shader code before continuing.

- [ ] **Step 3: Run full managed tests and build**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests/build pass; diff check prints no output.

- [ ] **Step 4: Run editor smoke**

Run:

```powershell
dotnet run --project Terrain.Editor\Terrain.Editor.csproj --no-restore
```

Manual checks:

- editor starts without ocean-related exceptions
- settings panel shows `Show Ocean` and `Sea Level`
- ocean is visible as a full-map horizontal plane
- changing `Sea Level` changes ocean height
- changing `Sea Level` changes river bottom ocean fade through `RiverBottomKeys._WaterHeight`
- saving writes `sea_level` to `game/map/default.toml`

- [ ] **Step 5: Capture visual evidence**

Use one of these verification paths:

```text
Editor screenshot:
  - capture viewport showing the ocean plane
  - compare water height after changing Sea Level

RenderDoc:
  - capture a frame
  - confirm an OceanSurface draw call
  - confirm water_color, ambient_normal, flowmap, flow_normal, foam textures are bound
  - confirm _SeaLevel equals the editor setting
```

- [ ] **Step 6: Commit verification fixes**

If smoke testing required code or asset fixes, run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.sln --no-restore
git diff --check
```

Commit verified fixes:

```powershell
git add -- Terrain Terrain.Editor Terrain.Editor.Tests game/map/default.toml game/map/water/flowmap.dds
git commit -m "fix: verify ocean runtime rendering"
```

---

## Task 11: Documentation and Session Log

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Create: `docs/log/2026/06/23/2026-06-23-ocean-system-implementation.md`

- [ ] **Step 1: Update architecture overview**

Add a concise row or paragraph covering:

```text
MapSurfaceRoot coordinates independent Terrain, River, and Ocean entities through MapSurfaceComponent. sea_level is stored in game/map/default.toml [settings] and distributed to OceanSurface and RiverBottom _WaterHeight. Ocean rendering is a full-map horizontal plane using game/map/water CK3-style textures and Stride scene lighting.
```

- [ ] **Step 2: Update current features**

Add or update the rendering table with:

```text
| 海洋渲染 | ✅ | Terrain/Rendering/Ocean/, Terrain/Effects/Ocean/, game/map/water/ | 全地图水平 Ocean plane；Editor 可调 sea_level 并保存；Ocean 和 RiverBottom 共用 sea_level；贴图来自 game/map/water，新增 flowmap.dds；shader 使用 Stride scene lighting/skybox 输入而不是 CK3 固定 sun/ambient。 |
```

- [ ] **Step 3: Write session log**

Create `docs/log/2026/06/23/2026-06-23-ocean-system-implementation.md` using `docs/log/TEMPLATE.md` structure. Include:

```text
Summary:
- Added sea_level map setting.
- Added MapSurface coordinator entity/component.
- Added Ocean component/processor/render feature/shader.
- Wired river under-ocean fade to the same sea level.
- Added CK3 flowmap.dds to game/map/water.

Validation:
- dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
- dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
- dotnet build Terrain.sln --no-restore
- Editor smoke / screenshot or RenderDoc capture result

Follow-up:
- Coastline clipping and shoreline foam remain outside this slice.
```

- [ ] **Step 4: Run final checks**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests/build/asset compile pass; diff check prints no output.

- [ ] **Step 5: Commit docs**

Commit:

```powershell
git add -- docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/23/2026-06-23-ocean-system-implementation.md
git commit -m "docs: record ocean system implementation"
```

---

## Final Verification Checklist

- [ ] `game/map/default.toml` contains `sea_level = 3.8`.
- [ ] `RuntimeMapDefinitionReader` defaults missing sea level to `3.8`.
- [ ] Editor settings expose `Show Ocean` and `Sea Level`.
- [ ] Save persists the current sea level.
- [ ] Runtime scene contains independent `MapSurfaceRoot`, `Terrain`, `RiverSystem`, and `Ocean` entities.
- [ ] `MapSurfaceComponent` references terrain/river/ocean entities and does not expose a public `SeaLevel` property.
- [ ] `RiverRenderFeature` binds `_WaterHeight` from map sea level, not hard-coded `3.0`.
- [ ] `RiverProcessor` uses coordinator-provided runtime input before its compatibility fallback.
- [ ] `OceanRenderObject` creates a full-map horizontal plane after terrain dimensions are known.
- [ ] `OceanRenderFeature` binds all required water textures, including `flowmap.dds`.
- [ ] Ocean shader uses `RiverStrideLighting`/Stride scene lighting inputs and avoids CK3-only province/fog/flatmap inputs.
- [ ] Stride shader generated keys and asset compile succeed.
- [ ] Editor smoke or RenderDoc capture proves visible water.
