# Runtime River Design

## Goal

Runtime must display rivers from `game/map/rivers.png` in `Terrain.Windows` using the same river mesh and render pipeline as the editor. The first runtime slice only needs normal rendering. Editor-only debug UI and modes stay in `Terrain.Editor`.

## Architecture

River core moves from `Terrain.Editor` into `Terrain`.

Runtime scene integration is asset-driven:

- `Terrain/Assets/MainScene.sdscene` owns a `RiverSystem` entity.
- `RiverSystem` has a `RiverComponent`.
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp` owns `RiverRenderFeature` registration and the transparent-stage selector.
- `Terrain.Windows` does not create the river entity, component, or render feature in code.

`TerrainRuntimeResourceBundle` remains a resource/config carrier. It exposes `RiversPath`, `RiverMinWidth`, `RiverMaxWidth`, and `HeightScale`, but it must not create entities, attach components, or register render features.

## Components

### RiverComponent

`RiverComponent` is the scene-asset hook and mesh data container.

Responsibilities:

- Store `RiverMeshData` snapshots.
- Expose `SetMeshes(...)` and `Clear()`.
- Track `Version`.
- Store `RiverRenderSettings`.
- Store runtime load state and failed config snapshots so failed loads do not retry every frame.

It does not load `rivers.png` and does not call mesh generation directly.

### RiverProcessor

`RiverProcessor` remains the single processor for `RiverComponent`.

Responsibilities:

- Discover scene-authored `RiverComponent` instances through Stride's scene system.
- If a runtime component has no mesh and can attempt load, resolve runtime resources through `GameRuntimeResourceBootstrap`.
- Generate meshes with `RiverMapService` and `RiverMeshService`.
- Write generated meshes back through `RiverComponent.SetMeshes(...)`.
- Rebuild/release `RiverRenderObject` when `RiverComponent.Version` changes.
- Synchronize settings, world matrix, enabled state, render group, and visibility group.

No separate `RiverRuntimeProcessor` is introduced.

### RiverRenderFeature

`RiverRenderFeature` moves to `Terrain.Rendering.River`. It keeps the existing bottom -> refraction -> surface pipeline.

Runtime registration is done in `GraphicsCompositor.sdgfxcomp`, not in `Terrain.Windows` code. The compositor selector targets:

- `RenderStage = Transparent`
- `RenderGroup = Group1`
- `EffectName = RiverSurface`

Editor may keep a dynamic safety fallback for embedded viewport setup, but runtime relies on the asset.

## Data Flow

1. Stride loads `MainScene.sdscene`.
2. The scene contains `TerrainComponent` and `RiverComponent`.
3. `TerrainProcessor` loads terrain runtime resources and initializes height data.
4. `RiverProcessor` waits until terrain height sampling is available.
5. `RiverProcessor` resolves runtime map resources.
6. `RiverMapService` loads `rivers.png`, validates it, and extracts segments using `RiverMinWidth` and `RiverMaxWidth`.
7. `RiverMeshService` samples terrain height, builds centerlines, preserves local width samples, and emits `RiverMeshData`.
8. `RiverComponent.SetMeshes(...)` stores snapshots and bumps `Version`.
9. `RiverProcessor` creates `RiverRenderObject` instances.
10. `RiverRenderFeature` draws bottom/refraction/surface passes.

## Height Sampling

`RiverMeshService` must not depend on editor `TerrainManager`.

Introduce a public height sampling abstraction in `Terrain` named `IRiverTerrainHeightSource`, with enough data for:

- bilinear height sampling in world XZ
- height scale
- heightmap width and height
- map extent for normalized river width

Runtime uses initialized `TerrainComponent` / loaded terrain data as the height source. Editor adapts `TerrainManager` to the same interface.

## Resource Handling

`GameRuntimeResourceBootstrap` continues to resolve `rivers.png` as optional, following `default.toml`.

Runtime behavior:

- No declared `rivers` path: clear river meshes, mark state as `NoRiverResource`, and continue terrain rendering.
- Declared `rivers` path missing: log warning, clear river meshes, mark `NoRiverResource`, and continue terrain rendering.
- `rivers.png` validation warnings: log warnings but generate from available data.
- Mesh generation exception: log error, clear river meshes, mark failure, and retry only after the config snapshot changes.
- Terrain not initialized yet: wait and retry next frame without marking failure.
- Missing `game/map/water/*.dds`: log error and disable river draw, but do not fail terrain loading.

## File Movement

Move shared river code into `Terrain`:

- river rendering: `RiverComponent`, `RiverProcessor`, `RiverRenderObject`, `RiverRenderFeature`, `RiverResourceLoader`, `RiverRenderSettings`, `RiverVertex`, `RiverMeshData`
- river generation: `RiverMapService`, `RiverMeshService`, `RiverSegment`, `RiverCell`
- river shaders: `RiverBottom.sdsl`, `RiverSurface.sdsl`, `RiverSceneSeed.sdsl`, `RiverCommon.sdsl`, `RiverWaterCommon.sdsl`, `RiverVertexStreams.sdsl`, `RiverStrideLighting.sdsl`

Editor-only wrappers remain in `Terrain.Editor`, including `RiverRenderingService`, `RiverViewModel`, and UI/debug controls.

## Stride Asset Requirements

Because River shaders move projects, the Stride shader asset workflow must be followed:

- `Terrain.sdpkg` must include the shader folder through `AssetFolders`.
- `Terrain.csproj` must compile generated `*.sdsl.cs` key files.
- Generated shader keys must be refreshed.
- Stride asset clean and compile targets must be run before runtime verification.

Runtime scene assets must be updated:

- `Terrain/Assets/MainScene.sdscene`: add `RiverSystem` with `RiverComponent`.
- `Terrain/Assets/GraphicsCompositor.sdgfxcomp`: add `RiverRenderFeature` and selector.
- `Terrain/Terrain.sdpkg`: add `River/Environment/reflection-specular` as a root asset so the runtime content database can load the reflection cubemap.

## Non-Goals

- Do not make `Terrain.Windows` reference `Terrain.Editor`.
- Do not create `RiverSystem` in runtime code.
- Do not register `RiverRenderFeature` in runtime code.
- Do not move editor debug UI or view-model controls into runtime.
- Do not simplify the river shader into a separate runtime-only visual path.

## Testing

Add text or behavior tests for:

- `Terrain.Windows.csproj` does not reference `Terrain.Editor`.
- River core types live under `Terrain`.
- `MainScene.sdscene` contains a `RiverSystem` entity with `RiverComponent`.
- `GraphicsCompositor.sdgfxcomp` contains `RiverRenderFeature`.
- The compositor selector uses `Transparent`, `Group1`, and `RiverSurface`.
- `GameRuntimeResourceBootstrap` exposes river path and width config without creating entities.
- `RiverProcessor` handles missing `rivers.png` without failing terrain.
- `RiverProcessor` waits when terrain height data is not initialized.
- Successful runtime generation bumps `RiverComponent.Version` and creates render objects.

Verification commands:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain/Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.sln --no-restore
git diff --check
```
