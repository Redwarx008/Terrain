<!-- GSD:project-start source:PROJECT.md -->
## Project

**Terrain Slot Editor**

独立地形编辑器应用（非 Stride Studio 插件），支持高度图编辑和材质槽绘制。通过笔刷系统实时编辑地形，导出为 .terrain 格式或 PNG 序列，供 Stride 游戏引擎运行时使用。

**Core Value:** 实时 3D 预览的笔刷式地形编辑 — 所见即所得的高度和材质编辑体验。

### Constraints

- **Tech Stack**: Stride 4.3 + ImGui.NET + .NET 10
- **Memory**: 大高度图需分页处理，避免一次性加载
- **GPU**: 实时预览需复用现有 LOD 系统
- **File Format**: 兼容现有 .terrain 格式
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# 12+ (.NET 10.0) - All game engine and editor code
- HLSL/SDSL (Stride Domain Specific Language) - GPU compute and pixel shaders
- XAML - Avalonia UI markup for preprocessor tool
## Runtime
- .NET 10.0 (preview) - Main game projects (Terrain, Terrain.Editor, Terrain.Windows)
- .NET 8.0 - TerrainPreProcessor (Avalonia desktop tool)
- Windows x64 only (`win-x64` runtime identifier)
- NuGet (standard .NET package management)
- Lockfile: Not committed (standard NuGet restore)
## Frameworks
- Stride Engine 4.3.0.2507 - Primary game engine framework
- `Stride.Video` - Video playback
- `Stride.Physics` - Physics simulation (Bullet)
- `Stride.Navigation` - Pathfinding
- `Stride.Particles` - Particle effects
- `Stride.UI` - In-game UI system
- `Stride.Core.Assets.CompilerApp` - Asset pipeline compilation
- ImGui.NET 1.90.0.1 - Immediate mode GUI for editor
- Stride.CommunityToolkit.ImGui 1.0.0-preview.60 - Stride ImGui integration
- Avalonia UI 11.3.9 - Cross-platform desktop UI framework
- CommunityToolkit.Mvvm 8.4.0 - MVVM toolkit for view models
- SixLabors.ImageSharp 3.1.12 - Image processing for heightmap/splatmap generation
## Key Dependencies
- Stride.Engine 4.3.0.2507 - Entire project built on this
- Stride.Graphics - DirectX 12/Vulkan rendering backend
- SixLabors.ImageSharp - Terrain data preprocessing (L16 heightmaps, mipmapping)
- `System.Runtime.InteropServices` - For native memory layout (Terrain file headers)
- `System.IO` - Binary file I/O for .terrain format
- `System.Numerics` - Vector math in preprocessor
## Configuration
- No environment variables required at runtime
- Terrain data path configured via `TerrainComponent.TerrainDataPath` property
- Graphics settings configured programmatically in `TerrainApp.cs` and `EditorGame.cs`
- `Terrain.sln` - Visual Studio solution
- `*.csproj` - SDK-style project files
- `app.manifest` - Windows application manifests (Terrain.Windows, Terrain.Editor)
- Output paths:
- SDSL files (`.sdsl`) compiled to C# by Stride asset compiler
- SDFX files (`.sdfx`) for effect compositions
- Auto-generated `.sdsl.cs` files checked in (DesignTime/AutoGen)
## Platform Requirements
- Windows 10/11 x64
- Visual Studio 2022 (v18.3+) or compatible
- .NET 10.0 SDK (preview)
- .NET 8.0 SDK (for preprocessor)
- Windows x64 only
- DirectX 12 or Vulkan compatible GPU
- No mobile/web platform support configured
## Shader Technologies
- `TerrainBuildLodMap.sdsl` - LOD map generation on GPU
- `TerrainBuildLodLookup.sdsl` - LOD lookup table construction
- `TerrainBuildNeighborMask.sdsl` - Neighbor mask computation
- `MaterialTerrainDiffuse.sdsl` - Diffuse terrain shading
- `MaterialTerrainDisplacement.sdsl` - Displacement mapping
- `TerrainHeightStream.sdsl` - Height data streaming
- `TerrainHeightParameters.sdsl` - Height sampling parameters
- `TerrainMaterialStreamInitializer.sdsl` - Material stream setup
- `TerrainForwardShadingEffect.sdfx` - Forward shading effect mixin
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- PascalCase for all C# files (e.g., `TerrainComponent.cs`, `TerrainRenderFeature.cs`)
- Descriptive names matching the primary type defined within
- Shader effect files use `.sdsl` extension with generated `.sdsl.cs` counterparts
- PascalCase for all type names
- Prefixed with domain when appropriate: `TerrainComponent`, `TerrainRenderObject`, `TerrainChunkNode`
- Internal implementation details marked `internal` or `private`
- Sealed classes preferred unless inheritance is explicitly designed: `sealed class TerrainProcessor`
- Not explicitly observed in codebase (relies on Stride framework interfaces)
- PascalCase for all methods: `GenerateComponentData`, `UpdateRenderObject`
- Private helper methods use PascalCase: `TryLoadTerrainData`, `ValidateTerrainDataPath`
- Async methods not prevalent (uses threaded approach with `TerrainStreamingManager`)
- PascalCase for all properties
- Auto-properties preferred: `public VisibilityGroup VisibilityGroup { get; set; } = null!;`
- Nullable reference types annotated: `public string? TerrainDataPath { get; set; }`
- Private fields: camelCase with underscore prefix OR PascalCase for static readonly
- Constants: PascalCase or ALL_CAPS for const values
- camelCase for local variables and parameters
- Descriptive names: `chunkNodeCapacity`, `lodLookupEntryCount`
## Code Style
- All files start with `#nullable enable`
- Nullable annotations used throughout: `string?`, `Texture?`
- Null-forgiving operator used where appropriate: `null!`
- 4-space indentation
- Opening braces on same line (K&R style)
- Single blank line between members
- No trailing whitespace
- Used for simple one-liners:
- Modern C# switch expressions used:
- Used for immutable data structures:
## Import Organization
- Used for disambiguation: `using Buffer = Stride.Graphics.Buffer;`
- Global namespace for generated code: `global::System.ReadOnlySpan<TerrainChunkNode>`
## Error Handling
- Try-Parse pattern for recoverable failures: `TryLoadTerrainData`, `TryGetResidentSlice`
- Early returns with logging for validation failures
- Exception handling at boundaries (file I/O, external resources)
- Guard clauses at method entry
- `Debug.Assert` for internal invariants (stripped in release builds)
- `ArgumentOutOfRangeException`, `InvalidDataException` for invalid inputs
## Logging
- Static readonly logger per class: `private static readonly Logger Log = GlobalLogger.GetLogger("Quantum");`
- Log level appropriate to message severity
- Warning for recoverable errors, Info for diagnostics
## Comments
- Complex algorithms explained in detail
- Non-obvious design decisions
- Workarounds for framework limitations
- Editor UI code contains Chinese comments for UI elements (e.g., `// 主窗口 - 编辑器主界面`)
- This is intentional for the editor's Chinese localization context
- Minimal use in runtime code
- Used in Editor UI framework for IntelliSense support
## Function Design
- Methods kept focused (generally under 50 lines)
- Large initialization logic broken into helper methods
- `ref` and `out` used for performance-critical structs
- `in` parameters not observed
- Nullable parameters clearly annotated
- Tuple returns for multiple values: `(int RenderCount, int NodeCount)`
- `out` parameters for Try-pattern methods
## Module Design
- Flat namespace for core terrain: `namespace Terrain;`
- Sub-namespaces for Editor UI: `namespace Terrain.Editor.UI.Controls;`
- `internal` for implementation details
- `public` for API surface
- `sealed` for classes not designed for inheritance
- `IDisposable` implemented for resource-heavy types
- Proper cleanup of GPU resources, file handles, threads
## Unsafe Code
- `unsafe` blocks for native memory operations
- `NativeMemory.Alloc/Free` for page buffer allocation
- `MemoryMarshal` for span conversions
## Struct Layout
- Used for GPU-bound structures:
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern Overview
- Built on Stride Game Engine 4.3.0.2507 (.NET 10)
- GPU-driven terrain rendering with compute shader-based LOD management
- Virtual Texture (VT) streaming system for heightmap data
- Quadtree-based chunk selection with screen-space error metrics
- Multi-project solution: Core Library, Windows Runtime, Editor, PreProcessor
## Layers
- Purpose: Terrain component definition, data structures, and runtime file I/O
- Location: `Terrain/Core/`
- Contains: `TerrainComponent.cs`, `TerrainProcessor.cs`
- Depends on: Stride.Engine, Stride.Rendering
- Used by: Terrain.Windows, Terrain.Editor
- Purpose: Custom render feature, compute dispatchers, and GPU resource management
- Location: `Terrain/Rendering/`
- Contains: `TerrainRenderFeature.cs`, `TerrainRenderObject.cs`, `TerrainQuadTree.cs`, `TerrainComputeDispatcher.cs`
- Depends on: Stride.Rendering, Stride.Graphics
- Used by: Core Terrain Layer
- Purpose: Asynchronous heightmap page streaming and GPU texture array management
- Location: `Terrain/Streaming/`
- Contains: `TerrainStreaming.cs`, `PageBufferAllocator.cs`
- Depends on: System.IO, System.Threading, Stride.Graphics
- Used by: TerrainQuadTree
- Purpose: HLSL compute and vertex/pixel shaders for terrain rendering
- Location: `Terrain/Effects/`
- Contains: Compute shaders (Build/), Material shaders (Material/), Stream shaders (Stream/)
- Depends on: Stride shader system
- Used by: TerrainRenderFeature
- Purpose: ImGui-based editor with dockable panels for terrain editing
- Location: `Terrain.Editor/`
- Contains: `EditorGame.cs`, `UI/` (Panels, Controls, Layout, Styling)
- Depends on: Terrain (core), ImGui.NET, Stride.CommunityToolkit.ImGui
- Used by: Editor executable
- Purpose: Avalonia desktop tool for converting heightmaps to .terrain files
- Location: `TerrainPreProcessor/`
- Contains: Models, Services, ViewModels, Views
- Depends on: Avalonia, SixLabors.ImageSharp
- Used by: Standalone tool
## Data Flow
```
```
## Key Abstractions
- Purpose: Entity component holding terrain configuration and runtime state
- Location: `Terrain/Core/TerrainComponent.cs`
- Pattern: Stride EntityComponent with `[DataContract]` for serialization
- Key Properties: `TerrainDataPath`, `HeightScale`, `MaxScreenSpaceErrorPixels`, `MaxVisibleChunkInstances`
- Purpose: Root render feature integrating with Stride's rendering pipeline
- Location: `Terrain/Rendering/TerrainRenderFeature.cs`
- Pattern: Extends `RootEffectRenderFeature`, manages sub-render-features
- Key Behaviors: Clones MeshRenderFeature lighting config, proxies shadow maps
- Purpose: Hierarchical chunk selection with LOD determination
- Location: `Terrain/Rendering/TerrainQuadTree.cs`
- Pattern: Recursive spatial subdivision with screen-space error metrics
- Key Method: `Select()` returns (renderCount, nodeCount) for GPU buffers
- Purpose: Async I/O and GPU upload coordination
- Location: `Terrain/Streaming/TerrainStreaming.cs`
- Pattern: Producer-consumer with `BlockingCollection`, background thread
- Key Types: `TerrainChunkKey` (LOD+XY), `TerrainPageKey` (Mip+XY)
- Purpose: GPU texture array slice management with LRU eviction
- Location: `Terrain/Streaming/TerrainStreaming.cs`
- Pattern: Dictionary mapping + LinkedList LRU + array pool
- Key Methods: `TryGetResidentSlice()`, `UploadPage()`, `TryEvictLeastRecentlyUsed()`
## Entry Points
- Location: `Terrain.Windows/` (thin executable)
- Triggers: Standard Stride game bootstrap
- Responsibilities: Hosts Terrain library, provides game runtime
- Location: `Terrain.Editor/Program.cs` → `EditorGame`
- Triggers: Editor executable launch
- Responsibilities: Initializes ImGui UI, dockable panels, scene editing
- Location: `TerrainPreProcessor/Program.cs`
- Triggers: Avalonia desktop app startup
- Responsibilities: Heightmap → .terrain conversion with progress UI
## Error Handling
- Null checks on GPU resources before use (e.g., `IsGpuDataValid()`)
- Try-pattern methods: `TryLoadTerrainData()`, `TryGetResidentPageForChunk()`
- Exception catching at I/O boundaries with user-facing messages
- CancellationToken support for streaming thread shutdown
## Cross-Cutting Concerns
<!-- GSD:architecture-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
