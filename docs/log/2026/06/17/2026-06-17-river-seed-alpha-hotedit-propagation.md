# River Seed Alpha Hot-Edit Propagation
**Date**: 2026-06-17
**Session**: river-seed-alpha-hotedit-propagation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用 RenderDoc 单进程 hot-edit 直接验证：把 current `debug.rdc` 的 seed pass alpha 从 `0` 改成非零后，bottom/surface 到底会发生什么变化。

**Secondary Objectives:**
- 区分 seed alpha 问题影响的是“主河道颜色”还是“河岸/折射边缘”。
- 把本轮 GPU 证据和 current/CK3 shader 代码路径逐条对齐，避免继续把问题全压在 seed alpha 上。

**Success Criteria:**
- 确认 hot-edit 确实改到了 `event 157`。
- 确认该改动是否传播到 `184` / `213`，以及传播到的是哪一类像素区域。
- 给出一条更精确的根因排序，而不是继续笼统地说“seed alpha 不对”。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 已经确认 current `157` seed pass 把 half-res refraction seed 的 alpha 清成 `0`。
- 但还没有直接证明这件事对 current `184 bottom` / `213 surface` 的实际影响范围。

**Why Now:**
- 用户明确要求“先 RenderDoc 热修改确认，再决定是否改 SDSL/runtime”。
- 如果 seed alpha 只影响河岸边缘，那么继续把主河道色相/亮度差距都归咎于 seed alpha 就是错误排序。

---

## What We Did

### 1. 搭了一条稳定的单进程 RenderDoc hot-edit 脚本链
**Files Changed:** 无仓库代码；新增临时分析文件

**Implementation:**
- 新增临时脚本：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\renderdoc_hotedit_runner.py`
- 新增临时 HLSL：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\seed_alpha50.hlsl`
- 工作流固定为同一 `renderdoc-mcp.exe` 进程里连续执行：
  - `open_capture`
  - `shader_encodings`
  - `shader_build`
  - `shader_replace`
  - `goto_event`
  - `export_render_target`
  - `pick_pixel`
  - `shader_restore_all`

**Rationale:**
- 之前 CLI 的跨进程 `shader-replace` 不稳定，replacement 经常丢。
- 这一轮用同一 stdio JSON-RPC 进程，把 replacement 生命周期固定在一次实验里，才能保证后续导图和 pick 看到的是同一套替换状态。

### 2. 先证明 `event 157` 的热替换确实生效
**Files Changed:** 无仓库代码

**Implementation:**
- 对 `debug.rdc` 的 `event 157` pixel shader 做最小替换：
  - 保留原 RGB 采样
  - 强制 `color.a = 50.0f`
- 代表像素 `(172,299)`：
  - baseline：`(0.50098, 0.60986, 0.18091, 0.0)`
  - hot：`(0.50098, 0.60986, 0.18091, 50.0)`
- 导图对比：
  - `157` 的 PNG 只有 alpha 通道变化，RGB 完全不变

**Rationale:**
- 这一步先排除“replacement 编译成功但没有真的落到 draw”这种假阳性。
- 结果说明 hot-edit 本身链路是可信的。

### 3. 追 `157 -> 184`：bottom pass 的河道内 RGB 完全不吃 seed alpha
**Files Changed:** 无仓库代码

**Implementation:**
- 继续导出 `event 184`（half-res bottom/refraction RT）：
  - 代表河道像素 `(172,299)` baseline/hot 完全一致：
    - `(0.11060, 0.12042, 0.12079, 29.03125)`
- PNG 像素差分统计：
  - 变化像素：`332,386 / 416,328`
  - 变化仅发生在 alpha 通道
  - RGB `max diff = 0`

**Rationale:**
- 说明 current bottom pass 在河道覆盖区域会写自己的 compressed-world alpha，seed alpha 不会改变河底本身的 RGB 着色结果。
- 也说明 current bottom 的“偏黑/偏冷”不是从 seed alpha 继承下来的，而是 bottom pass 自己的 lighting/material 语义问题。

### 4. 追 `184 -> 213`：surface 只在河岸/折射边界响应 seed alpha
**Files Changed:** 无仓库代码

**Implementation:**
- `event 213` 代表主河道像素 `(344,598)`：
  - baseline/hot 完全一致：
    - `(0.06964, 0.17822, 0.22668, 1.0)`
- `event 213` 边缘差分最大像素 `(352,705)`：
  - baseline：`(1.73828, 1.81934, 0.78271, 1.0)`
  - hot：`(0.08954, 0.16516, 0.08466, 1.0)`
- PNG 差分统计：
  - 变化像素：`89,870 / 1,665,312`，约 `5.4%`
  - bbox：`x=0..1671, y=317..774`
  - 差分图呈现为沿河岸两侧分布的一条细带，而不是整条河心一起变化
- 导出的差分图：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\hotedit_seed_alpha50\surface_triptych.png`
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\hotedit_seed_alpha50\surface_rgb_diff.png`

**Rationale:**
- 这直接证明 seed alpha 问题主要影响“河岸/折射边缘的 terrain leak”，不会决定当前主河道内部的青蓝色主体。
- 所以 current/CK3 在主河道中心的颜色差距，不能继续归因于 `157` alpha 被清零。

### 5. 把热替换结果和 current/CK3 shader 代码路径对上
**Files Changed:** 无仓库代码

**Implementation:**
- current `RiverSurface.sdsl`
  - `81-93`：`RefractionTexture.a` 参与 `RiverDecompressWorldSpace(...)`
  - `154`：主河道主体色直接来自 `lerp(WaterColorDeep.rgb, WaterColorShallow.rgb, facing)`
  - `173`：河岸 alpha 直接受 `_BankFade` 控制
- current `RiverCommon.sdsl`
  - `35-43`：`RiverDecompressWorldSpace(surfaceWorldPosition, 0, camera)` 会退化成 `cameraPosition`
- current `RiverBottom.sdsl`
  - `202-214`：bottom pass 自己写 `streams.ColorTarget.a = compressedWorld`
- CK3:
  - `jomini_water.fxh:171-183`：同样通过压缩距离反解折射位置
  - `jomini_river_surface.fxh:93-94`：同样用 `_BankFade` 控制 edge fade
  - 但 CK3 capture 的 half-res refraction alpha 最小值约 `41.09`，不会落到 current 这种 `0` 路径

**Rationale:**
- current surface 在 bank-edge 看到亮 terrain leak，本质上是：
  - `RefractionTexture.a == 0`
  - `RiverDecompressWorldSpace(...) -> cameraPosition`
  - refraction world UV / shore logic 退化到错误位置
- 而主河道主体色仍由 `WaterColorShallow/Deep` 这条 surface-local 路径控制，所以 seed alpha 热替换不会把中间大块青蓝河心改掉。

---

## Decisions Made

### Decision 1: seed alpha 不再被视为“主河道色相差距”的首要根因
**Context:** 之前已经证明 `157` alpha 为 `0`，但还没有量化它对后续 pass 的影响边界。

**Options Considered:**
1. 继续把 current/CK3 的大部分差距都归咎于 seed alpha
2. 用热替换追到 `184/213`，看它到底改变哪些像素

**Decision:** 选择 2
**Rationale:** 只有 pass 级联验证后，才能知道 seed alpha 是“主矛盾”还是“边缘问题”。
**Trade-offs:** 多花一轮 RenderDoc 自动化时间，但避免继续误判问题排序。
**Documentation Impact:** 记录在本 session log，并补充到 `stride-river-rendering-patterns.md`

### Decision 2: 当前后续优先级改为“bank-edge 语义”和“surface 主水色/底部 lighting”分开处理
**Context:** hot-edit 已证明主河道中心像素对 seed alpha 不敏感。

**Options Considered:**
1. 继续只盯 seed alpha
2. 把问题拆成：
   - bank-edge/refraction alpha 语义
   - core water color / missing water constants
   - bottom lighting/material semantics

**Decision:** 选择 2
**Rationale:** 这和实际热替换传播路径一致。
**Trade-offs:** 需要多条证据链并行维护，但结论更稳定。

---

## What Worked ✅

1. **单进程 JSON-RPC hot-edit**
   - What: 用同一个 `renderdoc-mcp.exe` 进程完成 build/replace/export/pick
   - Why it worked: 避免了 replacement 跨进程丢失
   - Reusable pattern: Yes

2. **“代表河心像素 + 差分最大边缘像素”双采样**
   - What: 同时比较 `(344,598)` 的河心和 `(352,705)` 的河岸边缘
   - Why it worked: 一次就把“中心不变、边缘大变”的传播特征抓出来了
   - Reusable pattern: Yes

3. **PNG 差分统计 + 可视化 triptych**
   - What: 不只看 `pick_pixel`，还导出整图差分
   - Impact: 能直接看到变化区域只沿河岸分布

---

## What Didn't Work ❌

1. **把 `157 alpha=0` 直接当成整个 river mismatch 的总根因**
   - What we tried: 在前序分析里把 seed alpha 当作“第一根因”继续外推
   - Why it failed: 它只解释 bank-edge/refraction leak，不能解释主河道中心颜色和 bottom 冷暗
   - Lesson learned: 必须追到 downstream pass，而不是停在 seed 层推理
   - Don't try this again because: 会把 surface/bottom 的主问题顺序排错

---

## Problems Encountered & Solutions

### Problem 1: hot-edit 成功了，但 `184/213` 代表像素一开始看起来“完全没变”
**Symptom:** `event 157` alpha 已变成 `50`，但原先选的 `184/213` 代表像素读数不变。
**Root Cause:** 这些代表像素位于主河道内部，不是最容易暴露 seed alpha 泄漏的 bank-edge 区域。
**Investigation:**
- Tried: 比较 `157` 河道像素
- Tried: 比较 `184` 同一河道像素
- Tried: 对 `213` 做整图 PNG 差分
- Found: 真正变化集中在 bank-edge，而不是河心

**Solution:**
```text
先验证 157 自身，
再用整图差分找出 surface 最大变化像素，
最后同时保留“河心像素不变”和“边缘像素大变”这两条证据
```

**Why This Works:** 能把“局部强变化”从“中心无变化”里分离出来。
**Pattern for Future:** 如果代表像素不变，不代表 hot-edit 无效；先看整图差分再换像素。

### Problem 2: 新发现了 `RiverDecompressWorldSpace` 和 CK3 `MaxHeight` 语义差异，但它不是本帧主因
**Symptom:** current `RiverCommon.sdsl` 没有 CK3 `jomini_water.fxh` 的 `MaxHeight=50` 相机高度钳制。
**Root Cause:** current 实现是简化版压缩/解压距离函数。
**Investigation:**
- Checked: current `_CameraWorldPosition_id33 = (4726.77, 27.91, 360.74)`
- Found: 当前 capture 的相机 `y=27.9 < 50`

**Solution:**
```text
先记录这个语义差异，
但本帧不把它列为首要根因，因为 CK3 的 MaxHeight 分支在当前 capture 中不会触发
```

**Why This Works:** 避免把“代码上确实不同”误判成“当前帧的主矛盾”。
**Pattern for Future:** 发现参考实现多了保护分支后，先检查当前 capture 是否真的落进该分支。

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本轮没有实现变更
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本轮没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 用 seed alpha 常数 hot-edit 区分“bank-edge 泄漏”和“主河道主体色”
- When to use: 怀疑 refraction alpha 语义不对，但不确定它是否真的是主河道颜色差距的根因
- Benefits: 一轮内区分 edge-only 问题和 core-color 问题
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 只看一个 river center pixel 就断言 seed alpha 热替换无效
- What not to do: 看到主河道中心像素不变，就判断 replacement 没传播
- Why it's bad: seed alpha 这类问题很可能只在 bank-edge 暴露
- Add warning to: 本 session log

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮是 RenderDoc 级验证，不是实现调整

---

## Code Quality Notes

### Testing
- **Tests Written:** 无仓库自动化测试
- **Coverage:** RenderDoc hot-edit、PNG export、pick pixel、差分图
- **Manual Tests:** 无需用户手工验证；本轮已由 Codex 自己导图和比对

### Technical Debt
- **Created:** 无仓库代码债务
- **Paid Down:** “seed alpha 可能解释了大部分 river mismatch”这条诊断债务
- **TODOs:** 下一轮如果继续 hot-edit，优先验证：
  - `_BankFade 0.15 -> 0.025`
  - `WaterColorShallow/Deep` 贴近 CK3
  - bottom lighting 语义进一步靠近 CK3 `ToSunDir/SunIntensity/Shadow`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 验证 `_BankFade` 对 bank-edge 宽度的影响 - 因为本轮已经证明 seed alpha 问题只在边缘区发作
2. 验证 `WaterColorShallow/Deep` 或 surface-local water diffuse 路径 - 因为主河道中心像素对 seed alpha 不敏感
3. 继续对比 bottom lighting 语义与 CK3 `ToSunDir / SunIntensity / ShadowTexture` - 因为 bottom RGB 完全不随 seed alpha 变化

### Questions to Resolve
1. current `_BankFade=0.15` 相比 CK3 `0.025`，是否在放大 seed-alpha 泄漏带宽？
2. current 主河道中心的青蓝色主体，有多少来自 `WaterColorShallow/Deep`，有多少来自缺失的 water constants？

### Docs to Read Before Next Session
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md) - 当前/CK3 water/light cbuffer 差异
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - RenderDoc 分层诊断模式

---

## Session Statistics

**Files Changed:** 1（本 session log；外加临时分析脚本/图像，不在仓库）
**Lines Added/Removed:** +319/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: current `RiverSurface.sdsl:81-93` 直接用 `RefractionTexture.a` 反解 refraction world position
- Critical decision: seed alpha 只解释 bank-edge leak，不解释主河道主体色
- Active pattern: `157 hot-edit -> 184 alpha-only diff -> 213 bank-edge RGB diff`
- Current status: 根因排序已更新，下一轮应转向 `_BankFade` / `WaterColorShallow/Deep` / bottom lighting

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无仓库代码改动
- Constraints: 当前 capture 相机 `y=27.9`，CK3 `MaxHeight=50` 分支不是这帧主因

**Gotchas for Next Session:**
- Watch out for: 不要因为 `157` 改了就默认 `184/213` 主河道一定会跟着变
- Don't forget: `184` river interior alpha 是 bottom 自己写的 compressed world，不是 seed alpha 直通
- Remember: current `WaterColorShallow/Deep` 与 CK3 数值量级差异仍然巨大

---

## Links & References

### Related Documentation
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\renderdoc_hotedit_runner.py`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\seed_alpha50.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\hotedit_seed_alpha50\summary.json`

### Code References
- current refraction reconstruct: `Terrain.Editor/Effects/RiverSurface.sdsl:81-93`
- current surface diffuse water color: `Terrain.Editor/Effects/RiverSurface.sdsl:154`
- current surface edge alpha: `Terrain.Editor/Effects/RiverSurface.sdsl:173-174`
- current bottom compressed-world write: `Terrain.Editor/Effects/RiverBottom.sdsl:202-214`
- current decompress helper: `Terrain.Editor/Effects/RiverCommon.sdsl:35-43`
- CK3 decompress helper: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water.fxh:157-183`
- CK3 river surface edge fade: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh:88-95`

---

## Notes & Observations

- 本轮最重要的新结论不是“seed alpha 确实有问题”，而是“它只解释 bank-edge 泄漏，不解释主河道主体色”。
- current `RiverSurface` 的主河道主体色仍然直接走 `WaterColorDeep/Shallow`，而这组 cbuffer 值与 CK3 差得非常大。
- current `RiverBottom` 的 RGB 对 seed alpha 热替换完全不敏感，因此 bottom 冷暗仍然要回到底部 lighting/material 语义上看。

---

*Template Version: 1.0 - Based on Archon-Engine template*
