# ADR: Remove River Surface ApplySurfacePostProcessing
**Date**: 2026-06-20
**Status**: Accepted

## Context

`RiverSurface` had a debug4-style `ApplySurfacePostProcessing` wrapper after `CalcRiverAdvanced`.
It added procedural cloud shadow, terrain shadow tint, map distance fog, extra alpha fades, editor terrain height-slice bindings, and `shadow_color.dds` loading.

RenderDoc hot replacement on `C:\Users\Redwa\Desktop\debug4.rdc` showed the wrapper was effectively inert for the inspected frame:
EID 149 and EID 176 changed only 16 exported RGB pixels after bypassing the wrapper, with a maximum delta of 1 LSB.

## Decision

Remove `ApplySurfacePostProcessing` and its exclusive inputs/bindings.
`RiverSurface` now writes `CalcRiverAdvanced` output directly.

## Consequences

- No procedural cloud shadow, terrain shadow tint, or map distance fog runs after `CalcRiverAdvanced`.
- `RiverRenderFeature` no longer binds editor terrain height slices, `_InverseWorldSize`, `_HasCloudShadowEnabled`, `ShadowNoiseTexture`, `ShadowNoiseSampler`, or `TerrainHeightSampler` for river surface.
- `RiverResourceLoader` no longer requires `shadow_color.dds`.
- Text tests now assert these wrapper dependencies stay removed.
- This intentionally sacrifices CK3 wrapper parity for lower shader/binding complexity and less `_GlobalTime` phase sensitivity.
