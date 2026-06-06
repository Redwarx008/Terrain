# River Rendering Resources

These files are copied into neutral project paths so runtime code never depends on an external game installation.

## Bottom

| Project file | Source path | Usage |
|---|---|---|
| `Bottom/bottom-diffuse.dds` | `game/gfx/map/rivers/river_bottom_diffuse.dds` | River bed base color. |
| `Bottom/bottom-normal.dds` | `game/gfx/map/rivers/river_bottom_normal.dds` | River bed normal detail. |
| `Bottom/bottom-properties.dds` | `game/gfx/map/rivers/river_bottom_gloss.dds` | River bed roughness/gloss-style property input. |
| `Bottom/bottom-depth.dds` | `game/gfx/map/textures/river_depth.dds` | River depth/profile lookup input. |

## Water

| Project file | Source path | Usage |
|---|---|---|
| `Water/ambient-normal.dds` | `game/gfx/map/water/ambient_normal.dds` | Ambient water normal detail. |
| `Water/flow-normal.dds` | `game/gfx/map/water/flow_normal.dds` | Animated flow normal detail. |
| `Water/foam.dds` | `game/gfx/map/water/foam.dds` | Foam pattern. |
| `Water/foam-ramp.dds` | `game/gfx/map/water/foam_ramp.dds` | Foam ramp lookup. |
| `Water/foam-map.dds` | `game/gfx/map/water/foam_map.dds` | Foam mask/noise distribution. |
| `Water/foam-noise.dds` | `game/gfx/map/water/foam_noise.dds` | Foam animation noise. |

## Environment

| Project file | Source path | Usage |
|---|---|---|
| `Environment/reflection-specular.dds` | `game/gfx/map/environment/qwantani_8k_nosun_cube_specular.dds` | Specular reflection fallback. |

Runtime code should refer only to the neutral project paths above.
