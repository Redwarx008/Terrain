# Phase 2: Brush System Core - Research

**Researched:** 2026-03-29
**Domain:** ImGui-based brush parameter configuration and viewport overlay rendering
**Confidence:** HIGH

## Summary

Phase 2 implements a brush parameter configuration system that allows users to adjust brush size, strength, and falloff via UI sliders, with real-time brush preview in the 3D viewport. The research reveals that significant infrastructure already exists: `BrushParamsPanel` and `BrushesPanel` in `RightPanel.cs` provide functional UI, `SceneViewPanel.cs` has established patterns for viewport mouse tracking, and the color palette offers a complete set of styling colors.

The primary work involves: (1) creating a shared `BrushParameters` service class to decouple state from UI, (2) modifying default values and slider ranges per CONTEXT.md decisions, (3) implementing viewport brush cursor overlay using ImGui draw list primitives, and (4) inverting the falloff slider's visual semantics (right=hard edge, left=soft edge).

**Primary recommendation:** Extract brush parameters into a singleton service, add overlay rendering to `SceneViewPanel`, and reuse existing UI infrastructure with minor adjustments.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** Brush parameters use global singleton state storage (`BrushParameters` service class)
- **D-02:** All editing tools share the same brush parameters; switching tools preserves parameters
- **D-03:** Parameter changes notify subscribers via events (viewport preview, future editing system)
- **D-04:** Size: default 30, range 1-200 (suitable for fine editing)
- **D-05:** Strength: default 0.5, range 0-1, linear mapping to editing intensity
- **D-06:** Falloff: default 0.5, range 0-1, **inverted logic** (1=hard edge, 0=soft edge)
- **D-07:** Mouse movement in viewport shows circular outline representing brush size
- **D-08:** Circular outline uses dashed/semi-transparent fill to distinguish hard edge vs falloff area
- **D-09:** Preview only visible when viewport has focus
- **D-10:** Modify existing `RightPanel.BrushParamsPanel` defaults and ranges
- **D-11:** Invert Falloff slider visual logic (right=harder, left=softer)
- **D-12:** Initially only enable Circle brush (other shapes shown but disabled, deferred to Phase 5)

### Claude's Discretion
- Specific rendering style for brush preview circle (dashed/filled/color)
- Whether preview circle follows terrain height contours

### Deferred Ideas (OUT OF SCOPE)
- **Square and Noise brush shapes** — Phase 5 implementation
- **Brush preset save/load** — Future feature, not in v1 scope
- **Per-tool independent parameters** — May be added later if user feedback warrants

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BRUSH-01 | User can adjust brush size via slider/input | `BrushParamsPanel` has functional size slider; needs range change (1-200) and default change (30) |
| BRUSH-02 | User can adjust brush strength/opacity via slider/input | `BrushParamsPanel` has functional strength slider; range 0-1 is correct, default 0.5 is correct |
| BRUSH-03 | User can select circular brush shape | `BrushesPanel` has brush selection grid; Circle is first option; need to disable Square/Smooth/Noise |
| BRUSH-06 | User can adjust brush falloff/feathering | `BrushParamsPanel` has falloff slider; needs semantic inversion (1=hard, 0=soft) with visual indicators |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Hexa.NET.ImGui | 1.90.0.1 | Immediate mode UI | Already integrated via Stride.CommunityToolkit.ImGui |
| Stride.CommunityToolkit.ImGui | 1.0.0-preview.62 | Stride ImGui integration | Project standard for editor UI |
| Stride.Engine | 4.3.0.2507 | Game engine framework | Core runtime for terrain rendering |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Numerics | .NET 10.0 | Vector math for UI | ImGui uses System.Numerics.Vector2/Vector4 |
| Stride.Core.Mathematics | 4.3.0.2507 | Engine math types | Stride APIs; requires conversion aliases |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ImGui overlay | Separate 2D rendering pass | ImGui draw list is simpler and already integrated; separate pass adds complexity |
| Singleton service | Dependency injection container | Singleton is simpler for single-window editor; DI is overkill |

**Version verification:** Confirmed via existing project references in Terrain.Editor.csproj.

## Architecture Patterns

### Recommended Project Structure
```
Terrain.Editor/
  Services/
    BrushParameters.cs        # NEW: Shared brush state service
  UI/
    Panels/
      RightPanel.cs           # MODIFY: Use BrushParameters service
      SceneViewPanel.cs       # MODIFY: Add brush preview overlay
    Styling/
      ColorPalette.cs         # USE: Existing colors for preview
      EditorStyle.cs          # USE: Scaling utilities
```

### Pattern 1: Service Singleton for Shared State
**What:** A static or singleton class that holds brush parameters and raises events on change.
**When to use:** Multiple UI components need access to the same state (RightPanel, SceneViewPanel, future editing system).
**Example:**
```csharp
// Source: Established pattern in Terrain.Editor.Services
namespace Terrain.Editor.Services;

public sealed class BrushParameters
{
    private static readonly Lazy<BrushParameters> _instance = new(() => new());
    public static BrushParameters Instance => _instance.Value;

    private float _size = 30.0f;
    private float _strength = 0.5f;
    private float _falloff = 0.5f;
    private int _selectedBrushIndex = 0;

    public float Size
    {
        get => _size;
        set { if (_size != value) { _size = value; ParametersChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public float Strength
    {
        get => _strength;
        set { if (_strength != value) { _strength = value; ParametersChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public float Falloff
    {
        get => _falloff;
        set { if (_falloff != value) { _falloff = value; ParametersChanged?.Invoke(this, EventArgs.Empty); } }
    }

    public event EventHandler? ParametersChanged;

    // Falloff is inverted: 1 = hard edge, 0 = soft edge
    public float EffectiveFalloff => 1.0f - Falloff;
}
```

### Pattern 2: ImGui Overlay Rendering
**What:** Draw primitives directly onto ImGui draw list after scene image is rendered.
**When to use:** Brush preview cursor that overlays the 3D viewport without affecting scene rendering.
**Example:**
```csharp
// Source: SceneViewPanel.cs Render3DView() pattern
private void RenderBrushPreview(NumericsVector2 viewPos, NumericsVector2 viewSize)
{
    if (!IsViewportHovered || !IsTerrainLoaded)
        return;

    var brushParams = BrushParameters.Instance;
    var drawList = ImGui.GetWindowDrawList();

    // Convert brush size (world units) to screen pixels
    float screenRadius = WorldToScreenRadius(brushParams.Size);

    // Outer circle - falloff boundary
    drawList.AddCircle(
        mousePosition,
        screenRadius,
        ColorPalette.Accent.WithAlpha(0.5f).ToUint(),
        0,  // segments (0 = auto)
        2.0f);  // thickness

    // Inner circle - 100% strength area
    float innerRadius = screenRadius * (1.0f - brushParams.Falloff);
    if (innerRadius > 1.0f)
    {
        drawList.AddCircle(
            mousePosition,
            innerRadius,
            ColorPalette.Accent.ToUint(),
            0,
            1.5f);
    }
}
```

### Pattern 3: Event Subscription for UI Updates
**What:** Subscribe to parameter change events to update dependent systems.
**When to use:** RightPanel needs to sync UI controls, SceneViewPanel needs to update preview.
**Example:**
```csharp
// Source: RightPanel.cs event pattern
public class RightPanel : PanelBase
{
    private readonly BrushParameters _brushParams = BrushParameters.Instance;

    public RightPanel()
    {
        _brushParams.ParametersChanged += OnBrushParametersChanged;
    }

    private void OnBrushParametersChanged(object? sender, EventArgs e)
    {
        // Sync UI controls to service state
        brushParamsPanel.BrushSize = _brushParams.Size;
        brushParamsPanel.BrushStrength = _brushParams.Strength;
        brushParamsPanel.BrushFalloff = _brushParams.Falloff;
    }
}
```

### Anti-Patterns to Avoid
- **Storing brush state in UI controls:** Makes state dependent on UI lifecycle, breaks when panels are hidden/destroyed. Use a service class instead.
- **Direct coupling between panels:** RightPanel and SceneViewPanel should not reference each other directly. Use the shared service.
- **Ignoring the falloff inversion requirement:** The UI should display "Hard/Soft" labels to clarify the inverted semantics.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Circle rendering | Custom shader or geometry | ImGui.AddCircle/AddCircleFilled | Already available, works in screen space, no GPU setup |
| State synchronization | Manual property copying | Event-driven updates | Existing pattern in RightPanel.ParamsChanged event |
| Color management | Hardcoded colors | ColorPalette static class | Consistent styling, alpha support via WithAlpha() |
| Mouse position tracking | Raw Win32 API | ImGui.GetIO().MousePos | Already working in SceneViewPanel.HandleCameraInput() |

**Key insight:** The existing codebase has all the primitives needed; the work is primarily wiring and parameter adjustments.

## Runtime State Inventory

> This phase does not involve rename/refactor/migration. Section omitted.

## Common Pitfalls

### Pitfall 1: Falloff Slider Confusion
**What goes wrong:** Users expect higher slider value = softer edge (more falloff), but D-06 requires inverted logic (higher = harder edge).
**Why it happens:** Intuitive mapping conflicts with technical implementation.
**How to avoid:** Add visual labels "Hard" and "Soft" at slider extremes; update tooltip to show current semantic.
**Warning signs:** User testing reveals confusion about brush edge behavior.

### Pitfall 2: Screen-Space vs World-Space Brush Size
**What goes wrong:** Brush preview size doesn't match actual brush effect size when camera distance changes.
**Why it happens:** Brush size is in world units, but preview is in screen pixels.
**How to avoid:** Implement WorldToScreenRadius() that accounts for camera distance and field of view. The formula: `screenPixels = worldUnits * (viewportHeight / (2 * distance * tan(fov/2)))`.
**Warning signs:** Preview circle appears same size regardless of zoom level.

### Pitfall 3: Viewport Focus State
**What goes wrong:** Brush preview visible when viewport doesn't have focus, interfering with other UI interactions.
**Why it happens:** Mouse position tracking doesn't check viewport focus state.
**How to avoid:** Only render preview when `IsViewportHovered && !IsViewportInteracting` (camera is not being manipulated).
**Warning signs:** Brush preview appears while dragging in other panels.

### Pitfall 4: Disabled Brush Shape Selection
**What goes wrong:** Disabled brushes look broken or confuse users.
**Why it happens:** Simply greying out without explanation.
**How to avoid:** Use `EditorStyle.PushDisabled()` for visual feedback; add tooltip "Coming in Phase 5" when hovering disabled shapes.
**Warning signs:** Users click disabled brushes multiple times expecting action.

## Code Examples

Verified patterns from existing codebase:

### Brush Preview Visualization (from RightPanel.cs)
```csharp
// Source: RightPanel.cs RenderBrushPreview() - existing pattern
private void RenderBrushPreview()
{
    float previewSize = Math.Min(ImGui.GetContentRegionAvail().X, 100);
    Vector2 cursor = ImGui.GetCursorScreenPos();

    var drawList = ImGui.GetWindowDrawList();

    // Background
    drawList.AddRectFilled(cursor, new Vector2(cursor.X + previewSize, cursor.Y + previewSize), ColorPalette.DarkBackground.ToUint());

    // Brush circle with falloff visualization
    Vector2 center = new Vector2(cursor.X + previewSize * 0.5f, cursor.Y + previewSize * 0.5f);
    float radius = previewSize * 0.4f * (BrushSize / 500.0f + 0.1f);

    // Outer circle (full strength area)
    drawList.AddCircleFilled(center, radius, ColorPalette.Accent.WithAlpha(0.3f).ToUint());

    // Inner circle (falloff area)
    float innerRadius = radius * (1.0f - BrushFalloff);
    drawList.AddCircleFilled(center, innerRadius, ColorPalette.Accent.WithAlpha(0.6f).ToUint());

    // Border
    drawList.AddCircle(center, radius, ColorPalette.Border.ToUint());

    ImGui.Dummy(new Vector2(previewSize, previewSize));
}
```

### Slider Value Changed Event (from RightPanel.cs)
```csharp
// Source: RightPanel.cs - existing event pattern
if (ImGui.SliderFloat("##brush_size", ref size, 1.0f, 500.0f, "%.0f"))
{
    BrushSize = size;
    ParamsChanged?.Invoke(this, new BrushParamsChangedEventArgs { Param = "Size", Value = BrushSize });
}
```

### Color with Alpha (from ColorPalette.cs)
```csharp
// Source: ColorPalette.cs - existing extension method
public static Color4 WithAlpha(this Color4 color, float alpha)
{
    return new Color4(color.R, color.G, color.B, alpha);
}
```

### Disabled State Styling (from EditorStyle.cs)
```csharp
// Source: EditorStyle.cs - existing utility methods
public static void PushDisabled()
{
    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
}

public static void PopDisabled()
{
    ImGui.PopStyleVar();
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Direct UI state | Service-based state | Phase 2 (this phase) | Enables multiple consumers, cleaner architecture |
| Fixed slider ranges | User-defined defaults | Phase 2 (this phase) | Better defaults for fine editing |
| Visual falloff = value | Inverted falloff semantics | Phase 2 (this phase) | 1=hard, 0=soft matches artist intuition |

**Deprecated/outdated:**
- BrushParamsPanel holding its own state: Should consume from BrushParameters service.

## Open Questions

1. **Should brush preview follow terrain height?**
   - What we know: SceneViewPanel has access to TerrainManager for terrain bounds, but not per-pixel height queries.
   - What's unclear: Performance impact of raycasting every frame for height sampling.
   - Recommendation: Start with flat circle at terrain center height; defer height-following to Phase 3 when height editing is implemented.

2. **What visual style for the brush preview circles?**
   - What we know: ColorPalette.Accent is blue (#007ACC), existing preview uses semi-transparent fills.
   - What's unclear: Whether to use dashed lines or solid with transparency for outer boundary.
   - Recommendation: Use solid circles with alpha transparency, matching the existing BrushParamsPanel preview style. Dashed lines are more complex in ImGui and provide minimal benefit.

## Environment Availability

> This phase has no external dependencies beyond the existing project setup.

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10.0 SDK | Build | ✓ | 10.0 | - |
| Stride 4.3.0 | Rendering | ✓ | 4.3.0.2507 | - |
| ImGui.NET | UI | ✓ | 1.90.0.1 | - |

**Missing dependencies with no fallback:**
- None

**Missing dependencies with fallback:**
- None

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | None detected |
| Config file | None |
| Quick run command | N/A |
| Full suite command | N/A |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| BRUSH-01 | Size slider adjusts brush size | Manual UI test | - | N/A |
| BRUSH-02 | Strength slider adjusts strength | Manual UI test | - | N/A |
| BRUSH-03 | Circle brush selectable, others disabled | Manual UI test | - | N/A |
| BRUSH-06 | Falloff slider with inverted semantics | Manual UI test | - | N/A |

### Sampling Rate
- **Per task commit:** Visual inspection in editor
- **Per wave merge:** Run editor, verify all sliders and preview
- **Phase gate:** Complete UI walkthrough with all parameters

### Wave 0 Gaps
- [ ] No automated test infrastructure exists for this project
- [ ] Tests would need to be UI automation tests (not unit tests)
- [ ] Recommend manual testing checklist for validation

**Validation approach:** Since this is a UI-focused phase with no business logic suitable for unit testing, validation will be manual. Create a testing checklist:
1. Launch editor
2. Open Right Panel -> Params tab
3. Verify Size slider: range 1-200, default 30
4. Verify Strength slider: range 0-1, default 0.5
5. Verify Falloff slider: right=Hard, left=Soft, default 0.5 (medium)
6. Open Brushes tab, verify Circle is selectable, Square/Smooth/Noise are disabled
7. Load terrain, hover viewport, verify brush preview circle appears
8. Adjust Size, verify preview circle scales
9. Adjust Falloff, verify inner circle changes size

## Sources

### Primary (HIGH confidence)
- Terrain.Editor/UI/Panels/RightPanel.cs - Existing brush parameter UI implementation
- Terrain.Editor/UI/Panels/SceneViewPanel.cs - Viewport rendering and mouse tracking patterns
- Terrain.Editor/UI/Styling/ColorPalette.cs - Color definitions and extensions
- Terrain.Editor/UI/Styling/EditorStyle.cs - Scaling and disabled state utilities
- .planning/phases/02-brush-system-core/02-CONTEXT.md - User decisions and constraints

### Secondary (MEDIUM confidence)
- Terrain.Editor/EditorGame.cs - Game loop and update patterns
- Terrain.Editor/UI/MainWindow.cs - Panel wiring and event handling
- Terrain.Editor/Services/TerrainManager.cs - Service class pattern example

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All libraries already integrated in project
- Architecture: HIGH - Existing codebase demonstrates patterns clearly
- Pitfalls: MEDIUM - Screen-space conversion formula needs verification during implementation

**Research date:** 2026-03-29
**Valid until:** 30 days (stable UI framework, no external API dependencies)
