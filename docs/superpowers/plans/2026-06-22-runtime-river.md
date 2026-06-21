# Runtime River Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Terrain.Windows` display runtime rivers from `game/map/rivers.png` through scene-authored `RiverSystem` and compositor-authored `RiverRenderFeature`.

**Architecture:** River core moves from `Terrain.Editor` into `Terrain`. `MainScene.sdscene` owns the `RiverSystem` entity, `GraphicsCompositor.sdgfxcomp` owns `RiverRenderFeature`, `RiverComponent` stores mesh snapshots, and `RiverProcessor` handles runtime mesh generation plus render object synchronization. `TerrainRuntimeResourceBundle` remains a resource/config carrier only.

**Tech Stack:** C#/.NET 10, Stride Engine scene/component/processor/render-feature pipeline, Stride SDSL shader asset workflow, ImageSharp, existing `Terrain.Editor.Tests` harness.

---

## File Structure

- Move to `Terrain/Rivers/`
  - `RiverPixelType.cs`: `RiverCell`, `RiverPixelType`, `SegmentEndKind`.
  - `RiverSegment.cs`: extracted river segment and width samples.
  - `RiverMapService.cs`: `rivers.png` loading, validation, tracing, configured width mapping.
  - `RiverMeshService.cs`: centerline generation, height sampling, mesh generation.
  - `IRiverTerrainHeightSource.cs`: public abstraction used by `RiverMeshService`.
- Move to `Terrain/Rendering/River/`
  - `RiverComponent.cs`: asset hook, mesh snapshots, settings, runtime load state.
  - `RiverProcessor.cs`: runtime mesh generation and render object synchronization.
  - `RiverRenderObject.cs`, `RiverRenderFeature.cs`, `RiverRenderResources.cs`, `RiverResourceLoader.cs`, `RiverRenderSettings.cs`, `RiverMeshData.cs`, `RiverVertex.cs`.
- Move shaders to `Terrain/Effects/River/`
  - `RiverBottom.sdsl`, `RiverSurface.sdsl`, `RiverSceneSeed.sdsl`, `RiverCommon.sdsl`, `RiverWaterCommon.sdsl`, `RiverVertexStreams.sdsl`, `RiverStrideLighting.sdsl`.
- Keep in `Terrain.Editor/`
  - `RiverRenderingService.cs`: editor façade only.
  - `RiverMeshGenerator.cs`, `IRiverMeshGenerator.cs`, `IRiverMapSource.cs`, `RiverViewModel.cs`: editor UI generation path.
- Modify runtime resources
  - `Terrain/Resources/TerrainRuntimeResourceBundle.cs`: add `RiverMinWidth` and `RiverMaxWidth`.
  - `Terrain/Resources/GameRuntimeResourceBootstrap.cs`: populate width settings.
  - `Terrain/Core/TerrainComponent.cs`: expose runtime height cache for River sampling.
  - `Terrain/Core/TerrainProcessor.cs`: retain loaded height data on `TerrainComponent`.
- Modify assets
  - `Terrain/Assets/MainScene.sdscene`: add scene-authored `RiverSystem` entity with `RiverComponent`.
  - `Terrain/Assets/GraphicsCompositor.sdgfxcomp`: add `RiverRenderFeature` and `Transparent` selector.
  - `Terrain/Terrain.sdpkg`: make River reflection cubemap a runtime root asset.
  - `Terrain/Terrain.csproj`: compile River generated shader key files.
- Tests
  - Add `Terrain.Editor.Tests/RuntimeRiverAssetTests.cs`.
  - Update namespace imports in existing River tests after moving types.
  - Update `Terrain.Editor.Tests/Program.cs` to run the new test file.

---

### Task 1: Lock Runtime River Asset and Project Boundaries

**Files:**
- Create: `Terrain.Editor.Tests/RuntimeRiverAssetTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Write failing text tests**

Create `Terrain.Editor.Tests/RuntimeRiverAssetTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Terrain.Editor.Tests;

internal static class RuntimeRiverAssetTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    public static void RunAll()
    {
        TestHarness.Run("runtime project does not reference editor project", RuntimeProjectDoesNotReferenceEditorProject);
        TestHarness.Run("runtime scene contains river system component", RuntimeSceneContainsRiverSystemComponent);
        TestHarness.Run("runtime compositor registers river render feature", RuntimeCompositorRegistersRiverRenderFeature);
        TestHarness.Run("runtime river shaders live in terrain project", RuntimeRiverShadersLiveInTerrainProject);
        TestHarness.Run("terrain package roots river reflection cubemap", TerrainPackageRootsRiverReflectionCubemap);
    }

    private static void RuntimeProjectDoesNotReferenceEditorProject()
    {
        string project = Read("Terrain.Windows", "Terrain.Windows.csproj");

        TestHarness.Assert(
            !project.Contains("Terrain.Editor", StringComparison.Ordinal),
            "Terrain.Windows must not reference Terrain.Editor.");
    }

    private static void RuntimeSceneContainsRiverSystemComponent()
    {
        string scene = Read("Terrain", "Assets", "MainScene.sdscene");

        TestHarness.Assert(
            scene.Contains("Name: RiverSystem", StringComparison.Ordinal),
            "MainScene.sdscene should define a RiverSystem entity.");
        TestHarness.Assert(
            scene.Contains("!Terrain.Rendering.River.RiverComponent,Terrain", StringComparison.Ordinal) ||
            scene.Contains("!RiverComponent", StringComparison.Ordinal),
            "RiverSystem should contain Terrain.Rendering.River.RiverComponent.");
    }

    private static void RuntimeCompositorRegistersRiverRenderFeature()
    {
        string compositor = Read("Terrain", "Assets", "GraphicsCompositor.sdgfxcomp");

        TestHarness.Assert(
            compositor.Contains("!Terrain.Rendering.River.RiverRenderFeature,Terrain", StringComparison.Ordinal),
            "GraphicsCompositor.sdgfxcomp should register RiverRenderFeature from Terrain.");
        TestHarness.Assert(
            compositor.Contains("EffectName: RiverSurface", StringComparison.Ordinal),
            "RiverRenderFeature selector should target RiverSurface.");
        TestHarness.Assert(
            compositor.Contains("Name: Transparent", StringComparison.Ordinal),
            "GraphicsCompositor should keep a Transparent render stage for river surface rendering.");
        TestHarness.Assert(
            Regex.IsMatch(compositor, "RenderGroup:\\s*Group1"),
            "RiverRenderFeature selector should use Group1.");
    }

    private static void RuntimeRiverShadersLiveInTerrainProject()
    {
        string[] shaderNames =
        [
            "RiverBottom.sdsl",
            "RiverSurface.sdsl",
            "RiverSceneSeed.sdsl",
            "RiverCommon.sdsl",
            "RiverWaterCommon.sdsl",
            "RiverVertexStreams.sdsl",
            "RiverStrideLighting.sdsl",
        ];

        foreach (string shaderName in shaderNames)
        {
            TestHarness.Assert(
                File.Exists(Path.Combine(RepositoryRoot, "Terrain", "Effects", "River", shaderName)),
                $"{shaderName} should live under Terrain/Effects/River.");
            TestHarness.Assert(
                !File.Exists(Path.Combine(RepositoryRoot, "Terrain.Editor", "Effects", shaderName)),
                $"{shaderName} should not remain under Terrain.Editor/Effects.");
        }
    }

    private static void TerrainPackageRootsRiverReflectionCubemap()
    {
        string package = Read("Terrain", "Terrain.sdpkg");

        TestHarness.Assert(
            package.Contains("River/Environment/reflection-specular", StringComparison.Ordinal),
            "Terrain.sdpkg should root River/Environment/reflection-specular for runtime content loading.");
    }

    private static string Read(params string[] path)
    {
        return File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));
    }
}
```

Modify `Terrain.Editor.Tests/Program.cs` after `RiverRenderFeatureRuntimeTests.RunAll();`:

```csharp
RuntimeRiverAssetTests.RunAll();
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: FAIL on missing `RiverSystem`, missing runtime `RiverRenderFeature`, and River shaders still under `Terrain.Editor`.

- [ ] **Step 3: Commit failing tests**

```powershell
git add -- Terrain.Editor.Tests/RuntimeRiverAssetTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "Add runtime river asset boundary tests"
```

---

### Task 2: Move River Domain and Mesh Generation Into Terrain

**Files:**
- Move: `Terrain.Editor/Models/RiverPixelType.cs` -> `Terrain/Rivers/RiverPixelType.cs`
- Move: `Terrain.Editor/Models/RiverSegment.cs` -> `Terrain/Rivers/RiverSegment.cs`
- Move: `Terrain.Editor/Services/RiverMapService.cs` -> `Terrain/Rivers/RiverMapService.cs`
- Move: `Terrain.Editor/Services/RiverMeshService.cs` -> `Terrain/Rivers/RiverMeshService.cs`
- Create: `Terrain/Rivers/IRiverTerrainHeightSource.cs`
- Modify: River tests and editor files importing old namespaces

- [ ] **Step 1: Move files**

Run:

```powershell
New-Item -ItemType Directory -Force -Path Terrain/Rivers
git mv Terrain.Editor/Models/RiverPixelType.cs Terrain/Rivers/RiverPixelType.cs
git mv Terrain.Editor/Models/RiverSegment.cs Terrain/Rivers/RiverSegment.cs
git mv Terrain.Editor/Services/RiverMapService.cs Terrain/Rivers/RiverMapService.cs
git mv Terrain.Editor/Services/RiverMeshService.cs Terrain/Rivers/RiverMeshService.cs
```

- [ ] **Step 2: Add height sampling interface**

Create `Terrain/Rivers/IRiverTerrainHeightSource.cs`:

```csharp
#nullable enable

namespace Terrain.Rivers;

public interface IRiverTerrainHeightSource
{
    bool HasHeightData { get; }
    int HeightmapWidth { get; }
    int HeightmapHeight { get; }
    float HeightScale { get; }
    float SampleHeight(float worldX, float worldZ);
}
```

- [ ] **Step 3: Update moved namespaces**

In `Terrain/Rivers/RiverPixelType.cs`:

```csharp
namespace Terrain.Rivers;
```

In `Terrain/Rivers/RiverSegment.cs`:

```csharp
namespace Terrain.Rivers;
```

In `Terrain/Rivers/RiverMapService.cs`:

```csharp
using Terrain.Rivers;

namespace Terrain.Rivers;
```

Remove `using Terrain.Editor.Models;`.

In `Terrain/Rivers/RiverMeshService.cs`:

```csharp
using Terrain.Rendering.River;

namespace Terrain.Rivers;
```

Remove `using Terrain.Editor.Models;`, `using Terrain.Editor.Rendering.River;`, and the `TerrainManager` dependency.

- [ ] **Step 4: Change RiverMeshService to use IRiverTerrainHeightSource**

In `Terrain/Rivers/RiverMeshService.cs`, replace the field and constructor:

```csharp
private readonly IRiverTerrainHeightSource? heightSource;

public RiverMeshService(IRiverTerrainHeightSource? heightSource)
{
    this.heightSource = heightSource;
}
```

Replace `SampleTerrainHeight` with:

```csharp
private float SampleTerrainHeight(float wx, float wz)
{
    if (heightSource == null || !heightSource.HasHeightData)
        return 0.0f;

    return heightSource.SampleHeight(wx, wz);
}
```

Replace `GetMapWorldSize` with:

```csharp
private Vector2 GetMapWorldSize()
{
    if (heightSource != null && heightSource.HeightmapWidth > 0 && heightSource.HeightmapHeight > 0)
    {
        return new Vector2(
            Math.Max(heightSource.HeightmapWidth - 1, 0) * TerrainWorldToRiverMapUnits,
            Math.Max(heightSource.HeightmapHeight - 1, 0) * TerrainWorldToRiverMapUnits);
    }

    return new Vector2(4096.0f, 4096.0f);
}
```

In `BuildRiverMesh`, set `RefractionMaxCameraHeight` from `heightSource`:

```csharp
RefractionMaxCameraHeight = MathF.Max(50.0f, heightSource?.HeightScale ?? 50.0f),
```

- [ ] **Step 5: Add editor adapter on TerrainManager**

Modify `Terrain.Editor/Services/TerrainManager.cs`:

```csharp
using Terrain.Rivers;
```

Change the class declaration:

```csharp
public sealed class TerrainManager : IDisposable, IRiverMapSource, IRiverTerrainHeightSource
```

Add explicit interface members near height cache properties:

```csharp
bool IRiverTerrainHeightSource.HasHeightData => HasHeightCache;
int IRiverTerrainHeightSource.HeightmapWidth => HeightCacheWidth;
int IRiverTerrainHeightSource.HeightmapHeight => HeightCacheHeight;
float IRiverTerrainHeightSource.HeightScale => HeightScale;
float IRiverTerrainHeightSource.SampleHeight(float worldX, float worldZ) => GetHeightAtPosition(worldX, worldZ) ?? 0.0f;
```

- [ ] **Step 6: Update using directives in editor and tests**

Replace old imports:

```csharp
using Terrain.Editor.Models;
```

with:

```csharp
using Terrain.Rivers;
```

in:

- `Terrain.Editor/Services/IRiverMapSource.cs`
- `Terrain.Editor/Services/IRiverMeshGenerator.cs`
- `Terrain.Editor/Services/RiverMeshGenerator.cs`
- `Terrain.Editor/Services/RiverRenderingService.cs`
- `Terrain.Editor/ViewModels/RiverViewModel.cs`
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs`

Keep editor-only namespaces for editor-only classes.

- [ ] **Step 7: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: build still fails because render classes remain in `Terrain.Editor.Rendering.River` while `RiverMeshService` now references `Terrain.Rendering.River`.

- [ ] **Step 8: Do not commit yet**

This task intentionally ends before commit because Task 3 completes the namespace move needed to restore compilation.

---

### Task 3: Move River Rendering Core and Shaders Into Terrain

**Files:**
- Move: `Terrain.Editor/Rendering/River/*` -> `Terrain/Rendering/River/*`
- Move: `Terrain.Editor/Effects/River*.sdsl` -> `Terrain/Effects/River/*`
- Modify: `Terrain/Terrain.csproj`
- Modify: `Terrain/Terrain.sdpkg`
- Modify: `Terrain.Editor/Terrain.Editor.csproj`
- Modify: editor/test namespace imports

- [ ] **Step 1: Move render files and shaders**

Run:

```powershell
New-Item -ItemType Directory -Force -Path Terrain/Rendering/River
New-Item -ItemType Directory -Force -Path Terrain/Effects/River
git mv Terrain.Editor/Rendering/River/*.cs Terrain/Rendering/River/
git mv Terrain.Editor/Effects/RiverBottom.sdsl Terrain/Effects/River/RiverBottom.sdsl
git mv Terrain.Editor/Effects/RiverSurface.sdsl Terrain/Effects/River/RiverSurface.sdsl
git mv Terrain.Editor/Effects/RiverSceneSeed.sdsl Terrain/Effects/River/RiverSceneSeed.sdsl
git mv Terrain.Editor/Effects/RiverCommon.sdsl Terrain/Effects/River/RiverCommon.sdsl
git mv Terrain.Editor/Effects/RiverWaterCommon.sdsl Terrain/Effects/River/RiverWaterCommon.sdsl
git mv Terrain.Editor/Effects/RiverVertexStreams.sdsl Terrain/Effects/River/RiverVertexStreams.sdsl
git mv Terrain.Editor/Effects/RiverStrideLighting.sdsl Terrain/Effects/River/RiverStrideLighting.sdsl
```

- [ ] **Step 2: Update namespaces**

In every moved `Terrain/Rendering/River/*.cs`, replace:

```csharp
namespace Terrain.Editor.Rendering.River;
```

with:

```csharp
namespace Terrain.Rendering.River;
```

Replace references to editor services:

```csharp
using Terrain.Editor.Services;
```

with:

```csharp
using Terrain.Rivers;
```

where only `RiverMapService`, `RiverMeshService`, or `RiverSegment` is needed.

- [ ] **Step 3: Move render group constants out of editor service**

Create `Terrain/Rendering/River/RiverRenderGroups.cs`:

```csharp
#nullable enable

using Stride.Rendering;

namespace Terrain.Rendering.River;

public static class RiverRenderGroups
{
    public const RenderGroup RiverRenderGroup = RenderGroup.Group1;
    public const RenderGroupMask RiverRenderGroupMask = RenderGroupMask.Group1;
}
```

In `Terrain/Rendering/River/RiverProcessor.cs`, replace:

```csharp
RiverRenderingService.RiverRenderGroup
```

with:

```csharp
RiverRenderGroups.RiverRenderGroup
```

In editor files that still need masks, replace `RiverRenderingService.RiverRenderGroupMask` usage with `RiverRenderGroups.RiverRenderGroupMask`.

- [ ] **Step 4: Update RiverRenderingService to use public Terrain river types**

Modify `Terrain.Editor/Services/RiverRenderingService.cs` imports:

```csharp
using Terrain.Rendering.River;
using Terrain.Rivers;
```

Keep only editor façade logic. Remove local render group constants if all call sites can use `RiverRenderGroups`.

- [ ] **Step 5: Update Terrain.Editor.csproj River shader items**

Remove the `Compile Update` and `None Update` entries for:

```xml
Effects\RiverSurface.sdsl.cs
Effects\RiverBottom.sdsl.cs
Effects\RiverSceneSeed.sdsl.cs
Effects\RiverVertexStreams.sdsl.cs
Effects\RiverCommon.sdsl.cs
Effects\RiverWaterCommon.sdsl.cs
Effects\RiverStrideLighting.sdsl.cs
```

Also remove:

```xml
<Compile Remove="Effects\RiverEffect.sdfx.cs" />
```

if `RiverEffect.sdfx.cs` is no longer present or needed.

- [ ] **Step 6: Update Terrain.csproj to include River shader key files**

Add to `Terrain/Terrain.csproj`:

```xml
  <ItemGroup>
    <Compile Update="Effects\River\RiverSurface.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverBottom.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverSceneSeed.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverVertexStreams.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverCommon.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverWaterCommon.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
    <Compile Update="Effects\River\RiverStrideLighting.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
  </ItemGroup>

  <ItemGroup>
    <None Update="Effects\River\RiverSurface.sdsl">
      <LastGenOutput>RiverSurface.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverBottom.sdsl">
      <LastGenOutput>RiverBottom.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverSceneSeed.sdsl">
      <LastGenOutput>RiverSceneSeed.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverVertexStreams.sdsl">
      <LastGenOutput>RiverVertexStreams.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverCommon.sdsl">
      <LastGenOutput>RiverCommon.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverWaterCommon.sdsl">
      <LastGenOutput>RiverWaterCommon.sdsl.cs</LastGenOutput>
    </None>
    <None Update="Effects\River\RiverStrideLighting.sdsl">
      <LastGenOutput>RiverStrideLighting.sdsl.cs</LastGenOutput>
    </None>
  </ItemGroup>
```

If MSBuild rejects the `RiverWaterCommon` entry because of a typo, correct it to:

```xml
    <Compile Update="Effects\River\RiverWaterCommon.sdsl.cs">
      <DesignTime>True</DesignTime>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
      <AutoGen>True</AutoGen>
    </Compile>
```

- [ ] **Step 7: Update Terrain.sdpkg root asset**

Modify `Terrain/Terrain.sdpkg`:

```yaml
RootAssets:
    - 2b74f12e-decc-423c-a6fe-90b68f91ee16:River/Environment/reflection-specular
```

If `RootAssets: []` exists, replace it with the block above.

- [ ] **Step 8: Update imports in tests**

Replace:

```csharp
using Terrain.Editor.Rendering.River;
```

with:

```csharp
using Terrain.Rendering.River;
```

in:

- `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `Terrain.Editor.Tests/RiverShaderCompileTests.cs`
- `Terrain.Editor.Tests/Program.cs`

Replace fully qualified `Terrain.Editor.Rendering.River.` with `Terrain.Rendering.River.` in `Terrain.Editor.Tests/Program.cs`.

- [ ] **Step 9: Run generated key update**

Run:

```powershell
dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
```

Expected: generated `Terrain/Effects/River/*.sdsl.cs` files are present or refreshed.

- [ ] **Step 10: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile/test failures only around runtime mesh generation not yet implemented, not namespace or missing shader key failures.

- [ ] **Step 11: Commit move**

```powershell
git add -- Terrain/Rivers Terrain/Rendering/River Terrain/Effects/River Terrain/Terrain.csproj Terrain/Terrain.sdpkg Terrain.Editor Terrain.Editor.Tests
git add -u -- Terrain.Editor/Models Terrain.Editor/Services Terrain.Editor/Rendering/River Terrain.Editor/Effects
git commit -m "Move river core into runtime terrain project"
```

---

### Task 4: Expose Runtime River Width Config From Bootstrap

**Files:**
- Modify: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Modify: `Terrain/Resources/GameRuntimeResourceBootstrap.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs`

- [ ] **Step 1: Write failing bootstrap assertions**

In `BootstrapLoadsFixedCompanionResources`, after height scale assertion:

```csharp
TestHarness.AssertEqual(2.0f, bundle.RiverMinWidth, "river min full width");
TestHarness.AssertEqual(6.0f, bundle.RiverMaxWidth, "river max full width");
```

In `WriteResourceBundle`, update the signature:

```csharp
float riverMinWidth = 2.0f,
float riverMaxWidth = 6.0f,
```

In `CreateDefaultToml`, update signatures to accept and write:

```csharp
float riverMinWidth,
float riverMaxWidth,
```

and emit:

```toml
river_min_width = {{riverMinWidth}}
river_max_width = {{riverMaxWidth}}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: build fails because `TerrainRuntimeResourceBundle.RiverMinWidth` and `RiverMaxWidth` do not exist.

- [ ] **Step 3: Add bundle fields**

Modify `Terrain/Resources/TerrainRuntimeResourceBundle.cs`:

```csharp
public float RiverMinWidth { get; init; } = 1.0f;
public float RiverMaxWidth { get; init; } = 4.0f;
```

Modify `Terrain/Resources/GameRuntimeResourceBootstrap.cs` object initializer:

```csharp
RiverMinWidth = mapDefinition.RiverMinWidth,
RiverMaxWidth = mapDefinition.RiverMaxWidth,
```

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: bootstrap width assertions pass.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain/Resources/TerrainRuntimeResourceBundle.cs Terrain/Resources/GameRuntimeResourceBootstrap.cs Terrain.Editor.Tests/VirtualResources/GameRuntimeResourceBootstrapTests.cs
git commit -m "Expose runtime river width settings"
```

---

### Task 5: Retain Runtime Terrain Height Data for River Sampling

**Files:**
- Modify: `Terrain/Core/TerrainComponent.cs`
- Modify: `Terrain/Core/TerrainProcessor.cs`
- Create: `Terrain/Rivers/TerrainComponentRiverHeightSource.cs`
- Modify: `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`

- [ ] **Step 1: Add failing runtime height source test**

In `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs`, add to `RunAll()`:

```csharp
TestHarness.Run("runtime terrain component exposes river height source after load", RuntimeTerrainComponentExposesRiverHeightSourceAfterLoad);
```

Add the test:

```csharp
private static void RuntimeTerrainComponentExposesRiverHeightSourceAfterLoad()
{
    var component = new TerrainComponent();
    var bundle = CreateValidBundle();
    using var reader = new FakeTerrainFileReader(width: 4, height: 4);

    bool loaded = TerrainProcessor.TryLoadRuntimeData(
        component,
        () => bundle,
        out _,
        _ => reader,
        static (_, _, heightData) => new RuntimeDetailMapData(new byte[4], new byte[4], 2, 2));

    TestHarness.Assert(loaded, "runtime terrain data should load");
    TestHarness.Assert(component.TryCreateRiverHeightSource(out var heightSource), "loaded runtime terrain should expose a river height source");
    TestHarness.Assert(heightSource.HasHeightData, "river height source should have height data");
    TestHarness.AssertEqual(4, heightSource.HeightmapWidth, "river height source width");
    TestHarness.AssertEqual(4, heightSource.HeightmapHeight, "river height source height");
}
```

If `CreateValidBundle()` or `FakeTerrainFileReader` names differ, use the existing helper in that file and keep the assertion body unchanged.

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: build fails because `TryCreateRiverHeightSource` and the new `detailMapBuilder` signature do not exist.

- [ ] **Step 3: Store runtime height data on TerrainComponent**

Modify `Terrain/Core/TerrainComponent.cs`:

```csharp
using Terrain.Rivers;
```

Add internal fields:

```csharp
[DataMemberIgnore]
internal ushort[]? RuntimeHeightData;

[DataMemberIgnore]
internal int RuntimeHeightDataWidth;

[DataMemberIgnore]
internal int RuntimeHeightDataHeight;
```

Add public method:

```csharp
public bool TryCreateRiverHeightSource(out IRiverTerrainHeightSource heightSource)
{
    if (RuntimeHeightData == null || RuntimeHeightDataWidth <= 0 || RuntimeHeightDataHeight <= 0)
    {
        heightSource = NullRiverTerrainHeightSource.Instance;
        return false;
    }

    heightSource = new TerrainComponentRiverHeightSource(this);
    return true;
}
```

- [ ] **Step 4: Add null and terrain height source implementations**

Create `Terrain/Rivers/TerrainComponentRiverHeightSource.cs`:

```csharp
#nullable enable

namespace Terrain.Rivers;

internal sealed class TerrainComponentRiverHeightSource : IRiverTerrainHeightSource
{
    private const float HeightSampleNormalization = 1.0f / ushort.MaxValue;
    private readonly TerrainComponent component;

    public TerrainComponentRiverHeightSource(TerrainComponent component)
    {
        this.component = component ?? throw new ArgumentNullException(nameof(component));
    }

    public bool HasHeightData => component.RuntimeHeightData != null
        && component.RuntimeHeightDataWidth > 0
        && component.RuntimeHeightDataHeight > 0;

    public int HeightmapWidth => component.RuntimeHeightDataWidth;

    public int HeightmapHeight => component.RuntimeHeightDataHeight;

    public float HeightScale => component.HeightScale;

    public float SampleHeight(float worldX, float worldZ)
    {
        ushort[]? data = component.RuntimeHeightData;
        int width = component.RuntimeHeightDataWidth;
        int height = component.RuntimeHeightDataHeight;
        if (data == null || width <= 0 || height <= 0 || data.LongLength < (long)width * height)
            return 0.0f;

        float x = Math.Clamp(worldX, 0.0f, width - 1);
        float z = Math.Clamp(worldZ, 0.0f, height - 1);
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        int x1 = Math.Min(x0 + 1, width - 1);
        int z1 = Math.Min(z0 + 1, height - 1);
        float tx = x - x0;
        float tz = z - z0;

        float h00 = data[z0 * width + x0] * HeightSampleNormalization * component.HeightScale;
        float h10 = data[z0 * width + x1] * HeightSampleNormalization * component.HeightScale;
        float h01 = data[z1 * width + x0] * HeightSampleNormalization * component.HeightScale;
        float h11 = data[z1 * width + x1] * HeightSampleNormalization * component.HeightScale;
        float hx0 = h00 + (h10 - h00) * tx;
        float hx1 = h01 + (h11 - h01) * tx;
        return hx0 + (hx1 - hx0) * tz;
    }
}

internal sealed class NullRiverTerrainHeightSource : IRiverTerrainHeightSource
{
    public static readonly NullRiverTerrainHeightSource Instance = new();

    private NullRiverTerrainHeightSource()
    {
    }

    public bool HasHeightData => false;
    public int HeightmapWidth => 0;
    public int HeightmapHeight => 0;
    public float HeightScale => 0.0f;
    public float SampleHeight(float worldX, float worldZ) => 0.0f;
}
```

- [ ] **Step 5: Read height data once in TerrainProcessor**

Modify the delegate type in `Terrain/Core/TerrainProcessor.cs`:

```csharp
Func<ITerrainFileReader, TerrainRuntimeResourceBundle, ushort[], RuntimeDetailMapData>? detailMapBuilder = null
```

Apply that signature in `TryLoadRuntimeData`, `CreateLoadedTerrainData`, and test call sites.

In `CreateLoadedTerrainData`, before detail generation:

```csharp
ushort[] heightData = fileReader.ReadAllHeightData();
RuntimeDetailMapData generatedDetailMaps = detailMapBuilder(fileReader, bundle, heightData);
```

Update `BuildRuntimeDetailMaps`:

```csharp
private static RuntimeDetailMapData BuildRuntimeDetailMaps(
    ITerrainFileReader fileReader,
    TerrainRuntimeResourceBundle bundle,
    ushort[] heightData)
{
    RuntimeBiomeMaskData biomeMask = RuntimeBiomeMaskReader.ReadFrom(bundle.BiomeMaskPath);
    ...
    return RuntimeDetailMapBuilder.Generate(
        heightData,
        fileReader.Header.Width,
        fileReader.Header.Height,
        ...);
}
```

Add `heightData` to `LoadedTerrainData`:

```csharp
ushort[] HeightData,
```

Pass it in the constructor and set component fields in `ApplyLoadedTerrainData`:

```csharp
component.RuntimeHeightData = loadedData.HeightData;
component.RuntimeHeightDataWidth = loadedData.Width;
component.RuntimeHeightDataHeight = loadedData.Height;
```

Clear these fields in `OnEntityComponentRemoved` before disposing:

```csharp
component.RuntimeHeightData = null;
component.RuntimeHeightDataWidth = 0;
component.RuntimeHeightDataHeight = 0;
```

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: runtime terrain height source test passes.

- [ ] **Step 7: Commit**

```powershell
git add -- Terrain/Core/TerrainComponent.cs Terrain/Core/TerrainProcessor.cs Terrain/Rivers/TerrainComponentRiverHeightSource.cs Terrain.Editor.Tests/VirtualResources/TerrainRuntimeLoadBehaviorTests.cs
git commit -m "Expose runtime terrain height source for rivers"
```

---

### Task 6: Add RiverProcessor Runtime Mesh Loading

**Files:**
- Modify: `Terrain/Rendering/River/RiverComponent.cs`
- Modify: `Terrain/Rendering/River/RiverProcessor.cs`
- Modify: `Terrain/Rendering/River/RiverResourceLoader.cs`
- Create: `Terrain/Rendering/River/RiverRuntimeLoadState.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [ ] **Step 1: Add failing component state tests**

In `Terrain.Editor.Tests/Program.cs`, update River component tests to use `Terrain.Rendering.River`. Add a new test registration near other River component tests:

```csharp
Run("river component records runtime load failure config", RiverComponentRecordsRuntimeLoadFailureConfig);
```

Add:

```csharp
void RiverComponentRecordsRuntimeLoadFailureConfig()
{
    var component = new Terrain.Rendering.River.RiverComponent();
    var config = new Terrain.Rendering.River.RiverRuntimeLoadConfig("map/rivers.png", 1.0f, 4.0f, 200.0f, 4096, 2048);

    component.MarkRuntimeLoadFailure(config);

    AssertEqual(Terrain.Rendering.River.RiverRuntimeLoadState.Failed, component.RuntimeLoadState, "runtime river state");
    Assert(!component.ShouldAttemptRuntimeLoad(config), "same failed config should not retry");

    var changed = new Terrain.Rendering.River.RiverRuntimeLoadConfig("map/rivers.png", 2.0f, 4.0f, 200.0f, 4096, 2048);
    Assert(component.ShouldAttemptRuntimeLoad(changed), "changed failed config should retry");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: build fails because runtime load state types do not exist.

- [ ] **Step 3: Add runtime load state types**

Create `Terrain/Rendering/River/RiverRuntimeLoadState.cs`:

```csharp
#nullable enable

namespace Terrain.Rendering.River;

public enum RiverRuntimeLoadState
{
    NotAttempted,
    Loaded,
    NoRiverResource,
    Failed,
}

public readonly record struct RiverRuntimeLoadConfig(
    string? RiversPath,
    float RiverMinWidth,
    float RiverMaxWidth,
    float HeightScale,
    int HeightmapWidth,
    int HeightmapHeight);
```

Modify `Terrain/Rendering/River/RiverComponent.cs`:

```csharp
public RiverRuntimeLoadState RuntimeLoadState { get; private set; } = RiverRuntimeLoadState.NotAttempted;
public RiverRuntimeLoadConfig FailedRuntimeLoadConfig { get; private set; }

public bool ShouldAttemptRuntimeLoad(RiverRuntimeLoadConfig config)
{
    return RuntimeLoadState != RiverRuntimeLoadState.Failed || FailedRuntimeLoadConfig != config;
}

public void MarkRuntimeLoadSuccess()
{
    RuntimeLoadState = RiverRuntimeLoadState.Loaded;
    FailedRuntimeLoadConfig = default;
}

public void MarkRuntimeNoRiverResource()
{
    Clear();
    RuntimeLoadState = RiverRuntimeLoadState.NoRiverResource;
    FailedRuntimeLoadConfig = default;
}

public void MarkRuntimeLoadFailure(RiverRuntimeLoadConfig config)
{
    Clear();
    RuntimeLoadState = RiverRuntimeLoadState.Failed;
    FailedRuntimeLoadConfig = config;
}
```

- [ ] **Step 4: Implement runtime mesh loading in RiverProcessor**

In `Terrain/Rendering/River/RiverProcessor.cs`, add imports:

```csharp
using System;
using System.IO;
using System.Linq;
using Stride.Core.Diagnostics;
using Terrain.Resources;
using Terrain.Rivers;
```

Add logger:

```csharp
private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");
```

In `UpdateRenderObjects`, before render object rebuild:

```csharp
TryEnsureRuntimeMeshes(component);
```

Add helper methods:

```csharp
private void TryEnsureRuntimeMeshes(RiverComponent component)
{
    if (component.Meshes.Count > 0 || component.RuntimeLoadState == RiverRuntimeLoadState.Loaded)
        return;

    TerrainComponent? terrainComponent = FindInitializedTerrainComponent();
    if (terrainComponent == null || !terrainComponent.TryCreateRiverHeightSource(out var heightSource) || !heightSource.HasHeightData)
        return;

    TerrainRuntimeResourceBundle bundle;
    try
    {
        var resolver = GameResourceResolverBootstrap.CreateForAppDirectory(AppContext.BaseDirectory);
        bundle = new GameRuntimeResourceBootstrap(resolver).Load();
    }
    catch (Exception exception)
    {
        var failedConfig = new RiverRuntimeLoadConfig(null, 1.0f, 4.0f, terrainComponent.HeightScale, heightSource.HeightmapWidth, heightSource.HeightmapHeight);
        component.MarkRuntimeLoadFailure(failedConfig);
        Log.Error($"River runtime resources could not be read: {exception.Message}");
        return;
    }

    var config = new RiverRuntimeLoadConfig(
        bundle.RiversPath,
        bundle.RiverMinWidth,
        bundle.RiverMaxWidth,
        bundle.HeightScale,
        heightSource.HeightmapWidth,
        heightSource.HeightmapHeight);

    if (!component.ShouldAttemptRuntimeLoad(config))
        return;

    foreach (string diagnostic in bundle.Diagnostics)
        Log.Warning(diagnostic);

    if (bundle.RiversPath == null)
    {
        component.MarkRuntimeNoRiverResource();
        Log.Warning("River runtime resource is not available; river rendering is disabled.");
        return;
    }

    try
    {
        var mapService = new RiverMapService(bundle.RiverMinWidth, bundle.RiverMaxWidth);
        mapService.Load(bundle.RiversPath);
        if (mapService.Cells == null)
            throw new InvalidDataException($"River map load failed: {string.Join("; ", mapService.Errors)}");

        foreach (string error in mapService.Errors)
            Log.Warning(error);

        var segments = mapService.ExtractSegments();
        foreach (var segment in segments)
        {
            segment.TaperStart = segment.StartKind == SegmentEndKind.Source || segment.StartKind == SegmentEndKind.None;
            segment.TaperEnd = segment.EndKind == SegmentEndKind.Confluence || segment.EndKind == SegmentEndKind.Bifurcation;
        }

        var meshService = new RiverMeshService(heightSource);
        meshService.BuildCenterlines(segments, mapService.Width, mapService.Height);
        var meshes = segments
            .Select(segment => meshService.BuildRiverMesh(segment, 1.0f))
            .Where(mesh => mesh.Vertices.Length > 0 && mesh.Indices.Length > 0)
            .ToArray();

        component.SetMeshes(meshes);
        component.MarkRuntimeLoadSuccess();
    }
    catch (Exception exception)
    {
        component.MarkRuntimeLoadFailure(config);
        Log.Error($"River runtime meshes could not be generated: {exception.Message}");
    }
}

private TerrainComponent? FindInitializedTerrainComponent()
{
    if (VisibilityGroup == null)
        return null;

    foreach (RenderObject renderObject in VisibilityGroup.RenderObjects)
    {
        if (renderObject is TerrainRenderObject { Source: { IsInitialized: true } terrainComponent })
            return terrainComponent;
    }

    return null;
}
```

If `TerrainRenderObject.Source` is not public, make the property public read-only in `Terrain/Rendering/TerrainRenderObject.cs`:

```csharp
public TerrainComponent Source { get; init; } = null!;
```

- [ ] **Step 5: Make RiverResourceLoader fail without crashing Terrain**

Modify `Terrain/Rendering/River/RiverResourceLoader.cs` to use logger name `Terrain`:

```csharp
private static readonly Logger Log = GlobalLogger.GetLogger("Terrain");
```

Modify `RiverRenderFeature.InitializeCore` around `riverResources.Load(...)`:

```csharp
try
{
    riverResources.Load(Context.GraphicsDevice, contentManager);
}
catch (Exception exception)
{
    Log.Error($"River render resources could not be loaded: {exception.Message}");
    riverResources.Dispose();
}
```

In `BindRiverTextures`, keep `SetTexture` calls tolerant of null resources. The pass will bind null textures and render disabled/empty if no render objects exist.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: River component state test passes. Existing render feature tests still pass.

- [ ] **Step 7: Commit**

```powershell
git add -- Terrain/Rendering/River Terrain/Rendering/TerrainRenderObject.cs Terrain.Editor.Tests/Program.cs
git commit -m "Load runtime river meshes in river processor"
```

---

### Task 7: Update Runtime Scene and Graphics Compositor Assets

**Files:**
- Modify: `Terrain/Assets/MainScene.sdscene`
- Modify: `Terrain/Assets/GraphicsCompositor.sdgfxcomp`

- [ ] **Step 1: Add RiverSystem to MainScene**

Modify `Terrain/Assets/MainScene.sdscene`.

Add a new root part id under `Hierarchy.RootParts`:

```yaml
        - ref!! c8f8f226-3477-45ec-84d8-d8e8de365e1b
```

Add a new entity under `Hierarchy.Parts`:

```yaml
        -   Entity:
                Id: c8f8f226-3477-45ec-84d8-d8e8de365e1b
                Name: RiverSystem
                Components:
                    1e60bd75c1fd45a98b5f89b08207e4f7: !TransformComponent
                        Id: 599af9e2-1c2b-4230-8c24-9f37111e5e55
                        Position: {X: 0.0, Y: 0.0, Z: 0.0}
                        Rotation: {X: 0.0, Y: 0.0, Z: 0.0, W: 1.0}
                        Scale: {X: 1.0, Y: 1.0, Z: 1.0}
                        Children: {}
                    17d6e522e35445b2aa1dbed5dddb62db: !Terrain.Rendering.River.RiverComponent,Terrain
                        Id: 764d8cb4-27be-4ed3-935f-3ed571bdc62e
```

- [ ] **Step 2: Add RiverRenderFeature to GraphicsCompositor**

Modify `Terrain/Assets/GraphicsCompositor.sdgfxcomp`.

Add this under `RenderFeatures:` after `TerrainRenderFeature`:

```yaml
    8df249f89b144227972a4c6388f267e1: !Terrain.Rendering.River.RiverRenderFeature,Terrain
        RenderStageSelectors:
            13c8f5ded00f4bc39341f9ab3e4afe54: !Stride.Rendering.SimpleGroupToRenderStageSelector,Stride.Rendering
                RenderStage: ref!! 0fbd7f2d-8037-4033-9616-14d59c88b1fd
                EffectName: RiverSurface
                RenderGroup: Group1
        PipelineProcessors: {}
        RenderFeatures: {}
```

Do not add runtime code that calls `EnsureRiverRenderFeature`.

- [ ] **Step 3: Run asset text tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: `RuntimeRiverAssetTests` pass.

- [ ] **Step 4: Commit**

```powershell
git add -- Terrain/Assets/MainScene.sdscene Terrain/Assets/GraphicsCompositor.sdgfxcomp
git commit -m "Add runtime river scene assets"
```

---

### Task 8: Update Editor Embedded Viewport to Shared River Core

**Files:**
- Modify: `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- Modify: `Terrain.Editor/Rendering/RiverWireframeModeController.cs`
- Modify: `Terrain.Editor/Services/RiverRenderingService.cs`
- Modify: `Terrain.Editor/Services/RiverMeshGenerator.cs`

- [ ] **Step 1: Update imports**

In `EmbeddedStrideViewportGame.cs`, replace:

```csharp
using Terrain.Editor.Rendering.River;
```

with:

```csharp
using Terrain.Rendering.River;
```

Also add:

```csharp
using Terrain.Rivers;
```

if river generation types are referenced.

In `RiverWireframeModeController.cs`, replace old render namespace with:

```csharp
using Terrain.Rendering.River;
```

- [ ] **Step 2: Keep editor dynamic fallback only for editor**

In `EmbeddedStrideViewportGame.EnsureRiverRenderFeature`, update group reference:

```csharp
RenderGroup = RiverRenderGroups.RiverRenderGroupMask,
```

Keep this method in editor because the embedded viewport wraps/clones scene and compositor assets differently. Do not add similar fallback to `Terrain.Windows`.

- [ ] **Step 3: Update editor RiverSystem creation**

In `InitializeTerrainManager`, keep editor-created `_riverEntity` path for embedded editor viewport:

```csharp
_riverEntity = new Entity("RiverSystem");
_riverComponent = new RiverComponent();
```

This is allowed only in editor because the editor scene clone strips non-editor scene components and constructs its viewport scene separately.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: editor façade and view model tests pass.

- [ ] **Step 5: Commit**

```powershell
git add -- Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs Terrain.Editor/Rendering/RiverWireframeModeController.cs Terrain.Editor/Services/RiverRenderingService.cs Terrain.Editor/Services/RiverMeshGenerator.cs
git commit -m "Update editor viewport to shared river core"
```

---

### Task 9: Run Stride Shader Asset Rebuild and Full Verification

**Files:**
- Generated/updated: `Terrain/Effects/River/*.sdsl.cs`
- No source files are manually edited in this task unless generated River shader key files change.

- [ ] **Step 1: Refresh generated shader key files**

Run:

```powershell
dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
```

Expected: command exits `0`; generated key files for `Terrain/Effects/River/*.sdsl` exist.

- [ ] **Step 2: Clean Stride assets**

Run:

```powershell
dotnet msbuild Terrain/Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug
```

Expected: command exits `0`.

- [ ] **Step 3: Compile Stride assets**

Run:

```powershell
dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug
```

Expected: command exits `0`; no `E1202 mixin ... is not in the module` errors.

- [ ] **Step 4: Run test harness**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 5: Build solution**

Run:

```powershell
dotnet build Terrain.sln --no-restore
```

Expected: build succeeds.

- [ ] **Step 6: Check whitespace**

Run:

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 7: Commit generated shader and verification fixes**

```powershell
git add -- Terrain/Effects/River
git add -- Terrain/Terrain.csproj Terrain/Terrain.sdpkg Terrain.Editor/Terrain.Editor.csproj
git commit -m "Rebuild runtime river shader assets"
```

If no generated files changed, skip the commit and note that asset rebuild produced no tracked changes.

---

### Task 10: Update Architecture Docs and Session Log

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Add: `docs/log/2026/06/22/2026-06-22-runtime-river-implementation.md`

- [ ] **Step 1: Update architecture overview**

In `docs/ARCHITECTURE_OVERVIEW.md`, update the river architecture section with:

```markdown
2026-06-22 Runtime river rendering now uses the same shared `Terrain.Rendering.River` core as the editor. `Terrain/Assets/MainScene.sdscene` owns the `RiverSystem` entity with `RiverComponent`, and `Terrain/Assets/GraphicsCompositor.sdgfxcomp` owns `RiverRenderFeature` registration. `RiverProcessor` generates runtime meshes from optional `game/map/rivers.png` after terrain height data is available, while `TerrainRuntimeResourceBundle` remains a resource/config carrier and does not create scene objects.
```

- [ ] **Step 2: Update current features**

In `docs/CURRENT_FEATURES.md`, update the Runtime table river status:

```markdown
| Runtime 河流渲染 | ✅ | `Terrain/Rendering/River/`, `Terrain/Rivers/`, `Terrain/Assets/MainScene.sdscene`, `Terrain/Assets/GraphicsCompositor.sdgfxcomp` | Runtime scene asset owns `RiverSystem`; compositor asset owns `RiverRenderFeature`; `RiverProcessor` loads optional `rivers.png` and generates meshes after terrain height data is initialized. Missing rivers disable river rendering without failing terrain. |
```

- [ ] **Step 3: Add session log**

Create `docs/log/2026/06/22/2026-06-22-runtime-river-implementation.md`:

```markdown
# Runtime River Implementation
**Date**: 2026-06-22
**Session**: runtime river implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Display `game/map/rivers.png` in `Terrain.Windows` using scene-authored `RiverSystem` and compositor-authored `RiverRenderFeature`.

---

## What We Did

### 1. Shared River core
- Moved river generation and rendering core from `Terrain.Editor` into `Terrain`.
- Kept editor UI/facade in `Terrain.Editor`.

### 2. Runtime scene integration
- Added `RiverSystem` with `RiverComponent` to `Terrain/Assets/MainScene.sdscene`.
- Added `RiverRenderFeature` to `Terrain/Assets/GraphicsCompositor.sdgfxcomp`.

### 3. Runtime mesh generation
- `RiverProcessor` now waits for initialized terrain height data.
- It loads optional `rivers.png`, preserves configured width gradients, builds meshes, and writes them to `RiverComponent`.
- Missing rivers disable only River rendering.

---

## Testing

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-restore`
- `dotnet msbuild Terrain/Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain/Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain/Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.sln --no-restore`
- `git diff --check`

---

## Quick Reference for Future Claude

- `Terrain.Windows` must not reference `Terrain.Editor`.
- Do not create `RiverSystem` in runtime code.
- Do not register `RiverRenderFeature` in runtime code.
- River runtime mesh generation belongs in `RiverProcessor`.
- River mesh data remains owned by `RiverComponent`.
```

- [ ] **Step 4: Run docs diff check**

Run:

```powershell
git diff --check -- docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/22/2026-06-22-runtime-river-implementation.md
```

Expected: no output.

- [ ] **Step 5: Commit docs**

```powershell
git add -- docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/22/2026-06-22-runtime-river-implementation.md
git commit -m "Document runtime river integration"
```

---

## Self-Review

Spec coverage:

- Shared River core in `Terrain`: Tasks 2 and 3.
- No `Terrain.Windows -> Terrain.Editor` dependency: Task 1.
- Scene-authored `RiverSystem`: Task 7.
- Compositor-authored `RiverRenderFeature`: Task 7.
- Bundle remains config/resource carrier: Task 4 and Task 6.
- `RiverComponent` stores mesh data and load state: Task 6.
- `RiverProcessor` generates runtime mesh and syncs render objects: Task 6.
- Runtime height sampling independent of `TerrainManager`: Task 5.
- Optional `rivers.png` handling: Task 6.
- Missing water DDS does not fail terrain: Task 6.
- Stride shader asset workflow: Task 9.
- Documentation and session log: Task 10.

Placeholder scan:

- No placeholder or deferred implementation markers are intended.
- Every task has explicit files, commands, expected results, and commit instructions.

Type consistency:

- River domain namespace: `Terrain.Rivers`.
- River render namespace: `Terrain.Rendering.River`.
- Height source interface: `IRiverTerrainHeightSource`.
- Runtime load status: `RiverRuntimeLoadState`.
- Runtime load config: `RiverRuntimeLoadConfig`.
- Render group constants: `RiverRenderGroups`.
