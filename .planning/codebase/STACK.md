# Technology Stack

**Analysis Date:** 2026-03-27

## Languages

**Primary:**
- C# 12+ (.NET 10.0) - All game engine and editor code
- HLSL/SDSL (Stride Domain Specific Language) - GPU compute and pixel shaders

**Secondary:**
- XAML - Avalonia UI markup for preprocessor tool

## Runtime

**Environment:**
- .NET 10.0 (preview) - Main game projects (Terrain, Terrain.Editor, Terrain.Windows)
- .NET 8.0 - TerrainPreProcessor (Avalonia desktop tool)
- Windows x64 only (`win-x64` runtime identifier)

**Package Manager:**
- NuGet (standard .NET package management)
- Lockfile: Not committed (standard NuGet restore)

## Frameworks

**Core Game Engine:**
- Stride Engine 4.3.0.2507 - Primary game engine framework
  - `Stride.Engine` - Core engine services
  - `Stride.Graphics` - DirectX/Vulkan rendering abstraction
  - `Stride.Rendering` - Render pipeline and materials
  - `Stride.Input` - Keyboard, mouse, gamepad input
  - `Stride.Core.Mathematics` - Vector/matrix math library
  - `Stride.Core` - Core engine types and serialization

**Stride Subsystems:**
- `Stride.Video` - Video playback
- `Stride.Physics` - Physics simulation (Bullet)
- `Stride.Navigation` - Pathfinding
- `Stride.Particles` - Particle effects
- `Stride.UI` - In-game UI system
- `Stride.Core.Assets.CompilerApp` - Asset pipeline compilation

**Editor UI:**
- ImGui.NET 1.90.0.1 - Immediate mode GUI for editor
- Stride.CommunityToolkit.ImGui 1.0.0-preview.60 - Stride ImGui integration

**PreProcessor Tool:**
- Avalonia UI 11.3.9 - Cross-platform desktop UI framework
  - `Avalonia` - Core framework
  - `Avalonia.Desktop` - Desktop platform support
  - `Avalonia.Themes.Fluent` - Fluent design theme
  - `Avalonia.Fonts.Inter` - Inter font family
- CommunityToolkit.Mvvm 8.4.0 - MVVM toolkit for view models
- SixLabors.ImageSharp 3.1.12 - Image processing for heightmap/splatmap generation

## Key Dependencies

**Critical:**
- Stride.Engine 4.3.0.2507 - Entire project built on this
- Stride.Graphics - DirectX 12/Vulkan rendering backend
- SixLabors.ImageSharp - Terrain data preprocessing (L16 heightmaps, mipmapping)

**Infrastructure:**
- `System.Runtime.InteropServices` - For native memory layout (Terrain file headers)
- `System.IO` - Binary file I/O for .terrain format
- `System.Numerics` - Vector math in preprocessor

## Configuration

**Environment:**
- No environment variables required at runtime
- Terrain data path configured via `TerrainComponent.TerrainDataPath` property
- Graphics settings configured programmatically in `TerrainApp.cs` and `EditorGame.cs`

**Build:**
- `Terrain.sln` - Visual Studio solution
- `*.csproj` - SDK-style project files
- `app.manifest` - Windows application manifests (Terrain.Windows, Terrain.Editor)
- Output paths:
  - Terrain.Windows: `Bin/Windows/$(Configuration)/`
  - Terrain.Editor: `Bin/Editor/$(Configuration)/`

**Shader Compilation:**
- SDSL files (`.sdsl`) compiled to C# by Stride asset compiler
- SDFX files (`.sdfx`) for effect compositions
- Auto-generated `.sdsl.cs` files checked in (DesignTime/AutoGen)

## Platform Requirements

**Development:**
- Windows 10/11 x64
- Visual Studio 2022 (v18.3+) or compatible
- .NET 10.0 SDK (preview)
- .NET 8.0 SDK (for preprocessor)

**Production:**
- Windows x64 only
- DirectX 12 or Vulkan compatible GPU
- No mobile/web platform support configured

## Shader Technologies

**Compute Shaders:**
- `TerrainBuildLodMap.sdsl` - LOD map generation on GPU
- `TerrainBuildLodLookup.sdsl` - LOD lookup table construction
- `TerrainBuildNeighborMask.sdsl` - Neighbor mask computation

**Material Shaders:**
- `MaterialTerrainDiffuse.sdsl` - Diffuse terrain shading
- `MaterialTerrainDisplacement.sdsl` - Displacement mapping
- `TerrainHeightStream.sdsl` - Height data streaming
- `TerrainHeightParameters.sdsl` - Height sampling parameters
- `TerrainMaterialStreamInitializer.sdsl` - Material stream setup

**Effect Composition:**
- `TerrainForwardShadingEffect.sdfx` - Forward shading effect mixin

---

*Stack analysis: 2026-03-27*
