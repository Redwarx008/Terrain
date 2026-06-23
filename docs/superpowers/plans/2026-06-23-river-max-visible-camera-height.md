# River Max Visible Camera Height Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `river_max_visible_camera_height`, expose it in Editor settings, persist it in map TOML, and skip the full river render chain when the camera is at or above the configured height.

**Architecture:** Treat the cutoff as river render settings, not mesh or shader behavior. Config flows from `RuntimeMapDefinition` through Editor/runtime bootstrap into `RiverRenderSettings`, and `RiverRenderFeature.Draw` skips seed/bottom/surface work before allocating river render targets.

**Tech Stack:** C#/.NET, Stride render feature pipeline, Avalonia AXAML, Tommy TOML, existing `Terrain.Editor.Tests` harness.

---

## File Structure

- Modify `Terrain/Resources/RuntimeMapDefinition.cs`: add `RiverMaxVisibleCameraHeight`.
- Modify `Terrain/Resources/RuntimeMapDefinitionReader.cs`: allow/read/validate `river_max_visible_camera_height`.
- Modify `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`: validate/write the new field.
- Modify `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`: scaffold default value through `RuntimeMapDefinition`.
- Modify `Terrain/Resources/TerrainRuntimeResourceBundle.cs`: carry runtime cutoff.
- Modify `Terrain/Resources/GameRuntimeResourceBootstrap.cs`: copy cutoff into runtime bundle.
- Modify `Terrain/Rendering/River/RiverRuntimeLoadState.cs`: include cutoff in runtime load config equality.
- Modify `Terrain/Rendering/River/RiverRenderSettings.cs`: add render setting default.
- Modify `Terrain/Rendering/River/RiverRenderObject.cs`: copy setting and expose it.
- Modify `Terrain/Rendering/River/RiverRenderFeature.cs`: resolve cutoff and skip draw when camera is too high.
- Modify `Terrain.Editor/ViewModels/SettingsViewModel.cs`: add Editor setting.
- Modify `Terrain.Editor/ViewModels/EditorShellViewModel.cs`: sync setting load/change/save.
- Modify `Terrain.Editor/Views/MainWindow.axaml`: expose editable control.
- Modify `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`: carry cutoff into background save.
- Modify `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`: save cutoff to TOML.
- Modify tests under `Terrain.Editor.Tests/VirtualResources` and `Terrain.Editor.Tests/RiverShaderTextTests.cs`.
- Update docs and session log after implementation.

---

## Task 1: Configuration Model and TOML Roundtrip

**Files:**
- Modify: `Terrain/Resources/RuntimeMapDefinition.cs`
- Modify: `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- Modify: `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorMapDataScaffoldTests.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs`

- [ ] **Step 1: Add failing reader/writer tests**

Add test coverage that:

```csharp
TestHarness.AssertEqual(3000.0f, map.RiverMaxVisibleCameraHeight, "missing river_max_visible_camera_height should default to 3000");
TestHarness.AssertEqual(750.0f, map.RiverMaxVisibleCameraHeight, "explicit river_max_visible_camera_height should round-trip");
TestHarness.AssertThrows<InvalidDataException>(
    () => RuntimeMapDefinitionReader.ReadFrom(output),
    "non-positive river_max_visible_camera_height should be rejected");
```

Also assert scaffolded `default.toml` contains:

```text
river_max_visible_camera_height = 3000
```

- [ ] **Step 2: Run focused tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -- --filter map
```

Expected: failing assertions or compile errors for `RiverMaxVisibleCameraHeight`.

- [ ] **Step 3: Implement config model**

Add to `RuntimeMapDefinition`:

```csharp
public float RiverMaxVisibleCameraHeight { get; init; } = 3000.0f;
```

Update `RuntimeMapDefinitionReader`:

```csharp
ValidateTableKeys(settings, filePath, "settings", "height_scale", "river_min_width", "river_max_width", "river_max_visible_camera_height");
float riverMaxVisibleCameraHeight = ReadOptionalFloat(settings, "river_max_visible_camera_height", filePath, 3000.0f);
ValidateRiverMaxVisibleCameraHeight(riverMaxVisibleCameraHeight, filePath);
```

Add validation:

```csharp
private static void ValidateRiverMaxVisibleCameraHeight(float value, string filePath)
{
    if (!float.IsFinite(value))
        throw new InvalidDataException($"river_max_visible_camera_height must be finite: {filePath}");
    if (value <= 0.0f)
        throw new InvalidDataException($"river_max_visible_camera_height must be greater than 0: {filePath}");
}
```

Set the returned model property:

```csharp
RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight,
```

- [ ] **Step 4: Implement writer/scaffold**

In `MapDefinitionWriter.Write`, validate and emit:

```csharp
ValidateRiverMaxVisibleCameraHeight(mapDefinition.RiverMaxVisibleCameraHeight);
...
["river_max_visible_camera_height"] = mapDefinition.RiverMaxVisibleCameraHeight,
```

Add writer validation:

```csharp
private static void ValidateRiverMaxVisibleCameraHeight(float value)
{
    if (!float.IsFinite(value))
        throw new InvalidDataException("Map definition river_max_visible_camera_height must be finite.");
    if (value <= 0.0f)
        throw new InvalidDataException("Map definition river_max_visible_camera_height must be greater than 0.");
}
```

No explicit scaffold assignment is required if `RuntimeMapDefinition` default is used, but adding it is acceptable:

```csharp
RiverMaxVisibleCameraHeight = 3000.0f,
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -- --filter default
```

Expected: relevant config/scaffold tests pass.

---

## Task 2: Editor Save and Settings UI

**Files:**
- Modify: `Terrain.Editor/ViewModels/SettingsViewModel.cs`
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
- Modify: `Terrain.Editor/Views/MainWindow.axaml`
- Modify: `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs`
- Modify: `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs`
- Test: `Terrain.Editor.Tests/VirtualResources/EditorResourceSaveServiceTests.cs`

- [ ] **Step 1: Add failing save roundtrip test**

Add or update a save-service test so the saved TOML contains and round-trips:

```csharp
RiverMaxVisibleCameraHeight = 875.0f,
```

Assert after save:

```csharp
RuntimeMapDefinition saved = RuntimeMapDefinitionReader.ReadFrom(mapDefinitionPath);
TestHarness.AssertEqual(875.0f, saved.RiverMaxVisibleCameraHeight, "save should persist river max visible camera height");
```

- [ ] **Step 2: Run focused test to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -- --filter save
```

Expected: compile failure or assertion failure until save path carries the value.

- [ ] **Step 3: Add Editor setting**

In `SettingsViewModel`:

```csharp
[ObservableProperty]
private float _riverMaxVisibleCameraHeight = 3000.0f;
```

In `MainWindow.axaml`, under `Show Rivers`, add:

```xml
<Grid ColumnDefinitions="*,72" ColumnSpacing="8">
  <StackPanel Spacing="2">
    <TextBlock Classes="fieldLabel" Text="River Max Camera Height" />
    <Slider Minimum="1" Maximum="10000"
            Value="{Binding Settings.RiverMaxVisibleCameraHeight}" />
  </StackPanel>
  <TextBox Grid.Column="1" Classes="valueBox" IsReadOnly="True"
           Text="{Binding Settings.RiverMaxVisibleCameraHeight, StringFormat='{}{0:F0}'}"
           VerticalAlignment="Bottom" />
</Grid>
```

- [ ] **Step 4: Sync setting changes to river render settings**

In `EditorShellViewModel.OnSettingsPropertyChanged`:

```csharp
else if (e.PropertyName == nameof(SettingsViewModel.RiverMaxVisibleCameraHeight))
{
    if (_viewportHost.RiverRenderingService != null)
    {
        _viewportHost.RiverRenderingService.RiverComponent.Settings.RiverMaxVisibleCameraHeight = Settings.RiverMaxVisibleCameraHeight;
        EditorDirtyState.Instance.MarkDirty();
    }
}
```

In `SyncSettingsFromTerrainManager`, after setting `HeightScale`, sync from the active resource session:

```csharp
if (_resourceSession != null)
{
    Settings.RiverMaxVisibleCameraHeight = _resourceSession.MapDefinitionModel.RiverMaxVisibleCameraHeight;
}
_viewportHost.RiverRenderingService?.SetMaxVisibleCameraHeight(Settings.RiverMaxVisibleCameraHeight);
```

Add to `RiverRenderingService`:

```csharp
public void SetMaxVisibleCameraHeight(float value)
{
    riverComponent.Settings.RiverMaxVisibleCameraHeight = value;
}
```

- [ ] **Step 5: Carry value through save snapshot**

Extend `EditorAuthoringSaveSnapshot` constructor and property:

```csharp
float riverMaxVisibleCameraHeight,
...
RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight;
...
public float RiverMaxVisibleCameraHeight { get; }
```

Create the snapshot from the current setting. If the snapshot is still created by `TerrainManager`, pass the value from `EditorShellViewModel.Save` by adding an overload or assigning the snapshot argument explicitly:

```csharp
var snapshot = terrainManager.CreateAuthoringSaveSnapshot(Settings.RiverMaxVisibleCameraHeight, progress);
```

Then update `EditorResourceSaveService.Save` to accept and write:

```csharp
float riverMaxVisibleCameraHeight,
...
RiverMaxVisibleCameraHeight = riverMaxVisibleCameraHeight,
```

- [ ] **Step 6: Run focused save/UI compile tests**

Run:

```powershell
dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: build passes.

---

## Task 3: Runtime Bootstrap and River Render Skip

**Files:**
- Modify: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Modify: `Terrain/Resources/GameRuntimeResourceBootstrap.cs`
- Modify: `Terrain/Rendering/River/RiverRuntimeLoadState.cs`
- Modify: `Terrain/Rendering/River/RiverRenderSettings.cs`
- Modify: `Terrain/Rendering/River/RiverRenderObject.cs`
- Modify: `Terrain/Rendering/River/RiverProcessor.cs`
- Modify: `Terrain/Rendering/River/RiverRenderFeature.cs`
- Test: `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- [ ] **Step 1: Add failing render wiring tests**

In `RiverShaderTextTests`, assert `RiverRenderFeature` contains:

```csharp
AssertContains(feature, "ResolveRiverMaxVisibleCameraHeight(renderViewStage, startIndex, endIndex)", "RiverRenderFeature should resolve the river camera height cutoff from render objects");
AssertContains(feature, "if (cameraWorldPosition.Y >= riverMaxVisibleCameraHeight)", "RiverRenderFeature should skip river rendering when the camera is above the cutoff");
AssertContains(feature, "return;", "RiverRenderFeature should return before pass work when river camera height cutoff is reached");
```

Also assert `RiverRenderObject` and `RiverRenderSettings` expose `RiverMaxVisibleCameraHeight`.

- [ ] **Step 2: Run focused test to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -- --filter river
```

Expected: test fails until render wiring is implemented.

- [ ] **Step 3: Carry runtime value**

Add to `TerrainRuntimeResourceBundle`:

```csharp
public float RiverMaxVisibleCameraHeight { get; init; } = 3000.0f;
```

In `GameRuntimeResourceBootstrap.Load`:

```csharp
RiverMaxVisibleCameraHeight = mapDefinition.RiverMaxVisibleCameraHeight,
```

Extend `RiverRuntimeLoadConfig`:

```csharp
float RiverMaxVisibleCameraHeight,
```

Pass `3000.0f` for unresolved fallback config and `bundle.RiverMaxVisibleCameraHeight` for loaded config.

In `RiverProcessor.TryEnsureRuntimeMeshes`, after loading the bundle and before/after `component.SetMeshes(meshes)`:

```csharp
component.Settings.RiverMaxVisibleCameraHeight = bundle.RiverMaxVisibleCameraHeight;
```

- [ ] **Step 4: Add render settings/object property**

In `RiverRenderSettings`:

```csharp
public float RiverMaxVisibleCameraHeight { get; set; } = 3000.0f;
```

In `RiverRenderObject`:

```csharp
public float RiverMaxVisibleCameraHeight { get; private set; } = 3000.0f;
...
RiverMaxVisibleCameraHeight = settings.RiverMaxVisibleCameraHeight;
```

Include the property in `RiverParametersMatch`.

- [ ] **Step 5: Skip river render chain in `RiverRenderFeature.Draw`**

After computing `cameraWorldPosition` and before `renderResources.EnsureResources`:

```csharp
float riverMaxVisibleCameraHeight = ResolveRiverMaxVisibleCameraHeight(renderViewStage, startIndex, endIndex);
if (cameraWorldPosition.Y >= riverMaxVisibleCameraHeight)
{
    return;
}
```

Add resolver:

```csharp
private float ResolveRiverMaxVisibleCameraHeight(RenderViewStage renderViewStage, int startIndex, int endIndex)
{
    float maxVisibleCameraHeight = 3000.0f;
    for (int index = startIndex; index < endIndex; index++)
    {
        var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
        var renderNode = GetRenderNode(renderNodeReference);
        if (renderNode.RenderObject is RiverRenderObject riverObject && riverObject.Enabled)
        {
            maxVisibleCameraHeight = riverObject.RiverMaxVisibleCameraHeight;
            break;
        }
    }

    return maxVisibleCameraHeight;
}
```

- [ ] **Step 6: Run focused river tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -- --filter river
```

Expected: river text/compile tests pass, except any known unrelated pre-existing failures.

---

## Task 4: Documentation, Verification, and Session Log

**Files:**
- Modify: `docs/design/map-data-toml-formats.md`
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Create: `docs/log/2026/06/23/2026-06-23-river-camera-height-visibility.md`

- [ ] **Step 1: Update docs**

Document:

```text
river_max_visible_camera_height
Default: 3000.0
Meaning: rivers render only while active camera world Y is below this value.
```

Add a short note in architecture/features under river rendering that the render chain is skipped above the configured camera height.

- [ ] **Step 2: Run verification**

Run serially:

```powershell
dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj
```

Expected: build passes. Full test run may still report the known pre-existing scene asset text failures if they remain unchanged; record exact failures in the session log.

- [ ] **Step 3: Write session log**

Create the session log from `docs/log/TEMPLATE.md` and include:

- Goal.
- Files changed.
- Validation commands and results.
- Any known pre-existing failures.
- Next steps.

- [ ] **Step 4: Final status**

Run:

```powershell
git status --short
```

Report changed files and verification results.
