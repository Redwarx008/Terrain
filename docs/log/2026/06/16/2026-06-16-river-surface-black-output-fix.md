# 河流水面黑色输出修复
**Date**: 2026-06-16
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 分析 `C:\Users\Redwa\Desktop\debug.rdc` 中更新后的河流为什么渲染为黑色，并修复当前 shader 输出。

**Success Criteria:**
- 确认黑色来自资源缺失、混合状态还是 shader 自身输出。
- 对照 CK3 `river_surface.shader` / `jomini_water_default.fxh` 的真实语义修正水面颜色路径。
- 跑完 River shader 文本测试、Stride asset workflow 和 Editor build。

---

## Context & Background

**Previous Work:**
- See: [2026-06-15-river-ck3-asset-driven-water.md](../15/2026-06-15-river-ck3-asset-driven-water.md)
- Related: [ADR-014 河流渲染架构](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 河流已是 bottom pass + surface pass 双 pass。
- 12 张 CK3 风格 DDS 已通过 `.sdtex` 和 `Terrain.Editor.sdpkg` `RootAssets` 进入 Stride 内容库。
- 新截帧中水面显示为近黑色，但不再是上一轮的缺资源问题。

---

## What We Did

### 1. RenderDoc 定位黑色来源
**Files Changed:** none

**Investigation:**
- 打开 `debug.rdc` 后确认新帧为 D3D11，67 draws / 80 events。
- 当前河流 draw 为 bottom pass `166/179/192/205`，surface pass `234/252/270/288`；用户提到的 208 在该 capture 中不是 draw。
- `ResourceId::545` 对应 water-color 贴图，使用记录为 surface pass 的 `PS_Resource`：`234/252/270/288`。
- `ResourceId::545` 统计范围为 RGB min `[0,0,0]`，max 约 `[0.322,0.255,0.224]`，本身是很暗的 lookup。
- 选取黑色河流像素 `(900, 780)`，pixel history 显示 terrain draw 119 先写入 `[4.82, 3.72, 2.57, 1]`，surface draw 252 自己输出 `[0.120, 0.159, 0.172, 1]` 并覆盖。

**Conclusion:**
- 黑色不是 blend state 造成，也不是 t2-t8 资源缺失造成。
- 根因是 `RiverSurface.sdsl` 将暗 `WaterColorTexture` 以 `0.65` 权重混入主水色，然后又以 `0.72` 向偏暗 bottom/refraction buffer 混合。

### 2. 按 CK3 参考语义重写 surface water-color / refraction
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Effects/RiverCommon.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
```sdsl
float4 waterColorAndSpec = WaterColorTexture.Sample(WaterTextureSampler, worldUv);
float glossMap = waterColorAndSpec.a;
float3 waterColorMap = lerp(waterColorAndSpec.rgb, _WaterColorMapTint, saturate(_WaterColorMapTintFactor));
float3 waterDiffuse = lerp(WaterColorDeep.rgb, WaterColorShallow.rgb, facing) * _WaterDiffuseMultiplier;
float3 refractionColor = SampleRefractionSeeThrough(screenUv + refractionOffset, streams.PositionWS.xyz, waterNormal, waterColorMap, worldDepth);
```

**Rationale:**
- CK3 参考 shader 在 `CalcWater` 中用 `WaterColorTexture.Sample(Input._WorldUV)`，不是按 `depthFactor` 采 1D ramp。
- water-color RGB 进入 refraction / see-through tint，alpha 是 gloss/spec map。
- 主水色来自 facing-based `lerp(_WaterColorDeep, _WaterColorShallow, Facing)`，再加 foam、refraction、reflection/fresnel。

### 3. 改 bottom RT alpha 为可反解 refraction distance
**Files Changed:** `Terrain.Editor/Effects/RiverCommon.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```sdsl
stage float3 _CameraWorldPosition = float3(0.0f, 0.0f, 0.0f);
float compressedWorld = RiverCompressWorldSpace(bottomWorldPosition, _CameraWorldPosition);
float3 refractionWorldPosition = RiverDecompressWorldSpace(surfaceWorldPosition, refraction.a, _CameraWorldPosition);
```

**Rationale:**
- CK3 bottom pass alpha 存的是可通过相机射线反解的 compressed world distance。
- 旧实现用 world position 点积压缩，不可逆，surface 只能做固定权重颜色 lerp。
- 新实现让 surface 可以估算 refraction depth，再用指数衰减做 underwater see-through。
- `Eye` 在 Stride 材质 shader 组合里常见，但当前 River 使用 `DynamicEffectInstance("RiverBottom"/"RiverSurface")`，不能假设裸 `Eye` 全局已导入；相机世界坐标必须由 `RiverRenderFeature` 从 `renderView.View` 逆矩阵显式绑定。

### 4. 增加回归测试
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 新增 `SurfaceShaderFollowsCk3WaterColorAndRefractionSemantics`。
- 新增 `BottomShaderPacksRefractionDistanceForSurfaceSeeThrough`。
- 测试锁定 world UV water-color、gloss alpha、see-through refraction、bottom distance packing，并禁止回退到 depth ramp / 强权重 lerp。

### 5. 修复窄 ribbon depth 让水面继续发黑
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Investigation:**
- 重新打开 `C:\Users\Redwa\Desktop\debug.rdc`，确认当前 capture 为 D3D11，66 draws / 79 events。
- 当前黑色仍出现在 surface draw `270`，pixel history 显示 surface shader 自己输出接近黑色，例如 `(900,570)` 为 `[0.080, 0.055, 0.029, 1]`，不是 blend state 导致。
- surface shader 已是新版本，disasm 中可见 `_CameraWorldPosition`，因此不是旧 shader 或 `Eye` 问题。
- `RefractionTexture` / bottom RT 为 `ResourceId::7785`，半分辨率 `836x498`；其最大 RGB 只有 `[0.138, 0.093, 0.046]`，典型 river 像素约 `[0.084, 0.058, 0.029]`。
- 当前 shader debug 显示 `RiverWidth` 是归一化半宽；还原后窄河深度低于 CK3 `WaterFadeShoreMaskDepth=0.5`，导致 `waterFade` 把主水色乘到 0，最终只剩暗 bottom/refraction。
- CK3 参考 draw `464` 中 `Input.Width` 更接近 full-width 语义（约 `1.2~1.7`），而不是我们的半宽语义。

**Implementation:**
```sdsl
return max(streams.RiverWidth * max(_MapExtent, 1.0f) * 2.0f, 0.001f);

float ComputeRiverWaterFade(float worldDepth, float depthFactor)
{
    float visualDepth = max(worldDepth, depthFactor * _WaterFadeShoreMaskDepth);
    return 1.0f - saturate((_WaterFadeShoreMaskDepth - visualDepth) * _WaterFadeShoreMaskSharpness);
}
```

**Rationale:**
- Mesh 侧 `RiverWidth` 存的是归一化 half-width；CK3 surface shader 的 `Input.Width` 参与 flow UV 和 depth 时表现为 full-width 尺度，因此 shader 侧还原时应乘 `2.0`。
- 当前 Stride ribbon 的真实世界深度小于 CK3 shore fade 阈值，直接使用 raw `worldDepth` 会让整条窄河只显示暗 bottom。使用 cross-section `depthFactor` 作为视觉深度下限，可以保留 CK3 岸边 fade，同时避免河流中心被清零。

---

## Decisions Made

### Decision 1: 不把缺失资源作为本次根因
**Context:** RenderDoc UI 中 reflection slot 名称仍显示 t2-t8，但 `get_resource_usage` 已确认对应 texture resources 在 surface pass 被作为 `PS_Resource` 使用。

**Decision:** 本次修复 shader 色彩合成，不再继续追 RootAssets / Content.Load。

**Rationale:** Pixel history 和 texture usage 已把问题定位到 shader output 值本身。

### Decision 2: water-color 贴图按 CK3 world UV / gloss alpha 语义使用
**Context:** 参考 `jomini_water_default.fxh` 显示 `WaterColorTexture` 以 `Input._WorldUV` 采样，`rgb` 用于 refraction tint，`a` 用于 gloss map。

**Decision:** 删除 depth-ramp 采样和强 lerp，改为 world UV 采样、RGB tint、alpha gloss。

**Trade-offs:** 当前仍是 Stride 近似实现，没有完全复制 CK3 的 shadow/fog/cubemap/flowmap 全部参数，但核心数据语义已经对齐。

### Decision 3: River shader 内部把归一化半宽还原为全宽
**Context:** `RiverMeshService` 为几何 offset 使用 half-width，并把 `halfWidth / mapExtent` 写入顶点流；CK3 surface shader 的 `Input.Width` 参与 `Depth * Input.Width + 0.1` 和 flow UV，参考 capture 中数值更接近 full-width。

**Decision:** `RiverBottom` / `RiverSurface` 的 `ComputeWorldWidth()` 从 `halfWidth` 改为 `halfWidth * 2`。

**Trade-offs:** 几何宽度不变，只改变水深/流动纹理尺度；这比扩大 mesh 更安全。

### Decision 4: WaterFade 对窄 ribbon 使用 visual depth 下限
**Context:** CK3 的 `WaterFadeShoreMaskDepth=0.5` 在当前 Stride ribbon 尺度下会把中心主水色也清零。

**Decision:** `RiverSurface` 继续保留 CK3 公式，但输入的 fade depth 改为 `max(worldDepth, depthFactor * _WaterFadeShoreMaskDepth)`。

**Trade-offs:** 这是单位适配，不是逐字复制 CK3；优先保证当前编辑器 HDR terrain 下水面不会退化成纯暗 bottom。

---

## Problems Encountered & Solutions

### Problem 1: 水面输出接近黑色
**Symptom:** 截图中河流主体接近黑色。

**Root Cause:** `RiverSurface.sdsl` 当前色彩路径把 CK3 water-color 错当 depth ramp / 主颜色使用，并且 bottom alpha 不可反解，无法按 CK3 的 refraction depth 做 see-through。

**Solution:** 按参考 shader 改为 world UV water-color、gloss alpha、bottom distance packing 和指数 see-through refraction。

**Why This Works:** RenderDoc debug pixel 已显示最终 shader output 是 `[0.120,0.159,0.172,1]`，因此修正 shader 输出路径会直接影响结果；参考 shader 也证明 water-color 不应作为 depth ramp 高权重混入。

### Problem 2: RiverBottom 运行时 shader 编译找不到 `Eye`
**Symptom:** Editor 运行时报 `RiverBottom.sdsl(65,82): E0237 The variable [Eye] in class [RiverBottom] is not defined`。

**Root Cause:** `Eye` 不是 River DynamicEffect shader 的可靠可见变量；之前把 CK3-style bottom distance packing 写成依赖材质全局变量，asset build 没暴露，但运行时 effect compile 暴露了未定义变量。

**Solution:**
```csharp
Matrix.Invert(ref renderView.View, out var viewInverse);
var cameraWorldPosition = viewInverse.TranslationVector;
bottomEffect.Parameters.Set(RiverBottomKeys._CameraWorldPosition, cameraWorldPosition);
surfaceEffect.Parameters.Set(RiverSurfaceKeys._CameraWorldPosition, cameraWorldPosition);
```

**Why This Works:** bottom pass 和 surface pass 使用同一个显式相机世界坐标参数，避免依赖 shader mixin 外部隐式全局，同时保持 Compress/Decompress 的相机射线语义一致。

### Problem 3: 修复 `Eye` 后水面仍然接近黑色
**Symptom:** 新 `debug.rdc` 中河流仍显示为黑色。

**Root Cause:** surface draw 不是混合后变黑，而是 shader 输出本身接近黑。`waterFade` 在窄 ribbon 的 raw `worldDepth` 下为 0，主水色被清零；剩余 refraction 直接来自很暗的 bottom RT。

**Solution:** width 还原改为 full-width；`waterFade` 使用 `ComputeRiverWaterFade(worldDepth, depthFactor)`，用 cross-section 视觉深度保留河流中心水色。

**Why This Works:** 对 river center，`depthFactor` 接近 1，会让 `visualDepth` 达到 CK3 shore fade 阈值，从而保留 `WaterColorShallow/Deep` 主水色；对边缘，`depthFactor` 接近 0，仍会保持岸边淡出。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录 world UV water-color、gloss alpha 和 bottom distance refraction。
- [x] Update `docs/CURRENT_FEATURES.md` - 记录参考 shader 语义对齐。
- [ ] ADR not required - 本次是 bugfix / 调色规则收敛，不改变 ADR-014 架构。

---

## Code Quality Notes

### Testing
- 新增 2 条 River shader 文本回归测试。
- 增补断言：`RiverBottom` / `RiverSurface` 必须声明 `_CameraWorldPosition`、不得包含 `Eye.xyz`，`RiverRenderFeature` 必须绑定 bottom/surface 相机参数。
- 新增 `SurfaceShaderAdaptsShoreFadeToNarrowRibbonDepth`，锁定 full-width 还原和 `ComputeRiverWaterFade`，避免窄河中心主水色再次被 raw worldDepth fade 清零。
- 按 TDD 先观察测试失败，再修改 shader，最后验证通过。

### Verification
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`：通过；新增 `Eye` 与窄 ribbon fade 防回归断言已覆盖。
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`：通过，`RiverBottom` / `RiverSurface` 被处理。
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`：通过。
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`：通过，907 succeeded / 0 failed。
- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`：通过，18 warnings / 0 errors。

---

## Next Session

### Immediate Next Steps
1. 重新运行 Editor 并截新帧，确认 `RiverSurface` disasm 中已出现 full-width `* 2.0f` 和 `ComputeRiverWaterFade`。
2. 如果画面仍偏暗，优先对照 `game/gfx/map/rivers/riverwater.settings` 调 `WaterColorShallow/Deep`、see-through density、fresnel 和 foam/specular；不要先回退资源绑定。
3. 后续可继续补齐 CK3 flowmap、cubemap、shadow/cloud/fog 等尚未完全迁移的水体参数。

### Gotchas
- RenderDoc bindings 里出现 shader reflection slot 名称不等于资源缺失；应以 `get_resource_usage` / pixel shader trace 判断实际绑定。
- `eventId 208` 在本次新 capture 不是 draw；当前 surface draw 是 `234/252/270/288`。
- `WaterColorTexture` 当前 RGB 数值非常暗，参考 shader 也不是直接高权重作为最终水面颜色，而是作为 refraction tint map。
- River DynamicEffect shader 不要直接使用 `Eye.xyz`；需要相机位置时从 `renderView.View` 逆矩阵取 `TranslationVector` 并绑定到显式 stage 参数。
- 当前 Stride river vertex 宽度流是归一化 half-width；shader 中参与 CK3 depth / flow UV 的宽度需要还原为 full-width。
- CK3 raw worldDepth shore fade 直接移植到窄 ribbon 会把中心主水色清零；需要用 `depthFactor` 适配视觉深度。

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Root cause: shader color math and water-color/refraction semantic mismatch, not missing CK3 resources.
- Key implementation: `Terrain.Editor/Effects/RiverSurface.sdsl`
- Regression test: `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- Current status: 资源绑定可用，水面路径已改为参考 shader 的 world UV water-color、gloss alpha、显式 camera-position bottom-distance see-through，并适配了 full-width depth 与窄 ribbon waterFade。

**What Changed Since Last Doc Read:**
- `RiverSurface.sdsl` water-color lookup 从 depth ramp 改为 world UV。
- `RiverBottom.sdsl` RT alpha 从不可逆点积压缩改为 camera-relative bottom distance，且相机位置由 `RiverRenderFeature` 显式绑定 `_CameraWorldPosition`。
- `RiverBottom.sdsl` / `RiverSurface.sdsl` 的 width scale 从 half-width 改为 full-width；`RiverSurface.sdsl` 增加 `ComputeRiverWaterFade`，避免 raw worldDepth 让水面继续黑。

---

## Notes & Observations

- 当前修复对齐了 CK3 的核心数据语义；最终视觉仍需要更多截帧对比和参数调校。
