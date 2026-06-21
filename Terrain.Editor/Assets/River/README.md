# River Rendering Resources

Runtime/editor shared river environment assets now live under `Terrain/Assets/River/Environment/` and are referenced through the neutral Stride content URL `River/Environment/*`. River bottom and water DDS files live under `game/map/water` and are loaded directly from the file system by `RiverResourceLoader`.

## Bottom And Water

| Project file | Source path | Usage |
|---|---|---|
| `game/map/water/bottom_diffuse.dds` | `game/gfx/map/rivers/river_bottom_diffuse.dds` | River bed base color. |
| `game/map/water/bottom_normal.dds` | `game/gfx/map/rivers/river_bottom_normal.dds` | River bed normal detail. |
| `game/map/water/bottom_properties.dds` | `game/gfx/map/rivers/river_bottom_gloss.dds` | River bed roughness/gloss-style property input. |
| `game/map/water/bottom_depth.dds` | `game/gfx/map/textures/river_depth.dds` | River depth/profile lookup input. |
| `game/map/water/ambient_normal.dds` | `game/gfx/map/water/ambient_normal.dds` | Ambient water normal detail. |
| `game/map/water/flow_normal.dds` | `game/gfx/map/water/flow_normal.dds` | Animated flow normal detail. |
| `game/map/water/foam.dds` | `game/gfx/map/water/foam.dds` | Foam pattern. |
| `game/map/water/foam_ramp.dds` | `game/gfx/map/water/foam_ramp.dds` | Foam ramp lookup. |
| `game/map/water/foam_map.dds` | `game/gfx/map/water/foam_map.dds` | Foam mask/noise distribution. |
| `game/map/water/foam_noise.dds` | `game/gfx/map/water/foam_noise.dds` | Foam animation noise. |
| `game/map/water/shadow_color.dds` | `game/gfx/map/textures/shadow_color.dds` | Terrain shadow tint noise/color input. |
| `game/map/water/water_color.dds` | `game/gfx/map/water/watercolor_rgb_waterspec_a.dds` | Water color RGB and water spec alpha lookup. |

## Environment

| Project file | Source path | Usage |
|---|---|---|
| `Terrain/Assets/River/Environment/reflection-specular.dds` | `game/gfx/map/environment/qwantani_8k_nosun_cube_specular.dds` | Specular reflection fallback. |

Runtime code should refer to `game/map/water` for Bottom/Water textures and to the neutral Stride content URL `River/Environment/reflection-specular` for the environment cubemap.
