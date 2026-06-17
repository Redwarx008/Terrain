# River RenderDoc Pass Analysis
**Date**: 2026-06-17
**Session**: River RenderDoc pass-by-pass analysis
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 `C:\Users\Redwa\Desktop\debug.rdc` 与 `C:\Users\Redwa\Desktop\ck3-river.rdc`，按 pass 重新锁定当前 river 链路，并用 RenderDoc 证据确认差距主要发生在哪一层。

**Secondary Objectives:**
- 在动 `RiverBottom.sdsl` / `RiverSurface.sdsl` 之前，先做至少一轮 RenderDoc 热替换验证。
- 把本轮发现沉淀成可复用的 river RenderDoc 规则。

**Success Criteria:**
- 给出当前 capture 与 CK3 capture 的准确 event 分组，而不是沿用旧文档里的过时 event 号。
- 用 pixel history / resource usage / hot-replace 证明问题主要在 seed/bottom，而不是最后一层 surface。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](../16/2026-06-16-river-refraction-buffer-vs-ck3-analysis.md)
- See: [2026-06-16-river-ck3-parity-implementation.md](../16/2026-06-16-river-ck3-parity-implementation.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 旧文档里经常把 `184/213`、`332/460` 当成当前/CK3 的 river pass 关键 event，但这会混淆“首个 draw”与“整条 pass 的最终 RT 状态”。

**Why Now:**
- 用户要求“逐个 pass 分析，并且在修改 SDSL 前优先在 RenderDoc 热修改验证”，因此必须先把 pass 边界和根因层级重新校准。

---

## What We Did

### 1. 重新建立两边 capture 的 river pass 拓扑
**Files Changed:** 无

**Implementation:**
- 打开两个 `.rdc`，结合 `list_events`、`get_resource_usage` 与 `export_snapshot` 重新标定当前链路：
  - 当前 capture：
    - `157` 写 half-res seed RT `ResourceId::7754`
    - `158` 把 `7754` copy 到 working refraction RT `7757`
    - `184 / 197 / 210 / 223` 共同写 `7757`（bottom pass）
    - `252 / 270 / 288 / 306` 共同读 `7757` 并写 `4055`（surface pass）
  - CK3 capture：
    - `283` clear `49006`
    - `304 / 317` 先写 `49006` 的 pre-bottom 内容
    - `332 / 334 / 336 / 338` 共同写 `49006`（bottom pass）
    - `460 / 462 / 464 / 466` 共同读 `49006` 并写 `49000`（surface pass）

**Rationale:**
- 这一步确认了“river pass 是多 draw 共享一张 RT 的分段写入”，不能再把组内第一个 draw 直接当成整条 pass 的完成态。

**Architecture Compliance:**
- ✅ 符合现有 `RiverRenderFeature` 的 half-res refraction + full-res surface 架构
- ✅ 与 CK3 的 half-res bottom / full-res surface 结构对得上

### 2. 对比 seed / pre-bottom：当前用 scene copy，CK3 用独立 half-res 链路
**Files Changed:** 无

**Implementation:**
- 当前 `7757` 的 usage 明确显示它先在 `158` 被 `CopyDst`，再被 bottom draws 覆盖，说明当前是“scene seed copy -> bottom overlay”。
- CK3 `49006` 的 usage 则是 `283 Clear -> 304/317 ColorTarget -> 332..338 ColorTarget -> 460..466 PS_Resource`，说明它的 pre-bottom 内容来自独立 draw，不是简单复制 final HDR scene。
- representative pixel：
  - 当前 half-res river 像素 `(172,299)` 在 `158` copy 之后是 `float3(3.865, 3.098, 1.896)`
  - CK3 half-res river 像素 `(596,316)` 在 `304` pre-bottom 后是 `float3(0.0243, 0.0324, 0.0127)`

**Rationale:**
- 当前 refraction 链路起点就是一个远亮于 CK3 的 HDR terrain seed，这会直接抬高后续 river 颜色基线，也会让 bottom pass 更容易被误判成“只是太黑”。

### 3. 对比 bottom pass：当前把亮 seed 压成近黑，CK3 把暗底推成暖棕
**Files Changed:** 无

**Implementation:**
- 当前 bottom 绑定：
  - `BottomDiffuse / BottomNormal / BottomProperties / EnvironmentMap`
- CK3 bottom 绑定：
  - `BottomDiffuse / BottomNormal / BottomProperties / EnvironmentMap / ShadowTexture`
- representative pixel：
  - 当前 `(172,299)` 在 `223` 后变成 `float3(0.0291, 0.0261, 0.0196), a=18.156`
  - CK3 `(596,316)` 在 `336` 后变成 `float3(0.2768, 0.1758, 0.0863), a=57.258`

**Rationale:**
- 这说明当前 capture 的 raw bottom/refraction 结果和 CK3 不是“同色系但亮度略差”，而是连颜色趋势都不同：当前是近黑/冷暗，CK3 是明显暖棕。
- 同时，CK3 bottom 比当前多一个 `ShadowTexture` 绑定，lighting 语义也更完整。

### 4. 用热替换验证 surface 不是主战场
**Files Changed:** 无

**Implementation:**
- 对当前 surface pixel shader 做最小热替换：直接采样 `RefractionTexture_id52` 并输出，不保留原水面着色逻辑。
- 在 full-res river 像素 `(344,598)`：
  - 原始 surface 输出：`float3(0.0528, 0.0808, 0.0815)`
  - 热替换后输出：`float3(0.0291, 0.0261, 0.0196)`
  - 该值与 half-res bottom RT `(172,299)` 的 `223` 后颜色完全一致

**Rationale:**
- 这个热替换证明：当前 surface 确实是在“沿着已有 refraction 继续上色”，不是把一个本来正确的暖棕 bottom 误处理成黑色。
- 换句话说，surface 会加一点冷色水层，但救不回已经错误的 raw refraction。

### 5. 验证一个失败的 bottom scratch hot-replace，不把它误当成实现依据
**Files Changed:** 无

**Implementation:**
- 尝试写了两版 scratch bottom HLSL（直接采 `BottomDiffuse` 的 world-position / river-UV 版本）挂到 `184` 上。
- 它们都没得到可作为最终实现依据的结果。

**Rationale:**
- 这两版 scratch shader 没有保留当前 bottom 的真实 `tangentUv/parallax/compressedWorld` 语义，只能说明“简化版 hot-replace 不足以证明 bottom 最终修法”，不能说明原方案就对或就错。

### 6. 缩窄 current bottom 采样变量：parallax / `_TextureUvScale` 都不是代表像素的主根因
**Files Changed:** 无

**Implementation:**
- 在当前 capture 的 representative pixel `(172,299)` 上，连续做了三版“只保留 `bottomDiffuse` 颜色输出”的 hot-replace：
  - 保留 current 语义，仅绕开 lighting：`float3(0.0381, 0.0270, 0.0152)`
  - 去掉 `tangentSpaceOffset`，直接用 `scaledRiverUv`：`float3(0.0365, 0.0297, 0.0136)`
  - 再把 `scaledRiverUv` 退回 `riverUv`：数值与上一版基本一致
- 再试了 `worldUv` 直接采 `BottomDiffuse`，同一代表像素仍停留在 `float3(0.0396, 0.0264, 0.0152)` 一档。

**Rationale:**
- 这说明对当前这颗像素而言，单独切掉 parallax offset 或 `_TextureUvScale` 不会把河床从黑/冷暗拉到 CK3 的暖棕。
- 当前 raw `BottomDiffuse` 采样本身仍然偏暗，后续如果改 `SDSL`，不能只改一条 `tangentUv` 公式就收工。

### 7. CK3 capture 实际走的是 `CalcRiverBottom` 非 advanced world-UV 路径
**Files Changed:** 无

**Implementation:**
- 在 CK3 capture 上重新查看 `336` 的 pixel trace / shader disasm：
  - 输入签名里没有可用的 `UV.x` 主采样链路
  - disasm 第 `57` 行先构造 `worldUv = worldPos.xz + worldSpaceParallax * width`
  - 第 `71-73` 行用这个 `worldUv` 采样 `BottomDiffuse / BottomProperties / BottomNormal`
  - 第 `241-246` 行的 alpha 只由 `connection/depth/bank fade` 推出，不依赖 `bottomDiffuse.a`
- 这与仓库里当前 `RiverBottom.sdsl` / `RiverShaderTextTests.cs` 锁定的 advanced `tangentUv` 路径相冲突。
- 同时补读了旧日志 [2026-06-16-river-bottom-world-uv-renderdoc-fix.md](../16/2026-06-16-river-bottom-world-uv-renderdoc-fix.md)，发现它已经记录过同一方向的 RenderDoc 证据。

**Rationale:**
- 问题不再只是“当前 bottom 比 CK3 暗”，而是“当前实现分支本身就和 CK3 这帧实际使用的 shader 变体不同”。
- 后续如果要回到 `SDSL`，首先要审视的是：我们是不是把一个已经验证过的 world-UV/non-advanced 路径，又被后续 parity 计划和测试重新覆盖回了 advanced `tangentUv` 路径。

### 8. RenderDoc MCP 在最后一次完整 world-UV 路径替换时崩溃
**Files Changed:** 无

**Implementation:**
- 准备把一版更接近 CK3 `CalcRiverBottom` 的完整 world-UV/non-advanced bottom shader 替到 current capture 上。
- 在 capture 切换后执行 `shader_replace` 时，`renderdoc-mcp` transport 直接断开，后续 `open_capture` 也无法恢复。

**Rationale:**
- 本轮因此没能完成“完整 non-advanced world-UV hot-replace 在 current capture 上的最终 RT 导图”。
- 但在崩溃前，已经拿到了足够多的 current/CK3 侧证据，足以把下一步收敛到 `RiverBottom` 路径分支与测试假设上。

---

## Decisions Made

### Decision 1: 后续 pass 对位一律使用“组内最后一个 draw 的 RT 状态”
**Context:** 旧文档把 `184`、`252`、`332`、`460` 当成 pass 输出，容易把单个 section draw 误当成完整 pass。

**Options Considered:**
1. 继续沿用首个 draw 号 - 方便，但会误导 RT 导图与 pixel history
2. 只看 draw count，不重新分组 - 信息不够
3. 用 RT usage + 组内最后一个 draw 导出 pass 完成态

**Decision:** 选择 3
**Rationale:** 最符合 RenderDoc 的真实资源写入链。
**Trade-offs:** 记录时需要多记一层“组首 draw / 组尾 draw”的区别。
**Documentation Impact:** 已更新 `stride-river-rendering-patterns.md`

### Decision 2: 不在本轮直接改 SDSL
**Context:** 现在已经确定问题层级，但 bottom 的“具体哪一条实现改动最值当”还需要更精确的 hot-replace，尤其是要保留当前 parallax / compressedWorld 语义。

**Options Considered:**
1. 立刻改 `RiverBottom.sdsl`
2. 先继续做 capture 级最小变量 hot-replace

**Decision:** 选择 2
**Rationale:** 符合本轮用户要求，也符合 system/debugging skill 的根因优先原则。
**Trade-offs:** 本轮不产生运行时代码改动。
**Documentation Impact:** 仅记录 session log 与 learning

### Decision 3: CK3 移植优先以 capture disasm 为准，不以源码里存在的 advanced 分支为准
**Context:** `jomini_river_bottom.fxh` 同时存在 `CalcRiverBottom` 与 `CalcRiverBottomAdvanced`，但当前 CK3 capture 的实际 draw 明显走的是前者。

**Options Considered:**
1. 继续按 `CalcRiverBottomAdvanced` 推 current parity
2. 以当前 CK3 capture 的实际 shader 路径为准，回头审视仓库里的 parity spec / tests

**Decision:** 选择 2
**Rationale:** 用户要求的是“对照 capture 逐 pass 分析”，capture 才是当前这帧真实运行的事实。
**Trade-offs:** 意味着现有 spec / tests 里关于 `tangentUv` advanced 路径的结论可能需要回滚或重写。
**Documentation Impact:** 需要补充到 learning，并在后续实现前重新校正相关 spec/test 文档。

---

## What Worked ✅

1. **RT usage + pixel history 联合建模**
   - What: 先查 `get_resource_usage` 再查 representative pixel 的 `pixel_history`
   - Why it worked: 同时拿到了“pass 结构”和“单像素颜色演化”，避免只看截图猜测
   - Reusable pattern: Yes

2. **surface 最小热替换**
   - What: 把 current surface 直接改成输出 `RefractionTexture`
   - Impact: 直接证明 surface 不是主根因，当前 raw refraction 才是问题底层

---

## What Didn't Work ❌

1. **scratch bottom HLSL 直接替代 current bottom**
   - What we tried: 用最小 HLSL 直接采 `BottomDiffuse`
   - Why it failed: 没有复用 current bottom 的 `tangentUv/parallax/compressedWorld` 语义
   - Lesson learned: bottom hot-replace 不能只保留 texture slot，必须保留关键空间语义
   - Don't try this again because: 否则只能得到“示意图”，不能得到可落地的实现结论

---

## Problems Encountered & Solutions

### Problem 1: 旧文档中的 event 号已经不适合作为完整 pass 标识
**Symptom:** 文档里常提 `184/213`、`332/460`，但当前 capture 里并没有 `213` 这个 surface draw。
**Root Cause:** 之前混淆了“单个 draw 事件”和“完整 pass 的最终 RT 状态”。
**Investigation:**
- Tried: `list_events`
- Tried: `get_resource_usage`
- Found: 当前完整 surface 组实际是 `252/270/288/306`

**Solution:**
```text
current bottom full pass  = 223
current surface full pass = 306
ck3 bottom full pass      = 338
ck3 surface full pass     = 466
```

**Why This Works:** 组尾 draw 才代表同一 RT 的本轮完整写入状态。
**Pattern for Future:** 多 draw 共用一张 RT 的 river pass，始终以 usage 分组和组尾 draw 为准。

### Problem 2: 当前 surface 看起来在“做很多事”，但其实救不回错误的 raw refraction
**Symptom:** 只看最终图时，很容易怀疑是 surface tint / foam / reflection 配置错了。
**Root Cause:** 当前 raw refraction 自身已经过黑，surface 只是叠了一层冷色水层。
**Investigation:**
- Tried: current surface 最小热替换
- Found: 热替换后 full-res 像素与 half-res bottom RT 像素一一对应

**Solution:**
```text
先修 seed / bottom，再修 surface
```

**Why This Works:** root cause 在上游时，末端调色只能掩盖，不会复现 CK3 的河床结构。
**Pattern for Future:** water surface 先做“直接 refraction 输出”热替换，快速确认主战场是否在上游。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 增加“组尾 draw 代表完整 pass”的规则
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本次没有架构变化
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本次没有功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 用组尾 draw 导出完整 river pass
- When to use: RenderDoc 中一个 river pass 分成多个 draw 写同一张 RT
- Benefits: 避免把首段 section draw 当成完整 pass
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 用简化 scratch bottom shader 直接判断最终修法
- What not to do: 丢掉 parallax / compressedWorld / tangentUv 语义，只保留贴图采样槽位
- Why it's bad: 会得到误导性的“示意输出”
- Add warning to: 本 session log

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮只做 RenderDoc 诊断和热替换验证

---

## Code Quality Notes

### Performance
- **Measured:** 无性能测量
- **Target:** 本轮目标是诊断准确性
- **Status:** ⚠️ Close

### Testing
- **Tests Written:** 无
- **Coverage:** RenderDoc pass 结构、resource usage、pixel history、shader hot-replace
- **Manual Tests:** 如继续推进实现，先重新截一帧包含最新代码/资产状态的 `.rdc`

### Technical Debt
- **Created:** 无代码债务
- **Paid Down:** “旧 event 号 = 完整 pass” 这类诊断债务
- **TODOs:** 下轮做真正可落地的 bottom hot-replace，应直接基于 `shader_RiverBottom_526d4ef6acd402564deb95ff34afdab2.hlsl` 微改，而不是 scratch shader

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 恢复/重启 `renderdoc-mcp`，把“完整 CK3 `CalcRiverBottom` 风格的 world-UV/non-advanced bottom shader”真正替到 current capture 上，再导 `223` 的完整 RT
2. 复核 `Terrain.Editor/Effects/RiverBottom.sdsl`、`Terrain.Editor.Tests/RiverShaderTextTests.cs` 与 `docs/superpowers/specs/2026-06-16-river-ck3-parity-design.md` 中锁定 advanced `tangentUv` 的假设
3. 只有当完整 hot-replace 证明方向有效后，再改 `Terrain.Editor/Effects/RiverBottom.sdsl`、相关测试与 spec

### Blocked Items
- **Blocker:** `renderdoc-mcp` 在最后一次 `shader_replace` 后 transport 崩溃
- **Needs:** 恢复 RenderDoc 工具通道，完成最后一轮完整 non-advanced world-UV hot-replace
- **Owner:** Codex

### Questions to Resolve
1. 当前 `RiverBottom` 是否需要从 advanced `tangentUv` 路径回退/改写为 CK3 capture 实际使用的 non-advanced world-UV 路径？
2. 如果回到 world-UV 路径，当前 capture 里仍偏黑的 raw diffuse 究竟是 lighting 语义问题，还是 world-UV 频率/偏移还没对齐？

### Docs to Read Before Next Session
- [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](../16/2026-06-16-river-refraction-buffer-vs-ck3-analysis.md) - 上一轮对 raw refraction 的结论
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 本轮新增了组尾 draw 规则

---

## Session Statistics

**Files Changed:** 2
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: 当前 capture 完整 bottom/surface pass 不是 `184/252`，而是 `223/306`
- Critical decision: CK3 这帧实际 bottom shader 走的是 non-advanced `worldUv` 路径，不是仓库当前锁定的 advanced `tangentUv` 路径
- Active pattern: `resource usage -> pixel history -> minimal shader replace -> export RT`
- Current status: 已确认主根因层级在 seed/bottom，不在 surface；并且 parity spec/test 很可能锁定了错误的 CK3 shader 分支

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无运行时代码改动
- Constraints: `renderdoc-mcp` 当前已崩溃，完整 non-advanced world-UV hot-replace 还差最后一次验证

**Gotchas for Next Session:**
- Watch out for: 不要再拿 `184/252/332/460` 直接当完整 pass 图
- Don't forget: current surface 直接输出 refraction 后，会与 half-res bottom RT 对上
- Remember: CK3 源码里虽然有 `CalcRiverBottomAdvanced`，但这帧 capture 的实际 draw 不是它

---

## Links & References

### Related Documentation
- [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](../16/2026-06-16-river-refraction-buffer-vs-ck3-analysis.md)
- [2026-06-16-river-ck3-parity-implementation.md](../16/2026-06-16-river-ck3-parity-implementation.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_bottom.fxh`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_river_surface.fxh`

### Code References
- current generated HLSL: `Bin/Editor/Debug/win-x64/log/shader_RiverBottom_526d4ef6acd402564deb95ff34afdab2.hlsl`
- current generated HLSL: `Bin/Editor/Debug/win-x64/log/shader_RiverSurface_e5ab8d2c154c034d36f8b49237578124.hlsl`

---

## Notes & Observations

- 本轮最重要的新事实不是“哪一个 shader 更黑”，而是“当前 seed 链路和 CK3 根本不是同一类输入”。
- current surface 的 hot-replace 证据很硬：它可以直接退化成 half-res raw refraction，而且像素值与 bottom RT 对得上。

---

*Template Version: 1.0 - Based on Archon-Engine template*
