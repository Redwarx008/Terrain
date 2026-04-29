# Research: Stride Overlay Rendering for Brush Cursor

- **Query**: How to render screen-space overlays (terrain brush cursors) in the Stride game engine
- **Scope**: mixed (internal codebase + external Stride engine source)
- **Date**: 2026-04-29

## Findings

### Viable Approaches

Three approaches were identified for rendering a terrain-following brush circle overlay:

1. **SceneRenderer approach (recommended)**: Create a custom `SceneRendererBase` subclass that draws a circle overlay using `Sprite3DBatch` or custom vertex buffer logic. Add it to the compositor as a child renderer (same pattern as the wireframe renderer). Simplest, most consistent with existing project patterns, and allows direct access to `RenderDrawContext`.

2. **Entity + Material approach**: Use `GeometricPrimitive.Disc` mesh with a transparent material, similar to how Stride's grid gizmo works. Simplest conceptually but least flexible for terrain-following and custom visual effects.

3. **SubRenderFeature approach**: Add a `SubRenderFeature` to `EditorTerrainRenderFeature` that injects per-draw color/blend data. More complex, tightly coupled to the terrain feature, and harder to maintain independently.

**Recommendation**: Approach 1 (SceneRenderer) aligns best with the existing `TerrainWireframeModeController` pattern and allows direct access to `RenderDrawContext` for drawing.

---

### Files Found

| File Path | Description |
|---|---|
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\RenderFeature.cs` | Base class for all render features with lifecycle methods |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\RootRenderFeature.cs` | Adds RenderObjects, RenderNodes, RenderStageSelectors, SortKey |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\RootEffectRenderFeature.cs` | Adds effect compilation, PipelineProcessors, descriptor set management |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\SubRenderFeature.cs` | Lightweight feature attached to RootRenderFeature; has ProcessPipelineState virtual |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\PipelineProcessor.cs` | Abstract: `Process(RenderNodeReference, ref RenderNode, RenderObject, PipelineStateDescription)` |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\WireframePipelineProcessor.cs` | Example processor: sets RasterizerState.Wireframe for specific render stage |
| `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\AssetEditors\GameEditor\Game\AlphaBlendPipelineProcessor.cs` | Sets BlendStates.AlphaBlend + DepthStencilStates.DepthRead for a stage |
| `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\SceneEditor\HighlightRenderFeature.cs` | SubRenderFeature for highlighting with alpha blend and depth read |
| `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\SceneEditor\WireframeRenderFeature.cs` | SubRenderFeature for wireframe + selection pulsing with alpha blend |
| `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\SceneEditor\PickingRenderFeature.cs` | SubRenderFeature for picking, uses CreateDrawCBufferOffsetSlot |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Sprites\SpriteRenderFeature.cs` | Uses Sprite3DBatch in Draw; ThreadLocal pattern |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\Sprite3DBatch.cs` | 3D sprite batch: Begin(viewProjection), Draw(texture, worldMatrix, ...) |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\SpriteBatch.cs` | 2D sprite batch; Begin/End pattern; internal Bytecode property |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\UIBatch.cs` | Has DrawRectangle, DrawCube, DrawBackground but NO DrawCircle/DrawLine |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\PrimitiveQuad.cs` | Full-screen triangle using SpriteEffect |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Images\ImageEffectShader.cs` | Post-effect using DynamicEffectInstance + PrimitiveQuad |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\GeometricPrimitives\GeometricPrimitive.Disc.cs` | Procedural disc/circle geometry: `New(device, radius, angle, tessellation, ...)` |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Extensions\GeometricPrimitiveExtensions.cs` | `ToMeshDraw<T>()` converts GeometricPrimitive to MeshDraw |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Compositing\SceneRendererBase.cs` | Base: CollectCore(RenderContext) + DrawCore(RenderContext, RenderDrawContext) |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Compositing\DelegateSceneRenderer.cs` | Wraps `Action<RenderDrawContext>` for simple custom drawing |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Compositing\SingleStageRenderer.cs` | Renders a single RenderStage |
| `E:\WorkSpace\stride\sources\engine\Stride.Rendering\Rendering\Compositing\SceneRendererCollection.cs` | Renders list of ISceneRenderer children sequentially |
| `E:\WorkSpace\stride\sources\engine\Stride.Engine\Rendering\Compositing\EditorTopLevelCompositor.cs` | PreGizmoCompositors / PostGizmoCompositors lists |
| `E:\WorkSpace\stride\sources\editor\Stride.Assets.Presentation\AssetEditors\GameEditor\Game\EditorGameGridService.cs` | Key pattern: add render stage + selector + processor + renderer |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\BlendStates.cs` | AlphaBlend: src=One, dst=InverseSourceAlpha (premultiplied) |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\DepthStencilStates.cs` | DepthRead: enable=true, write=false; None: enable=false, write=false |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\RasterizerStateDescription.cs` | Has DepthBias, SlopeScaleDepthBias for Z-fighting resolution |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\FastTextRenderer.cs` | Low-level: MapSubResource + SetPipelineState + Apply effect + DrawIndexed |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\VertexPositionColorTexture.cs` | Position (Vector3), Color (Color), TextureCoordinate (Vector2), Size=24 |
| `E:\WorkSpace\stride\sources\engine\Stride.Graphics\VertexPositionColorTextureSwizzle.cs` | Position (Vector4), ColorScale/ColorAdd (Color4), UV (Vector2), Swizzle (float), Size=44 |
| `Terrain.Editor\Rendering\EditorTerrainRenderFeature.cs` | Project's terrain render feature; extends RootEffectRenderFeature |
| `Terrain.Editor\Rendering\EditorTerrainModeController.cs` | Dynamically adds wireframe stage + selector + processor + renderer |
| `Terrain.Editor\Rendering\NativeViewport\PresenterViewportSceneRenderer.cs` | SceneRendererBase drawing to back buffer with depth |
| `Terrain.Editor\Rendering\NativeViewport\EmbeddedStrideViewportGame.cs` | Main game; UpdateBrush handles brush input; RaycastTerrain for world position |
| `Terrain.Editor\Services\BrushParameters.cs` | Singleton: Size (1-200), Strength (0-1), Falloff (0-1, inverted) |
| `Terrain.Editor\Services\EditorState.cs` | Singleton: editor modes, tool states, events for change notification |

---

### Code Patterns

#### Pattern 1: Dynamically Adding Overlay Render Stages (EditorGameGridService)

From `EditorGameGridService.cs` (Stride editor source):

1. Create `RenderStage("GridGizmo", "Main")` and add to `GraphicsCompositor.RenderStages`
2. Add `SimpleGroupToRenderStageSelector` to `MeshRenderFeature.RenderStageSelectors`
3. Add `AlphaBlendPipelineProcessor` for the stage
4. Add `SingleStageRenderer` to `EditorTopLevelCompositor.PostGizmoCompositors`

This is the canonical Stride pattern for adding overlay render stages.

#### Pattern 2: Custom SceneRendererBase for Overlay Drawing

From `EditorTerrainModeController.cs` (project source):

The project already uses the pattern of dynamically adding render stages/selectors/processors/renderers. The wireframe mode controller:
- Ensures wireframe stage exists in compositor
- Ensures selector is registered on EditorTerrainRenderFeature
- Ensures WireframePipelineProcessor is registered
- Ensures SingleStageRenderer is added to SceneRendererCollection
- Controls visibility by swapping selectors in/out and setting `renderer.RenderStage`

For a brush overlay, a simpler approach is possible: create a custom `SceneRendererBase` that directly draws the circle using `Sprite3DBatch` or custom vertex buffer, without needing a separate render stage. The `DelegateSceneRenderer` class (wrapping `Action<RenderDrawContext>`) could even be used for prototyping.

#### Pattern 3: Sprite3DBatch for 3D Overlay Rendering

From `SpriteRenderFeature.cs` (Stride engine source):

```csharp
// ThreadLocal<ThreadContext> with Sprite3DBatch per thread
batchContext.SpriteBatch.Begin(
    context.GraphicsContext,
    renderView.ViewProjection,
    SpriteSortMode.Deferred,
    blendState,       // BlendStates.AlphaBlend for overlays
    samplerState,     // SamplerState.LinearClamp
    depthStencilState,// DepthStencilStates.DepthRead for overlays
    rasterizerState,  // RasterizerStates.CullNone
    currentEffect);

batchContext.SpriteBatch.Draw(
    texture,
    ref worldMatrix,
    ref sourceRegion,
    ref sprite.SizeInternal,
    ref color,
    orientation, swizzle, depth);

batchContext.SpriteBatch.End();
```

Key: `Sprite3DBatch` accepts a `viewProjection` matrix and draws sprites positioned in world space. For a brush cursor, a texture with a circle could be drawn at the terrain intersection point, oriented to face the camera or aligned to the terrain normal.

#### Pattern 4: GeometricPrimitive.Disc for Procedural Circle

From `GeometricPrimitive.Disc.cs`:

```csharp
GeometricPrimitive.New(device, radius, angle, tessellation, uScale, vScale, toLeftHanded)
```

Returns `GeometricPrimitive<VertexPositionNormalTexture>`. Can convert to `MeshDraw` via `ToMeshDraw()` extension. This could be used to create a disc mesh for the brush cursor, rendered with a transparent material.

#### Pattern 5: MutablePipelineState for Custom Drawing

From `FastTextRenderer.cs` and `ImageEffectShader.cs`:

```csharp
var pipelineState = new MutablePipelineState(device);
pipelineState.State.BlendState = BlendStates.AlphaBlend;
pipelineState.State.DepthStencilState = DepthStencilStates.DepthRead;
pipelineState.State.RasterizerState = RasterizerStates.CullNone;
pipelineState.State.PrimitiveType = PrimitiveType.TriangleList; // or LineList
pipelineState.State.InputElements = ...;
pipelineState.State.Effect = ...;
pipelineState.Update();
```

Then draw:
```csharp
commandList.SetPipelineState(pipelineState.CurrentState);
effectInstance.Apply(context.GraphicsContext); // binds shader resources
commandList.Draw(vertexCount, instanceCount);
```

#### Pattern 6: Brush Position Acquisition (Existing in Project)

From `EmbeddedStrideViewportGame.cs`:

The project already has `RaycastTerrain()` for getting the world-space intersection point and `UpdateBrush()` for handling brush input. `BrushParameters.Instance` provides Size/Strength/Falloff. The brush world position is already available -- only the visual rendering is missing.

#### Pattern 7: Pipeline State for Overlay Rendering

Recommended pipeline state for brush cursor overlay:
- **Blend**: `BlendStates.AlphaBlend` (src=One, dst=InverseSourceAlpha, premultiplied alpha)
- **Depth**: `DepthStencilStates.DepthRead` (test against depth buffer but don't write, so overlay appears behind objects that occlude the terrain)
- **Rasterizer**: `RasterizerStates.CullNone` with optional `DepthBias` / `SlopeScaleDepthBias` to prevent Z-fighting with terrain surface
- **Primitive**: `PrimitiveType.LineList` for wireframe circle or `PrimitiveType.TriangleList` for filled disc

---

### External References

- Stride engine source at `E:\WorkSpace\stride` (local clone)
- No external web references needed; all API surface is from the local Stride source

### Related Specs

- `.trellis/tasks/04-29-fix-brush-projection/` -- task for fixing brush projection after Avalonia migration

---

## Recommended Implementation Plan

### Simplest Approach: Custom SceneRendererBase

1. Create `EditorTerrainBrushOverlayRenderer : SceneRendererBase`
2. In `DrawCore`, use `Sprite3DBatch` to draw a circle texture at the brush world position
   - The circle texture can be generated procedurally or loaded from assets
   - Position the sprite at the terrain intersection point, oriented by terrain normal
3. Add this renderer to the `SceneRendererCollection` in the graphics compositor, after the main scene renderer (same pattern as wireframe stage renderer in `EditorTerrainModeController`)
4. Feed brush position from `EmbeddedStrideViewportGame.UpdateBrush()` into the overlay renderer

### Alternative: Direct Vertex Buffer Drawing

For maximum control (e.g., line-based circle rendering):
1. Create `EditorTerrainBrushOverlayRenderer : SceneRendererBase`
2. Generate circle vertices in world space (terrain-following)
3. Use `MutablePipelineState` with `PrimitiveType.LineList`, alpha blend, depth read
4. Map vertex data via `commandList.MapSubResource`, then `Draw`
5. This matches the `FastTextRenderer` pattern

---

## Caveats / Not Found

- **UIBatch lacks circle/line primitives**: `UIBatch` only has `DrawRectangle`, `DrawCube`, `DrawBackground`, `DrawImage`. No `DrawCircle` or `DrawLine` methods. A custom approach is required.
- **Sprite3DBatch requires a texture**: To draw a circle via `Sprite3DBatch`, you need a circle texture (can be procedurally generated or embedded as an asset).
- **No existing brush overlay code in the project**: The brush position is computed but no visual cursor is currently rendered.
- **Premultiplied alpha**: Stride's `BlendStates.AlphaBlend` uses premultiplied alpha (src=One, dst=InverseSourceAlpha). Circle texture colors must be premultiplied, or use a custom blend state with src=SourceAlpha.
- **Z-fighting**: If drawing a filled disc on the terrain surface, use `DepthBias` or a small world-space offset to prevent Z-fighting.
- **Compositor structure**: The project uses `PresenterViewportSceneRenderer` wrapping a `SceneRendererCollection`. The overlay renderer should be added as a child of this collection, after the main scene camera renderer.
