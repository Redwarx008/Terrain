# River Local Water Texture Files Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move river bottom and water DDS textures to `game/map/water` and load them directly from files instead of Stride content assets.

**Architecture:** `RiverResourceLoader` becomes a hybrid loader: local DDS files for Bottom/Water textures, `ContentManager` only for `River/Environment/reflection-specular`. The render feature still owns lifecycle and shader binding; shaders and texture parameter names stay unchanged.

**Tech Stack:** C#/.NET, Stride `Texture.Load`, existing `GameResourceRootLocator`, PowerShell file operations, `Terrain.Editor.Tests` text regression harness.

---

## File Structure

- Modify `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
  - Resolve `game/` root.
  - Load `game/map/water/*.dds` with `Texture.Load`.
  - Keep `ReflectionSpecular` loaded through `ContentManager`.
  - Dispose file-loaded textures directly; unload only the Stride content texture via `ContentManager`.
- Modify `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
  - Pass `Context.GraphicsDevice` into `RiverResourceLoader.Load`.
- Modify `Terrain.Editor.Tests/RiverShaderTextTests.cs`
  - Replace `.sdtex`/RootAsset expectations for Bottom/Water with local DDS expectations.
  - Keep environment cubemap tests.
- Modify `Terrain.Editor/Terrain.Editor.sdpkg`
  - Remove `River/Bottom/*` and `River/Water/*` RootAssets.
  - Keep `River/Environment/reflection-specular`.
- Move/delete resource files
  - Move Bottom/Water DDS into `game/map/water` with underscore names.
  - Delete Bottom/Water `.sdtex` files.
  - Leave `Terrain.Editor/Assets/River/Environment/*` unchanged.
- Modify `Terrain.Editor/Assets/River/README.md`
  - Document `game/map/water` as the active Bottom/Water resource location.
- Modify `docs/ARCHITECTURE_OVERVIEW.md` and `docs/CURRENT_FEATURES.md`
  - Update current architecture references.
- Create `docs/log/2026/06/20/2026-06-20-river-local-water-texture-files.md`
  - Session closeout log.

---

### Task 1: Move DDS Files To `game/map/water`

**Files:**
- Create directory: `game/map/water`
- Move:
  - `Terrain.Editor/Assets/River/Bottom/bottom-diffuse.dds` -> `game/map/water/bottom_diffuse.dds`
  - `Terrain.Editor/Assets/River/Bottom/bottom-normal.dds` -> `game/map/water/bottom_normal.dds`
  - `Terrain.Editor/Assets/River/Bottom/bottom-properties.dds` -> `game/map/water/bottom_properties.dds`
  - `Terrain.Editor/Assets/River/Bottom/bottom-depth.dds` -> `game/map/water/bottom_depth.dds`
  - `Terrain.Editor/Assets/River/Water/ambient-normal.dds` -> `game/map/water/ambient_normal.dds`
  - `Terrain.Editor/Assets/River/Water/flow-normal.dds` -> `game/map/water/flow_normal.dds`
  - `Terrain.Editor/Assets/River/Water/foam.dds` -> `game/map/water/foam.dds`
  - `Terrain.Editor/Assets/River/Water/foam-ramp.dds` -> `game/map/water/foam_ramp.dds`
  - `Terrain.Editor/Assets/River/Water/foam-map.dds` -> `game/map/water/foam_map.dds`
  - `Terrain.Editor/Assets/River/Water/foam-noise.dds` -> `game/map/water/foam_noise.dds`
  - `Terrain.Editor/Assets/River/Water/shadow-color.dds` -> `game/map/water/shadow_color.dds`
  - `Terrain.Editor/Assets/River/Water/water-color.dds` -> `game/map/water/water_color.dds`
- Delete:
  - `Terrain.Editor/Assets/River/Bottom/*.sdtex`
  - `Terrain.Editor/Assets/River/Water/*.sdtex`

- [ ] **Step 1: Move files with native PowerShell**

Run:

```powershell
New-Item -ItemType Directory -Force -LiteralPath 'game\map\water'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-diffuse.dds' -Destination 'game\map\water\bottom_diffuse.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-normal.dds' -Destination 'game\map\water\bottom_normal.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-properties.dds' -Destination 'game\map\water\bottom_properties.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-depth.dds' -Destination 'game\map\water\bottom_depth.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\ambient-normal.dds' -Destination 'game\map\water\ambient_normal.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\flow-normal.dds' -Destination 'game\map\water\flow_normal.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam.dds' -Destination 'game\map\water\foam.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-ramp.dds' -Destination 'game\map\water\foam_ramp.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-map.dds' -Destination 'game\map\water\foam_map.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-noise.dds' -Destination 'game\map\water\foam_noise.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\shadow-color.dds' -Destination 'game\map\water\shadow_color.dds'
Move-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\water-color.dds' -Destination 'game\map\water\water_color.dds'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-diffuse.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-normal.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-properties.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Bottom\bottom-depth.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\ambient-normal.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\flow-normal.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-ramp.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-map.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\foam-noise.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\shadow-color.sdtex'
Remove-Item -LiteralPath 'Terrain.Editor\Assets\River\Water\water-color.sdtex'
```

Expected: all commands complete without errors.

- [ ] **Step 2: Verify target inventory**

Run:

```powershell
Get-ChildItem -LiteralPath 'game\map\water' | Select-Object Name,Length
```

Expected: the 12 DDS files listed above are present, with non-zero lengths.

---

### Task 2: Rewrite Loader For Local Bottom/Water Files

**Files:**
- Modify: `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- Modify: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

- [ ] **Step 1: Replace loader URL constants with local filenames**

In `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, add these usings:

```csharp
using System.IO;
using Terrain.Resources;
```

Replace the Bottom/Water URL constants with:

```csharp
    private const string WaterDirectory = "map/water";
    private const string BottomDiffuseFileName = "bottom_diffuse.dds";
    private const string BottomNormalFileName = "bottom_normal.dds";
    private const string BottomPropertiesFileName = "bottom_properties.dds";
    private const string BottomDepthFileName = "bottom_depth.dds";
    private const string AmbientNormalFileName = "ambient_normal.dds";
    private const string FlowNormalFileName = "flow_normal.dds";
    private const string FoamFileName = "foam.dds";
    private const string FoamRampFileName = "foam_ramp.dds";
    private const string FoamMapFileName = "foam_map.dds";
    private const string FoamNoiseFileName = "foam_noise.dds";
    private const string ShadowColorFileName = "shadow_color.dds";
    private const string WaterColorFileName = "water_color.dds";
    public const string ReflectionSpecularUrl = "River/Environment/reflection-specular";
```

- [ ] **Step 2: Change `Load` signature and implementation**

Replace `public void Load(ContentManager content)` with:

```csharp
    public void Load(GraphicsDevice graphicsDevice, ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(content);

        string gameRoot = GameResourceRootLocator.LocateFromAppContext();
        string waterDirectory = Path.Combine(gameRoot, "map", "water");

        BottomDiffuse = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomDiffuseFileName, loadAsSrgb: true);
        BottomNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomNormalFileName, loadAsSrgb: false);
        BottomProperties = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomPropertiesFileName, loadAsSrgb: false);
        BottomDepth = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, BottomDepthFileName, loadAsSrgb: false);
        AmbientNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, AmbientNormalFileName, loadAsSrgb: false);
        FlowNormal = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FlowNormalFileName, loadAsSrgb: false);
        Foam = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamFileName, loadAsSrgb: false);
        FoamRamp = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamRampFileName, loadAsSrgb: false);
        FoamMap = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamMapFileName, loadAsSrgb: false);
        FoamNoise = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, FoamNoiseFileName, loadAsSrgb: false);
        ShadowColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, ShadowColorFileName, loadAsSrgb: true);
        WaterColor = LoadRequiredLocalTexture(graphicsDevice, waterDirectory, WaterColorFileName, loadAsSrgb: false);
        ReflectionSpecular = LoadRequiredContentTexture(content, ReflectionSpecularUrl);
    }
```

- [ ] **Step 3: Split unload/dispose responsibilities**

Replace `Unload` and `Dispose` with:

```csharp
    public void Unload(ContentManager content)
    {
        ArgumentNullException.ThrowIfNull(content);

        DisposeLocalTexture(BottomDiffuse);
        DisposeLocalTexture(BottomNormal);
        DisposeLocalTexture(BottomProperties);
        DisposeLocalTexture(BottomDepth);
        DisposeLocalTexture(AmbientNormal);
        DisposeLocalTexture(FlowNormal);
        DisposeLocalTexture(Foam);
        DisposeLocalTexture(FoamRamp);
        DisposeLocalTexture(FoamMap);
        DisposeLocalTexture(FoamNoise);
        DisposeLocalTexture(ShadowColor);
        DisposeLocalTexture(WaterColor);
        UnloadContentTexture(content, ReflectionSpecular);
        Dispose();
    }

    public void Dispose()
    {
        BottomDiffuse = null;
        BottomNormal = null;
        BottomProperties = null;
        BottomDepth = null;
        AmbientNormal = null;
        FlowNormal = null;
        Foam = null;
        FoamRamp = null;
        FoamMap = null;
        FoamNoise = null;
        ShadowColor = null;
        WaterColor = null;
        ReflectionSpecular = null;
    }
```

- [ ] **Step 4: Add local and content helper methods**

Replace `LoadRequiredTexture` and `UnloadTexture` with:

```csharp
    private static Texture LoadRequiredLocalTexture(GraphicsDevice graphicsDevice, string directory, string fileName, bool loadAsSrgb)
    {
        string path = Path.Combine(directory, fileName);
        try
        {
            using var stream = File.OpenRead(path);
            return Texture.Load(
                graphicsDevice,
                stream,
                TextureFlags.ShaderResource,
                GraphicsResourceUsage.Immutable,
                loadAsSrgb);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"River local texture file '{path}' could not be loaded from game/map/water.", exception);
        }
    }

    private static Texture LoadRequiredContentTexture(ContentManager content, string url)
    {
        try
        {
            return content.Load<Texture>(url);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"River texture asset '{url}' could not be loaded. Ensure the .sdtex is included as a RootAsset in Terrain.Editor.sdpkg.", exception);
        }
    }

    private static void DisposeLocalTexture(Texture? texture)
    {
        texture?.Dispose();
    }

    private static void UnloadContentTexture(ContentManager content, Texture? texture)
    {
        if (texture == null) return;
        content.Unload(texture);
    }
```

- [ ] **Step 5: Pass graphics device from render feature**

In `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, replace:

```csharp
        riverResources.Load(contentManager);
```

with:

```csharp
        riverResources.Load(Context.GraphicsDevice, contentManager);
```

- [ ] **Step 6: Build check**

Run:

```powershell
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug --no-restore
```

Expected: build succeeds, or only existing unrelated warnings remain.

---

### Task 3: Remove Bottom/Water RootAssets

**Files:**
- Modify: `Terrain.Editor/Terrain.Editor.sdpkg`

- [ ] **Step 1: Remove these root asset lines**

Delete only these lines:

```yaml
    - 5b2594a0-c91f-4538-b571-edadbf35b53a:River/Bottom/bottom-depth
    - a734d0f8-42ac-44f7-a2e6-de5ebfd17f93:River/Bottom/bottom-diffuse
    - 3fabb930-58ad-480d-9f2d-2e5f29d1f163:River/Bottom/bottom-normal
    - 73e4886a-251d-46a8-8edd-64d4dc9b38fd:River/Bottom/bottom-properties
    - ee6be492-c410-468e-9cb4-e171aa233664:River/Water/ambient-normal
    - 2b422889-3914-4ea2-86ee-668f809ca907:River/Water/flow-normal
    - fda200cf-a11d-48cf-9e83-6cad2401afbf:River/Water/foam-map
    - bdee9afb-8a7d-4828-b97e-964471588f93:River/Water/foam-noise
    - 819998d2-13aa-44e1-95a1-3d815decc639:River/Water/foam-ramp
    - bc203c25-afb8-43f3-8bd9-239a58f80a76:River/Water/foam
    - b61b28b5-e48d-4769-96dd-373a084f331f:River/Water/shadow-color
    - 55163118-1126-4be4-8fc1-ced8dcbd678a:River/Water/water-color
```

Keep:

```yaml
    - 2b74f12e-decc-423c-a6fe-90b68f91ee16:River/Environment/reflection-specular
```

- [ ] **Step 2: Verify package no longer references Bottom/Water URLs**

Run:

```powershell
rg -n "River/(Bottom|Water)" Terrain.Editor/Terrain.Editor.sdpkg
```

Expected: no matches.

---

### Task 4: Update Regression Tests

**Files:**
- Modify: `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- [ ] **Step 1: Replace descriptor test with local DDS inventory test**

Replace `RiverTextureAssetsHaveStrideDescriptors` with:

```csharp
    private static void RiverTextureFilesExistInGameMapWater()
    {
        string[] fileNames =
        [
            "bottom_diffuse.dds",
            "bottom_normal.dds",
            "bottom_properties.dds",
            "bottom_depth.dds",
            "ambient_normal.dds",
            "flow_normal.dds",
            "foam.dds",
            "foam_ramp.dds",
            "foam_map.dds",
            "foam_noise.dds",
            "shadow_color.dds",
            "water_color.dds",
        ];

        foreach (string fileName in fileNames)
        {
            string fullPath = GetRepositoryPath($"game/map/water/{fileName}");
            TestHarness.Assert(File.Exists(fullPath), $"{fileName} should exist in game/map/water for direct river texture loading");
            TestHarness.Assert(new FileInfo(fullPath).Length > 0, $"{fileName} should not be empty");
        }
    }
```

Update `RunAll` call:

```csharp
        TestHarness.Run("river texture files exist in game map water", RiverTextureFilesExistInGameMapWater);
```

- [ ] **Step 2: Replace RootAsset test with package exclusion test**

Replace `RiverTextureAssetsAreBundleRoots` with:

```csharp
    private static void RiverBottomAndWaterTexturesAreNotBundleRoots()
    {
        string package = ReadRepositoryText("Terrain.Editor/Terrain.Editor.sdpkg");

        string[] removedUrls =
        [
            "River/Bottom/bottom-diffuse",
            "River/Bottom/bottom-normal",
            "River/Bottom/bottom-properties",
            "River/Bottom/bottom-depth",
            "River/Water/ambient-normal",
            "River/Water/flow-normal",
            "River/Water/foam",
            "River/Water/foam-ramp",
            "River/Water/foam-map",
            "River/Water/foam-noise",
            "River/Water/shadow-color",
            "River/Water/water-color",
        ];

        foreach (string url in removedUrls)
        {
            AssertNotContains(package, $":{url}", $"{url} should not be a RootAsset because river Bottom/Water textures are direct files under game/map/water");
        }

        AssertContains(package, ":River/Environment/reflection-specular", "River environment reflection cubemap should remain a Stride RootAsset");
    }
```

Update `RunAll` call:

```csharp
        TestHarness.Run("river bottom and water textures are not bundle roots", RiverBottomAndWaterTexturesAreNotBundleRoots);
```

- [ ] **Step 3: Update loader assertions**

In `SurfaceShaderPostStepAppliesTargetShadowAndFogWrapper`, replace:

```csharp
        AssertContains(loader, "ShadowColorUrl = \"River/Water/shadow-color\"", "RiverResourceLoader should load the shadow tint texture through a neutral asset URL");
```

with:

```csharp
        AssertContains(loader, "ShadowColorFileName = \"shadow_color.dds\"", "RiverResourceLoader should load the shadow tint texture from game/map/water");
```

Replace `ResourceLoaderDoesNotSilentlyIgnoreMissingTextures` with:

```csharp
    private static void ResourceLoaderDoesNotSilentlyIgnoreMissingTextures()
    {
        string loader = ReadRepositoryText("Terrain.Editor/Rendering/River/RiverResourceLoader.cs");

        AssertContains(loader, "LoadRequiredLocalTexture", "RiverResourceLoader should have an explicit local file loading path for Bottom/Water textures");
        AssertContains(loader, "game/map/water", "RiverResourceLoader should report the local river texture directory in failures");
        AssertContains(loader, "File.OpenRead(path)", "RiverResourceLoader should load Bottom/Water textures directly from files");
        AssertContains(loader, "Texture.Load(", "RiverResourceLoader should create GPU textures directly from DDS streams");
        AssertContains(loader, "ReflectionSpecularUrl = \"River/Environment/reflection-specular\"", "RiverResourceLoader should keep the environment reflection cubemap as a Stride content asset");
        AssertNotContains(loader, "River/Water/", "RiverResourceLoader should not keep Stride content URLs for water textures");
        AssertNotContains(loader, "River/Bottom/", "RiverResourceLoader should not keep Stride content URLs for bottom textures");
        AssertNotContains(loader, "Ensure the .sdtex is included as a RootAsset in Terrain.Editor.sdpkg.\", exception);", "Local Bottom/Water load failures should not mention Stride RootAssets");
    }
```

- [ ] **Step 4: Run targeted tests**

Run:

```powershell
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RiverShaderTextTests"
```

Expected: tests pass.

---

### Task 5: Update Resource README And Current Docs

**Files:**
- Modify: `Terrain.Editor/Assets/River/README.md`
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`

- [ ] **Step 1: Update README**

Replace the opening text in `Terrain.Editor/Assets/River/README.md` with:

```markdown
# River Rendering Resources

`Environment/` remains a Stride content asset directory. River bottom and water DDS files now live under `game/map/water` and are loaded directly from the file system by `RiverResourceLoader`.
```

Replace the Bottom and Water tables to list `game/map/water/...` project files using underscore names.

- [ ] **Step 2: Update architecture docs**

In `docs/ARCHITECTURE_OVERVIEW.md`, replace the `RiverResourceLoader` bullet that says it loads `Assets/River/` `.sdtex` resources with:

```markdown
- `RiverResourceLoader` loads river bottom/water DDS files directly from `game/map/water` using underscore filenames, bypassing Stride `.sdtex`/RootAsset management for those textures; `River/Environment/reflection-specular` remains a Stride content asset.
```

Also update any current-status references to `Terrain.Editor/Assets/River/Water/shadow-color.sdtex` or `Terrain.Editor/Assets/River/Water/water-color.sdtex` to `game/map/water/shadow_color.dds` and `game/map/water/water_color.dds`.

- [ ] **Step 3: Update feature docs**

In `docs/CURRENT_FEATURES.md`, update the river rendering row key files from:

```markdown
`Terrain.Editor/Assets/River/`
```

to include:

```markdown
`game/map/water/`, `Terrain.Editor/Assets/River/Environment/`
```

Update the surface post-chain and water-color rows to refer to `game/map/water/shadow_color.dds` and `game/map/water/water_color.dds`.

---

### Task 6: Session Log And Verification

**Files:**
- Create: `docs/log/2026/06/20/2026-06-20-river-local-water-texture-files.md`

- [ ] **Step 1: Create session log**

Create the log with this structure:

```markdown
# 河流 Water/Bottom 纹理迁移到 game/map/water
**Date**: 2026-06-20
**Session**: River local water texture files
**Status**: Complete
**Priority**: Medium

---

## Session Goal

- 将 `Terrain.Editor/Assets/River/Water` 和 `Terrain.Editor/Assets/River/Bottom` 中的 DDS 迁移到 `game/map/water`。
- 使用下划线文件名。
- Bottom/Water 纹理不再通过 Stride `.sdtex`、RootAsset 或 `ContentManager` 加载。
- `River/Environment` 保持现有 Stride 内容资源路径。

## What We Did

### 1. 资源位置与命名

- 新增 `game/map/water`。
- 将 Bottom/Water 12 张 DDS 迁移到该目录，并把中划线文件名改为下划线文件名。
- 删除 Bottom/Water `.sdtex` 描述文件。

### 2. 加载链路

- `RiverResourceLoader` 改为对 Bottom/Water DDS 使用文件流和 `Texture.Load`。
- `reflection-specular` 继续使用 `ContentManager.Load<Texture>("River/Environment/reflection-specular")`。
- 缺失本地 DDS 时错误消息指向 `game/map/water` 的实际文件路径。

### 3. Stride 包配置

- 从 `Terrain.Editor.sdpkg` 移除 `River/Bottom/*` 和 `River/Water/*` RootAssets。
- 保留 `River/Environment/reflection-specular` RootAsset。

## Verification

Ran:

```powershell
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug --no-restore
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RiverShaderTextTests"
rg -n "River/(Bottom|Water)" Terrain.Editor/Terrain.Editor.sdpkg
git diff --check
```

## Notes

- `River/Environment` 未迁移。
- Shader 参数名与采样语义未改。
```

- [ ] **Step 2: Final verification**

Run:

```powershell
dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug --no-restore
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RiverShaderTextTests"
rg -n "River/(Bottom|Water)" Terrain.Editor/Terrain.Editor.sdpkg
rg -n "Terrain.Editor/Assets/River/(Bottom|Water)|River/Water/|River/Bottom/" Terrain.Editor/Rendering/River Terrain.Editor.Tests/RiverShaderTextTests.cs docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md
git diff --check
```

Expected:

- Build succeeds.
- Targeted tests pass.
- Package search has no matches.
- Current code/tests/current docs no longer describe Bottom/Water as Stride content URLs.
- `git diff --check` has no whitespace errors.

