# River Max Visible Camera Height Design

## Goal

Add a configurable river visibility cutoff based on camera world height.
When the active render camera reaches or exceeds the configured height, river rendering is skipped. The default cutoff is `3000.0`.

## User-Facing Contract

`map/default.toml` gains a `[settings]` field:

```toml
river_max_visible_camera_height = 3000.0
```

Meaning:

- If `cameraWorldY < river_max_visible_camera_height`, rivers render normally.
- If `cameraWorldY >= river_max_visible_camera_height`, rivers do not render.
- The existing Editor `Show Rivers` switch remains a separate manual visibility control.
- Effective river visibility is `ShowRivers && cameraWorldY < river_max_visible_camera_height`.

## Architecture

The cutoff belongs to river render settings, not river mesh generation or shader logic.

Data flow:

1. `RuntimeMapDefinitionReader` reads `river_max_visible_camera_height` from `[settings]`.
2. Missing legacy TOML fields fall back to `3000.0f`.
3. `RuntimeMapDefinition` carries `RiverMaxVisibleCameraHeight`.
4. Editor resource loading syncs the value into `SettingsViewModel`.
5. Editor settings changes sync into `RiverComponent.Settings`.
6. Runtime bootstrap copies the value into river render settings when runtime river meshes are loaded.
7. `RiverRenderFeature.Draw` reads the active `RenderView` camera world position and skips the whole river chain when the camera is above the cutoff.

The render skip should happen before river seed, bottom, refraction, and surface work. This avoids unnecessary GPU work and keeps shader code unchanged.

## Components

### Configuration Model

Add `RuntimeMapDefinition.RiverMaxVisibleCameraHeight` with default `3000.0f`.

`RuntimeMapDefinitionReader`:

- Allows `river_max_visible_camera_height` in `[settings]`.
- Reads it as an optional numeric field with fallback `3000.0f`.
- Rejects non-finite values and values below or equal to zero.

`MapDefinitionWriter`:

- Validates the value is finite and greater than zero.
- Writes `river_max_visible_camera_height`.

`EditorMapDataScaffoldService`:

- Emits the default value in generated `default.toml`.

### Editor Settings

Add `SettingsViewModel.RiverMaxVisibleCameraHeight`, defaulting to `3000.0f`.

Expose it in the Settings panel near `Show Rivers`, using a numeric editor suitable for heights greater than current terrain scale. The control should not be read-only.

On resource/session load, sync the TOML value to the setting. On setting change, update `RiverComponent.Settings.RiverMaxVisibleCameraHeight`.

On authoring save, include the current setting in the map definition snapshot written back to `default.toml`.

### Runtime Rendering

Add `RiverRenderSettings.RiverMaxVisibleCameraHeight`.

`RiverRenderObject.ApplySettings` copies this value from settings so `RiverRenderFeature` can inspect it alongside existing render object settings.

`RiverRenderFeature` computes the current camera world position from `renderView.View` as it already does for refraction and lighting. If the active camera height is greater than or equal to the resolved cutoff, return before:

- `renderResources.EnsureResources`
- `SeedSceneColorFromScene`
- bottom pass draw
- surface pass draw

If multiple river render objects are visible, all must agree on shared render settings as they already do for other river parameters. The existing parameter consistency debug assertion should include `RiverMaxVisibleCameraHeight`.

## Error Handling

Invalid TOML values fail resource loading with a clear `InvalidDataException`, matching existing behavior for `height_scale`, `river_min_width`, and `river_max_width`.

Missing values in old projects are accepted and default to `3000.0f`.

## Testing

Use deterministic tests for configuration and render wiring:

- Reader accepts missing `river_max_visible_camera_height` and returns `3000.0f`.
- Reader reads an explicit value.
- Reader rejects non-finite or non-positive values.
- Writer writes the field and round-trips it.
- Scaffold-generated `default.toml` contains the default field.
- Editor save writes the current setting value.
- River render feature text or focused behavior test verifies the cutoff uses camera world Y and skips the whole river chain before pass work.

Shader compile tests remain useful as smoke tests, but no shader changes are intended.

## Non-Goals

- No per-pixel or per-segment river height clipping.
- No river mesh regeneration when the cutoff changes.
- No change to `rivers.png` parsing.
- No CK3 shader semantic changes.
