# River Surface Refraction Attenuation Hot-Edit
**Date**: 2026-06-17
**Session**: river-surface-refraction-attenuation-hotedit
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续把 current `RiverSurface` 的 `refraction -> see-through -> final surface` 链拆开，确认主河道主体色为什么仍然和 CK3 差很大。

**Secondary Objectives:**
- 用 hot-edit 区分：
  - base raw refraction
  - selected refraction（base/distorted 二选一后）
  - water-color map sample
  - see-through 最终混色
  - attenuation / shoreMask / refractionDepth
- 对 CK3 做最小 no-distort 对照，确认 current/CK3 的 refraction-depth / attenuation 是否属于同一量级。

**Success Criteria:**
- current 侧能明确回答：
  - see-through 是否在保留 bottom/refraction，还是直接塌回 water-color map
  - 如果塌回去，是 `shoreMask=1` 还是 `attenuation≈0`
- CK3 侧至少拿到一组 no-distort scalar/R-pack 对照，而不是继续只看最终色。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-surface-bankfade-watercolor-hotedit.md](./2026-06-17-river-surface-bankfade-watercolor-hotedit.md)
- See: [2026-06-17-river-seed-alpha-hotedit-propagation.md](./2026-06-17-river-seed-alpha-hotedit-propagation.md)
- See: [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 已知：
  - `_BankFade` 只修 edge，不修主体
  - `WaterColorShallow/Deep` 是主体色一阶因素，但修不完
  - `waterDiffuse=0` 后中心像素 `R` 基本不变，说明主体 `R` 不来自 surface-local water diffuse
- 因此下一条主线必须回到 `refractionColor / see-through / bottom-distance`。

**Why Now:**
- 用户明确要求“认真分析每一个 pass 的输出，先在 RenderDoc 热修改确认，再落到代码”。
- 在继续改 SDSL 前，必须把 current surface 到底是“看见河床”还是“基本只剩 water-color map”确认清楚。

---

## What We Did

### 1. 把 current surface 先拆成 `base raw refraction` 和 `see-through only`
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_raw_refraction_only.hlsl`
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_refraction_see_through_only.hlsl`
- `event 213` 代表河心像素 `(344,598)`：
  - baseline final：`0.06964 / 0.17822 / 0.22668`
  - base raw refraction only：`0.11060 / 0.12042 / 0.12079`
  - see-through only：`0.03229 / 0.05365 / 0.04102`

**Rationale:**
- 这一步先证明 current final 里的 `R` 不是在最早的 refraction sample 就已经没了，而是在 see-through 之后继续被压掉。

### 2. 修正“raw refraction”定义：区分 base sample 与 selected refraction
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 发现第一版 `surface_raw_refraction_only.hlsl` 只采了 `screenUv` 的 base sample，不等于 current `SampleRefractionSeeThrough(...)` 里真正使用的 `base/distorted` 选择结果。
- 新增：
  - `surface_selected_refraction_only.hlsl`
- `event 213` 像素 `(344,598)`：
  - selected refraction：`0.13013 / 0.13831 / 0.13574`

**Rationale:**
- 这让后续对比不再混淆“最早的 RT 采样值”和“current 真正带入 see-through 的 refractionColor”。

### 3. 用同一次执行打包 `selectedRefraction / waterColorMap / seeThrough`
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `surface_refraction_r_pack.hlsl`
- 打包方式：
  - `R = selectedRefraction.r`
  - `G = waterColorMap.r`
  - `B = seeThroughFinal.r`
- current `event 213`：
  - 像素 `(344,598)`：
    - `R=0.13013`
    - `G=0.03229`
    - `B=0.03229`
  - 像素 `(392,606)`：
    - `R=0.12140`
    - `G=0.03577`
    - `B=0.05026`

**Rationale:**
- `(344,598)` 这颗像素里，`seeThrough.r == waterColorMap.r`，而且明显不等于 `selectedRefraction.r`，说明 see-through 在这颗像素上已经完全塌回 water-color map。
- `(392,606)` 这颗历史对比像素里，see-through 仍然更接近 water-color map，只是还保留了一部分 bottom/refraction。

### 4. 用 scalar pack 区分 `attenuation / shoreMask / refractionDepth`
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `surface_refraction_scalar_pack.hlsl`
- 打包方式：
  - `R = attenuation`
  - `G = shoreMask`
  - `B = refractionDepth / _WaterSeeThroughShoreMaskDepth`
- current `event 213`：
  - 像素 `(344,598)`：
    - attenuation `≈ 4.77e-07`
    - shoreMask `0`
    - normalized depth `0.7827`，即 refractionDepth 约 `15.65`
  - 像素 `(392,606)`：
    - attenuation `0.1692`
    - shoreMask `0`
    - normalized depth `0.0975`，即 refractionDepth 约 `1.95`
  - edge 像素 `(352,705)`：
    - attenuation `1`
    - shoreMask `0`
    - normalized depth `0`

**Rationale:**
- 这一步把关键分叉钉死了：
  - current 主河道中心发暗不是 `shoreMask=1`
  - 而是 `attenuation` 已经低到接近 0，底部 refraction 在 see-through 阶段几乎被完全抛弃

### 5. 验证 current 的 refraction collapse 和 flow-distortion 无关
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `surface_refraction_see_through_nodistort.hlsl`
- 保留 see-through 全路径，只把 `refractionOffset = 0`
- current `event 213` 像素 `(344,598)`：
  - no-distort see-through：`0.03232 / 0.05362 / 0.04102`
  - 与原 `see-through only` 几乎一致

**Rationale:**
- 这说明 current 这颗中心像素的主问题不是“flow refraction offset 把采样点带偏”，而是连 base refraction 的世界位置重建后，也已经让 attenuation 几乎归零。

### 6. 比较 surface-world UV 与 refraction-world UV 对 water-color map 的影响
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `surface_watercolor_surface_vs_refraction_r.hlsl`
- 打包：
  - `R = WaterColor.r(surface world uv)`
  - `G = WaterColor.r(refraction world uv)`
  - `B = abs(diff)`
- current `event 213`：
  - 像素 `(344,598)`：
    - surface-world `0.03616`
    - refraction-world `0.03232`
    - diff `0.00386`

**Rationale:**
- 当前这颗中心像素里，surface-world 与 refraction-world 的 water-color sample 差异很小。
- 因此 current see-through 的暗色主要不是“折射 UV 跑到完全错误的贴图区域”，而是这片 water-color map 本身就在较暗区，同时 attenuation 又太低。

### 7. 在 CK3 上做 no-distort 对照：scalar pack 与 R-pack
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `ck3_surface_refraction_scalar_pack_nodistort.hlsl`
  - `ck3_surface_refraction_r_pack_nodistort.hlsl`
- 代表像素：
  - `(110,738)`：这是 `ck3_surface_raw_refraction_only` 整图 diff 最大像素
  - `(720,940)`：另一颗稳定河道像素
- CK3 `event 466`：
  - scalar pack `(110,738)`：
    - attenuation `0.4805`
    - shoreMask `0`
    - normalized depth `0.0259`，即 refractionDepth 约 `0.52`
  - scalar pack `(720,940)`：
    - attenuation `0.5181`
    - shoreMask `0`
    - normalized depth `0.0325`，即 refractionDepth 约 `0.65`
  - R-pack `(110,738)`：
    - refraction.r `0.27124`
    - waterColorMap.r `0.01213`
    - seeThrough.r `0.13660`
  - R-pack `(720,940)`：
    - refraction.r `0.06155`
    - waterColorMap.r `0.01503`
    - seeThrough.r `0.03912`

**Rationale:**
- 对这两颗 CK3 河道像素，see-through 仍然保留了显著的 bottom/refraction 成分。
- 与 current 的 `(344,598)` / `(392,606)` 相比，CK3 的 attenuation 明显更高，see-through 更不容易塌回 water-color map。

### 8. 排除 CK3 `MaxHeight=50` 分支对对比的干扰
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增：
  - `ck3_surface_refraction_scalar_pack_nodistort_noclamp.hlsl`
- CK3 capture 相机：
  - `CameraPosition.y = 53.623`
  - 会命中 `jomini_water.fxh` 的 `MaxHeight = 50` 分支
- 同样两颗像素在“禁用 clamp”的热替换下结果都变成：
  - attenuation `1`
  - shoreMask `0`
  - refractionDepth `0`

**Rationale:**
- 这直接证明 CK3 capture 的 `MaxHeight` 相机高度钳制对 refraction-depth / attenuation 结果影响极大。
- 所以跨 capture 直接比较 “current depth=1.95 / CK3 depth=0.52” 时必须注明：
  - current capture 相机 `y=27.9`，该分支不触发
  - CK3 capture 相机 `y=53.6`，该分支会显著改变结果

---

## Decisions Made

### Decision 1: current surface 的主河道暗/冷问题已经收敛到 see-through 链，而不是 `_BankFade`
**Context:** `_BankFade` 与 `WaterColorShallow/Deep` 已分别验证过 edge/core-color 作用域。
**Options Considered:**
1. 继续微调 `_BankFade`
2. 继续只看 `WaterColorShallow/Deep`
3. 把 refraction/see-through 链拆到变量级

**Decision:** 选择 3
**Rationale:** 只有这样才能知道 current 是“还看得到河床”还是“已经完全塌回 water-color map”。
**Trade-offs:** 要写多份临时 HLSL，但能把下游结论钉死。

### Decision 2: 用单次执行的 packed debug 输出替代跨 run 人工拼变量
**Context:** 单独跑 `selected_refraction_only`、`watercolor_only`、`seeThrough_only` 时，容易因为定义边界不同而出现表面矛盾。
**Options Considered:**
1. 继续分别写多份“单变量输出”
2. 在同一份 replacement 里把多个中间值一起打包输出

**Decision:** 选择 2
**Rationale:** 同一次执行里 `selectedRefraction / waterColorMap / seeThrough` 的关系不会再被跨 run 误差污染。
**Trade-offs:** 只能一次打包少量标量，但结论更可靠。

### Decision 3: 跨 capture refraction-depth 对比必须显式标注 CK3 `MaxHeight` 分支
**Context:** CK3 capture 的相机 `y=53.6`，current capture `y=27.9`。
**Options Considered:**
1. 直接拿 scalar pack 数值作 current/CK3 一对一结论
2. 先做 `noclamp` 对照，确认 `MaxHeight` 分支影响量级

**Decision:** 选择 2
**Rationale:** `noclamp` 热替换显示 CK3 的这一分支会把 attenuation/depth 结果从 `(0.48, 0.52)` 改成 `(1.0, 0.0)`，影响足够大，不能忽略。
**Trade-offs:** 让跨 capture 对比变得更谨慎，但避免误判。

---

## What Worked ✅

1. **single-pass packed hot-edit**
   - What: 在同一份 replacement 里打包多个中间量，例如 `selectedRefraction.r / waterColorMap.r / seeThrough.r`
   - Why it worked: 把跨 run 的“定义不一致”噪声直接消掉了
   - Reusable pattern: Yes

2. **current 与 CK3 都做 no-distort scalar/R-pack**
   - What: 用最小 see-through 子集而不是完整 `CalcWater`
   - Why it worked: 足够回答“底部 refraction 是否还活着”这个主问题
   - Reusable pattern: Yes

3. **把 CK3 `MaxHeight` 分支单独热替换掉**
   - What: 写 `noclamp` 变体直接量化 camera-height clamp 的影响
   - Impact: 避免把 capture 视角差异误判成 shader 语义差异

---

## What Didn't Work ❌

1. **第一版 `raw_refraction_only` 直接当作“实际 refractionColor”**
   - What we tried: 直接输出 `RefractionTexture.Sample(screenUv)`
   - Why it failed: 它没有覆盖 current 真正的 `base/distorted` 选择逻辑
   - Lesson learned: “base raw sample” 和 “selected refraction” 必须分开
   - Don't try this again because: 会把 current see-through 之前的真实输入看错

2. **单独输出 `waterColorMap.rgb + attenuation.a` 再跨 run 拼结论**
   - What we tried: 用一份独立 replacement 看 `waterColorMap`
   - Why it failed: 不同 replacement 的边界不一致，容易制造表面矛盾
   - Lesson learned: 关键中间值优先在同一次执行里打包输出

---

## Problems Encountered & Solutions

### Problem 1: `raw refraction` 与 `see-through` 结果表面矛盾
**Symptom:** base raw refraction 比 see-through 亮很多，但还不能确定 current 真正送进 see-through 的 refraction 是哪一层。
**Root Cause:** 第一版 `raw_refraction_only` 只输出了 base sample，不等于 `SampleRefractionSeeThrough(...)` 使用的 selected refraction。
**Investigation:**
- Tried: `surface_raw_refraction_only.hlsl`
- Tried: `surface_refraction_see_through_only.hlsl`
- Found: 还需要单独输出 selected refraction

**Solution:**
```text
新增 `surface_selected_refraction_only.hlsl`
再用 `surface_refraction_r_pack.hlsl`
在同一次执行里打包 selectedRefraction / waterColorMap / seeThrough
```

**Why This Works:** 把“哪一层值进了 see-through”直接钉死。
**Pattern for Future:** 复杂中间链路不要只看一端和终端，中间关键选择器要单独输出。

### Problem 2: CK3/current 的 scalar pack 不能直接横向比大小
**Symptom:** CK3 scalar pack 的 depth/attenuation 看起来比 current 温和很多。
**Root Cause:** CK3 capture 相机 `y=53.6` 触发了 `MaxHeight=50`，而 current capture 相机 `y=27.9` 没触发。
**Investigation:**
- Tried: CK3 no-distort scalar pack
- Tried: CK3 no-distort scalar pack without MaxHeight clamp
- Found: `noclamp` 后 CK3 结果直接变成 `attenuation=1, depth=0`

**Solution:**
```text
跨 capture 汇报时，显式把 “CK3 相机高度钳制活跃” 记为前提，
不要把这组数值直接当成纯 shader 公式差异。
```

**Why This Works:** 把 capture 条件差异和 shader 差异拆开。
**Pattern for Future:** 参考实现里只要有条件分支，就先验证当前 capture 是否真的命中该分支。

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本轮没有实现变更
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本轮没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 用 packed-channel replacement 一次输出多个中间量
- When to use: RenderDoc 热替换里出现“单变量结果互相矛盾”时
- Benefits: 直接消除跨 run 定义差异
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 忽略 CK3 capture 的 `MaxHeight` 分支状态直接比 refractionDepth
- What not to do: 拿 current/CK3 scalar pack 数值直接横向下结论
- Why it's bad: capture 视角本身就会把 depth/attenuation 改到另一量级
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮仍然是 RenderDoc 热替换分析，不是实现调整

---

## Code Quality Notes

### Testing
- **Tests Written:** 无仓库自动化测试
- **Coverage:** RenderDoc hot-edit、pick pixel、整图 diff、同执行 packed debug
- **Manual Tests:** 无需用户参与；本轮由 Codex 自己完成 replacement/export/pick 比对

### Technical Debt
- **Created:** 无仓库代码债务
- **Paid Down:** “refraction/see-through 还不知道在吃哪一层颜色” 这条诊断债务
- **TODOs:** 下一步如果开始改 SDSL，优先验证：
  - `RiverSurface` 是否要继续维持当前简化 surface 栈，还是直接补齐 CK3 `JominiWater` 缺失参数和公式
  - `RiverCommon` 是否要引入 CK3 `MaxHeight` camera clamp 做语义对齐
  - `RiverRenderFeature / RiverRenderSettings` 是否要扩展 missing `JominiWater` 参数绑定

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 对照本轮 current/CK3 packed 结果，决定 `RiverSurface` 是继续修简化模型，还是直接补齐 CK3 `JominiWater` surface 栈
2. 检查 `RiverRenderSettings / RiverRenderObject / RiverRenderFeature` 中缺失的 water 参数绑定链路，为后续 surface 迁移做准备
3. 如果开始改 SDSL，优先在日志中已验证有效的低风险项先落地：
   - `_BankFade 0.15 -> 0.025`
   - `WaterColorShallow/Deep -> CK3 值`
   - `RiverCommon` 的 `Compress/DecompressWorldSpace` 加入 CK3 `MaxHeight` 分支（注意：不是当前 capture 主因）

### Questions to Resolve
1. current 主河道和 CK3 的主要差距，下一刀应该放在：
   - see-through 前的 depth semantics
   - 还是 current 缺失的大量 `JominiWater` surface controls？
2. 在当前 capture 相机高度下，是否还需要专门做一次“人为抬高 current camera”的对照，排除 `MaxHeight` 分支影响？

### Docs to Read Before Next Session
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md) - current 缺失的 `JominiWater` 常量清单
- [2026-06-17-river-surface-bankfade-watercolor-hotedit.md](./2026-06-17-river-surface-bankfade-watercolor-hotedit.md) - `_BankFade` / `WaterColorShallow/Deep` 的已验证作用域
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water_default.fxh` - CK3 `CalcRefraction` / `CalcTerrainUnderwaterSeeThrough`

---

## Session Statistics

**Files Changed:** 1（本 session log；外加桌面分析目录临时 HLSL/summary）
**Lines Added/Removed:** +336/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: current `RiverSurface.sdsl:69-93` 的 see-through 路径已经被 packed hot-edit 拆开
- Critical decision: current 主河道中心像素的暗/冷问题，已经定位到 `selected refraction -> see-through -> waterColorMap` 这条链，而不是 `_BankFade`
- Active pattern: `single-pass packed debug`（同一次执行里输出多个中间值）
- Current status: 下一步已经可以开始决定是否把 current 简化 surface 栈升级为更接近 CK3 `JominiWater`

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无仓库代码改动
- Constraints:
  - current capture 相机 `y=27.9`
  - CK3 capture 相机 `y=53.6`，`MaxHeight=50` 分支活跃

**Gotchas for Next Session:**
- Watch out for: 不要再把 base raw refraction 当成 current 真正的 `refractionColor`
- Don't forget: 对 CK3 做 refraction-depth 对照时，一定先说明 `MaxHeight` 分支是否活跃
- Remember: current `(344,598)` 和 `(392,606)` 都在主河道内，但 attenuation 量级不同，不能用单颗像素代表整条河

---

## Links & References

### Related Documentation
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- [2026-06-17-river-surface-bankfade-watercolor-hotedit.md](./2026-06-17-river-surface-bankfade-watercolor-hotedit.md)
- [2026-06-17-river-seed-alpha-hotedit-propagation.md](./2026-06-17-river-seed-alpha-hotedit-propagation.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_refraction_r_pack.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\surface_refraction_scalar_pack.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\ck3_surface_refraction_scalar_pack_nodistort.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\ck3_surface_refraction_r_pack_nodistort.hlsl`

### Code References
- current see-through path: `Terrain.Editor/Effects/RiverSurface.sdsl:69-93`
- current main surface composition: `Terrain.Editor/Effects/RiverSurface.sdsl:132-174`
- current compress/decompress helper: `Terrain.Editor/Effects/RiverCommon.sdsl:35-43`
- CK3 decompress helper: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water.fxh:157-183`
- CK3 see-through / refraction path: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water_default.fxh:198-244`

---

## Notes & Observations

- current `(344,598)` 这颗中心像素里，see-through 已经完全塌回 water-color map；current `(392,606)` 这颗历史基准像素里，see-through 仍保留一小部分 bottom/refraction，但明显比 CK3 弱。
- `no-distort` 与原 see-through 在 current 中心几乎相同，说明这帧主问题不是 flow offset，而是 base refraction 的 depth semantics。
- CK3 的 `MaxHeight` 分支在本参考帧非常敏感；如果后续要拿 scalar pack 做精确 current/CK3 数值对比，最好控制到同类相机高度。

---

*Template Version: 1.0 - Based on Archon-Engine template*
