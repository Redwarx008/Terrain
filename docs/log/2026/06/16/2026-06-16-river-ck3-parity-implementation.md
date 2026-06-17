# River CK3 Parity Implementation
**Date**: 2026-06-16
**Session**: River CK3 Parity
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 把 Editor 河流渲染链路从错误的旧 bottom/refraction 语义推进到 CK3 对齐的 `bottom -> refraction -> surface` 三段实现。

**Secondary Objectives:**
- 完成 Task 3 的规范审查与代码质量审查。
- 落地 Task 4：分离 scene seed 与 working refraction buffer，并让 surface bank fade 显式化。
- 更新总览、功能清单和 learnings。

**Success Criteria:**
- `RiverBottom` 维持已通过审查的 advanced tangent-UV/parallax/bank fade 语义。
- `RiverRenderFeature` 分离 `SceneSeedColor` 与 `BottomColor`。
- `RiverSurface` 使用显式 bank fade 且 refraction sampler 不再是 `LinearClamp`。
- `Terrain.Editor.Tests` 全绿。
- 如可能，补一份新的 RenderDoc capture 验证实际画面。

---

## Context & Background

**Previous Work:**
- Related: [River CK3 对标渲染设计](../../../../docs/superpowers/specs/2026-06-16-river-ck3-parity-design.md)
- Related: [River CK3 Parity Implementation Plan](../../../../docs/superpowers/plans/2026-06-16-river-ck3-parity.md)
- See: [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](2026-06-16-river-refraction-buffer-vs-ck3-analysis.md)
- See: [2026-06-16-river-bottom-hotreplace-validation.md](2026-06-16-river-bottom-hotreplace-validation.md)

**Current State:**
- Task 1、Task 2 已完成。
- Task 3 的 advanced bottom 已实装，但此前仍有两个 Task 4 范围的红测：
  - `river surface shader follows ck3 water color and refraction semantics`
  - `river render feature separates scene seed from working refraction buffer`

**Why Now:**
- 当前 text tests 已把 CK3 parity 的关键语义锁死；如果不继续把 scene seed 分离和 surface bank fade 落地，后续 RenderDoc 分析会继续被旧链路误导。

---

## What We Did

### 1. 完成 Task 3 审查闭环
**Files Changed:** 无代码新增；审查对象为 `Terrain.Editor/Effects/RiverBottom.sdsl`、`Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 复核当前 `RiverBottom.sdsl` 已满足 approved advanced 语义：
  - `CalcBottomDepth`
  - `CalculateParallaxOffsetSteep`
  - `CalcParallaxedBottomUvs`
  - 显式 `edgeFade1/edgeFade2`
  - 分量式 `bottomWorldPosition`
- 复核 `RiverShaderTextTests.cs` 的两处同步：
  - 不再强制 `BottomDepthTexture.Sample`
  - 不再锁死单行式 `bottomWorldPosition = ...`

**Rationale:**
- 先确认 Task 3 没有遗漏或错误扩展，再进入 Task 4，避免把 surface/refraction 问题误报成 bottom 语义问题。

**Architecture Compliance:**
- ✅ 符合 [2026-06-16-river-ck3-parity-design.md](../../../../docs/superpowers/specs/2026-06-16-river-ck3-parity-design.md)
- ✅ 代码质量审查无 Critical/Important 问题；仅保留一条 Minor：`BottomDepthTexture` 等旧 shader API 仍是兼容保留

### 2. 分离 `SceneSeedColor` 与 `BottomColor`
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderResources.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```csharp
public Texture? SceneSeedColor { get; private set; }
...
refractionSeedScaler.SetOutput(renderResources.SceneSeedColor);
commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);
surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp);
```

**Rationale:**
- 把“场景种子”与“bottom pass working refraction buffer”拆开后，RenderDoc 可以直接区分是哪一层在污染岸边颜色。
- `PointClamp` 避免 surface 再次把窄河岸附近的 terrain 颜色线性抹进 refraction。

**Architecture Compliance:**
- ✅ 保持 existing half-res refraction 架构，不额外引入新的 RT 层级
- ✅ 与现有 dual-source bottom pass 兼容

### 3. 让 `RiverSurface` 的 bank fade 显式化
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
```c
float edgeFade1 = smoothstep(0.0f, max(_BankFade, 0.0001f), riverUv.y);
float edgeFade2 = smoothstep(0.0f, max(_BankFade, 0.0001f), 1.0f - riverUv.y);
float alpha = edgeFade1 * edgeFade2 * connectionFade * transparency * saturate(_ZoomBlendOut);
```

**Rationale:**
- 明确让 surface 的河岸透明度遵循 CK3 bank fade 语义，而不是继续依赖隐含 helper 或旧黑边视觉。

**Architecture Compliance:**
- ✅ 与已通过的 text tests 对齐
- ✅ 不把额外颜色修正、hot-fix multiplier 或 Task 5 文档逻辑混入 shader

### 4. 更新文档与经验沉淀
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 总览与功能清单不再声称 bottom 主采样仍是 `worldPosition.xz` 旧语义。
- 补充 `SceneSeedColor` / `BottomColor` 分离、explicit bank fade、`PointClamp` refraction 的现状。
- 在 learnings 中新增：
  - CK3 河床优先对齐 tangent-UV + parallax
  - scene seed 与 working refraction buffer 必须分离

---

## Decisions Made

### Decision 1: 不为 Task 3 的 Minor 债务停住主线
**Context:** Code review 指出 `BottomDepthTexture`、`_BottomUvScale`、`_ParallaxStrength` 等旧 shader API 仍保留，但不再参与 advanced 路径。

**Options Considered:**
1. 立即清理旧 API - 可减少误导，但会把 Task 3/4 扩 scope
2. 保持兼容保留并记录债务 - 主线继续，后续再清理

**Decision:** 选择 2
**Rationale:** 当前没有 Critical/Important 风险，且用户主诉仍是 CK3 parity，而不是 API 清理。
**Trade-offs:** 读代码时仍可能误以为存在独立 `BottomDepthTexture` 主驱动路径。
**Documentation Impact:** 已记录到 session log 与 reviewer 结论；后续可在专门清理时补注释或删除。

### Decision 2: 自动 RenderDoc 截帧失败时如实保留 Partial 状态
**Context:** `capture_frame` 启动 `Terrain.Editor.exe` 后超时，没有得到新的 `.rdc` 文件。

**Options Considered:**
1. 直接用旧 `debug.rdc` 充当本轮 GPU 验证
2. 声称“应该没问题”并结束
3. 标记为 Partial，等待新 capture

**Decision:** 选择 3
**Rationale:** 旧 capture 不能代表本轮代码；没有新 `.rdc` 就不能声称 GPU 结果已对齐。
**Trade-offs:** 本轮无法给出新的 `184/213` 对照证据。
**Documentation Impact:** 本日志明确记录自动 capture 失败，供下次会话直接接上。

---

## What Worked ✅

1. **Task 3 两阶段审查**
   - What: 先做 spec review，再做 code quality review
   - Why it worked: 先锁定语义，再看维护性，避免把 Task 4 职责误混进 Task 3
   - Reusable pattern: Yes

2. **用 text tests 驱动 Task 4 落地**
   - What: 在改 `SceneSeedColor` / `PointClamp` / explicit bank fade 后立即顺序 build + run
   - Impact: 两个剩余红测全部消失，快速确认改动命中目标

---

## What Didn't Work ❌

1. **自动 RenderDoc capture**
   - What we tried: 用 `capture_frame` 启动 `E:\Stride Projects\Terrain\Bin\Editor\Debug\win-x64\Terrain.Editor.exe`
   - Why it failed: RenderDoc 在本次尝试里超时，且没有生成输出 `.rdc`
   - Lesson learned: 当前自动截帧链路仍不稳定，不能当成已具备的验证手段
   - Don't try this again because: 不是永远不能再试，而是下次需要先缩小启动/帧延迟/场景稳定性问题，再把它当正式验证

---

## Problems Encountered & Solutions

### Problem 1: Task 4 的两个红测阻塞推进
**Symptom:** `Terrain.Editor.Tests` 只剩 surface bank fade / scene seed split 两项失败
**Root Cause:** 代码仍把 scene seed 与 bottom working buffer 混在一起，surface alpha 也还没有显式 bank fade
**Investigation:**
- Tried: 顺序重跑 `Terrain.Editor` / `Terrain.Editor.Tests` build 与 test
- Found: 失败点和 plan 中的 Task 4 精确一致

**Solution:**
```csharp
refractionSeedScaler.SetOutput(renderResources.SceneSeedColor);
commandList.CopyRegion(renderResources.SceneSeedColor, 0, null, renderResources.BottomColor, 0);
surfaceEffect.Parameters.Set(RiverSurfaceKeys.RefractionSampler, graphicsDevice.SamplerStates.PointClamp);
```

**Why This Works:** 把 refraction seed 的来源与 bottom pass 的输出目标拆开后，surface 读取的语义清楚了；显式 bank fade 则把岸边透明度控制拉回 CK3 语义。
**Pattern for Future:** 先用测试锁定“资源语义分离”和“shader alpha 结构”，再改实际渲染代码。

### Problem 2: 新 GPU capture 缺失
**Symptom:** `capture_frame` 返回 `Capture timed out and no capture file found`
**Root Cause:** 当前自动 RenderDoc capture 链路未稳定，不足以保证 Editor 启动后在延迟帧内产出 `.rdc`
**Investigation:**
- Tried: `delayFrames=180`，显式指定 exe、workingDir、outputPath
- Found: 进程路径有效，但 RenderDoc 未生成 capture 文件

**Solution:**
- 本轮不伪造 GPU 结论。
- 把实现状态标为 `⚠️ Partial`，等待下一次提供新的人工或自动 capture。

**Why This Works:** 维持证据链完整，不把旧 capture 或推测当成新结果。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 记录 `SceneSeedColor` / `BottomColor` 分离与 advanced bottom 语义
- [x] Update `CURRENT_FEATURES.md` - 记录新的三段 river pipeline
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 追加新 pattern / anti-pattern

### New Patterns/Anti-Patterns Discovered
**New Pattern:** `SceneSeedColor` / `BottomColor` 分离
- When to use: screen-space refraction 既需要 scene seed，又需要 bottom working buffer 时
- Benefits: RenderDoc 分析更清晰，减少滤波串扰
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 把自动 capture 失败当成“应该已经修好”
- What not to do: 没有新的 `.rdc` 还声称 RenderDoc 已验证
- Why it's bad: 直接破坏河流 parity 的证据链
- Add warning to: 本 session log

### Architectural Decisions That Changed
- **Changed:** river refraction 中间缓冲语义
- **From:** `BottomColor` 同时承担 scene seed 与 working refraction
- **To:** `SceneSeedColor` 保存 scene seed，`BottomColor` 只做 working refraction
- **Scope:** `RiverRenderResources`、`RiverRenderFeature`、`RiverSurface`
- **Reason:** 对齐 CK3 语义并降低窄河岸颜色串扰

---

## Code Quality Notes

### Performance
- **Measured:** 未做新的 GPU 性能测量
- **Target:** 本轮优先解决语义正确性，不做性能收敛声明
- **Status:** ⚠️ Close

### Testing
- **Tests Written:** 无新增测试文件；复用既有 river text tests
- **Coverage:** `RiverBottom` advanced 语义、scene seed 分离、surface bank fade、refraction sampler
- **Manual Tests:** 需要新的 RenderDoc capture 验证 `184/213` 类似 draw 的实际 RT 输出

### Technical Debt
- **Created:** 无新的阻断债务
- **Paid Down:** scene seed / working refraction 混用；surface 隐式 bank fade
- **TODOs:** 后续清理 `BottomDepthTexture`、`_BottomUvScale`、`_ParallaxStrength` 这类兼容保留 shader API

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新获得新的 river RenderDoc capture - 这是确认实际画面的前置条件
2. 对照新 capture 中的 bottom/surface draw 与 CK3 `332/460` - 验证是否还存在河床偏暗或岸边不露底的问题
3. 如仍有差距，优先看 raw bottom/refraction，而不是直接调 surface 最终色

### Blocked Items
- **Blocker:** 自动 RenderDoc capture 没有产出新 `.rdc`
- **Needs:** 新的人工 capture，或把 `capture_frame` 链路调通
- **Owner:** Codex / 用户协同

### Questions to Resolve
1. 当前最新 build 的实际 bottom RT 是否已经出现接近 CK3 的暖色岸边河床？ - 需要新 capture 才能回答
2. 自动 capture 失败是启动时序问题，还是 Editor 对 RenderDoc 注入不稳定？ - 关系到后续是否能自动回归

### Docs to Read Before Next Session
- [2026-06-16-river-ck3-parity-implementation.md](2026-06-16-river-ck3-parity-implementation.md) - 直接承接本次实现与未完成验证
- [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](2026-06-16-river-refraction-buffer-vs-ck3-analysis.md) - 保留旧 capture 的对照证据

---

## Session Statistics

**Files Changed:** 6
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Rendering/River/RiverRenderResources.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Effects/RiverSurface.sdsl`
- Critical decision: 没有新的 `.rdc` 就不要声称 GPU parity 已验证
- Active pattern: `SceneSeedColor -> CopyRegion -> BottomColor -> Surface(PointClamp)`
- Current status: 代码与文本测试完成，GPU capture 验证未完成

**What Changed Since Last Doc Read:**
- Architecture: river refraction 中间缓冲从单资源语义改为 scene seed / working buffer 分离
- Implementation: surface bank fade 显式化，refraction sampler 改为 `PointClamp`
- Constraints: 自动 RenderDoc capture 仍不稳定

**Gotchas for Next Session:**
- Watch out for: 不要再引用旧文档里的 `worldPosition.xz` bottom 主采样说法
- Don't forget: `BottomDepthTexture` 现在只是兼容保留，不是 advanced 路径主输入
- Remember: 旧 `debug.rdc` 不能证明本轮实现效果

---

## Links & References

### Related Documentation
- [River CK3 对标渲染设计](../../../../docs/superpowers/specs/2026-06-16-river-ck3-parity-design.md)
- [River CK3 Parity Implementation Plan](../../../../docs/superpowers/plans/2026-06-16-river-ck3-parity.md)

### Related Sessions
- [2026-06-16-river-refraction-buffer-vs-ck3-analysis.md](2026-06-16-river-refraction-buffer-vs-ck3-analysis.md)
- [2026-06-16-river-bottom-hotreplace-validation.md](2026-06-16-river-bottom-hotreplace-validation.md)

### External Resources
- CK3 `jomini_river_bottom.fxh`
- CK3 `jomini_river_surface.fxh`

### Code References
- `Terrain.Editor/Rendering/River/RiverRenderResources.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverSurface.sdsl`

---

## Notes & Observations

- 本轮没有生成新的 RenderDoc capture；任何关于“画面已经对齐 CK3”的结论都还不能下。
- 文本测试已经足够说明 Task 4 代码层目标已完成，下一步的价值主要在 GPU 实证而不在继续盲改 shader。

---

*Template Version: 1.0 - Based on Archon-Engine template*
