# River Bottom 热替换验证
**Date**: 2026-06-16
**Session**: river-bottom-hotreplace-validation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在 `C:\Users\Redwa\Desktop\debug.rdc` 中直接热替换 `184` 的 `RiverBottom` 像素着色器，验证“bottom pass 过暗”是否主要由当前 bottom lighting 导致。

**Success Criteria:**
- 不改仓库运行时代码，只用 RenderDoc 热替换得到可复核结论。
- 区分“bottom 自身确实被压暗”与“整体最终画面差距主要不在 bottom lighting”。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-bottom-ck3-332-vs-debug-184-contrast-analysis.md](./2026-06-16-river-bottom-ck3-332-vs-debug-184-contrast-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- `debug.rdc` 的 `184` 已能输出河床，但用户观察到 bottom pass 很暗。
- 先前对比表明当前工程的 seed 场景比 CK3 亮一个数量级，且 bottom 使用固定 cubemap 近似，不是 CK3 的场景级 light/environment 语义。

---

## What We Did

### 1. 建立基线
**Files Changed:** 无代码改动

- 基线 capture：`C:\Users\Redwa\Desktop\debug.rdc`
- 关键 draw：
  - bottom: `184`
  - surface/final: `213`
- 基线中心像素：
  - `184 @ (420,276)` → `shaderOut ≈ (0.1521, 0.1422, 0.0997, 17.8767)`
  - `213 @ (840,552)` → `postMod ≈ (0.1504, 0.1836, 0.1760, 1.0)`

### 2. 粗热替换：直接输出 `BottomDiffuse`
- 第一个热替换版本只输出 `BottomDiffuse.rgb`，但没有保住原始 `RT0.a` 的 bottom-distance 打包。
- 结果：
  - `184 @ (420,276)` 明显变亮：
    - `shaderOut ≈ (0.2411, 0.1970, 0.1294, 4342.1)`（`a` 已失真）
    - `postMod ≈ (0.4653, 0.4739, 0.1965, 3980.0)`
- 结论：
  - 当前 bottom lighting 确实把河床压暗了。
  - 但这个版本不能用于判断 `213` 的最终图，因为错误的 `o0.a` 会连带破坏 surface 的折射深度解释。

### 3. 改进热替换：保留原始 depth/alpha 语义，只替换 RGB
- 通过原 shader 公式近似重建：
  - `RiverDepthFromCrossSection`
  - `worldWidth`
  - `bottomEdgeFade / connectionFade`
  - `bottomWorldPosition`
  - `compressedWorld ≈ distance(approxCamera, bottomWorldPosition)`
- 只把 `color = CalculateRiverBottomLighting(...)` 改为 `color = BottomDiffuse.rgb`
- 关键验证：
  - `184 @ (420,250)` 的 `o1 alpha` 与原始值对齐到 `0.300086`
  - `o0.w` 也从错误的 `4342` 回到 `18~19` 量级

### 4. 对比改进热替换后的结果
- bottom RT：
  - `184 @ (420,276)` 原始 `shaderOut ≈ (0.1521, 0.1422, 0.0997, 17.8767)`
  - 热替换后 `shaderOut ≈ (0.2411, 0.1970, 0.1294, 19.0376)`
  - 说明 bottom lighting 的确让 bottom 自身偏暗。
- 最终图：
  - `213 @ (840,552)` 原始 `postMod ≈ (0.1504, 0.1836, 0.1760, 1.0)`
  - 热替换后 `postMod ≈ (0.1561, 0.1870, 0.1805, 1.0)`
  - 变化极小。
- 岸边像素：
  - `213 @ (840,500)` 原始 `postMod ≈ (3.1953, 3.5469, 1.0713, 1.0)`
  - 热替换后 `postMod ≈ (3.2070, 3.5508, 1.0723, 1.0)`
  - 几乎不变。

---

## Conclusions

1. **当前 bottom lighting 的确会把 `184` 的河床压暗。**
   - 直接把颜色改成 `BottomDiffuse.rgb` 后，bottom RT 的中心像素从约 `0.152/0.142/0.100` 提高到约 `0.241/0.197/0.129`。

2. **但这不是当前“整体和 CK3 差距很大”的主要原因。**
   - 在尽量保住 `RT0.a` / `o1 alpha` 后，`213` 的最终像素只发生了很小变化。
   - 说明最终视觉差距主要仍然在 surface/refraction/seed 场景亮度语义，而不是单纯 bottom lighting 太暗。

3. **因此后续优先级不应是继续调 bottom 亮度。**
   - 更该继续查：
     - surface pass 如何消费 bottom RT
     - refraction / water-color / see-through 路径
     - 亮 seed 地表与 alpha 混合带来的相对对比度失真

---

## Documentation Impact

- 已更新：
  - `docs/log/learnings/stride-river-rendering-patterns.md`
    - 新增“热替换若破坏 bottom-distance，不要用 final surface 图做颜色结论”的经验。

---

## Next Session

### Immediate Next Steps
1. 对比 `213` 与 CK3 对应 surface pass，继续看 refraction/bottom RT 的消费路径。
2. 在 `debug.rdc` 中优先抽岸边像素做 pixel history/debug，确认为什么 bottom 提亮后 final 几乎不变。
3. 若要继续热替换，必须保持 `RT0.a` 与 `o1` 语义，否则只看 `184`，不看 `213`。

### Questions to Resolve
1. 当前 `RiverSurface` 对 bottom RT RGB 的权重到底有多大？
2. 岸边 CK3 可见的河床颜色，当前是被 surface 自身水色盖掉了，还是 refraction world-position 本身就错了？

---

## Session Statistics

**Files Changed:** 2
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- RenderDoc 热替换已验证：bottom lighting 会让 `184` 变暗，但不是最终差距主因。
- 可信证据优先级：
  - `184` bottom RT / pixel history：可信
  - 破坏 `RT0.a` 的 `213` 最终图：不可信
  - 近似保住 `RT0.a` 后的 `213` 最终图：可用于辅助结论

**Gotchas for Next Session:**
- `get_cbuffer_contents` 在这个 capture 上仍返回全零，不能直接读运行时常量。
- 如果要热替换 `RiverBottom`，必须保住 `o0.a` 的 bottom-distance 打包，否则 surface 会一起跑偏。
