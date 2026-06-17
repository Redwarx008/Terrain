# 河流水面 waterFade 黑岸热替换验证与 MapExtent 修正
**Date**: 2026-06-16
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 复查新版 `C:\Users\Redwa\Desktop\debug.rdc` 中河流呈线条状、两侧河岸仍黑的问题，并在 RenderDoc 中先热替换验证后再改源码。

**Success Criteria:**
- 证明 `_MapExtent` 实际运行值，不依赖 SDSL 默认值或不可信 cbuffer dump。
- 用 RenderDoc shader hot-edit 验证黑岸是否来自 surface `waterFade` 链路。
- 修复 source shader 与 map extent 数据流，并跑完测试与 Stride asset workflow。

---

## What We Found

### 1. 当前 capture 的 `_MapExtent` 不是默认 4096
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`

**RenderDoc Evidence:**
- `RiverSurface.sdsl` 默认值仍是 `4096.0f`，但 `RiverRenderFeature` 在每个 draw 设置 `RiverSurfaceKeys._MapExtent = riverObject.MapExtent`。
- 当前 `debug.rdc` surface PS disasm 中 `_MapExtent` 对应寄存器值反解为 `18432.0f`。
- `get_cbuffer_contents` 仍返回全 0，不可信；真实值来自 shader trace 寄存器解码。

**Data-flow Finding:**
- `RiverMeshService.GetMapExtent()` 原本返回 `max(HeightCacheWidth, HeightCacheHeight)`。
- 地形世界坐标使用高度图像素坐标范围 `0..width-1` / `0..height-1`。

**Fix:**
```csharp
return MathF.Max(terrainManager.HeightCacheWidth - 1, terrainManager.HeightCacheHeight - 1);
```

### 2. 黑岸不是贴图缺失，也不是 `_MapExtent=4096`
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**RenderDoc Evidence:**
- 当前河流 bottom draws：`168/182/196/210`。
- 当前河流 surface draws：`239/257/275/293`。
- 黑岸像素 `(1000,550)`：
  - terrain 先写 `RGB≈(2.19,2.58,0.75), A=1`
  - surface event `275` 输出 `RGB≈(0.156,0.148,0.112), A≈0.987`
- 黑岸像素 `(1000,710)`：
  - terrain 先写 `RGB≈(5.72,4.30,2.96), A=1`
  - surface event `275` 输出 `RGB≈(0.135,0.155,0.114), A=1`

**Conclusion:**
- 黑岸由 surface shader 自己输出暗色且高 alpha 造成。
- 当前问题是 `waterFade` 在可见岸内区域过低，导致主水色被压掉，只剩暗 refraction/少量 reflection。

### 3. RenderDoc hot-edit 验证了 waterFade 假设
**Files Changed:** none in capture

**Hot-edit:**
- 用 `shader_build` 编译 HLSL debug PS。
- 用 `shader_replace` 替换 event `275` 的 PS，影响所有使用 `ResourceId::7771` 的 surface draw。
- Debug PS 只重算 old/fixed waterFade：
  - Red channel = old waterFade
  - Green/Blue = fixed waterFade
  - alpha = edgeFade * connectionFade * transparency

**Verification:**
- `(1000,550)` 热替换后变为 `RGB≈(0.079,0.839,0.855)`。
- `(1000,710)` 热替换后变为 `RGB≈(0.063,0.830,0.870)`。

**Conclusion:**
- 黑岸确实来自原 surface `waterFade` 对可见岸内像素过度压黑。
- 修复应让 `waterFade` 跟随 `edgeFade` 的可见区域，而不是继续调 bottom brightness。

### 4. 第二轮 hot-edit 证明 partial edge 不能用线性 depth floor
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**RenderDoc Evidence:**
- 新抓帧 `C:\Users\Redwa\Desktop\terrain_river_after_waterfade_fix.rdc` 已经包含第一版 `edgeVisibleDepth = edgeFade * _WaterFadeShoreMaskDepth`。
- 边缘暗线像素 `(623,617)`：
  - input `RiverUV.y≈0.906`
  - shader output `RGB≈(0.130,0.200,0.222), A≈0.690`
  - post blend `RGB≈(0.145,0.228,0.230)`
- 该像素 alpha 不是 0，也不是 texture missing；问题是 partial-edge 的 `waterFade` 仍明显低于 edge alpha。

**Hot-edit Fix:**
- 将 `edgeFade` 通过 water-fade 函数的反函数转成 visual depth floor：
```sdsl
float fadeSharpness = max(_WaterFadeShoreMaskSharpness, 0.0001f);
float edgeVisibleDepth = _WaterFadeShoreMaskDepth - (1.0f - edgeFade) / fadeSharpness;
```

**Verification:**
- 同一像素 `(623,617)` 第二版 hot-edit 后：
  - shader output `RGB≈(0.126,0.344,0.467), A≈0.690`
  - post blend `RGB≈(0.141,0.372,0.476)`
- 新真实 capture `C:\Users\Redwa\Desktop\terrain_river_after_edgefade_inverse_frame928.rdc` 的 surface PS disasm 已出现：
  - `float edgeVisibleDepth = _WaterFadeShoreMaskDepth_id43 - (1.0f - edgeFade) / fadeSharpness;`
- 结论：宽黑岸和剩余边缘黑线都在 `waterFade` 与 `edgeFade` 不一致；第二版公式修正 partial alpha 边缘。

---

## Implementation

### 1. `waterFade` 纳入 edge-visible depth
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

```sdsl
float ComputeRiverWaterFade(float worldDepth, float depthFactor, float edgeFade)
{
    float fadeSharpness = max(_WaterFadeShoreMaskSharpness, 0.0001f);
    float edgeVisibleDepth = _WaterFadeShoreMaskDepth - (1.0f - edgeFade) / fadeSharpness;
    float visualDepth = max(worldDepth, max(depthFactor * _WaterFadeShoreMaskDepth, edgeVisibleDepth));
    return 1.0f - saturate((_WaterFadeShoreMaskDepth - visualDepth) * fadeSharpness);
}
```

**Why This Works:**
- 岸线外缘仍由 alpha fade 控制。
- `edgeFade` 先通过 water-fade ramp 的反函数转换为视觉深度下限，保证 partial alpha 边缘的 `waterFade` 不低于其可见覆盖度。
- 避免 `A≈0.7` 但 RGB 仍被 near-shore fade 压成暗 refraction 的细黑边。

### 2. `MapExtent` 改为世界坐标跨度
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`

**Why This Works:**
- `RiverWidth` 的归一化与 `PositionWS / _MapExtent` 的 world UV 现在使用同一套世界坐标尺度。
- 对 `18433` samples 的高度图，运行时会传 `18432`，而不是采样数。

### 3. 回归测试
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**Tests Added/Updated:**
- `river surface shader adapts shore fade to narrow ribbon depth`
- `river mesh map extent uses world coordinate span`

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`：通过，`907 succeeded, 0 failed`。
- `C:\Users\Redwa\Desktop\terrain_river_after_edgefade_inverse_frame928.rdc`：新真实 capture surface PS 已包含反函数公式，且绑定 9 张 surface 贴图资源，无 HIGH RenderDoc 日志。

Warnings:
- 仍有 NuGet vulnerability warnings、CS0649/CS0067、WFO0003，以及 Stride shader X3557 loop unroll warning；本次未新增 fatal error。

---

## Quick Reference for Future Claude

**Key Evidence:**
- `_MapExtent` in current capture = `18432.0f`，不是 SDSL 默认 `4096.0f`。
- 原黑岸 surface output：
  - `(1000,550)` -> `RGB≈(0.156,0.148,0.112), A≈0.987`
  - `(1000,710)` -> `RGB≈(0.135,0.155,0.114), A=1`
- Hot-edit debug PS 后：
  - `(1000,550)` -> `RGB≈(0.079,0.839,0.855)`
  - `(1000,710)` -> `RGB≈(0.063,0.830,0.870)`

**Root Cause:**
- `waterFade` 在 alpha-visible 岸内区域仍接近 0，主水色被压掉，只剩暗 refraction。

**Fix:**
- `ComputeRiverWaterFade(worldDepth, depthFactor, edgeFade)` 使用 `edgeVisibleDepth = _WaterFadeShoreMaskDepth - (1.0f - edgeFade) / max(_WaterFadeShoreMaskSharpness, 0.0001f)`。
- `RiverMeshService.GetMapExtent()` 使用 `max(width - 1, height - 1)`。

**Next Manual Check:**
- 重新截帧后检查 surface draw，旧黑岸点不应再输出暗 RGB + 高 alpha；剩余深色细线若在 `A=1` 且 `RiverUV.y` 位于内部，则属于 flow/normal/spec 水面纹理，而不是岸边黑带。
