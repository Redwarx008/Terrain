# Codebase Structure

**Analysis Date:** 2026-03-27

## Directory Layout

```
[project-root]/
├── Terrain/                    # Core terrain library (net10.0-windows)
│   ├── Core/                   # Component and processor
│   ├── Rendering/              # Render feature, quadtree, compute dispatch
│   ├── Streaming/              # Async I/O and GPU texture management
│   ├── Effects/                # Stride shaders (sdsl/sdfx)
│   │   ├── Build/              # Compute shaders (LOD lookup, LOD map, neighbor mask)
│   │   ├── Material/           # Material features (displacement, diffuse)
│   │   ├── Stream/             # Height sampling and stream initialization
│   │   └── TerrainForwardShadingEffect.sdfx
│   └── Resources/              # Embedded textures (heightmap.png, grid)
├── Terrain.Windows/            # Windows runtime executable (WinExe)
├── Terrain.Editor/             # ImGui-based editor (WinExe)
│   └── UI/                     # Editor UI framework
│       ├── Controls/           # ImGui control wrappers
│       ├── Layout/             # Dockable layout manager
│       ├── Panels/             # Editor panels (Hierarchy, Inspector, etc.)
│       └── Styling/            # Colors, fonts, styling
├── TerrainPreProcessor/        # Avalonia heightmap converter (WinExe)
│   ├── Models/                 # Data models (config, headers, results)
│   ├── Services/               # Processing services
│   ├── ViewModels/             # MVVM view models
│   ├── Views/                  # Avalonia XAML views
│   └── Resources/              # Localization strings
├── Shared/                     # Shared code between projects
│   └── VirtualTextureLayout.cs # VT layout calculations
├── Bin/                        # Build output
│   ├── Windows/                # Terrain.Windows output
│   └── Editor/                 # Terrain.Editor output
└── .planning/codebase/         # This documentation
```

## Directory Purposes

**Terrain/Core:**
- Purpose: ECS component definitions and entity processor
- Contains: `TerrainComponent.cs`, `TerrainProcessor.cs`
- Key files:
  - `TerrainComponent.cs`: Main component with `[DataContract]` properties
  - `TerrainProcessor.cs`: Entity processor implementing `IEntityComponentRenderProcessor`

**Terrain/Rendering:**
- Purpose: Custom render pipeline integration
- Contains: Render feature, render object, quadtree, compute dispatcher, wireframe controllers
- Key files:
  - `TerrainRenderFeature.cs`: Root render feature with sub-render-feature management
  - `TerrainRenderObject.cs`: GPU resource holder (buffers, textures)
  - `TerrainQuadTree.cs`: LOD selection and frustum culling
  - `TerrainComputeDispatcher.cs`: Compute shader dispatch coordination

**Terrain/Streaming:**
- Purpose: Heightmap virtual texture streaming
- Contains: Streaming manager, page allocator, file reader
- Key files:
  - `TerrainStreaming.cs`: Main streaming types (`TerrainStreamingManager`, `GpuHeightArray`, `TerrainFileReader`)
  - `PageBufferAllocator.cs`: Buffer pooling for async I/O

**Terrain/Effects:**
- Purpose: Stride shader definitions
- Key files:
  - `Build/TerrainBuildLodLookup.sdsl`: Compute shader for LOD lookup table
  - `Build/TerrainBuildLodMap.sdsl`: Compute shader for LOD map generation
  - `Build/TerrainBuildNeighborMask.sdsl`: Compute shader for crack-fixing masks
  - `Material/MaterialTerrainDisplacement.sdsl`: Vertex displacement shader
  - `Material/MaterialTerrainDiffuse.sdsl`: Diffuse shading shader
  - `Stream/TerrainHeightStream.sdsl`: Height sampling utilities
  - `TerrainForwardShadingEffect.sdfx`: Main effect composition

**Terrain.Editor/UI:**
- Purpose: Editor UI framework built on ImGui.NET
- Key files:
  - `MainWindow.cs`: Root editor window with dockable layout
  - `EditorUIRenderer.cs`: ImGui rendering integration with Stride
  - `Layout/LayoutManager.cs`: Dockable panel layout system
  - `Panels/`: Individual editor panels (Hierarchy, Inspector, SceneView, etc.)

**TerrainPreProcessor:**
- Purpose: Standalone tool for heightmap preprocessing
- Key files:
  - `Services/TerrainProcessor.cs`: Main processing logic
  - `Models/ProcessingConfig.cs`: Configuration model
  - `Views/MainWindow.axaml.cs`: Avalonia main window

**Shared:**
- Purpose: Code shared across multiple projects
- Key files:
  - `VirtualTextureLayout.cs`: Mipmap layout calculations used by both runtime and preprocessor

## Key File Locations

**Entry Points:**
- `Terrain.Windows/`: Implicit (project reference only, no custom entry point)
- `Terrain.Editor/Program.cs`: Editor entry point
- `TerrainPreProcessor/Program.cs`: Preprocessor entry point

**Configuration:**
- `Terrain.sln`: Solution file
- `Terrain/Terrain.csproj`: Core library project
- `Terrain.Windows/Terrain.Windows.csproj`: Windows runtime project
- `Terrain.Editor/Terrain.Editor.csproj`: Editor project
- `TerrainPreProcessor/TerrainPreProcessor.csproj`: Preprocessor project

**Core Logic:**
- `Terrain/Core/TerrainComponent.cs`: Component definition
- `Terrain/Core/TerrainProcessor.cs`: Runtime initialization and updates
- `Terrain/Rendering/TerrainQuadTree.cs`: LOD selection algorithm
- `Terrain/Streaming/TerrainStreaming.cs`: Streaming and I/O

**Testing:**
- Not detected in codebase

## Naming Conventions

**Files:**
- PascalCase for all C# files: `TerrainComponent.cs`, `TerrainRenderFeature.cs`
- Shader files use .sdsl (Stride DSL) and .sdfx (Stride Effect) extensions
- Generated shader C# files: `.sdsl.cs` suffix

**Directories:**
- PascalCase: `Terrain/`, `Core/`, `Rendering/`
- Descriptive names matching purpose

**Types:**
- Classes: PascalCase, descriptive: `TerrainComponent`, `TerrainRenderObject`
- Structs: PascalCase for interop: `TerrainChunkNode`, `TerrainFileHeader`
- Interfaces: PascalCase with I prefix: `IEntityComponentRenderProcessor` (Stride)
- Enums: PascalCase: `TerrainLodLookupNodeState`

**Members:**
- Public properties: PascalCase
- Private fields: camelCase with underscore or no prefix
- Constants: PascalCase or ALL_CAPS for magic values

## Where to Add New Code

**New Feature (e.g., new terrain material):**
- Primary code: `Terrain/Effects/Material/`
- C# feature class: `Terrain/Rendering/Materials/`
- Integration: `Terrain/Core/TerrainProcessor.cs` (material setup)

**New Component/Module:**
- Implementation: `Terrain/[Category]/`
- Registration: `Terrain/Core/TerrainProcessor.cs` or `Terrain/Rendering/TerrainRenderFeature.cs`

**Utilities:**
- Shared helpers: `Shared/` (if used by multiple projects)
- Project-specific: `Terrain/[RelevantCategory]/`

**Editor Features:**
- New panel: `Terrain.Editor/UI/Panels/`
- New control: `Terrain.Editor/UI/Controls/`
- Integration: `Terrain.Editor/UI/MainWindow.cs`

**PreProcessor Features:**
- New service: `TerrainPreProcessor/Services/`
- New model: `TerrainPreProcessor/Models/`

## Special Directories

**Bin/:**
- Purpose: Build output directory (configured in .csproj files)
- Generated: Yes (MSBuild output)
- Committed: No (in .gitignore)

**obj/:**
- Purpose: Intermediate build files
- Generated: Yes
- Committed: No

**.vs/:**
- Purpose: Visual Studio workspace state
- Generated: Yes
- Committed: No

**Resources/ (in Terrain):**
- Purpose: Embedded runtime resources
- Contains: `heightmap.png`, `Grid_Gray_128x128.png`
- Build action: `CopyToOutputDirectory` or EmbeddedResource

---

*Structure analysis: 2026-03-27*
