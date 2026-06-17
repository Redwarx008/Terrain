# 河流 bottom pass 明暗对比分析（CK3 332 vs 当前 184）
**Date**: 2026-06-16
**Session**: river-bottom-contrast-analysis
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 `C:\Users\Redwa\Desktop\debug.rdc` event `184` 和 `C:\Users\Redwa\Desktop\ck3-river.rdc` event `332`，判断当前 bottom pass 为什么“看起来很暗”。

---

## What We Did

### 1. 对比两边 bottom shader 结构
- 当前 `184` 已经是 world-space bottom UV 版本：BottomDiffuse/Normal/Properties/Depth 使用 `worldPosition.xz` 采样，depth profile 也已切到 CK3 的 cosine 轮廓。
- CK3 `332` 仍是 `CalcRiverBottom` 路径，使用 `WorldUV = Input.WorldSpacePos.xz + WorldSpaceParallax * Input.Width`。
- 当前与 CK3 的剩余主要 shader 级差异：当前没有 CK3 的 iterative parallax offset，也没有接入 CK3 `CalculateSunLighting/GetRiverBottomSunLightingProperties` 那套场景光照与 shadow 流程。

### 2. 对比可比河心像素
- CK3 `332 @ (450,468)`：`UV.y≈0.490`，bottom shader 输出 `RGB≈(0.118, 0.082, 0.045)`。
- 当前 `184 @ (420,276)`：`UV.y≈0.459`，bottom shader 输出 `RGB≈(0.160, 0.151, 0.108)`。
- 结论：当前 bottom shader 的绝对输出并不比 CK3 更暗，数值反而更高，只是更偏灰、偏黄，缺少 CK3 那种暖棕色。

### 3. 对比 seed 场景亮度
- CK3 同一可比像素在 seed draw `304` 后约为 `RGB≈(0.049, 0.057, 0.024)`，bottom 写入后变为 `RGB≈(0.118, 0.081, 0.045)`。
- 当前同一可比像素在 seed draw `157` 后约为 `RGB≈(2.96, 3.55, 0.94)`，bottom 写入后变为 `RGB≈(0.160, 0.151, 0.108)`。
- 结论：当前“黑沟感”的主因不是 bottom shader 绝对值太低，而是 copied scene seed 比 CK3 亮一个数量级以上，河床相对周围地表过暗。

### 4. 对比 IBL / 环境资源语义
- 当前本地代码中，`RiverRenderFeature` 直接把 `riverResources.ReflectionSpecular` 绑定给 `RiverBottomKeys.EnvironmentMapTexture`。
- 当前 capture 中 `184/213` 使用的环境资源是一个 BC3 cubemap（RenderDoc `ResourceId::547`）。
- CK3 `332` shader 明确走 scene environment lighting 路径，使用 `EnvironmentMap_Texture`、`CubemapIntensity`、`CubemapYRotation` 和 shadow 相关常量。
- 这意味着当前 bottom IBL 仍是“固定资源近似”，不是 CK3 的场景级 environment lighting。

---

## Conclusions

1. 当前 `184` 的 bottom shader 绝对输出不比 CK3 `332` 更暗，问题不在 world-UV 修复本身。
2. 当前视觉差距的主要来源是 scene seed 亮度和场景 lighting 语义，而不是 bottom diffuse 采样本身。
3. 若继续向 CK3 靠拢，优先级应是：
   - 让 bottom lighting 接入更真实的场景 sun/environment/shadow，而不是继续调底图亮度倍率。
   - 检查河流所在区域的地表 seed 是否本身过亮，导致 riverbed 相对对比度失真。

---

## Verification

- `debug.rdc` event `184` / `213` RenderDoc 检查完成
- `ck3-river.rdc` event `332` RenderDoc 检查完成
- 无代码功能修改，仅补充文档与结论沉淀
