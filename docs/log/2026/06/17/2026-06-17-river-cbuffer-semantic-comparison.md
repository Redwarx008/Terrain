# River CBuffer Semantic Comparison
**Date**: 2026-06-17
**Session**: river-cbuffer-semantic-comparison
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 current `debug.rdc` 与 `ck3-river.rdc` 的 CBuffer 参数语义，而不是只看 RT 输出截图。

**Secondary Objectives:**
- 结合 `seed -> bottom -> surface` 三层 pass 输出，判断差异是“参数数值偏了”还是“buffer 语义本身不同”。
- 把 current 运行时代码中真正绑定到 shader 的 river 参数与 CK3 shader 代码路径对齐。

**Success Criteria:**
- 说明 current 与 CK3 哪些常量缓冲字段是一致的，哪些字段缺失、被硬编码、或语义不同。
- 给出至少一个可以直接解释 current/CK3 输出差异的 CBuffer 级根因。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- See: [2026-06-17-river-bottom-lighting-energy-gain.md](./2026-06-17-river-bottom-lighting-energy-gain.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture 的完整链路已经确认是 `157 seed -> 158 copy -> 184 bottom -> 213 surface`。
- 用户明确要求继续比较 CBuffer 参数本身，而不是停留在截图差异。

**Why Now:**
- 如果共享 river 参数已经对齐，那么接下来的重点就不该继续浪费在 `BankFade/Depth/ParallaxIterations` 这类字段上，而应该转向 buffer payload 和缺失的 water/light 常量。

---

## What We Did

### 1. 对齐 current runtime 绑定参数与 CK3 `JominiRiver`
**Files Changed:** 无

**Implementation:**
- 读取 current：
  - `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
  - `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
  - `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
  - `Terrain.Editor/Effects/RiverBottom.sdsl`
  - `Terrain.Editor/Effects/RiverSurface.sdsl`
- 对比 CK3：
  - `jomini/gfx/FX/jomini/jomini_river.fxh`

**Rationale:**
- current 的 `_TextureUvScale/_FlowNormalUvScale/_FlowNormalSpeed/_RiverFoamFactor/_NoiseScale/_NoiseSpeed/_OceanFadeRate/_BankAmount/_BankFade/_Depth/_DepthWidthPower/_DepthFakeFactor/_ParallaxIterations` 这组共享 river 字段，布局和默认值基本都与 CK3 `JominiRiver` 对应。
- 结论：shared river CBuffer 不是这轮差异的主根因。

### 2. 锁定 first root cause：current seed pass 主动把 half-res alpha payload 清零
**Files Changed:** 无

**Implementation:**
- 读取 `RiverRenderFeature.cs`，确认：
  - `refractionSeedScaler.Color = new Color4(1.0f, 1.0f, 1.0f, 0.0f);`
- 与 capture 对照：
  - current `157` 代表像素：`(2.260, 2.625, 0.816, 0)`
  - CK3 `304` 代表像素：`(0.0353, 0.0420, 0.0169, 47.80)`
  - current `seed_half` alpha `min/max = 0/0`
  - CK3 `49006` alpha `min/max = 41.09/195.13`
- 复核 CK3 代码：
  - `jomini_mesh_helper.fxh` 在 `UNDERWATER` 路径会返回 `CompressWorldSpace(WorldSpacePos)`

**Rationale:**
- current seed pass 不是 CK3 那种 pre-bottom terrain/refraction pass，而只是 scene copy/scaler，并且显式把 alpha 乘成了 `0`。
- 这使得 current half-res working RT 从第一层开始就没有 CK3 那份 distance payload。

### 3. 对齐 bottom pass：差异在 lighting CBuffer 语义，不只是数值
**Files Changed:** 无

**Implementation:**
- current bottom 在 `RiverBottom.sdsl` 中引入并绑定：
  - `_BottomSunDirection`
  - `_BottomSunColor`
  - `_BottomSunIntensity`
  - `_BottomEnvironmentIntensity`
  - `_BottomSpecularIntensity`
  - `_ShadowTermFallback`
  - `_CloudMaskFallback`
- current 运行时还在 `EmbeddedStrideViewportGame.cs` 中覆盖：
  - `BottomSunDirection = (0.32898992, 0.81915206, 0.4698463)`
  - `BottomSunColor = (1,1,1)`
  - `BottomSunIntensity = 2.0`
- CK3 bottom 代码则走：
  - `GetRiverBottomSunLightingProperties(..., ShadowTexture)`
  - `CalculateSunLighting(MaterialProps, LightingProps, EnvironmentMap)`
- capture 对照：
  - current `184` 像素：`(0.1016, 0.1129, 0.1079, 13.36)`
  - CK3 `332` 像素：`(0.1099, 0.0735, 0.0424, 49.72)`

**Rationale:**
- current 不是“底部受光太弱”，因为运行时已经把 `BottomSunIntensity` 提到 `2.0`，shader 里还追加了 `* 3.0f`。
- 问题在于 current 走的是本地 fallback lighting 语义；CK3 则走全局 sun/shadow/material lighting 语义。

### 4. 对齐 surface pass：current 只保留了 `JominiWater` 的一个子集
**Files Changed:** 无

**Implementation:**
- current `RiverSurface.sdsl` 只暴露并绑定了：
  - `_FlowNormal*`
  - `_RiverFoamFactor`
  - `_Noise*`
  - `_BankFade`
  - `_Depth/_DepthWidthPower`
  - `_FlatMapLerp/_ZoomBlendOut`
  - `_ShadowTermFallback/_CloudMaskFallback`
  - `_WaterDiffuseMultiplier`
  - `_WaterColorMapTint*`
  - `_WaterFadeShoreMask*`
  - `_WaterSeeThrough*`
  - `_WaterFresnel*`
  - `WaterColorShallow/Deep`
- CK3 `JominiWater` 还包含而 current 缺失或硬编码的字段：
  - `_WaterToSunDir`
  - `_WaterSpecular`
  - `_WaterSpecularFactor`
  - `_WaterGlossScale/_WaterGlossBase`
  - `_WaterCubemapIntensity`
  - `_WaterFoamScale/_WaterFoamDistortFactor/_WaterFoamShoreMask*`
  - `_WaterRefractionScale/_WaterRefractionShoreMask* / _WaterRefractionFade`
  - 三组 wave 参数
  - `_WaterFlowMapSize/_WaterFlowNormalScale/_WaterFlowNormalFlatten`
  - `_WaterHeight`
- current shader中还直接硬编码：
  - `refractionOffset = flowNormal.xz * (0.0025 + depthFactor * 0.0035)`
  - `reflectionColor = specularMask * float3(0.18, 0.24, 0.25)`
- capture 对照：
  - current `213` 像素：`(0.0748, 0.1785, 0.2229)`
  - CK3 `460` 像素：`(0.2253, 0.1376, 0.0611)`

**Rationale:**
- current surface 不是简单“参数没调准”，而是整套水面 CBuffer 维度明显比 CK3 少。
- 这会直接限制它复现 CK3 的 refraction / foam / gloss / reflection 行为。

---

## Decisions Made

### Decision 1: shared river CBuffer 不再作为首要怀疑对象
**Context:** `JominiRiver` 这组字段在 current 和 CK3 源码中非常接近。

**Options Considered:**
1. 继续围绕 `Depth/BankFade/TextureUvScale` 做参数微调
2. 转向 seed alpha payload、bottom lighting 语义和 surface water 常量完整度

**Decision:** 选择 2
**Rationale:** 现有 capture 已经证明首个硬差异发生在 seed alpha 和 water/light 语义，而不是 shared river shape 参数。
**Trade-offs:** 不再把精力花在低价值的数值微调上。
**Documentation Impact:** 记录在本 session log

---

## What Worked ✅

1. **源码绑定路径与 capture 联合比对**
   - What: 先核对 `RiverRenderFeature` 真实参数绑定，再回看 RenderDoc 输出
   - Why it worked: 能区分“编译期默认值”和“运行时实际传值”
   - Reusable pattern: Yes

2. **把 seed alpha 单独拿出来分析**
   - What: 单独比较 `SceneSeedColor` 的 alpha 语义
   - Impact: 直接发现 first root cause，不再把问题全压到 bottom/surface

---

## What Didn't Work ❌

1. **只从 `debug pixel summary` 反推全部 CBuffer 值**
   - What we tried: 依赖 pixel debug summary 直接读所有常量
   - Why it failed: CLI 只能稳定给输入/输出，不能像完整 RenderDoc 绑定面板那样列出整块 CBuffer 内容
   - Lesson learned: 当前环境下要用“运行时代码绑定 + capture 输出 + CK3 shader 代码”三方交叉验证
   - Don't try this again because: 信息不完整，容易把默认值误当成运行时值

---

## Problems Encountered & Solutions

### Problem 1: generated HLSL 中的常量注释可能误导为运行时实值
**Symptom:** 反汇编注释里有 `_BottomSunIntensity = 1.35` 等值，但 runtime 代码里已经改成 `2.0`
**Root Cause:** HLSL 注释反映的是生成/默认常量，不是 capture 中所有绑定后的最终参数
**Investigation:**
- Tried: 直接看 generated HLSL buffer 注释
- Tried: 对照 `RiverRenderFeature`、`EmbeddedStrideViewportGame`
- Found: runtime 绑定路径会覆盖 shader 默认值

**Solution:**
```text
以 runtime 参数绑定代码为准，再用 capture 输出验证后果
```

**Why This Works:** 把“默认值”和“实际值”分离后，参数分析才不会跑偏。
**Pattern for Future:** 分析 Stride shader 参数时，必须同时看 `*.sdsl`、`RenderFeature` 绑定和 runtime 初始化代码。

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 暂不需要，本轮没有架构变更
- [ ] Update `CURRENT_FEATURES.md` - 暂不需要，本轮没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先分离 shared river CBuffer 与 water/light CBuffer
- When to use: 河流效果与参考实现差距大、但基础形状参数看起来已对齐时
- Benefits: 能快速识别问题是在 shared shape、buffer payload，还是 lighting/water 语义
- Add to: 本 session log

**New Anti-Pattern:** 把 generated HLSL 注释当作 capture 实际绑定参数
- What not to do: 仅凭 `shader_*.hlsl` 里的常量注释推断 runtime 值
- Why it's bad: runtime 绑定代码可能已经覆盖
- Add warning to: 后续 river RenderDoc 分析流程

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮仅做参数语义分析

---

## Code Quality Notes

### Performance
- **Measured:** 无
- **Target:** 本轮目标是诊断准确性
- **Status:** ⚠️ Close

### Testing
- **Tests Written:** 无
- **Coverage:** RenderDoc 输出、current runtime 参数绑定、CK3 shader 代码路径
- **Manual Tests:** 如继续推进，优先验证 seed/pre-bottom alpha payload 修正后的 capture

### Technical Debt
- **Created:** 无代码债务
- **Paid Down:** “shared river 参数也许不对”这一类诊断债务
- **TODOs:** 如能恢复稳定 hot-edit，应先验证修复 seed alpha payload 对 `184/213` 的连锁影响

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 继续验证 current seed/off-river alpha payload 为 `0` 对 surface `refractionDepth` 的具体影响
2. 恢复可用的 capture 内热替换，优先实验“保留 RGB、仅恢复 seed alpha payload”方向
3. 只有当 seed 语义靠近 CK3 后，再讨论 bottom/surface 的数值调参与 SDSL 修改

### Blocked Items
- **Blocker:** `renderdoc-mcp` transport 仍不稳定，完整绑定面板不可用
- **Needs:** 稳定的热替换或更完整的 RenderDoc API 访问
- **Owner:** Codex

### Questions to Resolve
1. current 是否需要像 CK3 一样，在 pre-bottom/underwater seed 阶段就写入可解压 world-distance alpha？
2. current bottom 是否需要回到 CK3 capture 实际使用的 world-UV/non-advanced 变体，而不是继续沿着 advanced tangent-UV 路径补丁？

### Docs to Read Before Next Session
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md) - pass 分层证据
- [2026-06-17-river-bottom-lighting-energy-gain.md](./2026-06-17-river-bottom-lighting-energy-gain.md) - 当前代码状态中的 `* 3.0f`

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +239/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `RiverRenderFeature` 的 seed scaler 当前把 alpha 乘成 `0`
- Critical decision: shared river CBuffer 暂时不是主战场，先看 seed alpha payload 和 water/light 语义
- Active pattern: runtime 参数绑定代码 + capture 输出 + CK3 shader 源码三方交叉验证
- Current status: 已确认 current/CK3 的第一层差异发生在 half-res seed alpha 语义

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无运行时代码改动
- Constraints: `renderdoc-mcp` 仍不可直接依赖

**Gotchas for Next Session:**
- Watch out for: 不要把 generated HLSL 注释常量当成 runtime capture 实值
- Don't forget: current `RiverSurface` 会直接用 `RefractionTexture.a` 解压世界位置
- Remember: CK3 `JominiWater` 比 current `RiverSurface` 暴露的参数多很多

---

## Links & References

### Related Documentation
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- [2026-06-17-river-bottom-lighting-energy-gain.md](./2026-06-17-river-bottom-lighting-energy-gain.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\buffer_data\buffer_compare_summary.json`

### Code References
- current seed alpha write: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs:153`
- current bottom shader params: `Terrain.Editor/Effects/RiverBottom.sdsl:5-27`
- current surface shader params: `Terrain.Editor/Effects/RiverSurface.sdsl:5-32`
- CK3 shared river params: `jomini/gfx/FX/jomini/jomini_river.fxh:4-21`
- CK3 bottom shader paths: `jomini/gfx/FX/jomini/jomini_river_bottom.fxh:223-344`
- CK3 water params: `jomini/gfx/FX/jomini/jomini_water.fxh:6-64`

---

## Notes & Observations

- current 和 CK3 最不像的地方，不是 `BankFade` 或 `Depth` 数值，而是 first-pass buffer payload 和后续 water/light 常量语义。
- 只要 current seed 还是 `alpha = 0`，surface 用 `RefractionTexture.a` 做世界坐标反解这条链就天然和 CK3 不同。

---

*Template Version: 1.0 - Based on Archon-Engine template*
