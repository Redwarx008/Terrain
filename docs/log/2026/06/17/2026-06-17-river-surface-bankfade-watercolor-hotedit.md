# River Surface BankFade And WaterColor Hot-Edit
**Date**: 2026-06-17
**Session**: river-surface-bankfade-watercolor-hotedit
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不改项目 SDSL/runtime 的前提下，继续用 RenderDoc 热替换验证当前 `RiverSurface` 的两个主要嫌疑项：
  - `_BankFade`
  - `WaterColorShallow/Deep`

**Secondary Objectives:**
- 先构造一份与 current `event 213` 完全等价的 HLSL replacement，确保后续单变量实验有隔离意义。
- 用像素和整图差分分别判断“边缘问题”和“主河道主体色问题”。

**Success Criteria:**
- 证明 `surface_current_like.hlsl` 能逐像素复现 current `213`。
- 分别量化：
  - `_BankFade 0.15 -> 0.025` 改了什么
  - `WaterColorShallow/Deep -> CK3 值` 改了什么
- 给出下一步应优先改 edge 还是优先改 core water/refraction 的排序。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-seed-alpha-hotedit-propagation.md](./2026-06-17-river-seed-alpha-hotedit-propagation.md)
- See: [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 上一轮 hot-edit 已经证明 seed alpha 只解释 bank-edge/refraction leak，不解释主河道中心的青蓝主体色。
- 因此 surface 现在要拆成两类问题继续查：
  - edge fade / bank-edge 覆盖带宽
  - 主河道主体水色 / refraction 叠加语义

**Why Now:**
- current `debug_e213_ps.json` 和 CK3 `ck3_e460_ps.json` 已明确表明：
  - `_BankFade`：current `0.15`，CK3 `0.025`
  - `WaterColorShallow/Deep`：current 的 G/B 量级远高于 CK3

---

## What We Did

### 1. 先手写一份与 current `213` 完全等价的 HLSL
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_current_like.hlsl`
- 用 RenderDoc reflection 确认真实输入语义：
  - `POSITION_WS`
  - `SV_Position`
  - `TEXCOORD1`
  - `TEXCOORD4`
  - `TEXCOORD5`
  - `TEXCOORD0`
- 手写 HLSL 时保留 current shader 的完整路径：
  - `RefractionTexture.a -> RiverDecompressWorldSpace`
  - `WaterColorShallow/Deep -> waterDiffuse`
  - `ComputeFoam`
  - `ReflectionSpecularTexture`
  - `_BankFade` alpha edge fade

**Rationale:**
- 不先构造 current-like replacement，就没法证明后续变化真的是某个参数造成的，而不是 HLSL 近似误差。

### 2. 验证 `surface_current_like.hlsl` 与 current `213` 逐像素一致
**Files Changed:** 无仓库代码

**Implementation:**
- 把 `surface_current_like.hlsl` 替到 `event 213`。
- 代表中心像素 `(344,598)`：
  - baseline = hot = `(0.06964, 0.17822, 0.22668, 1.0)`
- 代表边缘像素 `(352,705)`：
  - baseline = hot = `(1.73828, 1.81934, 0.78271, 1.0)`
- 整图差分：
  - `changed_pixels = 0`
  - `max_rgba = (0,0,0,0)`

**Rationale:**
- 说明这份 HLSL 已经足够作为后续单变量实验的可信基线。

### 3. 热替换 `_BankFade = 0.025`
**Files Changed:** 无仓库代码

**Implementation:**
- 新增：
  - `surface_bankfade_0025.hlsl`
- 只改最终 edge fade：
  - `bankFade = 0.025f`
- 中心像素 `(344,598)`：
  - baseline = hot = `(0.06964, 0.17822, 0.22668, 1.0)`
- 边缘像素 `(352,705)`：
  - baseline：`(1.73828, 1.81934, 0.78271, 1.0)`
  - hot：`(1.67969, 1.70703, 0.73926, 1.0)`
- 整图差分统计：
  - `changed_pixels = 94,267 / 1,665,312`，约 `5.66%`
  - bbox：`x=0..1671, y=317..773`
  - diff 图只沿河岸形成一条窄带

**Rationale:**
- `_BankFade` 的作用域非常明确：它只在 bank-edge 带上工作，不碰主河道中心。
- 这和上一轮 seed-alpha 实验完全一致，进一步证明 edge 类问题和 core water color 是两条独立问题线。

### 4. 热替换 `WaterColorShallow/Deep -> CK3 值`
**Files Changed:** 无仓库代码

**Implementation:**
- 新增：
  - `surface_watercolor_ck3.hlsl`
- 只改：
  - `WaterColorShallow = (0.0055146, 0.0078107, 0.0120865)`
  - `WaterColorDeep = (0.0001385, 0.0001975, 0.0002263)`
- 中心像素 `(344,598)`：
  - baseline：`(0.06964, 0.17822, 0.22668, 1.0)`
  - hot：`(0.07123, 0.09625, 0.08972, 1.0)`
- 边缘像素 `(352,705)`：
  - baseline = hot = `(1.73828, 1.81934, 0.78271, 1.0)`
- 整图差分统计：
  - `changed_pixels = 165,250 / 1,665,312`，约 `9.92%`
  - bbox：`x=0..1671, y=351..719`
  - diff 图主要覆盖整条河心主体

**Rationale:**
- 这说明主河道中心的青蓝主体色，确实有很大一部分直接来自 `WaterColorShallow/Deep`。
- 但它也没有把 current 直接拉成 CK3：R 几乎不升，只是 G/B 明显下来了，说明 bottom/refraction 仍在强烈主导最终颜色。

### 5. 组合热替换：`_BankFade=0.025 + CK3 WaterColor`
**Files Changed:** 无仓库代码

**Implementation:**
- 新增：
  - `surface_bankfade_0025_watercolor_ck3.hlsl`
- 中心像素 `(344,598)`：
  - hot：`(0.07123, 0.09625, 0.08972, 1.0)`，与 `watercolor_ck3` 完全一致
- 边缘像素 `(352,705)`：
  - hot：`(1.67969, 1.70703, 0.73926, 1.0)`，与 `bankfade_0025` 完全一致
- 整图差分统计：
  - `changed_pixels = 259,517 / 1,665,312`，约 `15.58%`
  - bbox：`x=0..1671, y=317..773`

**Rationale:**
- 这证明两者几乎是正交影响：
  - `_BankFade` 负责边缘
  - `WaterColorShallow/Deep` 负责主体河心色相

### 6. 额外验证：把 `waterDiffuse` 直接清零
**Files Changed:** 无仓库代码

**Implementation:**
- 新增：
  - `surface_waterdiffuse_zero.hlsl`
- 只改：
  - `float3 waterDiffuse = 0.0f.xxx;`
- 中心像素 `(344,598)`：
  - baseline：`(0.06964, 0.17822, 0.22668, 1.0)`
  - hot：`(0.06964, 0.09406, 0.08630, 1.0)`
- 边缘像素 `(352,705)`：
  - baseline = hot = `(1.73828, 1.81934, 0.78271, 1.0)`
- 整图差分统计：
  - `changed_pixels = 165,262 / 1,665,312`
  - bbox：`x=0..1671, y=351..719`

**Rationale:**
- 这个结果和 `WaterColorShallow/Deep -> CK3` 非常接近：
  - `watercolor_ck3`：`(0.07123, 0.09625, 0.08972)`
  - `waterDiffuse=0`：`(0.06964, 0.09406, 0.08630)`
- 说明 CK3 的这组 `WaterColorShallow/Deep` 对 current 来说几乎等价于“把 current 的 surface-local water diffuse 关到接近 0”。
- 同时中心像素 `R` 在 `waterDiffuse=0` 时完全不变，这直接证明当前河心的 `R` 几乎完全不来自 `waterDiffuse`，而是来自 refraction/bottom 路径。

---

## Decisions Made

### Decision 1: `_BankFade` 不是主河道主体色根因
**Context:** current `_BankFade=0.15` 与 CK3 `0.025` 相差 6 倍。
**Options Considered:**
1. 把 `_BankFade` 继续视为 current/CK3 大差距的主要原因
2. 先热替换验证它到底改的是边缘还是主体

**Decision:** 选择 2
**Rationale:** 实验表明 `_BankFade` 只动 edge band，中心像素完全不变。
**Trade-offs:** 它仍然是要修的问题，但优先级低于主河道主体色。

### Decision 2: `WaterColorShallow/Deep` 是 surface 主体色的一阶因素，但不是唯一因素
**Context:** current/CK3 这组值量级差距极大。
**Options Considered:**
1. 继续只看 bottom/refraction
2. 先把 surface-local 水色换成 CK3 值，测主河道中心响应

**Decision:** 选择 2
**Rationale:** 中心像素立刻从 `0.0696/0.1782/0.2267` 变成 `0.0712/0.0963/0.0897`，G/B 大幅下降，证明它是一阶因素。
**Trade-offs:** 但 R 仍明显过低，说明 bottom/refraction 仍要继续追。

### Decision 3: current 河心的 R 通道主导来源已锁定为 refraction/bottom，而不是 waterDiffuse
**Context:** 只改 `WaterColorShallow/Deep` 还不足以判断 R 来自哪里。
**Options Considered:**
1. 继续从 surface-local 水色里推断 R 来源
2. 直接把 `waterDiffuse` 清零，看中心像素还剩什么

**Decision:** 选择 2
**Rationale:** `waterDiffuse=0` 后中心像素 R 完全不变，只是 G/B 明显下降，因此可以直接排除“waterDiffuse 主导 R”的可能。
**Trade-offs:** 这不是最终修法，但它把下一轮重点非常明确地推回了 refraction/bottom。

---

## What Worked ✅

1. **先做 current-like 完全等价 replacement**
   - What: 在改参数前先做一份逐像素完全等价的 HLSL
   - Why it worked: 后续每个变体都能明确归因
   - Reusable pattern: Yes

2. **`center pixel + edge pixel + full-frame diff` 三联验证**
   - What: 中心像素看主体，边缘像素看 bank leak，整图差分看空间范围
   - Why it worked: 一次就把 `_BankFade` 和 `WaterColor` 的作用域分开了
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **并行跑多个 RenderDoc replacement/export 实验**
   - What we tried: 三个 surface hot-edit 并行跑
   - Why it failed: `renderdoc-mcp` 在 `export_render_target` 上出现空响应，属于工具层噪声
   - Lesson learned: 单个 capture 的 replacement/export 序列串行跑更稳
   - Don't try this again because: 容易把 transport 噪声误判成 shader 问题

---

## Problems Encountered & Solutions

### Problem 1: 不知道 current `213` 的真实 input semantics，无法写稳定 replacement
**Symptom:** 只看 disasm 知道寄存器 `v0/v1/v2/v3`，但不知道语义名。
**Root Cause:** DXBC 反汇编默认不带完整语义表。
**Investigation:**
- Tried: 直接从 disasm 反推
- Tried: 调 `get_shader mode=reflect`
- Found:
  - `POSITION_WS`
  - `SV_Position`
  - `TEXCOORD1`
  - `TEXCOORD4`
  - `TEXCOORD5`
  - `TEXCOORD0`

**Solution:**
```text
先拿 RenderDoc reflection 的 inputSignature，
再写 current-like HLSL，而不是猜测语义
```

**Why This Works:** replacement shader 的输入签名不再是猜的，后续实验才可比。
**Pattern for Future:** 任何复杂 PS hot-edit，先拿 reflection 再手写 HLSL。

### Problem 2: 单看 `WaterColor` 改动容易误以为“surface 已接近 CK3”
**Symptom:** 改成 CK3 `WaterColorShallow/Deep` 后，主河道明显不那么蓝了。
**Root Cause:** 这只说明 surface-local 水色生效，不代表 bottom/refraction 已对齐。
**Investigation:**
- Found:
  - R 从 `0.06964 -> 0.07123`，几乎没涨
  - G 从 `0.17822 -> 0.09625`
  - B 从 `0.22668 -> 0.08972`
- 对比 CK3 参考代表像素：
  - `0.22535 / 0.13761 / 0.06111`

**Solution:**
```text
把 WaterColor 改动解释为“显著修正了 G/B 主体色偏移”，
但不把它解释成“surface 已接近 CK3”
```

**Why This Works:** 它保留了正确的根因排序：surface-local 水色是主因之一，但不是唯一主因。
**Pattern for Future:** 如果替换只明显改 G/B、不改 R，说明 refraction/bottom 仍在主导底色。

### Problem 3: 还需要进一步确认 current 河心 R 到底是不是来自 bottom/refraction
**Symptom:** 改成 CK3 `WaterColorShallow/Deep` 后，中心像素还是明显偏暗，尤其 R 仍低。
**Root Cause:** surface-local `waterDiffuse` 不是最终颜色的唯一来源。
**Investigation:**
- Tried: `WaterColorShallow/Deep -> CK3`
- Tried: `waterDiffuse = 0`
- Found:
  - `waterDiffuse=0` 后中心 `R` 完全不变
  - `waterDiffuse=0` 与 `WaterColor -> CK3` 的中心像素几乎相同

**Solution:**
```text
把下一轮主线收敛到 refractionColor / bottom buffer，
而不是继续在 surface-local diffuse 色里打转
```

**Why This Works:** 它直接用热替换把 surface-local 路径和 bottom/refraction 路径分开了。
**Pattern for Future:** 当怀疑 surface 某条本地着色分支时，直接把该分支置零，比继续猜常量更快。

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本轮没有实现变更
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本轮没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先做 current-like 完全等价 HLSL，再做单变量 hot-edit
- When to use: 复杂 shader 需要逐项验证参数影响时
- Benefits: 避免把手写 HLSL 近似误差当成参数结论
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 在未验证等价 replacement 前直接改单个常量
- What not to do: 直接写一版“差不多”的 replacement，然后据此得出参数结论
- Why it's bad: 无法区分是参数影响还是 replacement 偏差
- Add warning to: 本 session log

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮仅做 RenderDoc surface 参数热验证

---

## Code Quality Notes

### Testing
- **Tests Written:** 无仓库自动化测试
- **Coverage:** RenderDoc shader replace、像素采样、整图差分
- **Manual Tests:** 无需用户参与；本轮已由 Codex 自己完成 replacement/export/比对

### Technical Debt
- **Created:** 无仓库代码债务
- **Paid Down:** “surface 差距也许主要是 `_BankFade`”这条诊断债务
- **TODOs:** 下一轮优先验证：
  - `_WaterColorMapTintFactor`
  - `refractionColor` / bottom contribution
  - bottom lighting 进一步贴近 CK3 `ToSunDir/SunIntensity`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 继续验证 `refractionColor` / bottom 对主河道中心 R 通道的贡献 - 因为 `waterDiffuse=0` 时中心 R 完全不变
2. 验证 `_WaterColorMapTintFactor` 和 refraction-world-position 相关项 - 因为 surface-local 水色之外，仍有 see-through tint 路径
3. 只有在确认主体色链后，再把 `_BankFade` 落回 runtime 默认值/参数绑定

### Questions to Resolve
1. 当前主河道中心 R 过低，主要是 bottom/refraction buffer 偏冷偏暗，还是 reflection/specular/fresnel 路径仍然不对？
2. `_WaterColorMapTintFactor` 从 `0` 补到 CK3 `0.0107` 后，会不会进一步把河心往暖色推？

### Docs to Read Before Next Session
- [2026-06-17-river-seed-alpha-hotedit-propagation.md](./2026-06-17-river-seed-alpha-hotedit-propagation.md) - edge-only 传播结论
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md) - water/light cbuffer 差异

---

## Session Statistics

**Files Changed:** 1（本 session log；外加临时 HLSL/导图，不在仓库）
**Lines Added/Removed:** +322/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `surface_current_like.hlsl` 已经能逐像素复现 current `213`
- Critical decision: `_BankFade` 只修边，不修主体；`WaterColorShallow/Deep` 修主体，但修不完
- Active pattern: `current-like exact clone -> single-variable hot-edit -> center/edge/full-frame diff`
- Current status: 下一轮应优先继续追 `refractionColor / bottom` 对 R 通道的贡献

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无仓库代码改动
- Constraints: 单个 capture 的 hot-edit/export 串行跑更稳，避免并发 transport 抖动

**Gotchas for Next Session:**
- Watch out for: 不要把 `WaterColor` 热替换后的“没那么蓝”误判成“已经像 CK3”
- Don't forget: `surface_current_like.hlsl` 已经验证过等价，可以直接继续派生新变体
- Remember: edge 和 core-color 现在已经被热替换实证分开了

---

## Links & References

### Related Documentation
- [2026-06-17-river-seed-alpha-hotedit-propagation.md](./2026-06-17-river-seed-alpha-hotedit-propagation.md)
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_current_like.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_bankfade_0025.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_watercolor_ck3.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\surface_variant_diff_triptych.png`

### Code References
- current surface diffuse water color: `Terrain.Editor/Effects/RiverSurface.sdsl:154`
- current refraction reconstruct: `Terrain.Editor/Effects/RiverSurface.sdsl:81-93`
- current edge alpha: `Terrain.Editor/Effects/RiverSurface.sdsl:173-174`
- CK3 surface edge fade: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh:88-95`

---

## Notes & Observations

- `_BankFade` 和 `WaterColorShallow/Deep` 的作用域已经被热替换完全拆开。
- `_BankFade` 是 edge-only 问题；`WaterColor` 是 core-color 问题。
- 即便直接灌入 CK3 的 `WaterColorShallow/Deep`，current 中心像素 R 仍然远低于 CK3，说明 bottom/refraction 还是下一条主线。
- `waterDiffuse=0` 与 `WaterColor -> CK3` 几乎等价，说明 CK3 这组水色对 current 来说基本就是“把 current 的表层青蓝 diffuse 关掉”。

---

*Template Version: 1.0 - Based on Archon-Engine template*
