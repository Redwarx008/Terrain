# River Camera Height Visibility
**Date**: 2026-06-23
**Session**: River render camera-height cutoff
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add an Editor- and TOML-configurable camera-height cutoff for river rendering, defaulting to `3000.0`.

**Secondary Objectives:**
- Preserve old `default.toml` compatibility.
- Skip the whole river render chain when the active camera is above the cutoff.
- Cover deterministic config/save/render wiring with automated tests.

**Success Criteria:**
- `river_max_visible_camera_height` is read, written, scaffolded, and saved.
- Editor Settings exposes the value.
- Runtime and Editor river render settings receive the value.
- `RiverRenderFeature` returns before seed/bottom/surface work when `cameraWorldY >= cutoff`.

---

## Context & Background

**Previous Work:**
- [River rendering architecture](../../decisions/adr-014-river-rendering-architecture.md)
- [Map TOML formats](../../../design/map-data-toml-formats.md)
- [River max visible camera height design](../../../superpowers/specs/2026-06-23-river-max-visible-camera-height-design.md)

**Current State Before This Session:**
- River visibility only had a manual `Show Rivers` / `RiverRenderSettings.Visible` path.
- `map/default.toml` supported `height_scale`, `river_min_width`, and `river_max_width`.
- River rendering always ran its seed, bottom, and surface chain when visible.

**Why Now:**
- High camera views should stop rendering rivers, but the threshold should be configurable from Editor and persisted in map config.

---

## What We Did

### 1. Added `river_max_visible_camera_height` to Map Config
**Files Changed:** `Terrain/Resources/RuntimeMapDefinition.cs`, `Terrain/Resources/RuntimeMapDefinitionReader.cs`, `Terrain.Editor/Services/Resources/MapDefinitionWriter.cs`, `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`

**Implementation:**
- Added `RuntimeMapDefinition.RiverMaxVisibleCameraHeight = 3000.0f`.
- Reader allows and reads `[settings].river_max_visible_camera_height`.
- Missing field defaults to `3000.0f`.
- Reader and writer reject non-finite or non-positive values.
- Writer always emits the field.

**Rationale:**
- This matches the existing strict TOML contract and keeps old configs loadable.

### 2. Exposed and Persisted the Editor Setting
**Files Changed:** `SettingsViewModel.cs`, `MainWindow.axaml`, `EditorShellViewModel.cs`, `RiverRenderingService.cs`, `EditorAuthoringSaveSnapshot.cs`, `EditorResourceSaveService.cs`, `TerrainManager.cs`

**Implementation:**
- Added `Settings.RiverMaxVisibleCameraHeight`.
- Added a Settings panel numeric control labeled `River Max Camera Height`.
- Syncs loaded TOML value into Settings.
- Settings changes update `RiverComponent.Settings` and mark authoring state dirty.
- Save snapshots carry the current value so background save writes the same value.
- Existing save API calls that omit the new parameter preserve `session.MapDefinitionModel.RiverMaxVisibleCameraHeight` instead of rewriting it to the default.
- The Editor control was changed from a numeric up/down to a Slider matching the `Height Scale` UI pattern.

**Rationale:**
- Save already uses immutable snapshots for background file IO. Carrying this value through the snapshot avoids reading UI state from the background worker.

### 3. Skipped River Render Chain Above the Cutoff
**Files Changed:** `RiverRenderSettings.cs`, `RiverRenderObject.cs`, `RiverRenderFeature.cs`, `RiverProcessor.cs`, `TerrainRuntimeResourceBundle.cs`, `GameRuntimeResourceBootstrap.cs`, `RiverRuntimeLoadState.cs`

**Implementation:**
- Runtime bootstrap copies the TOML value into `TerrainRuntimeResourceBundle`.
- `RiverProcessor` copies runtime bundle value into `RiverRenderSettings`.
- `RiverRenderObject.ApplySettings` caches `RiverMaxVisibleCameraHeight`.
- `RiverRenderFeature.Draw` now computes camera world position before allocating river render targets.
- If `cameraWorldPosition.Y >= RiverMaxVisibleCameraHeight`, `Draw` returns before seed, bottom, refraction, and surface work.
- If multiple river render objects enter the same draw range, the cutoff resolver uses the maximum configured value, giving Release builds a deterministic no-premature-hide strategy.

**Rationale:**
- RenderFeature-level skipping saves GPU work and avoids touching shader or mesh generation semantics.

### 4. Added Regression Coverage
**Files Changed:** `EditorResourceWriterTests.cs`, `EditorMapDataScaffoldTests.cs`, `EditorResourceSaveServiceTests.cs`, `GameRuntimeResourceBootstrapTests.cs`, `RiverShaderTextTests.cs`, `Program.cs`, `EditorWorkflowTextTests.cs`

**Coverage:**
- Default value for old TOML.
- Explicit TOML value.
- Invalid config values.
- Writer roundtrip.
- Scaffold output.
- Editor save persistence.
- Legacy save overload preservation for loaded non-default camera height.
- Runtime bundle propagation.
- Runtime bundle propagation for non-default camera height.
- RenderFeature cutoff wiring.
- Editor Settings XAML keeps `River Max Camera Height` on a Slider rather than a numeric up/down.

### 5. Follow-up UI and Default Adjustment
**Files Changed:** `RuntimeMapDefinition.cs`, `RuntimeMapDefinitionReader.cs`, `RiverRenderSettings.cs`, `RiverRenderObject.cs`, `RiverRenderFeature.cs`, `RiverProcessor.cs`, `SettingsViewModel.cs`, `MainWindow.axaml`, tests and docs

**Implementation:**
- Changed the default `RiverMaxVisibleCameraHeight` from `1000.0f` to `3000.0f` across runtime, editor settings, fallback config, scaffold output, tests, and docs.
- Changed the Editor Settings control from `NumericUpDown` to a `Slider` with the same label/value-box pattern used by `Height Scale`.
- Updated the local ignored `game/map/default.toml` value to `3000`.

**Rationale:**
- The new default better matches the desired high-camera cutoff, and the Slider makes the setting consistent with nearby terrain settings controls.

---

## Decisions Made

### Decision 1: Use RenderFeature-Level Early Return
**Context:** The cutoff is based on camera height, not river mesh height or pixel height.

**Options Considered:**
1. CPU/object visibility toggle in `RiverProcessor` - lacks current `RenderView` context.
2. Shader discard - preserves draw overhead and complicates shaders.
3. `RiverRenderFeature.Draw` early return - has camera context and owns the river pass chain.

**Decision:** Use `RiverRenderFeature.Draw` early return.

**Rationale:** It is the lowest-overhead point that already owns seed, bottom, and surface execution.

**Trade-offs:** The cutoff is per render view and shared across river objects, matching the existing shared river settings invariant.

---

## What Worked ✅

1. **TOML-first config path**
   - The existing `RuntimeMapDefinition` flow cleanly handled defaulting, validation, Editor load, runtime bootstrap, and Save.

2. **RenderFeature early return**
   - The camera world position was already computed in `RiverRenderFeature`; moving it before resource allocation made the skip straightforward.

---

## Problems Encountered & Solutions

### Problem 1: Save API Parameter Ordering
**Symptom:** Adding the new value before `progress` created an invalid optional-parameter shape.

**Root Cause:** Existing callers use named `progress`, and C# optional parameters must follow required parameters.

**Solution:**
- Kept the existing common argument order.
- Added `riverMaxVisibleCameraHeight` as a named optional parameter after `progress`.
- `TerrainManager.SaveAuthoringResources` passes both with explicit values.

### Problem 2: Existing Text Test Expected Old Snapshot Call
**Symptom:** `EditorWorkflowTextTests` checked for `CreateAuthoringSaveSnapshot(progress)`.

**Root Cause:** The snapshot now needs the current river cutoff setting.

**Solution:**
- Updated the test to expect `CreateAuthoringSaveSnapshot(Settings.RiverMaxVisibleCameraHeight, progress)`.

### Problem 3: Code Review Found Silent Config Regression
**Symptom:** A subagent review found that old save entry points could omit the new value and overwrite an existing non-default `river_max_visible_camera_height` with the hardcoded default.

**Root Cause:** The new save parameter had a hardcoded default, and `TerrainManager.SaveAuthoringResources(session, progress)` created a snapshot with the default value.

**Solution:**
- `EditorResourceSaveService.Save` now falls back to `session.MapDefinitionModel.RiverMaxVisibleCameraHeight` when the caller omits the parameter.
- `TerrainManager.SaveAuthoringResources(session, progress)` passes the loaded session value into the snapshot.
- Added a regression test for preserving a loaded `875.0f` value through the legacy save overload.

### Problem 4: Release Cutoff Strategy Was Order-Dependent
**Symptom:** The first implementation returned the first enabled river object's cutoff from the sorted draw range.

**Root Cause:** The shared river pass relies on render objects agreeing on pass-wide settings; debug builds assert the invariant, but release builds need a deterministic fallback.

**Solution:**
- `ResolveRiverMaxVisibleCameraHeight` now scans all enabled river objects in the draw range and uses the maximum cutoff, avoiding premature hiding if values diverge.
- Added text regression checks for the stable max-based resolver.

### Problem 5: Runtime Rivers Did Not Render
**Symptom:** Runtime river rendering disappeared after the scene/compositor assets changed.

**Root Cause:**
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp` no longer registered `Terrain.Rendering.River.RiverRenderFeature,Terrain`.
- `Terrain/Assets/MainScene.sdscene` serialized the `RiverSystem` component as short `!RiverComponent`, so the runtime scene no longer had the fully-qualified `Terrain.Rendering.River.RiverComponent,Terrain` component expected by Stride.

**Solution:**
- Restored the compositor `RiverRenderFeature` registration for `RiverSurface`, `Group1`, and the transparent render stage.
- Restored the fully-qualified `RiverComponent` type tag on `RiverSystem`.
- Re-ran the runtime asset checks and full test program; both asset checks now pass.

### Problem 6: Runtime Started Away From Visible Rivers
**Symptom:** After restoring the runtime river asset registration, the default runtime view still appeared to show no rivers.

**Root Cause:**
- Diagnostics showed runtime loaded `game/map/rivers.png`, generated 1748 river meshes, created 1748 `RiverRenderObject`s, and `RiverRenderFeature.Prepare` saw a valid river object.
- The original runtime camera at `{X: 128, Y: 78, Z: -96}` did not put any river objects into the transparent view stage from the default view.
- Moving the runtime camera near the first generated river bounds made `RiverRenderFeature.Draw` enter with `cameraY=300`, `cutoff=3160`, and a valid `sceneColor`.

**Solution:**
- Updated the runtime `MainScene` camera start position to `{X: 4300, Y: 300, Z: -200}`, which shows the river in the default runtime launch.
- Removed all temporary `[DEBUG-river-runtime]` file logging after diagnosis.

---

## Architecture Impact

### Documentation Updates Completed
- [x] Updated `docs/design/map-data-toml-formats.md`
- [x] Updated `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Updated `docs/CURRENT_FEATURES.md`

### New Pattern
**Render-view-driven feature skip**
- Use when an entire custom render feature can be skipped from per-view state before allocating render targets.
- Prefer this over shader discard when the condition is global to the pass.

---

## Code Quality Notes

### Performance
- High-camera river skipping avoids half-resolution river target allocation and seed/bottom/surface draw work.

### Testing
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` passed.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj` passed with exit code 0 after restoring runtime river asset registration.
- `dotnet build Terrain.Windows\Terrain.Windows.csproj --no-restore` passed after closing the stale runtime dotnet process that locked `Terrain.dll`.
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-build` passed after the runtime camera update. A normal rebuild-backed test run was blocked by the currently open `Terrain.Editor.exe` / Visual Studio locking `Bin/Editor/Debug/win-x64/Terrain.dll`.

### Technical Debt
- No visual/manual Editor smoke test was run in this session.
- `game/map/default.toml` is ignored by Git; it was updated locally so the live workspace shows the field, but it will not be committed.

---

## Next Session

### Immediate Next Steps
1. Open Editor and verify the Settings panel value appears and can be changed.
2. Use a high camera view to verify rivers disappear above `river_max_visible_camera_height`.
3. Address the two pre-existing scene asset text failures if they are still relevant.

### Docs to Read Before Next Session
- [Map TOML formats](../../../design/map-data-toml-formats.md)
- [Architecture Overview](../../../ARCHITECTURE_OVERVIEW.md)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Config field: `river_max_visible_camera_height`
- Code property: `RiverMaxVisibleCameraHeight`
- Default: `3000.0f`
- Editor UI: Slider range `1..10000` with a read-only value box.
- Render skip: `RiverRenderFeature.Draw` returns when `cameraWorldPosition.Y >= RiverMaxVisibleCameraHeight`.

**Gotchas for Next Session:**
- `game/` is ignored by Git; changes there are local workspace resources.
- Do not move this logic into shader discard unless the cutoff changes from pass-global to pixel-local.
