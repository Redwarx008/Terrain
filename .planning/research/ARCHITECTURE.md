# Architecture Patterns

**Domain:** Terrain Slot Editor
**Researched:** 2026-03-29

## Recommended Architecture

The terrain slot editor follows a layered architecture with clear separation between UI, editing logic, and rendering:

```
+----------------------------------------------------------+
|                    UI Layer (ImGui.NET)                   |
|  MainWindow → LayoutManager → Panels (Tools, Brush, etc.) |
+----------------------------------------------------------+
                              | Events
                              v
+----------------------------------------------------------+
|                 Editor Controller Layer                   |
|  EditorModeManager, ToolController, BrushController       |
+----------------------------------------------------------+
                              | Commands
                              v
+----------------------------------------------------------+
|                 Command/Undo System                       |
|  ICommand, UndoManager, TerrainEditCommand                |
+----------------------------------------------------------+
                              | Operations
                              v
+----------------------------------------------------------+
|                 Terrain Data Layer                        |
|  TerrainDocument, HeightmapPageManager, SplatMapManager   |
+----------------------------------------------------------+
                              | Data
                              v
+----------------------------------------------------------+
|                 Rendering Layer (Reuse)                   |
|  TerrainRenderFeature, TerrainQuadTree, StreamingManager  |
+----------------------------------------------------------+
```

### Component Boundaries

| Component | Responsibility | Communicates With |
|-----------|---------------|-------------------|
| **MainWindow** | Application shell, panel layout, menu handling | All panels via events |
| **LayoutManager** | Panel positioning, splitter handling, visibility | MainWindow, Panels |
| **ToolsPanel** | Tool selection UI, mode-specific tool filtering | ToolController via events |
| **RightPanel** | Brush parameters, brush shape selection | BrushController via events |
| **SceneViewPanel** | 3D viewport, camera controls, brush preview overlay | EditorControllers, Rendering |
| **AssetsPanel** | Material slots, foliage slots, layer management | MaterialManager via events |
| **ToolController** | Active tool state, tool execution coordination | UndoManager, TerrainDocument |
| **BrushController** | Brush parameters, brush shape computation | ToolController, SceneViewPanel |
| **EditorModeManager** | Mode switching (Sculpt/Paint/Foliage), UI coordination | MainWindow, Panels |
| **TerrainDocument** | Heightmap + SplatMap data, dirty state, file path | UndoManager, FileIO |
| **HeightmapPageManager** | Large heightmap paging, resident pages, modifications | TerrainDocument, Rendering |
| **SplatMapManager** | R8 SplatMap data, material slot assignments | TerrainDocument |
| **UndoManager** | Command history, undo/redo stack management | ToolController, MainWindow |
| **FileIOManager** | PNG import/export, .terrain serialization | TerrainDocument, MainWindow |

### Data Flow

**1. Tool Selection Flow:**
```
User clicks tool in ToolsPanel
  → ToolsPanel.ToolSelected event
  → ToolController.SetActiveTool(tool)
  → ToolController publishes ToolChangedEvent
  → UI updates (SceneViewPanel shows brush cursor)
```

**2. Brush Edit Flow:**
```
User paints in SceneViewPanel
  → SceneViewPanel captures mouse input
  → ToolController.BeginStroke(position)
  → ToolController computes affected area
  → BrushController.ComputeBrushMask(center, radius, falloff)
  → ToolController creates TerrainEditCommand
  → UndoManager.Execute(command)
  → TerrainDocument.ApplyHeightEdit(brushMask, strength)
  → HeightmapPageManager.MarkDirtyPages(region)
  → Rendering layer receives dirty notification
  → GPU resources updated
```

**3. Undo/Redo Flow:**
```
User presses Ctrl+Z
  → MainWindow captures shortcut
  → UndoManager.Undo()
  → Previous command.RestoreSnapshot()
  → TerrainDocument restores previous state
  → HeightmapPageManager.RefreshFromDocument()
  → Rendering layer updates
```

**4. File Operations Flow:**
```
User opens PNG heightmap
  → MainWindow.FileOpen()
  → FileIOManager.LoadHeightmapPNG(path)
  → Creates TerrainDocument with heightmap data
  → HeightmapPageManager initializes paging
  → Rendering layer receives terrain data
  → SceneViewPanel shows terrain

User exports .terrain
  → MainWindow.FileExport()
  → FileIOManager.ExportTerrain(document, path)
  → Generates mipmap chain
  → Writes MinMaxErrorMap
  → Writes SVT tiles (reuses TerrainPreProcessor patterns)
```

## Patterns to Follow

### Pattern 1: Command Pattern for Edit Operations
**What:** All terrain modifications are encapsulated as ICommand objects that store before/after state.
**When:** Every edit operation (brush stroke, flatten, smooth) must use this pattern.
**Why:** Enables undo/redo, operation batching, and operation history visualization.

**Example:**
```csharp
public interface ICommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class HeightEditCommand : ICommand
{
    private readonly TerrainDocument document;
    private readonly Rectangle affectedRegion;
    private readonly float[,] beforeData;
    private readonly float[,] afterData;

    public HeightEditCommand(TerrainDocument doc, Rectangle region,
        float[,] before, float[,] after)
    {
        document = doc;
        affectedRegion = region;
        this.beforeData = before;
        this.afterData = after;
    }

    public void Execute() => document.ApplyHeightData(affectedRegion, afterData);
    public void Undo() => document.ApplyHeightData(affectedRegion, beforeData);
}
```

### Pattern 2: Paged Data Management
**What:** Large heightmaps are divided into fixed-size pages, only resident pages are kept in memory.
**When:** Heightmaps exceed threshold (e.g., > 1024x1024), or when editing operations would be slow on full data.
**Why:** Enables editing of arbitrarily large terrains without memory pressure.

**Example:**
```csharp
public class HeightmapPageManager
{
    private readonly int pageSize;  // e.g., 128x128
    private readonly Dictionary<PageKey, HeightmapPage> residentPages;
    private readonly int maxResidentPages;
    private readonly LinkedList<PageKey> lruList;

    public HeightmapPage GetPage(int pageX, int pageY)
    {
        var key = new PageKey(pageX, pageY);
        if (residentPages.TryGetValue(key, out var page))
        {
            TouchPage(key);
            return page;
        }
        return LoadPage(key);
    }

    public void MarkDirty(Rectangle worldRegion)
    {
        foreach (var pageKey in GetOverlappingPages(worldRegion))
        {
            residentPages[pageKey].IsDirty = true;
        }
    }
}
```

### Pattern 3: Brush Falloff Computation
**What:** Brush influence is computed via falloff function (linear, smooth, constant).
**When:** Every brush stroke operation.
**Why:** Provides intuitive brush behavior with soft edges.

**Example:**
```csharp
public static class BrushFalloff
{
    public static float Compute(float distance, float radius, float falloff, FalloffType type)
    {
        float t = Mathf.Clamp01(distance / radius);
        float effectiveT = Mathf.Max(0, (t - (1 - falloff)) / falloff);

        return type switch
        {
            FalloffType.Linear => 1 - effectiveT,
            FalloffType.Smooth => 1 - effectiveT * effectiveT * (3 - 2 * effectiveT),
            FalloffType.Constant => 1,
            _ => 1 - effectiveT
        };
    }
}
```

### Pattern 4: Event-Driven UI Updates
**What:** UI panels subscribe to controller/document events, never poll.
**When:** All UI state changes triggered by data changes.
**Why:** Decouples UI from logic, enables multiple views of same data.

**Example:**
```csharp
public class TerrainDocument
{
    public event EventHandler<TerrainChangedEventArgs>? TerrainChanged;

    public void ApplyHeightEdit(BrushMask mask, float strength)
    {
        // ... apply edit ...
        TerrainChanged?.Invoke(this, new TerrainChangedEventArgs {
            Region = mask.AffectedRegion,
            Type = ChangeType.Height
        });
    }
}

public class SceneViewPanel
{
    private void OnTerrainChanged(object? sender, TerrainChangedEventArgs e)
    {
        // Schedule GPU update for affected region
        ScheduleRegionUpdate(e.Region);
    }
}
```

### Pattern 5: Editor Mode Coordination
**What:** EditorModeManager coordinates mode switches, updates relevant panels and tools.
**When:** User switches between Sculpt, Paint, Foliage modes.
**Why:** Ensures consistent state across all panels.

**Example:**
```csharp
public enum EditorMode { Sculpt, Paint, Foliage }

public class EditorModeManager
{
    private EditorMode currentMode;

    public event EventHandler<EditorModeChangedEventArgs>? ModeChanged;

    public void SetMode(EditorMode mode)
    {
        if (currentMode == mode) return;
        currentMode = mode;
        ModeChanged?.Invoke(this, new EditorModeChangedEventArgs { Mode = mode });
    }
}

// Panels subscribe:
public class ToolsPanel : PanelBase
{
    public void SetMode(EditorMode mode)
    {
        CurrentMode = mode;
        // Filter visible tools by mode
    }
}
```

## Anti-Patterns to Avoid

### Anti-Pattern 1: Direct Heightmap Array Access
**What:** UI code directly modifies heightmap array without going through document layer.
**Why bad:** Bypasses undo system, makes changes unrecoverable, breaks dirty tracking.
**Instead:** Always use TerrainDocument.ApplyHeightEdit() which creates undo commands.

### Anti-Pattern 2: Synchronous File I/O on Main Thread
**What:** Loading/saving large files blocks the UI thread.
**Why bad:** UI freezes during file operations, poor user experience.
**Instead:** Use async Task-based file operations with progress callbacks.

```csharp
// Bad
public void OpenFile(string path)
{
    var data = File.ReadAllBytes(path); // Blocks UI
    ProcessData(data);
}

// Good
public async Task OpenFileAsync(string path, IProgress<int>? progress = null)
{
    await Task.Run(() =>
    {
        // Background thread
        var data = File.ReadAllBytes(path);
        progress?.Report(50);
        ProcessData(data);
    });
    // UI update back on main thread
}
```

### Anti-Pattern 3: Full Heightmap Copy per Undo Step
**What:** Each undo command stores a copy of the entire heightmap.
**Why bad:** Memory explodes with edit history (100 undos = 100 copies of huge heightmap).
**Instead:** Store only the affected region (copy-on-write for dirty tiles).

### Anti-Pattern 4: Per-Frame GPU Updates
**What:** Every frame, upload entire heightmap to GPU even if unchanged.
**Why bad:** Wastes GPU bandwidth, causes stuttering on large terrains.
**Instead:** Only upload dirty pages, use dirty region tracking.

### Anti-Pattern 5: Monolithic Brush Logic
**What:** One giant Brush class handles all brush types, falloff, preview, etc.
**Why bad:** Hard to add new brush types, hard to test individual behaviors.
**Instead:** Use strategy pattern - IBrushShape interface with CircleBrush, SquareBrush, NoiseBrush implementations.

```csharp
public interface IBrushShape
{
    float[,] ComputeMask(int size, float falloff, FalloffType falloffType);
    string Name { get; }
}

public class CircleBrush : IBrushShape { ... }
public class SquareBrush : IBrushShape { ... }
public class NoiseBrush : IBrushShape { ... }
```

## Scalability Considerations

| Concern | At 512x512 | At 4096x4096 | At 16384x16384 |
|---------|------------|--------------|----------------|
| Memory (Full) | 1 MB | 64 MB | 1 GB |
| Memory (Paged, 128 resident) | 1 MB | 8 MB | 8 MB |
| Undo per stroke | 256 KB | 16 MB | Full = 256 MB, Paged = 128 KB |
| GPU Updates | Full upload OK | Page updates only | Page updates only |
| File I/O | Instant | Progress needed | Async + progress essential |

**Recommendations by Size:**

- **Small (< 1024):** Load full heightmap, simple undo (full copy acceptable)
- **Medium (1024-4096):** Page-based loading, region-based undo
- **Large (> 4096):** Full paging system, async I/O, background streaming

## Integration with Existing Code

**Reuse from Terrain/Rendering:**
- `TerrainRenderFeature` - Already handles GPU rendering
- `TerrainQuadTree` - Already handles LOD selection
- `TerrainStreamingManager` - Pattern for async page loading (adapt for editing)
- `GpuHeightArray` - Pattern for GPU texture array management

**Reuse from TerrainPreProcessor:**
- `TerrainProcessor.WriteTerrainFile()` - Export to .terrain format
- `CoordinateConsistentMipmap` - Mipmap generation for export
- `VirtualTextureLayout` - Tile/page coordinate calculations

**Reuse from Terrain.Editor/UI:**
- `PanelBase` - Base class for all panels
- `LayoutManager` - Panel positioning system
- `EditorStyle`, `ColorPalette`, `FontManager` - Styling
- `GridTileRenderer` - Grid tile rendering for material slots

## Component Build Order

Based on dependencies, recommended build sequence:

1. **Foundation Layer** (no dependencies)
   - ICommand, UndoManager (command system)
   - IBrushShape, BrushFalloff (brush abstraction)
   - TerrainDocument (data container)

2. **Data Layer** (depends on Foundation)
   - HeightmapPageManager (paging system)
   - SplatMapManager (material data)
   - FileIOManager (serialization)

3. **Controller Layer** (depends on Data Layer)
   - EditorModeManager
   - BrushController
   - ToolController

4. **UI Integration** (depends on Controllers)
   - Update existing ToolsPanel with ToolController
   - Update existing RightPanel with BrushController
   - Update SceneViewPanel with brush preview

5. **Rendering Integration** (depends on Data Layer)
   - Connect HeightmapPageManager to existing TerrainStreamingManager
   - Add dirty region update path

## Sources

- Existing codebase analysis: Terrain.Editor/UI/, Terrain/Streaming/, TerrainPreProcessor/Services/
- Command pattern: Standard software architecture pattern for undo/redo systems
- Terrain paging: Derived from existing TerrainStreamingManager pattern
- Brush falloff: Standard terrain editor technique used in Unity, Unreal Landscape

---

*Architecture research: 2026-03-29*
