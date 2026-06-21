# River Width Gradient Design

## Goal

River generation must honor the `rivers.png` palette width gradient locally instead of collapsing each segment to one average width. The width range is configurable in `game/map/default.toml`.

## Configuration

Add two full-width settings under `[settings]`:

```toml
[settings]
height_scale = 200
river_min_width = 1
river_max_width = 4
```

`river_min_width` and `river_max_width` are full-width values in the same map-unit space currently represented by the hardcoded palette comments. Internally the mesh generator uses half-width, so the configured value is divided by two when creating ribbon offsets.

Defaults:

- `river_min_width = 1`
- `river_max_width = 4`

Validation:

- `river_min_width` must be greater than zero.
- `river_max_width` must be greater than or equal to `river_min_width`.

Missing fields fall back to the defaults to keep existing maps loading.

## Palette Mapping

`RiverCell.Width` remains the palette index parsed from the river color. The width palette no longer owns fixed world-space half-width values. Instead, each palette index maps linearly from `river_min_width` to `river_max_width`.

For the current 11 palette entries:

- index `0` maps to full-width `river_min_width`
- index `10` maps to full-width `river_max_width`
- intermediate indices interpolate evenly

This preserves the existing semantic meaning: light blue is narrow, darker blue is wider, green continues wider.

## Data Flow

`RuntimeMapDefinition` carries `RiverMinWidth` and `RiverMaxWidth`.

`RuntimeMapDefinitionReader` reads optional settings fields, validates them, and supplies defaults when absent. `MapDefinitionWriter`, scaffold generation, and save flow write these fields so editor save does not drop river width settings.

`RiverMapService` converts each traced river cell's palette index into a local half-width. It no longer uses `AvgHalfWidth` as the only source of mesh width. Existing average width can remain as compatibility/debug metadata, but mesh generation must consume local width samples.

`RiverSegment` stores width samples aligned with `Cells` and later with `Centerline`.

`RiverMeshService.BuildCenterlines` builds position and width sample streams together:

1. Raw river pixel centers get terrain-sampled positions and local half-widths.
2. Simplification keeps the corresponding width samples for retained points.
3. Smoothing and Catmull-Rom interpolation resample widths alongside positions.
4. Final terrain-height resampling preserves the smoothed/interpolated width list.

`RiverMeshService.BuildRiverMesh` uses the per-centerline half-width at each point, multiplied by editor `WidthScale` and endpoint taper. The vertex `RiverWidth` stream remains normalized half-width; shaders still restore full-width with `* 2.0f`.

## Error Handling

Invalid TOML width settings fail map definition loading with a clear `InvalidDataException`.

If a `RiverSegment` has no width samples or a mismatch with `Centerline`, mesh generation falls back to the segment average half-width. This fallback avoids editor crashes while tests lock the normal generated path to local widths.

## Testing

Add or update tests for:

- `RuntimeMapDefinitionReader` defaults missing river width settings to `1` and `4`.
- `RuntimeMapDefinitionReader` reads explicit `river_min_width` and `river_max_width`.
- `MapDefinitionWriter` preserves the new settings.
- A mixed-width river segment generates vertex widths that vary along the mesh instead of using one average value.
- Endpoint taper still applies on top of local width without shrinking caps below the existing visible minimum behavior.

## Non-Goals

- Do not change river shader width semantics.
- Do not generate mesh from the full raster outline.
- Do not alter river direction normalization, junction tracing, or RenderDoc-driven surface/refraction fixes.

