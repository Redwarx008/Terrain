# 河流高度尺度与 refraction clamp 修复
**Date**: 2026-06-21
**Session**: river height-scale parity
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 核对当前项目地形高度编码/解码数值语义是否与 CK3 一致，并解决高低地形交界处河流 bottom bank 在高处消失的问题。

**Success Criteria:**
- 直接从 CK3 RenderDoc capture 与当前项目 capture 读出 terrain height / map size / river cbuffer 证据。
- 不再凭 `RefractionDepth`、surface alpha 或 mesh 方向猜测。
- 在 `height_scale=200` 下让 refraction distance payload 正常工作。

---

## What We Did

### 1. 对照 CK3 height/map cbuffer

**Files/Captures:**
- `C:\Users\Redwa\Desktop\ck3 river2.rdc`
- CK3 source `clausewitz/gfx/FX/cw/heightmap.fxh`
- CK3 source `jomini/gfx/FX/jomini/jomini_water.fxh`

**Findings:**
- CK3 terrain draw event `506` cbuffer:
  - `HeightScale=50.0`
  - `OriginalHeightmapToWorldSpace≈0.5`
  - `WorldSpaceToLookup≈(1/9216, 1/4608)`
  - `WorldExtents=9215x4607`
- CK3 river bottom event `534`:
  - `MapSize=9216x4608`
  - `_Depth=0.15`
  - `_DepthWidthPower=2`
  - `_DepthFakeFactor=2`
  - `_BankFade=0.025`
  - camera `CameraPosition.y≈28.33`
- CK3 bottom pixels on the visible river had `WorldSpacePos.y≈3.2..6.2` and width about `1.46..1.54`.

### 2. 对照当前项目 debug2 capture

**Capture:**
- `C:\Users\Redwa\Desktop\debug2.rdc`

**Findings:**
- 当前 terrain draw event `204` cbuffer:
  - `HeightScale=200.0`
  - `HeightmapDimensionsInSamples=18431x9215`
- 问题区域 river pixels 之前已确认：
  - bottom event `304` 坏点 `PositionWS.y≈48.63`
  - surface event `377` 坏点 `PositionWS.y≈48.62`
  - camera `_CameraWorldPosition.y≈87.85`
- `RiverCommon` 的 CK3 camera-distance pack/unpack 公式与 CK3 `CompressWorldSpace/DecompressWorldSpace` 等价，但 CK3 固定 `MaxHeight=50` camera clamp 只适配 CK3 自己的 `HeightScale=50`。

### 3. 误判：直接把 height_scale 降到 50

**Temporary Change:**
- 曾把 `game/map/default.toml` 与 `game/map_data/default.toml` 的 `height_scale` 从 `200` 改为 `50`。

**Verification:**
- `height50_verify.rdc` 确认 `HeightScale=50` 确实进入 GPU，默认视角河流可见。

**Why Rejected:**
- 用户指出这只是掩盖问题，不是解决高高度项目下的算法缺陷。
- 本项目需要保留 `height_scale=200` 的高地形起伏；不能为了 CK3 固定 clamp 降低全局地形高度。

### 4. 最终修复：参数化 refraction camera clamp

**Files Changed:**
- `Terrain.Editor/Effects/RiverCommon.sdsl`
- `Terrain.Editor/Effects/RiverCommon.sdsl.cs`
- `Terrain.Editor/Effects/RiverSceneSeed.sdsl`
- `Terrain.Editor/Rendering/River/RiverMeshData.cs`
- `Terrain.Editor/Services/RiverMeshService.cs`
- `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Change:**
- 新增 shader 参数 `_RefractionMaxCameraHeight`，`RiverCommon` 使用：
  - `max(_RefractionMaxCameraHeight, 50.0f)`
- `RiverMeshService` 从当前 terrain `HeightScale` 推导：
  - `RefractionMaxCameraHeight = MathF.Max(50.0f, terrainManager?.HeightScale ?? 50.0f)`
- `RiverRenderFeature` 把该值绑定到：
  - `RiverSceneSeed`
  - `RiverBottom`
  - `RiverSurface`
- `RiverSceneSeed` 改为 mix in `RiverCommon`，并写入 `RiverCompressWorldSpace(positionWS.xyz, Eye.xyz)`，不再写 raw distance。
- 临时 `height_scale=50` 已撤回，当前默认 descriptor 回到 `height_scale=200` / `200.0`。

---

## Verification

- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` 通过。
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore` 通过。
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug` 通过。
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug` 通过，仅保留既有 `X3557` warning。
- 新 RenderDoc capture：
  - `C:\Users\Redwa\Desktop\height200_refraction_clamp_verify.rdc`
  - D3D11, 72 draws, 85 events。
- `search_shaders("_RefractionMaxCameraHeight")` 在 seed / bottom / surface 三个 PS shader 中均找到：
  - `float maxHeight = max(_RefractionMaxCameraHeight..., 50.0f)`
- cbuffer 复核：
  - seed event `248`：`_RefractionMaxCameraHeight=200.0`
  - bottom event `276`：`_RefractionMaxCameraHeight=200.0`
  - surface event `351/391`：`_RefractionMaxCameraHeight=200.0`
- 用户同视角复核后确认画面正常。

---

## Decisions Made

### Decision 1: 保留高地形高度，参数化 CK3 camera clamp

**Decision:** 不把默认 `height_scale` 降为 `50`。保留本项目 `height_scale=200`，并让 river refraction distance payload 的 clamp 平面随 terrain height scale 提高。

**Rationale:**
- CK3 terrain `HeightScale=50` 和 water/refraction source 中的 `MaxHeight=50` camera clamp 是两个不同概念。
- 固定 50 clamp 在高地形项目里会截断高处相机/地形的 camera-relative distance payload。
- seed、bottom、surface 必须用同一个压缩/解压尺度，否则 downstream surface 会错误解释 bottom/refraction depth。

**Trade-offs:**
- 这不是严格复制 CK3 常量，而是让 CK3 算法适配本项目更大的垂直尺度。
- 如果将来支持每区域不同 height scale，需要把 `_RefractionMaxCameraHeight` 从全局 river object 参数升级为更细粒度的可见集合参数。

---

## What Worked

1. **直接读取 CK3 与本地 cbuffer**
   - 先区分 terrain `HeightScale` 和 shader `MaxHeight`，避免继续把常量混为一谈。

2. **重新抓帧验证最终高高度路径**
   - `height200_refraction_clamp_verify.rdc` 证明 `_RefractionMaxCameraHeight=200` 已进入 seed/bottom/surface GPU cbuffer。

3. **用户同视角视觉确认**
   - 最终修复在实际问题视角下恢复了 bottom bank 可见性。

---

## What Didn't Work

1. **降低 `height_scale` 到 50**
   - 能让 CK3 固定 clamp 不再暴露问题，但改变了本项目目标地形高度。

2. **继续改 mesh / surface alpha / `RefractionDepth`**
   - 尖端收束不是 mesh 拓扑缺陷，也不是 surface final alpha 问题；根因在 refraction distance payload 尺度。

---

## Gotchas

- 不要把 CK3 source 中的 `MaxHeight=50` camera clamp 误读成 terrain `HeightScale`。
- 不要把 `original_heightmap_size=18432x9216` 当成 shader `MapSize`；CK3 river/terrain runtime 使用的是 `9216x4608` map units。
- 不要用降低全局 terrain height scale 解决 refraction payload 问题；这会让验证看似通过，但牺牲项目真实高度语义。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 terrain runtime height scale: `50.0`
- 当前项目默认 descriptor: `height_scale=200`
- river refraction clamp: `_RefractionMaxCameraHeight=max(50, terrain HeightScale)`
- `RiverSceneSeed`、bottom、surface 必须共用 `RiverCommon` 的 `RiverCompressWorldSpace` / `RiverDecompressWorldSpace`。

**Current Status:**
- 高高度下 bottom bank 消失已由参数化 refraction clamp 修复。
- 新 capture 和用户视觉复核均确认正常。
