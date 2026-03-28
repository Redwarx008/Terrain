# Phase 1: Project Foundation - Research

**Researched:** 2026-03-29
**Domain:** Stride Engine 3D Preview + Camera Navigation + ImGui Integration
**Confidence:** HIGH

## Summary

Phase 1 establishes the foundational 3D preview and camera navigation system for the terrain slot editor. The editor already has a working ImGui-based UI shell with panel layout system, and the core terrain rendering pipeline (TerrainRenderFeature, TerrainQuadTree, TerrainStreamingManager) exists and is functional. The key integration work involves: (1) connecting Stride 3D rendering to an ImGui panel via RenderTarget, (2) implementing hybrid orbit/fly camera controls with terrain-adaptive behavior, and (3) adding File -> Open functionality to load heightmap PNGs and create terrain entities at runtime.

**Primary recommendation:** Extend existing SceneViewPanel with RenderTarget-based Stride rendering and implement camera controller that blends orbit mode (default) with free-fly mode (modifier key). Use Windows native file dialog for heightmap loading, and create TerrainComponent dynamically after loading.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Terrain Data Source**
- **D-01:** Editor starts with empty scene, user loads heightmap via File -> Open
- **D-02:** Support loading from PNG heightmap files (16-bit grayscale)
- **D-03:** After loading, automatically create Terrain entity and add to scene

**Camera Navigation Mode**
- **D-04:** Hybrid camera mode: default orbit rotation, hold key to switch to free flight
- **D-05:** Orbit center point can be adjusted by user (pan focus), double-click resets to terrain center
- **D-06:** Right-drag rotate, middle-drag pan, scroll wheel zoom (already has basic implementation)
- **D-07:** Free flight mode uses WASD + mouse look

**Rendering Integration**
- **D-08:** Stride 3D rendering embedded within ImGui window (SceneViewPanel area)
- **D-09:** Use RenderTarget to render Stride result to ImGui Image
- **D-10:** Need to handle ImGui window resize synchronization with Stride BackBuffer

**LOD Strategy**
- **D-11:** Reuse existing Terrain/Rendering/ complete LOD system
- **D-12:** Includes TerrainQuadTree, TerrainStreamingManager, TerrainComputeDispatcher
- **D-13:** Camera parameters passed to LOD system for chunk selection and streaming priority

### Claude's Discretion

- Default heightmap size (placeholder if user has not loaded file) -- can use small test heightmap or not display terrain at all
- Camera initial position and angle -- auto-adapt to loaded terrain bounds

### Deferred Ideas (OUT OF SCOPE)

None -- discussion stayed within phase scope.

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PREV-01 | User can see real-time 3D preview with terrain LOD | TerrainRenderFeature + TerrainQuadTree + TerrainStreamingManager exist and are functional; need to connect to RenderTarget for ImGui embedding |
| PREV-02 | User can orbit camera around terrain | SceneViewPanel has basic orbit input handling; extend with orbit center tracking and terrain-aware bounds |
| PREV-03 | User can pan camera | Middle-drag pan already detected in SceneViewPanel; need to implement world-space pan relative to orbit center |
| PREV-04 | User can zoom camera in/out | Scroll wheel zoom already detected; need to implement zoom-to-point and terrain-aware distance limits |

</phase_requirements>

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Stride Engine | 4.3.0.2507 | 3D rendering, scene management, camera | Existing codebase foundation; TerrainRenderFeature already implemented |
| ImGui.NET (via Hexa.NET.ImGui) | 1.91.6.1 | Editor UI panels | Integrated via Stride.CommunityToolkit.ImGui 1.0.0-preview.62 |
| SixLabors.ImageSharp | 3.1.12 | PNG heightmap loading | Used in TerrainPreProcessor; supports L16 format |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Stride.CommunityToolkit.ImGui | 1.0.0-preview.62 | ImGui-Stride integration | Handles context lifecycle, input forwarding |
| Windows.Storage.Pickers | Built-in (Win10+) | Native file dialogs | For File -> Open heightmap loading |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Windows.Storage.Pickers | ImGuiFileDialog | Native dialogs provide better OS integration, recent files, folder shortcuts |
| Custom camera controller | Stride CameraComponent only | Need orbit center + fly mode switch; standard CameraComponent insufficient |

**Installation:** No new packages required. All dependencies are in Terrain.Editor.csproj.

## Architecture Patterns

### Recommended Project Structure

```
Terrain.Editor/
├── EditorGame.cs           # Game bootstrap, scene initialization
├── UI/
│   ├── MainWindow.cs       # Menu bar, panel layout orchestration
│   ├── Panels/
│   │   ├── SceneViewPanel.cs   # 3D viewport (extends existing)
│   │   └── ...
│   └── ...
├── Rendering/
│   └── SceneRenderTarget.cs    # RenderTarget for ImGui embedding
├── Input/
│   └── HybridCameraController.cs   # Orbit + fly camera
└── Services/
    ├── HeightmapLoader.cs      # PNG -> Texture2D conversion
    └── TerrainManager.cs       # Dynamic terrain entity creation
```

### Pattern 1: RenderTarget-based ImGui Embedding

**What:** Render Stride 3D scene to a Texture2D (RenderTarget), then display it as an ImGui image in the SceneViewPanel.

**When to use:** All 3D viewport rendering in editor panels.

**Example:**
```csharp
// In EditorGame or a dedicated rendering service
public Texture? CreateSceneRenderTarget(GraphicsDevice device, int width, int height)
{
    return Texture.New2D(device, width, height, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
}

// During Draw()
GraphicsContext.CommandList.SetRenderTargetAndViewport(sceneRenderTarget, depthBuffer);
// Clear and render scene...
sceneSystem.Draw(gameTime);

// In SceneViewPanel ImGui rendering
if (sceneRenderTarget != null)
{
    ImGui.Image(sceneRenderTarget.NativeResource.NativePointer.ToPointer(),
        new Vector2(viewSize.X, viewSize.Y),
        new Vector2(0, 0), new Vector2(1, 1),
        new Vector4(1, 1, 1, 1), new Vector4(0, 0, 0, 0));
}
```

### Pattern 2: Hybrid Camera Controller

**What:** Camera that defaults to orbit mode around a center point, with optional fly mode on key press.

**When to use:** Editor viewport camera navigation.

**Example:**
```csharp
public class HybridCameraController
{
    private Vector3 orbitCenter;
    private float orbitDistance;
    private float yaw, pitch;

    public bool IsFlyModeActive { get; private set; }
    public CameraComponent? Camera { get; set; }

    public void Update(float deltaTime, InputManager input)
    {
        // Toggle fly mode with modifier key (Shift or mouse button)
        IsFlyModeActive = input.IsKeyDown(Keys.LeftShift);

        if (IsFlyModeActive)
        {
            UpdateFlyMode(deltaTime, input);
        }
        else
        {
            UpdateOrbitMode(deltaTime, input);
        }
    }

    private void UpdateOrbitMode(float deltaTime, InputManager input)
    {
        if (input.IsMouseButtonDown(MouseButton.Right))
        {
            yaw -= input.MouseDelta.X * rotationSpeed;
            pitch -= input.MouseDelta.Y * rotationSpeed;
            pitch = Math.Clamp(pitch, -89, 89);
        }

        // Middle-drag pan moves orbit center
        if (input.IsMouseButtonDown(MouseButton.Middle))
        {
            var right = Vector3.Normalize(Vector3.Cross(Camera.ViewMatrix.Forward, Vector3.UnitY));
            var up = Vector3.UnitY;
            orbitCenter += right * -input.MouseDelta.X * panSpeed;
            orbitCenter += up * input.MouseDelta.Y * panSpeed;
        }

        // Scroll zoom
        orbitDistance = Math.Max(minDistance, orbitDistance - input.MouseWheelDelta * zoomSpeed);

        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        var offset = Vector3.Transform(Vector3.Forward * orbitDistance,
            Quaternion.RotationYawPitchRoll(yaw, pitch, 0));
        Camera.Entity.Transform.Position = orbitCenter + offset;
        Camera.Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
    }
}
```

### Pattern 3: Dynamic Terrain Creation from Heightmap

**What:** Load PNG heightmap, create TerrainComponent with necessary data, add to scene.

**When to use:** File -> Open heightmap functionality.

**Example:**
```csharp
public class HeightmapLoader
{
    public static Texture? LoadHeightmapPng(GraphicsDevice device, string path)
    {
        using var image = Image.Load<L16>(path);
        var texture = Texture.New2D(device, image.Width, image.Height,
            PixelFormat.R16_UNorm, TextureFlags.ShaderResource);
        // Upload pixel data to texture...
        return texture;
    }
}

public class TerrainManager
{
    public Entity CreateTerrainEntity(string heightmapPath, GraphicsDevice device, Scene scene)
    {
        // 1. Load heightmap
        var heightmapTexture = HeightmapLoader.LoadHeightmapPng(device, heightmapPath);

        // 2. Process heightmap -> .terrain format (or use existing TerrainPreProcessor)
        //    For Phase 1, can generate .terrain file on disk and load it,
        //    or create TerrainComponent with in-memory data.

        // 3. Create entity with TerrainComponent
        var entity = new Entity("Terrain");
        var terrain = new TerrainComponent
        {
            TerrainDataPath = processedTerrainPath, // .terrain file path
            HeightScale = 100.0f,
            DefaultDiffuseTexture = CreateDefaultTexture(device)
        };
        entity.Add(terrain);

        // 4. Add to scene
        scene.Entities.Add(entity);

        return entity;
    }
}
```

### Anti-Patterns to Avoid

- **Direct SceneViewPanel input handling without camera abstraction:** Camera logic mixed with UI code is hard to test and extend. Instead, create a dedicated HybridCameraController class.
- **Creating TerrainComponent without .terrain file:** TerrainProcessor expects TerrainDataPath to point to a valid .terrain file with precomputed MinMaxErrorMaps. Cannot skip this preprocessing step.
- **Ignoring ImGui.WantCaptureMouse:** Mouse events in the viewport must check if ImGui already consumed them (e.g., over a UI element).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| File dialog for opening heightmaps | Custom ImGui file browser | Windows.Storage.Pickers (native) | Better OS integration, recent files, folder shortcuts |
| Camera transformation math | Manual matrix operations | Stride.Mathematics utilities | Quaternion, Matrix helpers exist and are tested |
| Heightmap to .terrain conversion | Runtime MinMaxErrorMap generation | TerrainPreProcessor (existing) | MinMaxErrorMap generation is expensive; reuse preprocessor logic |
| ImGui input handling | Custom event routing | EditorUIRenderer pattern | Already handles ImGui context and input forwarding |

**Key insight:** TerrainPreProcessor already handles PNG -> .terrain conversion. For Phase 1, can either (1) shell out to TerrainPreProcessor as a subprocess, (2) reference its logic directly, or (3) generate a simplified .terrain file inline. The MinMaxErrorMap generation is the expensive part and should not be hand-rolled.

## Common Pitfalls

### Pitfall 1: RenderTarget Size Mismatch

**What goes wrong:** When the ImGui window is resized, the Stride RenderTarget is not resized to match. The image appears stretched or has black bars.

**Why it happens:** ImGui windows can be resized freely, but Stride RenderTargets have fixed dimensions set at creation time.

**How to avoid:** Track SceneViewPanel content rect size. When size changes beyond a threshold, recreate the RenderTarget with new dimensions.

**Warning signs:**
- Image stretches when resizing window
- Black bars appear around viewport
- RenderTarget resolution looks wrong after maximizing

### Pitfall 2: Camera Orbit Center Drifts

**What goes wrong:** After panning and zooming, the orbit center is no longer on the terrain, causing disorienting camera behavior.

**Why it happens:** Pan operations move the orbit center but don't constrain it to reasonable bounds. Zooming while the center is off-terrain makes it hard to get back.

**How to avoid:** Clamp orbit center to terrain bounds (if terrain is loaded). Provide double-click to reset center to terrain center.

**Warning signs:**
- Camera "orbits around nothing"
- Users get lost in the scene
- Zooming feels wrong (zooms to empty space)

### Pitfall 3: Terrain Streaming Starvation on Load

**What goes wrong:** When a terrain is first loaded, the screen stays black for several seconds while chunks stream in. LOD system has no data to render.

**Why it happens:** TerrainStreamingManager uses background thread to load heightmap pages. The first frame after entity creation has no resident pages.

**How to avoid:** After creating terrain entity, wait for PreloadTopLevelChunks to complete before showing the terrain. Alternatively, show a loading indicator while the top-level chunks stream in.

**Warning signs:**
- Black screen for 1-2 seconds after File -> Open
- Console warnings about missing pages
- Terrain "pops in" suddenly

### Pitfall 4: ImGui Input Capture Race

**What goes wrong:** Mouse clicks in the viewport are sometimes consumed by ImGui (e.g., when hovering over a gizmo or UI overlay), sometimes passed to the camera controller. Behavior is inconsistent.

**Why it happens:** ImGui's WantCaptureMouse flag indicates whether ImGui wants to process the mouse event. Not checking this flag leads to camera movement when trying to click a button.

**How to avoid:** Always check `ImGui.GetIO().WantCaptureMouse` before processing camera input. Pass events through only when ImGui doesn't want them.

**Warning signs:**
- Camera moves when clicking on UI elements
- Right-drag sometimes doesn't work
- Mouse input feels "stolen"

## Code Examples

### RenderTarget Creation and Resize Handling

```csharp
// Source: Derived from existing EditorGame.cs pattern
public class SceneRenderTargetManager
{
    private Texture? renderTarget;
    private Texture? depthBuffer;
    private Vector2 lastSize;

    public Texture? GetOrCreate(GraphicsDevice device, Vector2 size)
    {
        if (renderTarget == null ||
            Math.Abs(renderTarget.Width - size.X) > 1 ||
            Math.Abs(renderTarget.Height - size.Y) > 1)
        {
            renderTarget?.Dispose();
            depthBuffer?.Dispose();

            int width = Math.Max(1, (int)size.X);
            int height = Math.Max(1, (int)size.Y);

            renderTarget = Texture.New2D(device, width, height,
                PixelFormat.R8G8B8A8_UNorm,
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);
            depthBuffer = Texture.New2D(device, width, height,
                PixelFormat.D24_UNorm_S8_UInt,
                TextureFlags.DepthStencil);
            lastSize = size;
        }
        return renderTarget;
    }
}
```

### Terrain Bounds Query for Camera

```csharp
// Source: Derived from TerrainComponent.cs and TerrainProcessor.cs
public static BoundingBox GetTerrainBounds(TerrainComponent terrain)
{
    if (!terrain.IsInitialized)
        return new BoundingBox(Vector3.Zero, Vector3.Zero);

    float worldHeightScale = terrain.HeightScale * TerrainComponent.HeightSampleNormalization;
    return new BoundingBox(
        new Vector3(0, terrain.MinHeight * worldHeightScale, 0),
        new Vector3(
            terrain.HeightmapWidth - 1,
            terrain.MaxHeight * worldHeightScale,
            terrain.HeightmapHeight - 1));
}

// Reset camera center to terrain center
public void ResetCameraToTerrainCenter(TerrainComponent terrain, HybridCameraController camera)
{
    var bounds = GetTerrainBounds(terrain);
    camera.OrbitCenter = (bounds.Minimum + bounds.Maximum) * 0.5f;
    camera.OrbitDistance = Math.Max(bounds.Maximum.X, bounds.Maximum.Z) * 1.5f;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Fixed camera controller | Hybrid orbit/fly camera | Phase 1 design | Better navigation for terrain inspection |
| Direct scene rendering | RenderTarget -> ImGui embedding | Phase 1 design | Enables dockable viewport panels |
| Loading .terrain at startup | Runtime terrain creation from PNG | Phase 1 design | Supports File -> Open workflow |

**Deprecated/outdated:**
- BasicCameraController: First-person style only, no orbit mode. Replace with HybridCameraController.

## Open Questions

1. **How to handle heightmap -> .terrain conversion at runtime?**
   - What we know: TerrainPreProcessor can convert PNG to .terrain with MinMaxErrorMaps.
   - What's unclear: Should we (a) shell out to TerrainPreProcessor.exe, (b) reference its logic, or (c) implement a simplified inline converter?
   - Recommendation: For Phase 1, reference TerrainPreProcessor.Services.TerrainProcessor logic directly. The heavy lifting (MinMaxErrorMap generation, SVT tiling) is already implemented and reusable.

2. **What default texture should TerrainComponent.DefaultDiffuseTexture use?**
   - What we know: TerrainComponent requires DefaultDiffuseTexture to be set for rendering.
   - What's unclear: Should we create a procedural texture, or require user to provide one?
   - Recommendation: Create a simple procedural checkerboard or solid color texture for Phase 1. Material management comes in later phases.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Stride Engine | 3D rendering | Yes | 4.3.0.2507 | -- |
| SixLabors.ImageSharp | PNG loading | Yes | 3.1.12 | -- |
| Stride.CommunityToolkit.ImGui | UI | Yes | 1.0.0-preview.62 | -- |
| Windows.Storage.Pickers | File dialog | Yes | Built-in | -- |
| .NET 10 SDK | Runtime | Yes | 10.0 | -- |

**Missing dependencies with no fallback:** None

**Missing dependencies with fallback:** None

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit (Stride standard) |
| Config file | None detected -- Wave 0 needed |
| Quick run command | `dotnet test --filter "Category=Unit"` |
| Full suite command | `dotnet test` |

### Phase Requirements -> Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PREV-01 | Real-time 3D preview with LOD | Integration | N/A -- visual verification | No -- Wave 0 |
| PREV-02 | Orbit camera around terrain | Unit | `dotnet test --filter "HybridCameraController"` | No -- Wave 0 |
| PREV-03 | Pan camera | Unit | `dotnet test --filter "HybridCameraController"` | No -- Wave 0 |
| PREV-04 | Zoom camera in/out | Unit | `dotnet test --filter "HybridCameraController"` | No -- Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test --filter "Category=Unit"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** All tests green + manual visual verification of 3D preview

### Wave 0 Gaps

- [ ] `tests/Terrain.Editor.Tests/HybridCameraControllerTests.cs` -- covers PREV-02, PREV-03, PREV-04
- [ ] `tests/Terrain.Editor.Tests/HeightmapLoaderTests.cs` -- covers PNG loading
- [ ] `tests/Terrain.Editor.Tests/xunit.json` -- test configuration
- [ ] Framework install: `dotnet add package xunit` -- if test project doesn't exist

## Sources

### Primary (HIGH confidence)

- Existing codebase analysis: Terrain/Rendering/, Terrain/Streaming/, Terrain.Editor/UI/
- TerrainComponent.cs, TerrainProcessor.cs -- terrain initialization flow
- TerrainQuadTree.cs -- LOD selection and camera integration
- TerrainStreaming.cs -- page loading and GPU upload
- SceneViewPanel.cs -- existing camera input handling

### Secondary (MEDIUM confidence)

- CLAUDE.md -- project conventions and patterns
- CONTEXT.md -- user decisions from discuss-phase
- STACK.md -- verified technology choices

### Tertiary (LOW confidence)

- None for this phase -- all research based on existing codebase

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- All dependencies verified in project files
- Architecture: HIGH -- Patterns derived from existing working code
- Pitfalls: HIGH -- Based on codebase analysis and domain knowledge

**Research date:** 2026-03-29
**Valid until:** 30 days (stable stack, no fast-moving dependencies)
