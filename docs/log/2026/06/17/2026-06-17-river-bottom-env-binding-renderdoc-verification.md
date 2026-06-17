# River Bottom Environment Binding RenderDoc Verification
**Date**: 2026-06-17
**Session**: River Bottom Environment Binding RenderDoc Verification
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用新的 RenderDoc capture 验证 river bottom 是否已经真正切到 scene skybox cubemap，而不是继续吃 `reflection-specular`。

**Secondary Objectives:**
- 对比修正前后的 bottom 代表像素，确认这轮 C# 绑定修复是否有效。
- 在决定是否继续改 `RiverBottom.sdsl` 前，先收敛“剩余差距还在不在 C# 绑定层”。

**Success Criteria:**
- 能给出 current capture 中 bottom/surface draw 的准确事件分组。
- 能用 resource usage 证明 bottom 和 surface 读取的是不同 cubemap。
- 能确认这轮修复是否只解决了 binding，而没有完全解决 CK3 亮度/色相差距。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-light-binding-fix.md](./2026-06-17-river-bottom-light-binding-fix.md)
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)

**Current State:**
- 上一轮已经把 `RiverRenderFeature` 改为 bottom 优先绑定 `Skybox texture`。
- 但在 `river-bottom-light-binding_frame1355.rdc` 里发现实现实际仍然吃的是 `reflection-specular`，原因是 `EnvironmentMapTexture` 被连续 `SetTexture` 两次，第二次覆盖了第一次。

**Why Now:**
- 用户提供了新的 `C:\\Users\\Redwa\\Desktop\\debug.rdc`，需要确认代码修正是否真的进入 GPU capture。

---

## What We Did

### 1. 先用红绿测试锁住“只能绑定一次 bottom env”
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```csharp
Texture? bottomEnvironment = riverResources.BottomEnvironment ?? riverResources.ReflectionSpecular;
SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, bottomEnvironment);
```

并新增文本测试，要求：
- bottom env 先在 CPU 侧做 `??` fallback
- `EnvironmentMapTexture` 只绑定一次
- 不允许再无条件写入 `riverResources.ReflectionSpecular`

**Rationale:**
- 这是 `frame1355` capture 直接暴露出的真实 bug，不需要先回到 SDSL。

### 2. 用新 `debug.rdc` 重新标定 river pass
**Files Changed:** 无

**Implementation:**
- 新 capture：`63 draws / 76 events / 0 HIGH log`
- 重新确认事件分组：
  - bottom：`184 / 197` -> `ResourceId::7757`
  - surface：`226 / 244` -> `ResourceId::4055`

**Rationale:**
- 这张新帧不再是 `frame1355` 的 `184 / 213` 双 draw 结构，必须重新对位。

### 3. 用 cubemap resource usage 证明 binding 已经修正
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 当前 capture 里真正的 cubemap 资源是：
  - `ResourceId::4051`：`R16G16B16A16_FLOAT`, `256x256x6`
  - `ResourceId::547`：`BC3_UNORM`, `512x512x6`
- usage：
  - `4051` 被 `138 / 184 / 197` 当作 `PS_Resource`
  - `547` 被 `226 / 244` 当作 `PS_Resource`
- 导出 face 0 后可视化：
  - `4051` 是蓝白渐变 skybox
  - `547` 是地景/天空 reflection cubemap

**Rationale:**
- 这说明 bottom 和 surface 已经不再共用同一张 cubemap。

### 4. 量化修正后的 river bottom 仍然偏暗
**Files Changed:** 无

**Implementation:**
- 修正前 `river-bottom-light-binding_frame1355.rdc` 的 bottom 代表像素：
  - `event 184 @ (471,331)` -> `RGB = (0.0529, 0.0468, 0.0300)`
- 修正后 `debug.rdc` 的 bottom 代表像素：
  - `event 197 @ (396,231)` -> `RGB = (0.0679, 0.0711, 0.0645)`
- 代表像素亮度约提升 `1.49x`
- 但 `debug_pixel` 显示当前 bottom shader 自己在 `event 184` 的直接输出只有：
  - `RGB = (0.0409, 0.0402, 0.0363)`
- `pixel_history` 说明当前最终 `0.0679 / 0.0711 / 0.0645` 仍然有一部分来自 `event 158` 的 scene seed 混合。

**Rationale:**
- skybox binding 修正是有效的，但它只修好了输入源，没有把 raw bottom shader 推到 CK3 的暖棕量级。

---

## Decisions Made

### Decision 1: 把这轮结果定性为“binding 修复完成，但 shader parity 未完成”
**Context:** 新 capture 已经显示 bottom 读取的是 skybox cubemap，但 bottom RT 视觉上仍明显偏暗。

**Options Considered:**
1. 看到 binding 正确就宣布问题解决
2. 继续把剩余差距归到 bottom shader/material lighting 语义

**Decision:** 选择 2
**Rationale:** RenderDoc 证据显示 raw bottom shader 仍只输出约 `0.04` 一档，不能把 CK3 差距继续归咎于 cubemap 来源。

### Decision 2: 不在本轮直接改 `RiverBottom.sdsl`
**Context:** 用户要求改 SDSL 前先用 RenderDoc 热验证。

**Options Considered:**
1. 直接继续改 shader
2. 先停在“binding 已验证”的阶段，下一轮再做 shader hot-edit

**Decision:** 选择 2
**Rationale:** 这轮已经完成了 C# 绑定层收敛；下一轮该进入 `RiverBottom.sdsl` 的最小热验证，而不是继续混着改。

---

## What Worked ✅

1. **resource usage 验证真实绑定**
   - What: 不再只看 `get_bindings` 的 slot 名，改用 cubemap resource usage + export_texture
   - Why it worked: 直接看出 bottom 读 `4051`，surface 读 `547`
   - Reusable pattern: Yes

2. **红测锁单次 fallback 绑定**
   - What: 用文本测试阻止 `EnvironmentMapTexture` 被第二次 `SetTexture` 覆盖
   - Impact: 让 RenderDoc 发现的 bug 直接变成可回归验证的代码约束

---

## What Didn't Work ❌

1. **假设“修完 cubemap 来源就会接近 CK3”**
   - What we tried: 先把 bottom env 改成 skybox 语义
   - Why it failed: 只修了输入源，raw bottom shader/material lighting 自身仍然偏暗偏冷
   - Lesson learned: env binding 是必要条件，不是充分条件

---

## Problems Encountered & Solutions

### Problem 1: bottom skybox 绑定在代码里被自己覆盖
**Symptom:** `frame1355` capture 里 bottom 和 surface 仍共用同一张 cubemap。
**Root Cause:** `RiverRenderFeature.BindRiverTextures` 对 `EnvironmentMapTexture` 连续执行两次 `SetTexture`。
**Investigation:**
- Tried: `get_resource_usage`
- Found: broken capture 里 bottom / surface 共同读取旧 reflection cubemap

**Solution:**
```csharp
Texture? bottomEnvironment = riverResources.BottomEnvironment ?? riverResources.ReflectionSpecular;
SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, bottomEnvironment);
```

**Why This Works:** 把 fallback 提前到 CPU 侧解析，避免第二次写参数覆盖第一次绑定。

### Problem 2: binding 修正后 river 仍然明显偏暗
**Symptom:** 新 `debug.rdc` 已正确读 skybox，但 bottom/export 仍接近黑色河带。
**Root Cause:** 当前 raw bottom shader 输出仍然落在 `~0.04` 一档，scene seed 只是把最终 RT 稍微抬亮。
**Investigation:**
- Tried: `pick_pixel`
- Tried: `pixel_history`
- Tried: `debug_pixel summary`
- Found: `event 184` shader output 低于最终 post-blend 颜色，说明当前问题层级已不在 env binding

**Pattern for Future:** 当 C# 绑定修正后仍暗，优先看 `debug_pixel` 的 raw shader output，而不是只看最终 RT。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录 RenderDoc 已验证 bottom/surface 分别读取不同 cubemap
- [x] Update `docs/CURRENT_FEATURES.md` - 记录单次 fallback 绑定与当前 capture 验证状态
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 增加双 `SetTexture` 覆盖坑

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 对同一 shader 参数连写两次 texture 期待“前者优先、后者仅 fallback”
- What not to do: 先绑 skybox，再无条件绑 reflection cubemap
- Why it's bad: 第二次写参数会直接覆盖第一次
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无新的大架构决策
- **Reason:** 本轮主要是验证前一轮实现是否真的进入 GPU capture

---

## Code Quality Notes

### Testing
- **Tests Written:** 扩展 `RiverShaderTextTests`
- **Coverage:** bottom env single-bind fallback
- **Manual Tests:** 新 `debug.rdc` RenderDoc 复核完成

### Technical Debt
- **Paid Down:** bottom / surface 共用同一 cubemap 的绑定错误
- **TODOs:** 下一轮对 `RiverBottom.sdsl` 做最小热验证，决定是继续调 lighting，还是回到 world-UV / non-advanced 路径

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 对当前 `debug.rdc` 的 `184` 做最小 bottom hot-edit，验证是否仅靠提高 bottom lighting 就能接近 CK3
2. 如果 lighting 不能单独解释差距，回到 `RiverBottom.sdsl` 的主采样路径分支，验证是否仍要从 advanced tangent-UV 回退到 capture 对齐路径
3. 再次截新帧，确认任何下一轮 shader 改动都真的进入 GPU capture

### Questions to Resolve
1. 当前 raw bottom shader 偏暗，主因是 lighting 参数/公式，还是底部主采样路径本身？
2. CK3 对照里当前最缺的是暖色 albedo、diffuse IBL 能量，还是 bank/connection 处的底色结构？

### Docs to Read Before Next Session
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `debug.rdc` 当前 river 结构是 `184/197` bottom + `226/244` surface
- bottom 现在实际读取 `ResourceId::4051` skybox cubemap，surface 读取 `ResourceId::547` reflection cubemap
- binding bug 已修复，但 bottom raw shader output 仍然偏暗

**What Changed Since Last Doc Read:**
- Implementation: `RiverRenderFeature` 改成 bottom env 单次 `??` fallback 绑定
- Verification: 新 `debug.rdc` 已证明 skybox binding 进入 GPU
- Constraints: 还没有新的 shader hot-edit 证明下一步具体该改哪段 SDSL

**Gotchas for Next Session:**
- 不要再假设 `213` 是 surface；这张 capture 里 surface 是 `226/244`
- 不要只看 `get_bindings` 的 slot 名称；要查 cubemap usage
- `debug_pixel` 要打到真正写该像素的 event，上游 `197` / `244` 不一定命中 fragment

---

## Links & References

### Related Sessions
- [2026-06-17-river-bottom-light-binding-fix.md](./2026-06-17-river-bottom-light-binding-fix.md)
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\river-bottom-light-binding_frame1355.rdc`

### Code References
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---

## Notes & Observations

- 这轮最重要的结论不是“河流已经对齐 CK3”，而是“C# cubemap binding 层的问题已经收敛，剩余差距在 bottom shader 自身”。
- 代表像素亮度确实提升了，但提升幅度还不足以单独解释 CK3 的暖棕河床观感。

---

*Template Version: 1.0 - Based on Archon-Engine template*
