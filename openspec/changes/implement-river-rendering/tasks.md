## 1. River Component and Data Model

- [x] 1.1 Add `RiverComponent`, `RiverRenderSettings`, and `RiverMeshData` under the river rendering namespace with versioned mesh update and clear operations.
- [x] 1.2 Update `RiverRenderingService` to find or create a river entity and river component instead of using ModelComponent as the primary render path.
- [x] 1.3 Preserve `RiverRenderingService.UpdateMeshes`, `SetVisible`, `ClearMeshes`, and `Dispose` external behavior while delegating state to `RiverComponent`.
- [x] 1.4 Add tests for river component version increments, clear behavior, and visibility state updates.

## 2. River Vertex and Mesh Output

- [x] 2.1 Add `RiverVertex` with `POSITION + TEXCOORD0..5` layout for position, transparency, UV, tangent, normal, width, and distance-to-main.
- [x] 2.2 Extend `RiverMeshService` to output `RiverMeshData` using `RiverVertex` while preserving existing centerline, miter, cap, and index behavior.
- [x] 2.3 Compute normalized tangent, normal, UV, width, transparency, and distance-to-main values for each river vertex.
- [x] 2.4 Update river mesh tests to validate the new vertex layout and generated vertex attributes.

## 3. River Processor and Render Objects

- [x] 3.1 Add `RiverRenderObject` to hold segment draw metadata, bounds, vertex buffer, index buffer, index count, source version, and visibility state.
- [x] 3.2 Add `RiverProcessor` or equivalent render-system integration to synchronize `RiverComponent` mesh versions into `RiverRenderObject` instances.
- [x] 3.3 Ensure processor cleanup releases old buffers on mesh updates, clear, component removal, and game shutdown.
- [x] 3.4 Keep one segment per draw item for initial RenderDoc parity and debugging.

## 4. River Render Resources

- [x] 4.1 Add `RiverRenderResources` to own half-resolution bottom color/refraction RT and depth resources.
- [x] 4.2 Implement `EnsureResources(viewWidth, viewHeight)` to create and resize half-resolution targets.
- [x] 4.3 Use `R16G16B16A16_Float` or equivalent for bottom color/refraction RT and a compatible depth format for bottom depth.
- [x] 4.4 Dispose render resources reliably during feature shutdown and viewport/game disposal.

## 5. Shader Streams and Common Code

- [x] 5.1 Add `RiverVertexStreams.sdsl` declaring river-specific inputs for `TEXCOORD0..5`.
- [x] 5.2 Add `RiverCommon.sdsl` with shared depth, fade, and compressed world/refraction helper functions.
- [x] 5.3 Add `RiverWaterCommon.sdsl` with water color, ambient normal, flow normal, foam, reflection, and neutral fallback helpers.
- [x] 5.4 Update `Terrain.Editor.csproj` and shader generator metadata for any new shader files and generated key files.

## 6. River Bottom Shader

- [x] 6.1 Rewrite `RiverBottom.sdsl` to consume river streams and implement bottom pass logic with bottom diffuse, normal, properties, depth, parallax, fade, and compressed world/refraction output.
- [x] 6.2 Implement dual-source pixel output for primary color and secondary blend alpha on the target backend.
- [x] 6.3 Expose bottom shader parameters through generated keys or explicit parameter bindings.
- [x] 6.4 Verify shader generation and asset compilation for the bottom shader.

## 7. River Surface Shader

- [x] 7.1 Rewrite `RiverSurface.sdsl` to consume river streams and sample the bottom/refraction render target.
- [x] 7.2 Implement flow normal animation, water color, ambient normal, reflection, foam, transparency, edge fade, and distance-to-main fade.
- [x] 7.3 Add neutral fallback parameters for unavailable fog, cloud, shadow, flat-map, or zoom inputs.
- [x] 7.4 Verify shader generation and asset compilation for the surface shader.

## 8. River Resources

- [x] 8.1 Add neutral project resource directories under `Terrain.Editor/Assets/River/Water`, `Bottom`, and `Environment`.
- [x] 8.2 Copy required water, bottom, foam, and reflection textures into the project with neutral filenames.
- [x] 8.3 Add `Assets/River/README.md` documenting each copied resource source path and intended usage without using external names in code paths.
- [x] 8.4 Add `RiverResourceLoader` or equivalent centralized texture loading and disposal logic.
- [x] 8.5 Confirm `Terrain.Editor.sdpkg` asset folders include the resource location or adjust asset loading strategy.

## 9. River Render Feature

- [x] 9.1 Add `RiverRenderFeature` using `DynamicEffectInstance` or equivalent effect instances for bottom and surface passes.
- [x] 9.2 Configure bottom pipeline state with river vertex layout, dual-source blend, depth write disabled, configurable depth bias, and appropriate rasterizer state.
- [x] 9.3 Configure surface pipeline state with river vertex layout, alpha blend, depth write disabled, configurable depth bias, and appropriate rasterizer state.
- [x] 9.4 Implement bottom pass drawing into half-resolution render resources for all visible river render objects.
- [x] 9.5 Implement surface pass drawing into the current full-resolution target while sampling the bottom/refraction render target.
- [x] 9.6 Bind per-view, per-pass, per-resource, and per-object parameters consistently for both passes.

## 10. Viewport and Compositor Integration

- [x] 10.1 Register or ensure `RiverRenderFeature` from `EmbeddedStrideViewportGame` without breaking terrain render feature, brush decal render feature, camera, or viewport focus logic.
- [x] 10.2 Ensure river pass ordering draws bottom before surface and surface at the correct point relative to terrain and transparent overlays.
- [x] 10.3 Update river service initialization to connect `RiverRenderingService`, `RiverComponent`, `RiverProcessor`, and `RiverRenderFeature`.
- [x] 10.4 Ensure Show/Hide Rivers and Clear River Map work through the new component/render-feature path.

## 11. Wireframe and Debug Views

- [x] 11.1 Migrate river wireframe rendering away from the old MeshRenderFeature selector path or provide a temporary debug-only bridge.
- [x] 11.2 Add river render debug modes for wireframe and, if practical, bottom-only or surface-only visualization.
- [x] 11.3 Ensure existing scene wireframe mode still allows river mesh inspection.

## 12. Verification and Documentation

- [ ] 12.1 Run `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` and fix regressions.
- [ ] 12.2 Run Stride shader generated-file update for `Terrain.Editor/Terrain.Editor.csproj`.
- [ ] 12.3 Run `StrideCleanAsset` and `StrideCompileAsset` for `Terrain.Editor/Terrain.Editor.csproj`.
- [ ] 12.4 Run `dotnet build Terrain.sln -c Debug`.
- [ ] 12.5 Manually run the editor, generate rivers, and verify visible bottom/surface river rendering.
- [ ] 12.6 Capture a frame in RenderDoc and verify bottom pass, surface pass, vertex input layout, RT sizes, blend states, depth states, and texture bindings.
- [ ] 12.7 Update `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md` to reflect the new river rendering architecture.
- [ ] 12.8 Add a session log and, if the architecture is stable, add an ADR for the river render feature design.
