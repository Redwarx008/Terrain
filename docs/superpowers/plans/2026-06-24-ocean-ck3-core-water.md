# Ocean CK3 Core Water Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the simplified Ocean shader with a CK3-style core water path that shares the same refraction seed payload as River.

**Architecture:** Extract the current River scene seed into a shared water refraction seed provider under `Terrain/Rendering/Water`, then make River and Ocean consume that same seed. Ocean keeps its full-map sea-level quad, but its shader moves to CK3 core water semantics: refraction, see-through, water fade, flow normals, ambient wave normals, foam, fresnel, and reflection, excluding province/border/FOW/flatmap overlays.

**Tech Stack:** C#/.NET 10, Stride render features and SDSL, RenderDoc MCP/manual capture validation, existing `Terrain.Editor.Tests` text/runtime tests, Stride shader asset workflow.

---

## Scope Check

This is one focused vertical slice: shared water refraction seed plus Ocean core water. It deliberately avoids strategy-layer CK3 map overlays and coastline mesh generation.

Do not implement:

- province color or province indirection
- border distance field
- fog of war
- flat map texture
- coastline mesh clipping

Do implement:

- one shared refraction seed provider used by both River and Ocean
- River migration away from private seed generation
- Ocean shader refraction and CK3 core water path
- tests that prevent duplicate seed ownership and strategy-layer creep
- Stride shader generated-key and asset compile verification
- RenderDoc verification against `save.rdc` EID `1061`

---

## File Structure

- Create `Terrain/Rendering/Water/WaterRefractionSeedResources.cs`
  - Owns shared seed render target dimensions and texture lifetime.
- Create `Terrain/Rendering/Water/WaterRefractionSeedProvider.cs`
  - Shared per-graphics-device provider with frame/view cache and reference counting.
  - Generates seed once per scene color/depth/render view/time key where possible.
- Create `Terrain/Effects/Water/WaterSceneSeed.sdsl`
  - Renamed/generalized copy of `RiverSceneSeed.sdsl`.
- Generate `Terrain/Effects/Water/WaterSceneSeed.sdsl.cs`
  - Generated shader key file after Stride asset workflow.
- Modify `Terrain/Terrain.csproj`
  - Compile generated `WaterSceneSeed.sdsl.cs`.
- Modify `Terrain/Rendering/River/RiverRenderFeature.cs`
  - Replace private `ImageEffectShader("RiverSceneSeed")` and seed logic with `WaterRefractionSeedProvider`.
  - Continue copying shared seed into river-specific `BottomColor` before bottom pass.
- Modify `Terrain/Rendering/River/RiverRenderResources.cs`
  - Remove `SceneSeedColor`; keep `BottomColor` and `BottomDepth`.
- Modify `Terrain/Effects/River/RiverSceneSeed.sdsl`
  - Delete after River no longer uses it, or leave as deprecated only if removal breaks generated asset references. Preferred outcome: remove it and generated `.sdsl.cs`.
- Modify `Terrain/Effects/Ocean/OceanSurface.sdsl`
  - Replace simplified Ocean shader with CK3 core water path.
- Modify `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
  - Request shared seed and bind it to OceanSurface.
  - Bind frame parameters and CK3 core water parameters.
- Modify `Terrain.Editor.Tests/OceanShaderTextTests.cs`
  - Add tests for refraction, no `0.86` alpha, no strategy tokens, shared seed binding.
- Modify `Terrain.Editor.Tests/RiverShaderTextTests.cs`
  - Update tests to assert River uses shared seed provider and no longer owns scene seed.
- Modify `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`
  - Update resource expectations after `SceneSeedColor` is removed.
- Modify `Terrain.Editor.Tests/Program.cs`
  - Only if adding a new test file.
- Update `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, and session log after implementation and verification.

---

## Task 1: Add Shared Water Refraction Seed Provider

**Files:**
- Create: `Terrain/Rendering/Water/WaterRefractionSeedResources.cs`
- Create: `Terrain/Rendering/Water/WaterRefractionSeedProvider.cs`
- Create: `Terrain/Effects/Water/WaterSceneSeed.sdsl`
- Modify: `Terrain/Terrain.csproj`
- Test: `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- [ ] **Step 1: Add failing text tests for shared seed ownership**

In `Terrain.Editor.Tests/RiverShaderTextTests.cs`, add a new `RunAll` registration near the existing seed tests:

```csharp
TestHarness.Run("water refraction seed is shared outside river render feature", WaterRefractionSeedIsSharedOutsideRiverRenderFeature);
```

Add this method near the existing `SceneSeedUsesPresenterDepth` test:

```csharp
private static void WaterRefractionSeedIsSharedOutsideRiverRenderFeature()
{
    string resources = ReadRepositoryText("Terrain/Rendering/Water/WaterRefractionSeedResources.cs");
    string provider = ReadRepositoryText("Terrain/Rendering/Water/WaterRefractionSeedProvider.cs");
    string shader = ReadRepositoryText("Terrain/Effects/Water/WaterSceneSeed.sdsl");
    string riverFeature = ReadRepositoryText("Terrain/Rendering/River/RiverRenderFeature.cs");
    string oceanFeature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

    AssertContains(resources, "public sealed class WaterRefractionSeedResources", "Water refraction seed resources should live outside river");
    AssertContains(provider, "public sealed class WaterRefractionSeedProvider", "Water refraction seed provider should be shared outside river");
    AssertContains(provider, "GetOrCreateSeed(", "WaterRefractionSeedProvider should expose a shared seed acquisition method");
    AssertContains(provider, "ReferenceEquals(entry.SceneColor, sceneColor)", "WaterRefractionSeedProvider should key the cache by scene color identity");
    AssertContains(provider, "entry.FrameKey == frameKey", "WaterRefractionSeedProvider should avoid duplicate seed generation within a frame/view key");
    AssertContains(shader, "shader WaterSceneSeed : ImageEffectShader, DepthBase, Transformation, RiverCommon", "WaterSceneSeed should preserve river refraction payload helpers");
    AssertContains(shader, "RiverCompressWorldSpace(positionWS.xyz, Eye.xyz)", "WaterSceneSeed should preserve camera-distance alpha payload semantics");
    AssertContains(riverFeature, "WaterRefractionSeedProvider", "RiverRenderFeature should request the shared water refraction seed");
    AssertContains(oceanFeature, "WaterRefractionSeedProvider", "OceanRenderFeature should request the shared water refraction seed");
    AssertNotContains(riverFeature, "new ImageEffectShader(\"RiverSceneSeed\"", "RiverRenderFeature should not own the old private seed shader");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: failure because `WaterRefractionSeedResources.cs`, `WaterRefractionSeedProvider.cs`, and `WaterSceneSeed.sdsl` do not exist yet.

- [ ] **Step 3: Create shared seed resources**

Create `Terrain/Rendering/Water/WaterRefractionSeedResources.cs`:

```csharp
#nullable enable

using System;
using Stride.Graphics;

namespace Terrain.Rendering.Water;

public sealed class WaterRefractionSeedResources : IDisposable
{
    public const PixelFormat RefractionFormat = PixelFormat.R16G16B16A16_Float;

    public Texture? SeedColor { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public void EnsureResources(GraphicsDevice graphicsDevice, int viewWidth, int viewHeight)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        var (width, height) = ComputeHalfResolutionSize(viewWidth, viewHeight);
        if (SeedColor != null && Width == width && Height == height)
            return;

        ReleaseResources();

        Width = width;
        Height = height;
        SeedColor = Texture.New2D(
            graphicsDevice,
            width,
            height,
            RefractionFormat,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
    }

    public void ReleaseResources()
    {
        SeedColor?.Dispose();
        SeedColor = null;
        Width = 0;
        Height = 0;
    }

    public void Dispose()
    {
        ReleaseResources();
    }

    public static (int Width, int Height) ComputeHalfResolutionSize(int viewWidth, int viewHeight)
    {
        int width = Math.Max(1, (viewWidth + 1) / 2);
        int height = Math.Max(1, (viewHeight + 1) / 2);
        return (width, height);
    }
}
```

- [ ] **Step 4: Create shared seed provider**

Create `Terrain/Rendering/Water/WaterRefractionSeedProvider.cs`:

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Images;

namespace Terrain.Rendering.Water;

public sealed class WaterRefractionSeedProvider : IDisposable
{
    private static readonly Dictionary<GraphicsDevice, SharedEntry> SharedEntries = [];

    private readonly GraphicsDevice graphicsDevice;
    private bool disposed;

    public WaterRefractionSeedProvider(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        graphicsDevice = context.GraphicsDevice;
        lock (SharedEntries)
        {
            if (!SharedEntries.TryGetValue(graphicsDevice, out SharedEntry? entry))
            {
                entry = new SharedEntry(context);
                SharedEntries.Add(graphicsDevice, entry);
            }

            entry.ReferenceCount++;
        }
    }

    public WaterRefractionSeedResult GetOrCreateSeed(
        RenderDrawContext context,
        RenderView renderView,
        Texture sceneColor,
        float refractionMaxCameraHeight)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(renderView);
        ArgumentNullException.ThrowIfNull(sceneColor);

        SharedEntry entry = GetEntry();
        long frameKey = context.RenderContext.Time.FrameCount;
        Texture sceneDepthSource = GetPresenterSceneDepthSource(context.GraphicsDevice, sceneColor);

        if (entry.Matches(sceneColor, sceneDepthSource, frameKey, refractionMaxCameraHeight))
            return entry.Result;

        entry.Resources.EnsureResources(context.GraphicsDevice, sceneColor.ViewWidth, sceneColor.ViewHeight);
        if (entry.Resources.SeedColor == null)
            throw new InvalidOperationException("Water refraction seed target was not allocated.");

        var sceneDepth = context.Resolver.ResolveDepthStencil(sceneDepthSource);
        if (sceneDepth == null)
            throw new InvalidOperationException("Water refraction seed requires a depth buffer that can be resolved as a shader resource.");

        try
        {
            ImageEffectShader seedEffect = entry.SeedEffect;
            seedEffect.Parameters.Set(DepthBaseKeys.DepthStencil, sceneDepth);
            seedEffect.Parameters.Set(CameraKeys.ViewSize, new Vector2(sceneColor.Width, sceneColor.Height));
            seedEffect.Parameters.Set(CameraKeys.ZProjection, CameraKeys.ZProjectionACalculate(renderView.NearClipPlane, renderView.FarClipPlane));
            seedEffect.Parameters.Set(CameraKeys.NearClipPlane, renderView.NearClipPlane);
            seedEffect.Parameters.Set(CameraKeys.FarClipPlane, renderView.FarClipPlane);

            Matrix viewInverse = Matrix.Invert(renderView.View);
            seedEffect.Parameters.Set(TransformationKeys.ViewInverse, ref viewInverse);
            seedEffect.Parameters.Set(TransformationKeys.Eye, new Vector4(viewInverse.TranslationVector, 1.0f));
            Matrix.Invert(ref renderView.Projection, out Matrix projectionInverse);
            seedEffect.Parameters.Set(TransformationKeys.ProjectionInverse, ref projectionInverse);
            seedEffect.Parameters.Set(RiverCommonKeys._RefractionMaxCameraHeight, refractionMaxCameraHeight);
            seedEffect.Parameters.Set(TexturingKeys.Sampler, context.GraphicsDevice.SamplerStates.LinearClamp);
            seedEffect.SetInput(0, sceneColor);
            seedEffect.SetOutput(entry.Resources.SeedColor);
            seedEffect.Draw(context, "Water refraction scene seed");
        }
        finally
        {
            context.Resolver.ReleaseDepthStenctilAsShaderResource(sceneDepth);
        }

        entry.SceneColor = sceneColor;
        entry.SceneDepth = sceneDepthSource;
        entry.FrameKey = frameKey;
        entry.RefractionMaxCameraHeight = refractionMaxCameraHeight;
        entry.Result = new WaterRefractionSeedResult(entry.Resources.SeedColor, entry.Resources.Width, entry.Resources.Height);
        return entry.Result;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        lock (SharedEntries)
        {
            if (!SharedEntries.TryGetValue(graphicsDevice, out SharedEntry? entry))
                return;

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0)
                return;

            entry.Dispose();
            SharedEntries.Remove(graphicsDevice);
        }
    }

    private SharedEntry GetEntry()
    {
        lock (SharedEntries)
        {
            if (SharedEntries.TryGetValue(graphicsDevice, out SharedEntry? entry))
                return entry;
        }

        throw new ObjectDisposedException(nameof(WaterRefractionSeedProvider));
    }

    private static Texture GetPresenterSceneDepthSource(GraphicsDevice graphicsDevice, Texture sceneColor)
    {
        var presenter = graphicsDevice.Presenter;
        if (presenter == null)
            throw new InvalidOperationException("Water refraction seed requires GraphicsDevice.Presenter.");

        Texture? sceneDepth = presenter.DepthStencilBuffer;
        if (sceneDepth == null)
            throw new InvalidOperationException("Water refraction seed requires GraphicsDevice.Presenter.DepthStencilBuffer.");

        if (sceneDepth.ViewWidth != sceneColor.ViewWidth || sceneDepth.ViewHeight != sceneColor.ViewHeight)
            throw new InvalidOperationException($"Water refraction seed depth size {sceneDepth.ViewWidth}x{sceneDepth.ViewHeight} must match scene color size {sceneColor.ViewWidth}x{sceneColor.ViewHeight}.");

        return sceneDepth;
    }

    private sealed class SharedEntry : IDisposable
    {
        public readonly WaterRefractionSeedResources Resources = new();
        public readonly ImageEffectShader SeedEffect;
        public int ReferenceCount;
        public Texture? SceneColor;
        public Texture? SceneDepth;
        public long FrameKey = -1;
        public float RefractionMaxCameraHeight;
        public WaterRefractionSeedResult Result;

        public SharedEntry(RenderContext context)
        {
            SeedEffect = new ImageEffectShader("WaterSceneSeed", delaySetRenderTargets: true);
            SeedEffect.Initialize(context);
        }

        public bool Matches(Texture sceneColor, Texture sceneDepth, long frameKey, float refractionMaxCameraHeight)
        {
            return ReferenceEquals(SceneColor, sceneColor)
                && ReferenceEquals(SceneDepth, sceneDepth)
                && FrameKey == frameKey
                && RefractionMaxCameraHeight == refractionMaxCameraHeight
                && Result.Texture != null;
        }

        public void Dispose()
        {
            SeedEffect.Dispose();
            Resources.Dispose();
        }
    }
}

public readonly record struct WaterRefractionSeedResult(Texture Texture, int Width, int Height);
```

If `RenderTime.FrameCount` is unavailable in this Stride build, replace the `frameKey` line with this deterministic key and update the test assertion accordingly:

```csharp
long frameKey = context.RenderContext.Time.Total.Ticks;
```

- [ ] **Step 5: Create shared seed shader**

Create `Terrain/Effects/Water/WaterSceneSeed.sdsl`:

```c
namespace Terrain
{
    shader WaterSceneSeed : ImageEffectShader, DepthBase, Transformation, RiverCommon
    {
        stage float _SceneSeedExposure = 1.0f;
        stage float _SceneSeedColorScale = 1.5f;

        float3 CompressSceneSeedColor(float3 color)
        {
            float3 hdrColor = max(color * _SceneSeedExposure, float3(0.0f, 0.0f, 0.0f));
            return (hdrColor / (1.0f + hdrColor)) * _SceneSeedColorScale;
        }

        float ComputeSceneDistanceFromUV(float2 uv)
        {
            float zProjDepth = GetZProjDepthFromUV(uv);
            float4 positionClipSpace = float4((1.0f - uv * 2.0f) * float2(-1.0f, 1.0f), zProjDepth, 1.0f);
            float4 positionVS = mul(positionClipSpace, ProjectionInverse);
            positionVS.xyzw /= positionVS.w;
            float4 positionWS = mul(positionVS, ViewInverse);
            return RiverCompressWorldSpace(positionWS.xyz, Eye.xyz);
        }

        stage override float4 Shading()
        {
            float2 uv = streams.TexCoord;
            float3 seedColor = CompressSceneSeedColor(Texture0.Sample(LinearSampler, uv).rgb);
            float sceneDistance = ComputeSceneDistanceFromUV(uv);
            return float4(seedColor, sceneDistance);
        }
    };
}
```

- [ ] **Step 6: Register generated key file intent**

Do not manually write `WaterSceneSeed.sdsl.cs`. Add this expected compile entry to `Terrain/Terrain.csproj` only after the generated file exists in Task 5:

```xml
<Compile Update="Effects\Water\WaterSceneSeed.sdsl.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>WaterSceneSeed.sdsl</DependentUpon>
</Compile>
```

- [ ] **Step 7: Run tests to verify remaining failures**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: shared provider tests still fail because River and Ocean do not use it yet. Compile may fail if `FrameCount` is not available; apply the `Total.Ticks` replacement from Step 4 if needed.

---

## Task 2: Migrate River to Shared Refraction Seed

**Files:**
- Modify: `Terrain/Rendering/River/RiverRenderFeature.cs`
- Modify: `Terrain/Rendering/River/RiverRenderResources.cs`
- Modify: `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- Modify: `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs`

- [ ] **Step 1: Update River seed tests**

In `RiverShaderTextTests.SceneSeedUsesPresenterDepth`, replace expectations that require `RiverRenderFeature` to own `RiverSceneSeed` with shared provider expectations:

```csharp
AssertContains(feature, "waterRefractionSeedProvider = new WaterRefractionSeedProvider(Context);", "RiverRenderFeature should initialize the shared water refraction seed provider");
AssertContains(feature, "waterRefractionSeedProvider.GetOrCreateSeed(context, renderView, sceneColor, refractionMaxCameraHeight)", "RiverRenderFeature should acquire the shared water refraction seed");
AssertContains(feature, "CopySceneSeedToBottomColor(commandList, seed.Texture);", "RiverRenderFeature should copy shared seed into the river bottom working target");
AssertNotContains(feature, "new ImageEffectShader(\"RiverSceneSeed\"", "RiverRenderFeature should not initialize a river-private seed shader");
AssertNotContains(feature, "context.Resolver.ResolveDepthStencil(sceneDepthSource)", "RiverRenderFeature should delegate depth resolve to shared seed provider");
```

In `RiverShaderTextTests.SurfaceRefractionUsesDedicatedSeedAndBottomBuffers`, update the resource assertions:

```csharp
AssertNotContains(resources, "public Texture? SceneSeedColor { get; private set; }", "RiverRenderResources should not own the shared scene seed buffer");
AssertContains(resources, "public Texture? BottomColor { get; private set; }", "RiverRenderResources should keep the mutable river bottom/refraction buffer");
AssertContains(feature, "CopySceneSeedToBottomColor(commandList, seed.Texture);", "RiverRenderFeature should copy shared seed into BottomColor");
AssertContains(feature, "commandList.CopyRegion(sharedSeed, 0, null, renderResources.BottomColor, 0);", "RiverRenderFeature should copy the shared seed texture into the working bottom target");
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: River text tests fail because River still owns `RiverSceneSeed` and `SceneSeedColor`.

- [ ] **Step 3: Remove River private seed resource**

In `Terrain/Rendering/River/RiverRenderResources.cs`, remove:

```csharp
public Texture? SceneSeedColor { get; private set; }
```

Remove the `SceneSeedColor` checks/allocation/disposal. Keep:

```csharp
public Texture? BottomColor { get; private set; }
public Texture? BottomDepth { get; private set; }
```

`EnsureResources` should allocate only `BottomColor` and `BottomDepth`:

```csharp
if (BottomColor != null
    && BottomDepth != null
    && Width == width
    && Height == height)
{
    return;
}
```

- [ ] **Step 4: Replace River private seed effect with shared provider**

In `Terrain/Rendering/River/RiverRenderFeature.cs`, replace:

```csharp
private ImageEffectShader? sceneSeedEffect;
```

with:

```csharp
private WaterRefractionSeedProvider? waterRefractionSeedProvider;
```

In `InitializeCore`, replace:

```csharp
sceneSeedEffect = new ImageEffectShader("RiverSceneSeed", delaySetRenderTargets: true);
sceneSeedEffect.Initialize(Context);
```

with:

```csharp
waterRefractionSeedProvider = new WaterRefractionSeedProvider(Context);
```

In `Destroy`, replace `sceneSeedEffect?.Dispose(); sceneSeedEffect = null;` with:

```csharp
waterRefractionSeedProvider?.Dispose();
waterRefractionSeedProvider = null;
```

In `Draw`, update the resource check to:

```csharp
if (renderResources.BottomColor == null || renderResources.BottomDepth == null || waterRefractionSeedProvider == null)
{
    return;
}
```

Inside the bottom pass block, replace:

```csharp
SeedSceneColorFromScene(context, renderView, sceneColor, refractionMaxCameraHeight);
CopySceneSeedToBottomColor(commandList);
```

with:

```csharp
WaterRefractionSeedResult seed = waterRefractionSeedProvider.GetOrCreateSeed(context, renderView, sceneColor, refractionMaxCameraHeight);
CopySceneSeedToBottomColor(commandList, seed.Texture);
```

Replace `CopySceneSeedToBottomColor` with:

```csharp
private void CopySceneSeedToBottomColor(CommandList commandList, Texture sharedSeed)
{
    if (renderResources.BottomColor == null)
        return;

    commandList.CopyRegion(sharedSeed, 0, null, renderResources.BottomColor, 0);
}
```

Delete `SeedSceneColorFromScene` and `GetPresenterSceneDepthSource` from `RiverRenderFeature.cs`; their logic now lives in `WaterRefractionSeedProvider`.

- [ ] **Step 5: Keep River surface binding unchanged**

Keep this River surface binding:

```csharp
surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionTexture, renderResources.BottomColor);
surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
surfaceEffect.Parameters.Set(RiverSurfaceKeys._RefractionTextureSize, new Vector2(renderResources.Width, renderResources.Height));
```

Reason: River surface should sample river-specific `BottomColor`, because bottom pass modifies the shared seed with river-bed color before surface composition.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: River seed tests pass or expose compile issues in the shared provider. Fix only compile/signature mismatches introduced in this task.

---

## Task 3: Bind Shared Seed Into Ocean Render Feature

**Files:**
- Modify: `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
- Modify: `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- [ ] **Step 1: Add failing Ocean render-feature tests**

In `OceanShaderTextTests.RunAll`, add:

```csharp
TestHarness.Run("ocean render feature binds shared refraction seed", OceanRenderFeatureBindsSharedRefractionSeed);
```

Add:

```csharp
private static void OceanRenderFeatureBindsSharedRefractionSeed()
{
    string feature = ReadRepositoryText("Terrain/Rendering/Ocean/OceanRenderFeature.cs");

    AssertContains(feature, "private WaterRefractionSeedProvider? waterRefractionSeedProvider;", "OceanRenderFeature should own a handle to the shared water seed provider");
    AssertContains(feature, "waterRefractionSeedProvider = new WaterRefractionSeedProvider(Context);", "OceanRenderFeature should initialize the shared seed provider");
    AssertContains(feature, "waterRefractionSeedProvider.GetOrCreateSeed(context, renderView, sceneColor, oceanObject.SeaLevel)", "OceanRenderFeature should request the shared water refraction seed");
    AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionTexture, seed.Texture);", "OceanRenderFeature should bind the shared seed texture");
    AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._RefractionTextureSize, new Vector2(seed.Width, seed.Height));", "OceanRenderFeature should bind shared seed dimensions");
    AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._ViewSize, renderView.ViewSize);", "OceanRenderFeature should bind view size for screen UVs");
    AssertContains(feature, "oceanEffect.Parameters.Set(OceanSurfaceKeys._ViewMatrix, renderView.View);", "OceanRenderFeature should bind view matrix for refraction offset");
    AssertNotContains(feature, "new ImageEffectShader(\"Ocean", "OceanRenderFeature should not create an ocean-private seed effect");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: Ocean test fails because Ocean does not bind a refraction seed.

- [ ] **Step 3: Add provider field and lifecycle**

In `OceanRenderFeature.cs`, add:

```csharp
private WaterRefractionSeedProvider? waterRefractionSeedProvider;
```

In `InitializeCore`, after `sceneLightingBinder` initialization:

```csharp
waterRefractionSeedProvider = new WaterRefractionSeedProvider(Context);
```

In `Destroy`, before `base.Destroy()`:

```csharp
waterRefractionSeedProvider?.Dispose();
waterRefractionSeedProvider = null;
```

- [ ] **Step 4: Bind frame parameters and shared seed**

In `Draw`, after `sceneLightingBinder?.Bind(...)`, get scene color:

```csharp
Texture? sceneColor = commandList.RenderTargetCount > 0 ? commandList.RenderTargets[0] : null;
if (sceneColor == null || waterRefractionSeedProvider == null)
    return;
```

Move shared seed acquisition into the per-object loop after validating `oceanObject`, because `SeaLevel` is the refraction clamp height for Ocean:

```csharp
WaterRefractionSeedResult seed = waterRefractionSeedProvider.GetOrCreateSeed(context, renderView, sceneColor, oceanObject.SeaLevel);
oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionTexture, seed.Texture);
oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
oceanEffect.Parameters.Set(OceanSurfaceKeys._RefractionTextureSize, new Vector2(seed.Width, seed.Height));
oceanEffect.Parameters.Set(OceanSurfaceKeys._ViewSize, renderView.ViewSize);
oceanEffect.Parameters.Set(OceanSurfaceKeys._ViewMatrix, renderView.View);
```

Keep existing `World`, `WorldView`, and `WorldViewProjection` binding.

- [ ] **Step 5: Bind static refraction sampler once**

If generated keys are available after Task 5, move sampler binding into `BindStaticResources`:

```csharp
oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
```

If the key is unavailable before shader generation, keep the binding in Task 5.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: compile may fail until Ocean shader declares `RefractionTexture`, `_RefractionTextureSize`, `_ViewSize`, and `_ViewMatrix`. Proceed to Task 4 if failures are only missing generated Ocean shader keys.

---

## Task 4: Replace Ocean Shader With CK3 Core Water Path

**Files:**
- Modify: `Terrain/Effects/Ocean/OceanSurface.sdsl`
- Modify: `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- [ ] **Step 1: Add failing shader semantic tests**

In `OceanShaderTextTests.RunAll`, add:

```csharp
TestHarness.Run("ocean shader uses ck3 core water semantics", OceanShaderUsesCk3CoreWaterSemantics);
```

Add:

```csharp
private static void OceanShaderUsesCk3CoreWaterSemantics()
{
    string shader = ReadRepositoryText("Terrain/Effects/Ocean/OceanSurface.sdsl");

    AssertContains(shader, "stage Texture2D<float4> RefractionTexture;", "OceanSurface should declare the shared refraction seed texture");
    AssertContains(shader, "stage SamplerState RefractionSampler;", "OceanSurface should declare a refraction sampler");
    AssertContains(shader, "float SampleRefractionPayload(float2 screenUv)", "OceanSurface should read refraction alpha payload separately");
    AssertContains(shader, "RefractionTexture.Load(int3(ComputeRefractionPayloadCoord(screenUv), 0)).a", "OceanSurface should use unfiltered payload reads");
    AssertContains(shader, "float3 CalcRefraction(", "OceanSurface should have a dedicated refraction function");
    AssertContains(shader, "_WaterSeeThroughDensity", "OceanSurface should expose see-through attenuation");
    AssertContains(shader, "_WaterFadeShoreMaskDepth", "OceanSurface should expose water fade shore depth");
    AssertContains(shader, "_WaterFoamShoreMaskDepth", "OceanSurface should expose foam shore depth");
    AssertContains(shader, "_WaterFresnelBias", "OceanSurface should expose fresnel bias");
    AssertContains(shader, "CalcFoamFactor(", "OceanSurface should compute CK3-style foam");
    AssertContains(shader, "CalcOceanCoreWater(", "OceanSurface should route PSMain through the core water function");
    AssertContains(shader, "streams.ColorTarget = float4(waterColor, 1.0f);", "OceanSurface should output opaque ocean color for normal ocean pixels");
    AssertNotContains(shader, "float4(litColor, 0.86f)", "OceanSurface should not keep the old translucent cyan overlay alpha");
    AssertNotContains(shader, "RiverStrideComputeLighting(", "OceanSurface should not rely on the old simple diffuse lighting-only output path");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: Ocean shader semantic tests fail because current shader lacks refraction and core water functions.

- [ ] **Step 3: Replace OceanSurface parameters**

In `Terrain/Effects/Ocean/OceanSurface.sdsl`, keep the shader declaration:

```c
shader OceanSurface : ShaderBase, TransformationWAndVP, OceanVertexStreams, RiverStrideLighting
```

Replace the current simple material fields with CK3 core water fields. Keep `ShallowColor`, `DeepColor`, `_OceanRoughness`, and `_WaveScale` for compatibility with `OceanMaterialSettings`, but stop using them as the primary color path:

```c
stage float4 ShallowColor = float4(0.08f, 0.32f, 0.42f, 1.0f);
stage float4 DeepColor = float4(0.01f, 0.08f, 0.16f, 1.0f);
stage float _OceanRoughness = 0.08f;
stage float _WaveScale = 1.0f;
stage float _WaterHeight = 3.0f;
stage float2 _MapWorldSize = float2(4096.0f, 4096.0f);
stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);
stage float _GlobalTime = 0.0f;
stage float2 _ViewSize = float2(1.0f, 1.0f);
stage float2 _RefractionTextureSize = float2(1.0f, 1.0f);
stage float4x4 _ViewMatrix;
stage float _WaterDiffuseMultiplier = 1.0f;
stage float3 _WaterColorMapTint = float3(0.0f, 0.0f, 0.0f);
stage float _WaterColorMapTintFactor = 0.010695934f;
stage float _WaterRefractionScale = 500.0f;
stage float _WaterRefractionShoreMaskDepth = 3.0f;
stage float _WaterRefractionShoreMaskSharpness = 1.0f;
stage float _WaterRefractionFade = 1.0f;
stage float _WaterFadeShoreMaskDepth = 0.5f;
stage float _WaterFadeShoreMaskSharpness = 5.0f;
stage float _WaterSeeThroughDensity = 0.8f;
stage float _WaterSeeThroughShoreMaskDepth = 20.0f;
stage float _WaterSeeThroughShoreMaskSharpness = 1.0f;
stage float _WaterFresnelBias = 0.01f;
stage float _WaterFresnelPow = 4.3f;
stage float _WaterReflectionNormalFlatten = 3.0f;
stage float _WaterCubemapIntensity = 0.0f;
stage float _WaterFoamScale = 0.3f;
stage float _WaterFoamDistortFactor = 0.1f;
stage float _WaterFoamShoreMaskDepth = 0.0f;
stage float _WaterFoamShoreMaskSharpness = 1.0f;
stage float _WaterFoamNoiseScale = 0.005f;
stage float _WaterFoamNoiseSpeed = 0.025f;
stage float _WaterFoamStrength = 0.6f;
stage float2 _WaterWave1Scale = float2(10.0f, 10.0f);
stage float _WaterWave1Rotation = -0.35f;
stage float _WaterWave1Speed = 0.01f;
stage float2 _WaterWave2Scale = float2(2.0f, 1.0f);
stage float _WaterWave2Rotation = -1.6f;
stage float _WaterWave2Speed = 0.016f;
stage float2 _WaterWave3Scale = float2(0.2f, 0.1f);
stage float _WaterWave3Rotation = 1.725075f;
stage float _WaterWave3Speed = 0.005f;
stage float _WaterWave1NormalFlatten = 1.5f;
stage float _WaterWave2NormalFlatten = 1.5f;
stage float _WaterWave3NormalFlatten = 1.5f;
stage float _WaterFlowNormalFlatten = 1.5f;
stage float _WaterFlowMapSize = 1024.0f;
stage float _WaterFlowNormalScale = 0.4f;
stage float _WaterFlowTime = 0.0f;
stage float4 WaterColorShallow = float4(0.0055146287f, 0.0078107193f, 0.0120865023f, 1.0f);
stage float4 WaterColorDeep = float4(0.0001385075f, 0.0001974951f, 0.0002262951f, 1.0f);
```

Declare textures/samplers:

```c
stage Texture2D<float4> RefractionTexture;
stage SamplerState RefractionSampler;
stage Texture2D<float4> WaterColorTexture;
stage Texture2D<float4> AmbientNormalTexture;
stage Texture2D<float4> FlowMapTexture;
stage Texture2D<float4> FlowNormalTexture;
stage Texture2D<float4> FoamTexture;
stage Texture2D<float4> FoamRampTexture;
stage Texture2D<float4> FoamMapTexture;
stage Texture2D<float4> FoamNoiseTexture;
stage SamplerState OceanTextureSampler;
```

- [ ] **Step 4: Add refraction helpers**

Copy the refraction helper structure from `RiverSurface.sdsl`, adjusted to Ocean names:

```c
float2 ComputeScreenUv(float4 shadingPosition)
{
    return saturate(shadingPosition.xy / max(_ViewSize, float2(1.0f, 1.0f)));
}

int2 ComputeRefractionPayloadCoord(float2 screenUv)
{
    float2 textureSize = max(_RefractionTextureSize, float2(1.0f, 1.0f));
    float2 coord = clamp(floor(saturate(screenUv) * textureSize), float2(0.0f, 0.0f), textureSize - float2(1.0f, 1.0f));
    return int2(coord);
}

float SampleRefractionPayload(float2 screenUv)
{
    return RefractionTexture.Load(int3(ComputeRefractionPayloadCoord(screenUv), 0)).a;
}

float2 ComputeMapWorldUv(float3 worldPosition)
{
    float2 uv = worldPosition.xz / max(_MapWorldSize, float2(1.0f, 1.0f));
    uv.y = 1.0f - uv.y;
    return uv;
}

float3 DecodeRefractionWorldPosition(float3 surfaceWorldPosition, float compressedDistance)
{
    return RiverDecompressWorldSpace(surfaceWorldPosition, compressedDistance, _CameraWorldPosition);
}

float ComputeWaterFade(float depth)
{
    return 1.0f - saturate((_WaterFadeShoreMaskDepth - depth) * _WaterFadeShoreMaskSharpness);
}

float ComputeRefractionShoreMask(float depth)
{
    float sharpness = max(_WaterRefractionShoreMaskSharpness, 0.0001f);
    return 1.0f - saturate((_WaterRefractionShoreMaskDepth - depth) * sharpness);
}

float2 ComputeRefractionOffset(float3 normal, float shoreMask)
{
    float2 viewNormal = mul(float4(normal.x, 0.0f, normal.z, 0.0f), _ViewMatrix).xy;
    float2 refractionOffset = viewNormal * float2(-1.0f / 1920.0f, 1.0f / 1080.0f);
    return refractionOffset * max(_WaterRefractionScale, 0.0f) * shoreMask * saturate(_WaterRefractionFade);
}
```

- [ ] **Step 5: Add water normal and foam helpers**

Port these helper names from `RiverSurface.sdsl` into `OceanSurface.sdsl`:

```c
float3 DecodeWaterNormal(float4 packedNormal)
{
    return packedNormal.xyz - 0.5f;
}

float3 SampleNormalMapTexture(Texture2D<float4> textureSource, float2 uv, float2 scale, float rotation, float offset, float normalFlatten)
{
    float2 rotate = float2(cos(rotation), sin(rotation));
    float2 uvCoord = float2(uv.x * rotate.x - uv.y * rotate.y, uv.x * rotate.y + uv.y * rotate.x);
    uvCoord *= scale;
    uvCoord.x += offset;

    float3 normal = DecodeWaterNormal(textureSource.Sample(OceanTextureSampler, uvCoord)).xzy;
    float2 invRotate = float2(cos(-rotation), sin(-rotation));
    normal.xz = float2(normal.x * invRotate.x - normal.z * invRotate.y, normal.x * invRotate.y + normal.z * invRotate.x);
    normal.z *= -1.0f;
    normal.y *= normalFlatten;
    return normalize(normal);
}

float CalcFoamFactor(float2 uv01, float2 worldSpacePosXZ, float depth, float flowFoamMask, float3 flowNormal)
{
    float2 noiseUv = worldSpacePosXZ * _WaterFoamNoiseScale;
    float foamNoise1 = FoamNoiseTexture.Sample(OceanTextureSampler, noiseUv + float2(1.0f, 1.0f) * _GlobalTime * _WaterFoamNoiseSpeed).r * 0.75f;
    float foamNoise2 = (FoamNoiseTexture.Sample(OceanTextureSampler, noiseUv * 3.0f + float2(1.0f, -1.0f) * _GlobalTime * _WaterFoamNoiseSpeed).r - 0.5f) * 0.5f;
    float foamNoise3 = (FoamNoiseTexture.Sample(OceanTextureSampler, noiseUv * 5.0f + float2(-1.0f, 0.0f) * _GlobalTime * _WaterFoamNoiseSpeed).r - 0.5f) * 0.25f;
    float foamNoise = foamNoise1 + foamNoise2 + foamNoise3;

    float foamMap = 1.0f - FoamMapTexture.Sample(OceanTextureSampler, uv01).r;
    float foamBase = pow(foamMap, 2.0f) * 2.375f - 1.0f;
    float foamFactor = smoothstep(foamBase, foamBase + 2.0f, 1.0f - foamNoise);
    float foamShoreMask = 1.0f - saturate((_WaterFoamShoreMaskDepth - depth) * _WaterFoamShoreMaskSharpness);
    foamFactor *= _WaterFoamStrength * foamShoreMask;

    float3 foam = FoamTexture.Sample(OceanTextureSampler, worldSpacePosXZ * _WaterFoamScale + flowNormal.xz * _WaterFoamDistortFactor).rgb;
    float foamRampU = clamp(foamFactor * flowFoamMask, 0.5f / 256.0f, 1.0f - 0.5f / 256.0f);
    float3 foamRamp = FoamRampTexture.SampleLevel(OceanTextureSampler, float2(foamRampU, 0.5f), 0.0f).rgb;
    return saturate(dot(foam, foamRamp));
}
```

- [ ] **Step 6: Add simplified CK3 flow normal interpolation**

Add a first-pass flow normal function. This is less complete than CK3 EID `1061` but keeps the right structure and avoids the current one-sample flow:

```c
float4 SampleFlowNormalAtCell(float2 worldUv, float2 cellOffset, float2 derivativesX, float2 derivativesY)
{
    float2 flowMapSize = max(float2(_WaterFlowMapSize, _WaterFlowMapSize), float2(1.0f, 1.0f));
    float2 flowCell = (floor(worldUv * flowMapSize + cellOffset) + 0.5f) / flowMapSize;
    float4 flowMap = FlowMapTexture.SampleLevel(OceanTextureSampler, flowCell, 0.0f);
    float2 flowVector = flowMap.rg * 2.0f - 1.0f;
    float flowLength = max(length(flowVector), 0.000001f);
    float2 flowDir = flowVector / flowLength;
    float2 basisX = float2(-flowDir.y, flowDir.x);
    float2 basisY = -flowDir;
    float2 flowUv = float2(dot(basisX, worldUv * _WaterFlowNormalScale), dot(basisY, worldUv * _WaterFlowNormalScale));
    flowUv.y -= flowMap.b * _WaterFlowTime;
    float4 packedNormal = FlowNormalTexture.SampleGrad(OceanTextureSampler, flowUv, derivativesX, derivativesY);
    float3 normal = DecodeWaterNormal(packedNormal).xzy;
    normal.y *= _WaterFlowNormalFlatten;
    return float4(normalize(normal), flowMap.b * packedNormal.a);
}

float4 SampleOceanFlowNormal(float2 worldUv)
{
    float2 scaledUv = worldUv * _WaterFlowNormalScale;
    float2 derivativesX = ddx(scaledUv);
    float2 derivativesY = ddy(scaledUv);
    float2 local = frac(worldUv * max(_WaterFlowMapSize, 1.0f));
    float4 n00 = SampleFlowNormalAtCell(worldUv, float2(0.0f, 0.0f), derivativesX, derivativesY);
    float4 n10 = SampleFlowNormalAtCell(worldUv, float2(1.0f, 0.0f), derivativesX, derivativesY);
    float4 n01 = SampleFlowNormalAtCell(worldUv, float2(0.0f, 1.0f), derivativesX, derivativesY);
    float4 n11 = SampleFlowNormalAtCell(worldUv, float2(1.0f, 1.0f), derivativesX, derivativesY);
    float4 nx0 = lerp(n00, n10, local.x);
    float4 nx1 = lerp(n01, n11, local.x);
    float4 result = lerp(nx0, nx1, local.y);
    return float4(normalize(result.xyz), saturate(result.w));
}
```

This function is allowed to be refined by RenderDoc after first compile, but do not go back to one flow sample.

- [ ] **Step 7: Add refraction and core water composition**

Add:

```c
float3 ApplyTerrainUnderwaterSeeThrough(float refractionDepth, float3 refractionWorldPosition, float3 waterColorMap, float3 bottomColor)
{
    float3 toCameraDir = normalize(_CameraWorldPosition - refractionWorldPosition);
    float waterDistance = refractionDepth / max(toCameraDir.y, 0.0001f);
    float attenuation = saturate(exp(-_WaterSeeThroughDensity * max(waterDistance, 0.0f)));
    float3 color = lerp(waterColorMap, bottomColor, attenuation);
    float shoreMask = 1.0f - saturate((_WaterSeeThroughShoreMaskDepth - refractionDepth) * _WaterSeeThroughShoreMaskSharpness);
    return lerp(color, waterColorMap, shoreMask);
}

float3 CalcRefraction(float3 worldSpacePos, float3 normal, float2 screenPos, float3 waterColor, float depth)
{
    float3 waterColorMap = lerp(waterColor, _WaterColorMapTint, _WaterColorMapTintFactor);
    float2 screenUv = ComputeScreenUv(float4(screenPos, 0.0f, 1.0f));
    float4 refractionSample = RefractionTexture.Sample(RefractionSampler, screenUv);
    float refractionPayload = SampleRefractionPayload(screenUv);
    float3 refractionWorldPosition = DecodeRefractionWorldPosition(worldSpacePos, refractionPayload);
    float refractionDepth = worldSpacePos.y - refractionWorldPosition.y;
    depth = min(depth, refractionDepth);
    float refractionShoreMask = ComputeRefractionShoreMask(depth);
    float2 refractionOffset = ComputeRefractionOffset(normal, refractionShoreMask);

    float4 offsetRefractionSample = RefractionTexture.Sample(RefractionSampler, screenUv + refractionOffset);
    float offsetRefractionPayload = SampleRefractionPayload(screenUv + refractionOffset);
    float3 offsetRefractionWorldPosition = DecodeRefractionWorldPosition(worldSpacePos, offsetRefractionPayload);
    float offsetStep = step(worldSpacePos.y, offsetRefractionWorldPosition.y);
    refractionSample = lerp(offsetRefractionSample, refractionSample, offsetStep);
    refractionWorldPosition = lerp(offsetRefractionWorldPosition, refractionWorldPosition, offsetStep);
    refractionDepth = worldSpacePos.y - refractionWorldPosition.y;

    float2 refractionWorldUv = ComputeMapWorldUv(refractionWorldPosition);
    float4 refractionWaterColorAndSpec = WaterColorTexture.Sample(OceanTextureSampler, refractionWorldUv);
    float3 refractionWaterColorMap = lerp(refractionWaterColorAndSpec.rgb, _WaterColorMapTint, _WaterColorMapTintFactor);
    return ApplyTerrainUnderwaterSeeThrough(refractionDepth, refractionWorldPosition, refractionWaterColorMap, refractionSample.rgb);
}

float3 CalcReflection(float3 normal, float3 toCameraDir)
{
    float3 reflectionNormal = normal;
    reflectionNormal.y += _WaterReflectionNormalFlatten;
    reflectionNormal = normalize(reflectionNormal);
    float3 reflectionVector = normalize(reflect(-toCameraDir, reflectionNormal));
    return EnvironmentMapTexture.Sample(EnvironmentMapSampler, reflectionVector).rgb * _EnvironmentIntensity * _WaterCubemapIntensity;
}

float3 CalcOceanCoreWater(float4 screenSpacePos, float3 worldSpacePos, float2 worldUv)
{
    float4 waterColorAndSpec = WaterColorTexture.Sample(OceanTextureSampler, worldUv);
    float4 flowNormalSample = SampleOceanFlowNormal(worldUv);
    float3 flowNormal = flowNormalSample.xyz;
    float3 toCameraDir = normalize(_CameraWorldPosition - worldSpacePos);

    float2 mapUvSigned = worldSpacePos.xz * float2(1.0f, -1.0f) * max(_WaveScale, 0.0001f);
    float3 normalMap1 = SampleNormalMapTexture(AmbientNormalTexture, mapUvSigned, _WaterWave1Scale, _WaterWave1Rotation, _GlobalTime * _WaterWave1Speed, _WaterWave1NormalFlatten);
    float3 normalMap2 = SampleNormalMapTexture(AmbientNormalTexture, mapUvSigned, _WaterWave2Scale, _WaterWave2Rotation, _GlobalTime * _WaterWave2Speed, _WaterWave2NormalFlatten);
    float3 normalMap3 = SampleNormalMapTexture(AmbientNormalTexture, mapUvSigned, _WaterWave3Scale, _WaterWave3Rotation, _GlobalTime * _WaterWave3Speed, _WaterWave3NormalFlatten);
    float3 waterNormal = normalize(normalMap1 + normalMap2 + normalMap3 + flowNormal);

    float2 screenUv = ComputeScreenUv(screenSpacePos);
    float refractionPayload = SampleRefractionPayload(screenUv);
    float3 refractionWorldPosition = DecodeRefractionWorldPosition(worldSpacePos, refractionPayload);
    float refractionDepth = max(worldSpacePos.y - refractionWorldPosition.y, 0.0f);
    float foam = CalcFoamFactor(worldUv, worldSpacePos.xz, refractionDepth, flowNormalSample.a, flowNormal);

    float facing = 1.0f - max(dot(waterNormal, toCameraDir), 0.0f);
    float3 waterDiffuse = lerp(WaterColorDeep.rgb, WaterColorShallow.rgb, facing) * _WaterDiffuseMultiplier;
    float shadow = RiverStrideEvaluateSceneShadow(worldSpacePos, 0.0f, RiverStrideGetMainLightDirection(), streams.DepthVS);
    float3 litWater = RiverStrideComputeLighting(waterDiffuse + foam, float3(_OceanRoughness, _OceanRoughness, _OceanRoughness), saturate(1.0f - _OceanRoughness), waterNormal, toCameraDir, shadow, 1.0f);

    float3 refractionColor = CalcRefraction(worldSpacePos, waterNormal, screenSpacePos.xy, waterColorAndSpec.rgb, refractionDepth);
    float waterFade = ComputeWaterFade(refractionDepth);
    litWater *= waterFade;
    float3 reflectionColor = CalcReflection(waterNormal, toCameraDir);
    float fresnel = (saturate(_WaterFresnelBias) + pow(1.0f - saturate(abs(dot(toCameraDir, waterNormal))), max(_WaterFresnelPow, 0.0001f))) * waterFade;
    return litWater + lerp(refractionColor, reflectionColor, saturate(fresnel));
}
```

- [ ] **Step 8: Route PSMain through core path**

Replace current `PSMain` body with:

```c
stage override void PSMain()
{
    float3 worldPosition = streams.PositionWS.xyz;
    float2 worldUv = ComputeMapWorldUv(worldPosition);
    float3 waterColor = CalcOceanCoreWater(streams.ShadingPosition, worldPosition, worldUv);
    streams.ColorTarget = float4(waterColor, 1.0f);
}
```

If `streams.ShadingPosition` is unavailable for `OceanSurface`, use `streams.PositionH` if generated by `TransformationWAndVP`; otherwise inspect generated shader compile errors and bind the screen-space position exposed by the mixin. Do not use `SV_Position` manually unless SDSL compile confirms the stream name.

- [ ] **Step 9: Run tests expecting generated-key failures**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
```

Expected: Ocean text tests pass if file text is correct; C# compile may fail until generated `OceanSurface.sdsl.cs` is refreshed in Task 5.

---

## Task 5: Refresh Shader Keys and Bind Ocean Parameters

**Files:**
- Modify: `Terrain/Effects/Ocean/OceanSurface.sdsl.cs` generated
- Create: `Terrain/Effects/Water/WaterSceneSeed.sdsl.cs` generated
- Modify: `Terrain/Terrain.csproj`
- Modify: `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
- Modify: `Terrain.Editor.Tests/OceanShaderTextTests.cs`

- [ ] **Step 1: Run generated-file update**

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

Expected:

- command exits 0
- `Terrain/Effects/Ocean/OceanSurface.sdsl.cs` contains new keys
- `Terrain/Effects/Water/WaterSceneSeed.sdsl.cs` exists

- [ ] **Step 2: Add project compile entry for WaterSceneSeed**

In `Terrain/Terrain.csproj`, add:

```xml
<Compile Update="Effects\Water\WaterSceneSeed.sdsl.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>WaterSceneSeed.sdsl</DependentUpon>
</Compile>
```

- [ ] **Step 3: Bind Ocean core water parameters**

In `OceanRenderFeature.BindStaticResources`, add:

```csharp
oceanEffect.Parameters.Set(OceanSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.LinearClamp);
```

In `ApplyViewParameters`, add:

```csharp
effect.Parameters.Set(OceanSurfaceKeys._ViewSize, renderView.ViewSize);
effect.Parameters.Set(OceanSurfaceKeys._ViewMatrix, renderView.View);
```

In `Draw`, after global time binding, add:

```csharp
oceanEffect.Parameters.Set(OceanSurfaceKeys._WaterFlowTime, (float)context.RenderContext.Time.Total.TotalSeconds);
```

Keep material compatibility bindings for `ShallowColor`, `DeepColor`, `_OceanRoughness`, and `_WaveScale`.

- [ ] **Step 4: Run Stride clean and compile**

Run:

```powershell
dotnet msbuild Terrain\Terrain.csproj /t:StrideCleanAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
```

Expected: both commands exit 0. If SDSL compile fails due to stream names, inspect the generated error and adjust only the stream binding in `OceanSurface.sdsl`.

- [ ] **Step 5: Run tests and build**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests and build pass; diff check prints no errors except existing line-ending warnings.

- [ ] **Step 6: Commit shared seed and Ocean core shader**

Run:

```powershell
git add -- Terrain/Rendering/Water/WaterRefractionSeedResources.cs Terrain/Rendering/Water/WaterRefractionSeedProvider.cs Terrain/Effects/Water/WaterSceneSeed.sdsl Terrain/Effects/Water/WaterSceneSeed.sdsl.cs Terrain/Rendering/River/RiverRenderFeature.cs Terrain/Rendering/River/RiverRenderResources.cs Terrain/Rendering/Ocean/OceanRenderFeature.cs Terrain/Effects/Ocean/OceanSurface.sdsl Terrain/Effects/Ocean/OceanSurface.sdsl.cs Terrain/Terrain.csproj Terrain.Editor.Tests/OceanShaderTextTests.cs Terrain.Editor.Tests/RiverShaderTextTests.cs Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs
git commit -m "feat: share water refraction seed for ocean"
```

---

## Task 6: RenderDoc Verification and Hot-Edit Gate

**Files:**
- Verify: `Terrain/Effects/Ocean/OceanSurface.sdsl`
- Verify: `Terrain/Rendering/Ocean/OceanRenderFeature.cs`
- Optional modify after evidence: same files

- [ ] **Step 1: Launch editor or runtime and capture a fresh frame**

Run the editor:

```powershell
dotnet run --project Terrain.Editor\Terrain.Editor.csproj --no-restore
```

Manual capture requirement:

- Capture a RenderDoc frame with Ocean visible.
- Save it to `C:\Users\Redwa\Desktop\debug-ocean-core.rdc`.

- [ ] **Step 2: Inspect fresh Ocean draw**

Use RenderDoc MCP or GUI:

```text
Open: C:\Users\Redwa\Desktop\debug-ocean-core.rdc
Find: OceanSurface draw
Check:
  - RefractionTexture bound
  - WaterColorTexture bound
  - FlowMapTexture bound
  - FlowNormalTexture bound
  - Foam* textures bound
  - EnvironmentMapTexture bound
  - _RefractionTextureSize matches shared half-resolution seed
  - shader disassembly contains RefractionTexture.Load and Sample/offset path
```

- [ ] **Step 3: Compare against CK3 reference**

Open CK3 reference:

```text
C:\Users\Redwa\Desktop\save.rdc event 1061
```

Compare representative pixels:

```text
Deep ocean:
  CK3 target in this capture: roughly [0.011, 0.018, 0.022, 1.0]
Project target after fix:
  should no longer be HDR > 1.0 cyan
  should be in the same dark-water order of magnitude before tone mapping

Shoreline:
  should show depth/fade/foam changes
  should not be a flat translucent cyan overlay
```

- [ ] **Step 4: Hot-edit only if evidence points to a shader scalar issue**

If Ocean is structurally correct but too bright/dark, hot-edit the active Ocean draw first. Test only one hypothesis at a time:

```text
Candidate hot edits:
  - reduce _WaterDiffuseMultiplier
  - reduce _EnvironmentIntensity contribution for Ocean only
  - lower _WaterCubemapIntensity
  - adjust WaterColorDeep/Shallow defaults
  - adjust waterFade application
```

Do not port a hot edit into source unless the RenderDoc result improves the target pixels and screenshot.

- [ ] **Step 5: Port verified shader scalar changes**

If a hot edit is validated, change the exact source constants in `OceanSurface.sdsl`, rerun:

```powershell
dotnet msbuild Terrain\Terrain.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet build Terrain.sln --no-restore
git diff --check
```

- [ ] **Step 6: Commit verification fixes**

If Task 6 changed source:

```powershell
git add -- Terrain/Effects/Ocean/OceanSurface.sdsl Terrain/Effects/Ocean/OceanSurface.sdsl.cs Terrain/Rendering/Ocean/OceanRenderFeature.cs Terrain.Editor.Tests/OceanShaderTextTests.cs
git commit -m "fix: tune ocean core water against ck3 capture"
```

---

## Task 7: Documentation and Session Log

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Create: `docs/log/2026/06/24/2026-06-24-ocean-ck3-core-water.md`
- Optional create: `docs/log/learnings/water-refraction-seed-sharing.md`

- [ ] **Step 1: Update architecture overview**

In `docs/ARCHITECTURE_OVERVIEW.md`, update the MapSurface/Ocean row to include:

```text
Ocean core water now consumes the shared Water refraction seed used by River. River still copies that shared seed into its own BottomColor target before bottom/surface composition, while Ocean samples the shared seed directly for CK3-style refraction, see-through, water fade, foam, and reflection. Strategy-layer province/border/FOW/flatmap overlays remain intentionally out of scope.
```

- [ ] **Step 2: Update current features**

In `docs/CURRENT_FEATURES.md`, update Runtime Ocean rendering with:

```text
OceanSurface no longer uses the earlier simple translucent cyan overlay. It samples the shared Water refraction seed, reads alpha payload with unfiltered Load semantics, and applies CK3 core water behavior: refraction shore mask, see-through attenuation, water fade, flow/ambient normals, foam, fresnel, and reflection. It does not bind CK3 province/border/FOW/flatmap strategy-layer textures.
```

- [ ] **Step 3: Write session log**

Create `docs/log/2026/06/24/2026-06-24-ocean-ck3-core-water.md` with:

```markdown
# Ocean CK3 Core Water

**Date:** 2026-06-24
**Session:** ocean-ck3-core-water
**Status:** Complete

## Goal

Make project Ocean visually approach CK3 core ocean water using `save.rdc` event `1061` as the reference, without porting province/border/FOW/flatmap overlays.

## What Changed

- Extracted River scene seed generation into a shared Water refraction seed provider.
- River now copies the shared seed into its BottomColor target before bottom rendering.
- Ocean samples the shared seed directly.
- Replaced the simplified Ocean shader with CK3 core water semantics: refraction, see-through, water fade, flow/ambient normals, foam, fresnel, and reflection.

## Validation

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`
- `dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows`
- `dotnet build Terrain.sln --no-restore`
- RenderDoc fresh capture: `C:\Users\Redwa\Desktop\debug-ocean-core.rdc`
- CK3 reference: `C:\Users\Redwa\Desktop\save.rdc` event `1061`

## Follow-Up

- Province/border/FOW/flatmap overlays remain out of scope.
- Any remaining exposure mismatch should be investigated separately from water-core semantics.
```

- [ ] **Step 4: Run final verification**

Run:

```powershell
dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore
dotnet msbuild Terrain\Terrain.csproj /t:StrideCompileAsset /p:Configuration=Debug /p:TargetFramework=net10.0-windows
dotnet build Terrain.sln --no-restore
git diff --check
```

Expected: tests/build/asset compile pass; diff check has no errors.

- [ ] **Step 5: Commit docs**

Run:

```powershell
git add -- docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/24/2026-06-24-ocean-ck3-core-water.md docs/log/learnings/water-refraction-seed-sharing.md
git commit -m "docs: record ocean core water implementation"
```

If no learning file was created, omit it from `git add`.

---

## Final Verification Checklist

- [ ] River and Ocean both reference `WaterRefractionSeedProvider`.
- [ ] River no longer initializes `ImageEffectShader("RiverSceneSeed")`.
- [ ] Shared seed shader preserves `RiverCompressWorldSpace` payload semantics.
- [ ] River still copies shared seed into `BottomColor` before bottom pass.
- [ ] Ocean samples shared `RefractionTexture` directly.
- [ ] Ocean reads refraction alpha payload with `Texture2D.Load`, not linear sample alpha.
- [ ] Ocean no longer outputs hard-coded alpha `0.86`.
- [ ] Ocean shader excludes province/border/FOW/flatmap tokens.
- [ ] Stride generated keys are refreshed for Ocean and Water shaders.
- [ ] Stride asset compile passes.
- [ ] Managed tests and solution build pass.
- [ ] Fresh RenderDoc capture confirms Ocean draw binds shared refraction seed and water textures.
- [ ] Representative Ocean pixels are no longer HDR cyan and move toward CK3 deep-water scale.
