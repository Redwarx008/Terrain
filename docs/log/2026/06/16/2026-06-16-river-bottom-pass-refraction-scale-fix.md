# 河流 bottom pass 暗色根因诊断
**Date**: 2026-06-16
**Session**: 2
**Status**: 🚧 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用 RenderDoc 逐 pass 对比 CK3 河流和当前 `debug.rdc`，找到河流偏黑的真实原因，避免用 `_BottomDiffuseMultiplier` 这类亮度补偿掩盖问题。

**Success Criteria:**
- 对齐 CK3 bottom/refraction pass 与当前 bottom/refraction pass 的颜色输出。
- 确认暗色来自哪个 pass、哪个 shader 路径和哪些缺失输入。
- 将修复方向落到 CK3 同类渲染结构，而不是调亮参数。

---

## Context & Background

**Previous Work:**
- CK3 风格底部、水面、泡沫、水色与反射贴图已经接入。
- 当前 `debug.rdc` 仍显示河流接近黑色。
- 曾用 `_BottomDiffuseMultiplier` 热验证“提高 bottom RGB 会改善 surface”，但该手段只是诊断实验，不是可接受的最终修复。

---

## What We Found

### 1. 暗色发生在 bottom/refraction pass
**Files Changed:** none

**RenderDoc Evidence:**
- CK3 参考 capture：bottom/refraction 为 event `336`，写半分辨率 `ResourceId::49006`；surface 为 event `464`。
- 当前 `debug.rdc`：bottom/refraction 为 event `192`，写半分辨率 `ResourceId::7785`；surface 为 event `270`。
- 当前 bottom event `192` 同点写出 `RGB≈(0.023, 0.014, 0.007), A≈60.25`。
- 当前 surface event `270` 同点输出 `RGB≈(0.0197, 0.0143, 0.00624)`。
- CK3 bottom event `336` 同阶段同类像素输出 `RGB≈(0.168, 0.116, 0.061), A≈59.02`。

**Conclusion:**
- surface 不是首要根因；它采样的 refraction buffer 在 bottom pass 已经过暗。

### 2. 贴图不是缺失或全黑
**Files Changed:** none

**RenderDoc Evidence:**
- 当前 bottom diffuse 资源 `ResourceId::525` 为 `128x128 BC1_UNORM`，范围约 `RGB min=(0.129,0.110,0.094)`，`max=(0.322,0.302,0.259)`。
- 当前 bottom normal/properties/depth 也都有有效范围。
- 当前 bottom RT `ResourceId::7785` 最大仅约 `RGB=(0.138,0.093,0.046)`。

**Conclusion:**
- 问题不是底部资源没绑定，而是 shader 对这些输入的解释和受光路径与 CK3 不一致。

### 3. CK3 bottom 是 lit material pass
**Files Changed:** none

**RenderDoc Evidence:**
- CK3 bottom event `336` PS 绑定：
  - `BottomDiffuse_Texture`
  - `BottomNormal_Texture`
  - `BottomProperties_Texture`
  - `EnvironmentMap_Texture`
  - `ShadowTexture_Texture`
- CK3 bottom PS disasm 中出现：
  - `sample_c_lz` 采样 shadow texture
  - `SunIntensity` / `SunDiffuse` / `ToSunDir`
  - `sample_l(texturecube)` 采样 environment map
  - 最终 `direct light + specular + diffuse IBL + specular IBL` 合成输出

**Source Evidence:**
- `jomini_river_bottom.fxh` 的 `CalcRiverBottomAdvanced` 使用：
  - `GetMaterialProperties(Diffuse.rgb, Normal, Properties.a, Properties.g, Properties.b)`
  - `GetRiverBottomSunLightingProperties(WorldSpacePos, WorldSpaceDepth, ShadowTexture)`
  - `CalculateSunLighting(MaterialProps, LightingProps, EnvironmentMap)`
- `jomini_lighting.fxh` 的 `CalculateSunLighting` 返回 direct diffuse/specular 与 IBL diffuse/specular 的总和。

**Conclusion:**
- CK3 bottom 不把 `BottomDiffuse` 当最终颜色乘深度 tint；它把 diffuse/normal/properties 当材质输入并走 lighting。

### 4. 当前实现缺失 CK3 bottom lighting 输入
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`

**Before:**
```sdsl
float3 color = bottomDiffuse.rgb * depthTint * 2.0f;
color *= 1.0f - depthFactor * saturate(_DepthFakeFactor * 0.22f + propertyDarkening * 0.22f);
```

**Root Cause:**
- 当前 bottom shader 用很暗的 `depthTint` 直接压 albedo，再额外按 depth/property darken。
- 当前 bottom pass 没有 environment cubemap、direct sun、specular 或 shadow lighting 输入。
- 自定义 `RiverRenderFeature` 使用 `DynamicEffectInstance("RiverBottom")`，不自动接入 Stride 标准 ForwardLighting。

**Fix Direction:**
- 移除 `_BottomDiffuseMultiplier` 这类亮度补偿。
- 在 bottom pass 中引入 CK3 同类的 material lighting 结构：
  - bottom diffuse/normal/properties -> material inputs
  - direct sun term
  - environment cubemap diffuse/specular IBL
  - shadow/cloud fallback 作为 lighting 输入

---

## Problems Encountered & Solutions

### Problem 1: multiplier 热替换能改善画面但不是根因修复
**Symptom:** 将 bottom RGB 乘大可让 surface 不再黑。

**Rejected Fix:** `_BottomDiffuseMultiplier = 10.0f`

**Reason Rejected:** 该参数只是补偿 CK3 lighting 缺失后的能量差，不能解释为什么 CK3 不黑，也不能保持 shader 语义正确。

**Corrective Action:** 已从源码和测试方向撤销 multiplier，改为补 CK3 bottom lighting 路径。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 移除 multiplier 结论，记录 bottom pass 应走 CK3-style material lighting。
- [x] Update `docs/CURRENT_FEATURES.md` - 移除 bottom RGB multiplier 说法。
- [ ] 重新截新 RenderDoc frame，确认 bottom PS 已绑定 environment cubemap 并输出接近 CK3 量级。

---

## Next Session

### Immediate Next Steps
1. 编译 SDSL asset，确认 `RiverBottom` 的 `TextureCube EnvironmentMapTexture` 与 generated keys 正确。
2. 运行 Editor 截新 `debug.rdc`。
3. 对新 capture 检查 bottom event：
   - PS bindings 应出现 `EnvironmentMapTexture`
   - PS disasm 应出现 `sample_l(texturecube)`
   - 同点 bottom RGB 不应再停留在 `~0.02`
4. 若仍偏暗，继续对照 CK3 的 sun/shadow/cubemap intensity，而不是恢复 diffuse multiplier。

### Gotchas
- 旧 `debug.rdc` 无法完整 hot-edit 新 cubemap 输入，因为原 bottom pipeline 未绑定 cube SRV。
- `reflection-specular.dds` 是真正 cube DDS；surface 当前仍把它当 `Texture2D` 采样，这是后续需要单独复查的 surface 反射问题。
- CK3 normal 使用 `UnpackRRxGNormal`，不能用普通 `xyz * 2 - 1` 解包 bottom normal。

---

## Quick Reference for Future Claude

**Key RenderDoc Events:**
- Current bottom: `192`
- Current surface: `270`
- CK3 bottom: `336`
- CK3 surface: `464`

**Root Cause:**
- 当前 bottom/refraction buffer 过暗，因为 shader 缺 CK3 bottom lighting/env/shadow 路径，而不是贴图缺失。

**Rejected Approach:**
- 不要再使用 `_BottomDiffuseMultiplier` 或 `bottomDiffuse * depthTint * N` 作为最终修复。
