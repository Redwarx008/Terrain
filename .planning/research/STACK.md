# Technology Stack

**Project:** Terrain Slot Editor
**Researched:** 2026-03-29

## Recommended Stack

### Core Framework

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Stride Engine | 4.3.0.2507 | Game engine, rendering, graphics | Already in use; provides DirectX 12/Vulkan rendering, scene management, input handling. No reason to change. |
| .NET | 10.0 | Runtime | Stride 4.3 supports .NET 10; required by existing codebase. |
| ImGui.NET | 1.91.6.1 | Immediate mode GUI | Latest stable (Jan 2025). Already using via Stride.CommunityToolkit.ImGui. Use for all editor UI panels, toolbars, property editors. |
| Hexa.NET.ImGui | (via toolkit) | ImGui binding | Stride.CommunityToolkit.ImGui uses Hexa.NET binding internally. Do not mix with cimgui.NET directly. |

### Image Processing

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| SixLabors.ImageSharp | 3.1.12 | PNG heightmap/splatmap I/O | Latest stable (Oct 2025). Already used in TerrainPreProcessor. Pure C# implementation, no native dependencies. Supports L16, R8, Rgba32 pixel formats needed for heightmaps and splatmaps. |

### UI Architecture

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Stride.CommunityToolkit.ImGui | 1.0.0-preview.62 | Stride-ImGui integration | Latest (Nov 2025). Handles ImGui context lifecycle, input forwarding, font management. Already integrated in Terrain.Editor. |
| Custom UI Framework | (existing) | Panel/Control abstraction | Terrain.Editor/UI already has ControlBase, PanelBase, LayoutManager. Extend existing pattern rather than introducing new UI framework. |

### File Dialog

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Windows.Storage.Pickers | Built-in | Native file open/save dialogs | Windows 10+ native API. No NuGet dependency. Use via COM interop or Windows Runtime projection. Better UX than ImGui file dialogs for native Windows app. |

**Alternative considered:** ImGuiFileDialog - Rejected because native Windows dialogs provide better OS integration, recent files, and folder shortcuts.

### Undo/Redo System

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Custom Command Pattern | (to implement) | Undo/Redo stack | Implement classic Command pattern with IUndoableCommand interface. No external library needed; terrain editing commands are domain-specific. |

**Why not CommunityToolkit.Mvvm for undo/redo:** While CommunityToolkit.Mvvm 8.4.2 is used for MVVM in TerrainPreProcessor, it does not provide undo/redo functionality. The toolkit focuses on ObservableObject, RelayCommand, and source generators. A custom command stack is required.

### Brush System

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Custom Compute Shaders | SDSL | GPU brush operations | Stride's SDSL (Stride Domain Specific Language) for compute shaders. Already used in Terrain/Effects/Build/ for LOD computations. Extend for brush operations (raise, lower, smooth, flatten, paint). |
| Falloff Functions | Custom | Brush falloff curves | Implement math functions: Linear, Gaussian, Smooth (Hermite), Constant. No external library needed. |

## Architecture Decisions

### Brush Rendering Pipeline

**Recommended approach:** GPU-driven brush preview and application.

1. **Preview:** Render brush circle overlay in screen space using ImGui draw list (already done via ImGui.GetWindowDrawList())
2. **Application:** Dispatch compute shader to modify heightmap/splatmap texture
3. **Ray picking:** Cast ray from mouse through camera to terrain mesh; intersect with heightmap

**Why GPU over CPU:**
- Real-time preview requires 60+ FPS
- Large brush sizes (up to 512px radius) would be slow on CPU
- Stride already has compute shader infrastructure (TerrainComputeDispatcher)

### Heightmap/Splatmap Storage

| Format | Use Case | Rationale |
|--------|----------|-----------|
| L16 (16-bit grayscale) | Heightmap | Existing format; 65536 height levels; standard for terrain |
| R8 (8-bit single channel) | Splatmap | R8 format supports 256 material slots as planned; compact storage |
| Rgba32 (fallback) | Legacy splatmap | Support loading existing 4-channel splatmaps for compatibility |

### Data Flow

```
[Load] PNG heightmap/splatmap -> ImageSharp -> Texture2D (GPU)
[Edit] Brush stroke -> Compute Shader -> Texture2D (modified)
[Preview] Texture2D -> Terrain LOD System -> Rendered mesh
[Save] Texture2D -> ImageSharp -> PNG file / .terrain format
```

## Supporting Libraries

### Already Integrated (Do Not Change)

| Library | Version | Notes |
|---------|---------|-------|
| Stride.Video | 4.3.0.2507 | Keep for consistency |
| Stride.Physics | 4.3.0.2507 | Keep for consistency |
| Stride.Navigation | 4.3.0.2507 | Keep for consistency |
| Stride.Particles | 4.3.0.2507 | Keep for consistency |
| Stride.UI | 4.3.0.2507 | Keep for consistency |

### PreProcessor Tool (Separate Process)

| Library | Version | Notes |
|---------|---------|-------|
| Avalonia | 11.3.9 | Cross-platform desktop UI for preprocessor tool; not needed in main editor |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM for Avalonia preprocessor; can upgrade to 8.4.2 if needed |

## What NOT to Use

| Technology | Why Not |
|------------|---------|
| Unity Terrain Tools | Different engine; not applicable |
| Unreal Landscape | Different engine; not applicable |
| ImGui File Dialog implementations | Native Windows dialogs provide better UX |
| SQLite/Database for undo | In-memory command stack sufficient; terrain edits are not transactional |
| ReactiveUI | CommunityToolkit.Mvvm already in use; avoid mixing MVVM frameworks |
| Dear ImGui docking branch features | Already have custom LayoutManager; docking not required |

## Installation

No new packages required. All dependencies are already in the project.

```xml
<!-- Terrain.Editor.csproj - Current packages are correct -->
<PackageReference Include="Stride.Engine" Version="4.3.0.2507" />
<PackageReference Include="Stride.CommunityToolkit.ImGui" Version="1.0.0-preview.62" />

<!-- TerrainPreProcessor.csproj - Already has ImageSharp -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.12" />
```

## Version Verification

| Package | Source | Verified Date |
|---------|--------|---------------|
| Stride.Engine | NuGet.org | 2026-03-29 |
| ImGui.NET | NuGet.org | 2026-03-29 |
| SixLabors.ImageSharp | NuGet.org | 2026-03-29 |
| Stride.CommunityToolkit.ImGui | NuGet.org | 2026-03-29 |
| CommunityToolkit.Mvvm | NuGet.org | 2026-03-29 |

## Confidence Assessment

| Area | Confidence | Reason |
|------|------------|--------|
| Core Engine (Stride) | HIGH | Already in use; well understood |
| ImGui Integration | HIGH | Already working in Terrain.Editor |
| Image Processing | HIGH | ImageSharp already used in TerrainPreProcessor |
| File Dialog | MEDIUM | Native Windows API; requires P/Invoke testing |
| Compute Shaders for Brushes | MEDIUM | Stride compute infrastructure exists; brush-specific shaders need implementation |
| Undo/Redo | MEDIUM | Standard pattern; terrain-specific commands need design |

## Implementation Notes

### Brush Compute Shaders

Create new SDSL files in `Terrain.Editor/Effects/Brushes/`:

- `TerrainBrushRaise.sdsl` - Raise/lower height
- `TerrainBrushSmooth.sdsl` - Gaussian blur for smoothing
- `TerrainBrushFlatten.sdsl` - Set to target height
- `TerrainBrushPaint.sdsl` - Splatmap painting

### Command Pattern Interface

```csharp
public interface IUndoableCommand
{
    void Execute();
    void Undo();
    string Description { get; }
}

public class CommandHistory
{
    private readonly Stack<IUndoableCommand> undoStack = new();
    private readonly Stack<IUndoableCommand> redoStack = new();
    private readonly int maxHistorySize;

    public void Execute(IUndoableCommand command);
    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;
    public void Undo();
    public void Redo();
}
```

### Windows Native File Dialog

Use `Windows.Storage.Pickers` via Windows Runtime projection (available on Windows 10+). Alternative: P/Invoke to `comdlg32.dll` GetOpenFileName/GetSaveFileName for simpler integration.

---

*Stack research: 2026-03-29*
