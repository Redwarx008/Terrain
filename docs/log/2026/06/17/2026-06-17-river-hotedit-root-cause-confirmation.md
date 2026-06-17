# River Hot-Edit Root Cause Confirmation
**Date**: 2026-06-17
**Session**: River hot-edit root cause confirmation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 `C:\Users\Redwa\Desktop\debug.rdc` 与 `C:\Users\Redwa\Desktop\ck3-river.rdc`，在不先改 SDSL 的前提下，用 RenderDoc 热替换把当前河流与 CK3 的主要偏离点锁死。

**Secondary Objectives:**
- 核实 CK3 参考 shader 这帧实际走的是哪条 bottom 分支。
- 验证当前 bank 过亮到底是 bottom、surface，还是 pre-bottom/seed payload 语义错误。
- 尝试导出两边 cbuffer 数值，确认能否直接做参数对位。

**Success Criteria:**
- 给出 current/CK3 每个关键 pass 的功能差异和代表像素证据。
- 至少完成一轮 bottom 热替换和一轮 seed 热替换。
- 明确本轮是否适合直接改 `RiverBottom.sdsl` / `RiverSurface.sdsl`。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- See: [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 当前实现已经有 `RiverBottom.sdsl` / `RiverSurface.sdsl` 和 half-res refraction 链路，但视觉上与 CK3 河流差距很大，尤其是河心颜色和岸边高亮泄漏。

**Why Now:**
- 用户明确要求“逐个 pass 分析，并在改 SDSL 前优先做 RenderDoc 热修改”，因此必须先把“哪里错了”用 capture 级证据说清楚。

---

## What We Did

### 1. 核实 current 与 CK3 的 shader 分支并不等价
**Files Changed:** 无

**Implementation:**
- 重新读了当前 `RiverBottom.sdsl` / `RiverSurface.sdsl` 与 CK3 的：
  - `jomini_river_bottom.fxh`
  - `jomini_river_surface.fxh`
  - `jomini_water.fxh`
- 当前 `RiverBottom.sdsl` 固定走 advanced/tangent-UV 路径：
  - `BottomDiffuse/BottomNormal/BottomProperties` 都按 `tangentUv` 采样
  - 使用本地 fallback lighting，最终 `* 3.0f`
- CK3 源码同时存在两条路径：
  - `CalcRiverBottom`
  - `CalcRiverBottomAdvanced`
- `ck3-river.rdc` 的实际 bottom draw (`event 336`) 在 disasm 上明确表现为：
  - `worldUv = worldPos.xz + worldSpaceParallax * width`
  - `BottomDiffuse / BottomProperties / BottomNormal` 都用 `worldUv`
  - 这帧实际走的是 `CalcRiverBottom`，不是 advanced `tangentUv` 路径

**Rationale:**
- 这说明 current 与 CK3 不是“实现大体等价，只差参数”，而是底层就选了不同的 bottom 分支。

### 2. 用最小 bottom 热替换验证“分支错误”确实会直接改变最终河心颜色
**Files Changed:** 无

**Implementation:**
- 在 current `debug.rdc` 的 `event 184` 上，先记录代表像素基线：
  - bottom RT 半分辨率 `(172,299)`：`[0.1592, 0.1812, 0.1836, 14.0938]`
  - final surface 全分辨率 `(344,598)`：`[0.1254, 0.1425, 0.1377, 1.0]`
- 用 HLSL 热替换把 bottom 改成“保留原 distance packing，只把 `BottomDiffuse` 改成 `worldUv` 采样”的最小版本：
  - `event 184` 同像素变成：`[0.0366, 0.0281, 0.0151, 14.0938]`
  - `event 213` 同像素变成：`[0.0463, 0.0395, 0.0194, 1.0]`
- 对位 CK3 代表像素：
  - `event 466` `(1192,632)`：`[0.0461, 0.0421, 0.0357, 1.0]`

**Rationale:**
- 只改 bottom 主采样分支，current 河心最终色就直接掉到和 CK3 同一数量级。
- 这证明 bottom 分支错误不是次要噪音，而是主因之一。

### 3. 进一步验证：不是“只改成 worldUv 就行”，当前 bottom lighting 也不等价
**Files Changed:** 无

**Implementation:**
- 又做了一版更接近 CK3 `CalcRiverBottom` 的 non-advanced 热替换：
  - `Diffuse/Normal/Properties` 用 `worldUv`
  - depth 仍按河道横截面计算
  - 但继续使用 current 本地 fallback lighting
- 结果：
  - `event 184` `(172,299)` 回到 `"[0.1359, 0.1584, 0.1523, 14.0938]"`
  - `event 213` `(344,598)` 回到 `"[0.1373, 0.1628, 0.1289, 1.0]"`

**Rationale:**
- 这说明 current 与 CK3 的差异不止是 UV 主采样分支。
- 当前 bottom lighting 路径本身也和 CK3 的 `ShadowTexture + GetRiverBottomSunLightingProperties + CalculateSunLighting` 不等价。
- 因此不能只做一条 `tangentUv -> worldUv` 机械替换，就声称已经复刻 CK3。

### 4. 用 seed alpha 热替换证明：bank 问题不是“alpha 只是少传了一个值”
**Files Changed:** 无

**Implementation:**
- 当前 pre-bottom `event 157` 实际是一个简单 blit/copy 类 pass：
  - 只绑定一个输入纹理
  - 输出 RGB 就是 scene seed，alpha 当前为 `0`
- 在 `event 157` 上做最小热替换，只把 alpha 强制改成 `50.0f`：
  - `event 157` `(172,299)` 从 `[2.2480, 2.5938, 0.7549, 0.0]` 变成 `[2.2480, 2.5938, 0.7549, 50.0]`
- 结果：
  - 河心 final `(344,598)` 基本不变
  - bank 像素 `(352,705)` 从 `[4.1797, 5.0234, 1.4492, 1.0]` 进一步恶化到 `[4.3125, 5.1563, 1.5000, 1.0]`

**Rationale:**
- 这证明 current 与 CK3 的 pre-bottom 差异不是“少了一个非零 alpha”这么简单。
- 当前 seed pass 的 RGB 和 alpha 语义都不对；单独补 alpha 只会把岸边泄漏推得更严重。

### 5. 用 bank 像素 history 锁定：当前岸边高亮主要是 bright seed payload 被一路保留下来
**Files Changed:** 无

**Implementation:**
- current bank 代表像素对位：
  - half-res `(176,352)` 对应 full-res `(352,705)`
- current `debug.rdc`：
  - `event 157` pre-bottom seed：`[2.1484, 2.5977, 0.7310, 0.0]`
  - `event 184` bottom `shaderOut`：`[0.1158, 0.1269, 0.1209, 11.8770]`
  - `event 184` bottom `postMod`：`[2.0410, 2.4688, 0.6987, 0.6177]`
  - `event 213` surface `shaderOut`：`[2.0528, 2.4827, 0.7041, 0.0366]`
  - `event 213` final `postMod`：`[4.1797, 5.0234, 1.4492, 1.0]`
- CK3 `ck3-river.rdc` bank 代表像素：
  - pre-bottom `event 304` `(55,369)`：`[0.0210, 0.0383, 0.0161, 80.6875]`
  - bottom `event 338` `postMod`：`[0.2712, 0.1851, 0.1000, 81.75]`
  - surface `event 466` `shaderOut/final` `(110,738)`：`[0.0223, 0.0280, 0.0305, 1.0]`

**Rationale:**
- current bank 问题的关键不是 bottom shader 自己输出得太亮，而是：
  - pre-bottom seed 一开始就是亮 HDR terrain
  - bottom 在 bank 区域并没有把这个 seed 真正压掉
  - surface 又继续把这份亮 payload 当 see-through/refraction 输入
  - 最终 blend 继续把亮值叠上去
- CK3 则完全相反：pre-bottom 自己就是暗 payload，后续全链路都围绕这个暗底工作。

### 6. 尝试导出 cbuffer 数值，但当前工具结果不可信
**Files Changed:** 无

**Implementation:**
- current `event 213` 的 `Globals` cbuffer 可以列出 28 个变量名，但 `get_cbuffer_contents` 返回值全为 0
- CK3 `event 466` 的多个 cbuffer 同样能列出变量结构，但数值也几乎全为 0
- 示例：
  - current `Globals`：`_FlowNormalSpeed`、`_Depth`、`_MapExtent`、`_WaterSeeThroughDensity` 等全 0
  - CK3 `pdx_hlsl_cb52`：`ShadowFadeFactor`、`Bias`、`KernelScale`、`DiscSamples[]` 全 0

**Rationale:**
- 这轮 capture 上，`get_cbuffer_contents` 只能拿来确认变量结构，不能拿来做数值 parity 结论。
- 后续参数对位仍要优先依赖 disasm、pixel history 和热替换结果。

---

## Decisions Made

### Decision 1: 本轮不直接改 `RiverBottom.sdsl` / `RiverSurface.sdsl`
**Context:** 已经确定 current 与 CK3 的主要偏离点，但还没有得到一个“最小且安全”的单点 SDSL 修法。

**Options Considered:**
1. 立刻把 current bottom 改成 world-UV 路径
2. 先把 pre-bottom payload 和 bottom lighting 的非等价关系记录清楚，暂不盲改

**Decision:** 选择 2
**Rationale:** 热替换已经证明问题不止一处；现在直接改代码，风险是把错误假设固化进仓库。
**Trade-offs:** 本轮不产出运行时代码修改。
**Documentation Impact:** 新增 session log，并补充 learning。

### Decision 2: 根因优先级重新排序
**Context:** 本轮热替换后，已经能区分哪些层是真主因，哪些层是次级偏差。

**Options Considered:**
1. 继续把主要精力放在 surface
2. 把 pre-bottom payload 与 bottom path/lighting 作为主战场

**Decision:** 选择 2
**Rationale:** current 河心颜色和 bank 泄漏都能直接被上游解释；surface 只是继续放大/着色这份输入。
**Trade-offs:** 后续修复范围会比“改一个 surface 参数”大。
**Documentation Impact:** 需要在后续实现前重审 parity spec。

---

## What Worked ✅

1. **最小 bottom world-UV 热替换**
   - What: 只改 `BottomDiffuse` 的主采样分支，保留原 distance packing
   - Why it worked: 能直接量化“bottom 分支错误”对 final color 的影响
   - Reusable pattern: Yes

2. **seed alpha 单变量实验**
   - What: 只动 `event 157` 的 alpha，不动 RGB
   - Impact: 快速排除了“只是 alpha 少了”的误判

3. **bank pixel history 连链分析**
   - What: 把 `157 -> 184 -> 213` 和 `304 -> 338 -> 466` 串起来看
   - Impact: 直接证明 current bank 高亮来自 bright seed payload 泄漏

---

## What Didn't Work ❌

1. **把 current fallback lighting 继续挂在 CK3 non-advanced 采样语义上**
   - What we tried: 更接近 CK3 的 world-UV/non-advanced bottom 版，但保留 current lighting
   - Why it failed: 最终颜色又被 current lighting 拉回冷灰/偏亮
   - Lesson learned: current 与 CK3 的 bottom 差异不是纯 UV 问题
   - Don't try this again because: 否则会把“分支修正无效”误判成“branch 不是问题”

2. **只把 seed alpha 从 0 改成 50**
   - What we tried: 不动 RGB，只补 alpha
   - Why it failed: bank 泄漏反而更严重
   - Lesson learned: current pre-bottom payload 与 CK3 在 RGB/alpha 语义上都不等价
   - Don't try this again because: 它会把错误的 bright seed 更强地送进 downstream refraction 逻辑

---

## Problems Encountered & Solutions

### Problem 1: current 河心颜色偏差到底来自 bottom 还是 surface
**Symptom:** 最终截图里 current 河心和 CK3 差距很大，但 surface 逻辑也很复杂。
**Root Cause:** 单看 final frame 无法区分是 bottom 输入错，还是 surface 着色错。
**Investigation:**
- Tried: bottom world-UV 最小热替换
- Tried: 更接近 CK3 的 non-advanced bottom 热替换
- Found: 只动 bottom 分支就足以大幅改变 final center pixel

**Solution:**
```text
优先把 bottom 视为一级根因，而不是继续先调 surface。
```

**Why This Works:** current final center pixel 对 bottom 输入极其敏感。
**Pattern for Future:** 河心问题先打 bottom 最小热替换，再决定是否值得往 surface 深挖。

### Problem 2: current 岸边高亮到底来自 bottom 太亮还是 bright seed 泄漏
**Symptom:** bank 位置比 CK3 亮一个数量级以上。
**Root Cause:** current pre-bottom 本质是 HDR scene seed copy，CK3 pre-bottom 是独立暗 payload。
**Investigation:**
- Tried: seed alpha 0 -> 50 热替换
- Tried: bank pixel history 串 `157/184/213`
- Found: current bottom shaderOut 已经不高，但 postMod 仍保留 scene-like 亮值

**Solution:**
```text
把 pre-bottom payload 语义错误列为独立根因，不再把它简化成“alpha 没传”。
```

**Why This Works:** 这条证据链能解释 current bank 为什么在 bottom 和 surface 之后都还保留亮 terrain 色。
**Pattern for Future:** bank 泄漏分析必须同时看 pre-bottom、bottom postMod、surface shaderOut。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 记录 pre-bottom payload 不能简化成 scene copy + alpha 的坑
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本轮没有系统状态变化
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本轮没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 CK3 pre-bottom 当成“scene copy + 非零 alpha”
- What not to do: 只给 current seed 补一个高 alpha，就当成在逼近 CK3
- Why it's bad: 实际会放大 bank 泄漏，不能复现 CK3 暗底输入
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮只做 RenderDoc 诊断与热替换

---

## Code Quality Notes

### Performance
- **Measured:** 无
- **Target:** 本轮目标是诊断准确性
- **Status:** ⚠️ 未涉及

### Testing
- **Tests Written:** 无
- **Coverage:** current/CK3 bottom、pre-bottom、surface 的代表像素、资源绑定、热替换行为
- **Manual Tests:** 如后续进入实现，先重新截一帧与新代码对应的 `.rdc`

### Technical Debt
- **Created:** 无运行时代码债务
- **Paid Down:** “只看 surface 参数”和“只补 seed alpha”这两条误判路径
- **TODOs:** 后续真正修改 SDSL 前，先重新定义 parity 目标：是移植 CK3 non-advanced bottom 路径，还是保留 current advanced 路径但重建等价 lighting/payload

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 设计并验证 current 的独立 pre-bottom payload，而不是继续把 scene color 直接 seed 到 half-res working RT
2. 决定 `RiverBottom` 的 parity 目标：
   - 回到 CK3 这帧实际使用的 non-advanced `CalcRiverBottom`
   - 或者明确保留 advanced 路径，但补足等价 lighting 输入与纹理语义
3. 只有在 pre-bottom payload 与 bottom lighting 语义对齐后，再回头细调 surface

### Blocked Items
- **Blocker:** 无硬阻塞
- **Needs:** 如果要继续做参数级 parity，对 current/CK3 需要新的 capture 或新的 cbuffer 导出手段
- **Owner:** Codex

### Questions to Resolve
1. 当前 half-res 链路是否要新增一个 CK3 风格的独立 pre-bottom draw，而不是继续用 copy/scaler seed？
2. `RiverBottom.sdsl` 应该直接回到 non-advanced world-UV 分支，还是保留 advanced 但重构 lighting 语义？
3. current `Surface` 是否还需要在上游修完后再做第二轮 diff，而不是现在先改？

### Docs to Read Before Next Session
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md) - 本轮之前的 pass 拓扑证据
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 本轮新增了 pre-bottom payload 相关规则

---

## Session Statistics

**Files Changed:** 2
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: current `event 157` 只是 scene-color seed/copy 类 pass；CK3 `event 304` 是独立暗 payload
- Critical decision: 本轮不改运行时代码，因为最小安全修法还没被热替换证明
- Active pattern: `source/disasm -> hot-replace -> representative pixels -> bank pixel history`
- Current status: 已确认 current 主要错在 pre-bottom payload 和 bottom path/lighting，不在 surface 主逻辑

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无运行时代码改动
- Constraints: `get_cbuffer_contents` 在 current/CK3 这两份 capture 上都不能可靠给出真实数值

**Gotchas for Next Session:**
- Watch out for: 不要再把“seed alpha = 0”当成唯一根因
- Don't forget: current world-UV diffuse-only 替换能把河心 final color 直接拉近 CK3
- Remember: 更接近 CK3 的 non-advanced 采样语义如果继续挂 current lighting，效果仍会被拉回去

---

## Links & References

### Related Documentation
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_bottom.fxh`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh`

### Code References
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

---

## Notes & Observations

- 本轮最强的 current 证据不是“哪条 shader 更像 CK3”，而是 current bank 像素从 pre-bottom 开始就已经拿到了错误的 bright payload。
- CK3 的 bottom/source 分支问题和 current 的 pre-bottom payload 问题是叠加关系，不是二选一。

---
