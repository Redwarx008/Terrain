# River Bottom WorldUv Capture Alignment Implementation
**Date**: 2026-06-17
**Session**: River bottom worldUv capture alignment implementation
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 把 `RiverBottom.sdsl` 从 current 的 advanced tangent-UV 主路径改到与 `ck3-river.rdc` 这帧实际一致的 world-UV 主采样路径，并先用文本测试和 Stride 资产编译验证落地。

**Secondary Objectives:**
- 把 RenderDoc 热替换结论固化为可回归的文本测试。
- 更新状态文档，避免系统文档继续描述旧的 advanced bottom 语义。

**Success Criteria:**
- `RiverBottom.sdsl` 反映 CK3 capture 对齐的 world-UV 主采样、tangent-UV depth/profile 与 capture 对齐 alpha。
- 对应文本测试先红后绿。
- Stride shader 生成与 asset compile 流程重新跑通。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- Related: [2026-06-17-river-bottom-worlduv-capture-alignment-design.md](../../../superpowers/specs/2026-06-17-river-bottom-worlduv-capture-alignment-design.md)

**Current State:**
- RenderDoc 热替换已经证明：只把 current bottom 主采样改成 `worldUv`，河心最终像素就会从 current 数值直接落到接近 CK3 的数量级。

**Why Now:**
- 根因已经足够清楚，继续停留在 hot-edit 结论上没有意义，需要先把 capture 证明过的最小安全修复落库。

---

## What We Did

### 1. 把 bottom 文本测试从 advanced 语义切到 capture 对齐语义
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 将原先断言 advanced tangent-UV/`BottomNormal.b` 路径的测试改成：
  - `BottomDiffuse/BottomNormal/BottomProperties` 必须采样 `worldUv`
  - depth/profile 必须来自 `CalcBottomProfileDepth(tangentUv)`
  - alpha 必须使用 `fadeOut * connectionFade * saturate(depth * 13.0f)`
- 同时加入反向断言，防止回退到：
  - `CalcBottomDepth(float2 tangentUv, float4 bottomNormalSample)`
  - `Bottom*Texture.Sample(..., tangentUv)`
  - advanced diffuse-alpha/smoothstep bank fade

**Rationale:**
- 让 RenderDoc 结论变成可以在仓库里长期守住的回归约束。

### 2. 把 `RiverBottom.sdsl` 改到 CK3 capture 对齐主路径
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`

**Implementation:**
- `CalcBottomDepth(...)` 改为 `CalcBottomProfileDepth(float2 tangentUv)`，只保留河道横截面 depth/profile。
- steep parallax 循环不再依赖 `BottomNormalTexture` 参与 depth shaping。
- `BottomDiffuse` / `BottomNormal` / `BottomProperties` 全部改为采样 `worldUv`。
- depth 改为 `CalcBottomProfileDepth(tangentUv)`。
- alpha 改为：

```hlsl
float edgeFade = saturate(depth * 13.0f);
float alpha = fadeOut * connectionFade * edgeFade;
```

**Rationale:**
- 这是已经被 RenderDoc 热替换证明有效的最小修复面，先修主采样和 alpha 语义，不把 pre-bottom payload 问题混进同一批改动。

### 3. 更新系统状态文档
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 把河流 bottom 描述改为：
  - parallax 偏移仍从 ribbon/tangent UV 计算
  - material 主采样改为 parallax 后 `worldUv`
  - depth/profile 继续由 `tangentUv` 驱动
  - alpha 使用 capture 对齐公式
- 明确写出 pre-bottom seed payload 仍是已知剩余差异。

**Rationale:**
- 避免下一轮继续按旧文档误判 current 仍然走 advanced 主路径。

---

## Decisions Made

### Decision 1: 先落最小 capture 对齐修复，不在同轮同时重做 pre-bottom
**Context:** RenderDoc 已确认 current 与 CK3 至少有两层偏差：bottom 主采样分支、pre-bottom payload。

**Options Considered:**
1. 同时改 bottom 与 pre-bottom
2. 先落已被热替换证明有效的 bottom 主路径修复

**Decision:** 选择 2
**Rationale:** `worldUv` 主采样修复已经被 hot-edit 直接验证，pre-bottom 仍需额外 capture 设计，拆开改更安全。
**Trade-offs:** bank 泄漏不会在这一轮被完全解决。
**Documentation Impact:** 状态文档显式记录 pre-bottom 仍是已知剩余差异。

---

## What Worked ✅

1. **先红后绿的文本测试约束**
   - What: 先改 `RiverShaderTextTests`，再改 shader
   - Why it worked: 可以把 RenderDoc 结论稳定固化进仓库
   - Reusable pattern: Yes

2. **只修 main sampling path 的最小实现**
   - What: 只动 bottom 主采样、depth/profile 和 alpha 语义
   - Why it worked: 修复面足够小，验证链条清晰
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **继续让状态文档保留旧的 advanced 叙述**
   - What we tried: 无，本轮直接修正
   - Why it failed: 文档已经和代码、RenderDoc 证据不一致
   - Lesson learned: RenderDoc 驱动的 shader 变更必须同步更新状态文档
   - Don't try this again because: 否则下一轮会继续按错误上下文诊断

---

## Problems Encountered & Solutions

### Problem 1: 当前 bottom 描述与实际实现不一致
**Symptom:** 文档仍写着 advanced tangent-UV + `BottomNormal.b` depth shaping。
**Root Cause:** 之前的 hot-edit 结论已经推进到实现，但状态文档没有同步收口。
**Investigation:**
- Tried: 对照 `RiverBottom.sdsl`、文本测试和状态文档
- Found: `ARCHITECTURE_OVERVIEW.md` 与 `CURRENT_FEATURES.md` 都还停留在旧描述

**Solution:**
- 直接把状态文档改到当前 capture 对齐实现，并标注 pre-bottom 剩余差异。

**Why This Works:** 下次再读项目状态时，不会再把 current 当成旧的 advanced 版本。
**Pattern for Future:** 凡是 RenderDoc 结论落到 shader 代码，状态文档必须同步更新。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 河流 bottom 当前实现语义
- [x] Update `CURRENT_FEATURES.md` - 河流渲染能力描述

### Architectural Decisions That Changed
- **Changed:** River bottom 当前主采样语义
- **From:** advanced tangent-UV 主采样 + `BottomNormal.b` depth shaping
- **To:** capture 对齐的 `worldUv` 主采样 + `tangentUv` depth/profile + capture 对齐 alpha
- **Scope:** `RiverBottom.sdsl` 与相应文本测试/状态文档
- **Reason:** RenderDoc 热替换已证明这是当前 frame 与 CK3 的一级偏差

---

## Code Quality Notes

### Testing
- **Tests Written:** 更新 2 个河流 bottom 文本测试
- **Coverage:** bottom 主采样 UV 语义、depth/profile 语义、alpha 语义
- **Verification Commands:**
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug` ✅
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug` ✅
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug` ✅
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` ✅
- **Observed Warnings:**
  - Stride asset compile 仍有 `warning X3557: loop doesn't seem to do anything, forcing loop to unroll`
  - `dotnet build` / tests 仍有既有 NuGet 漏洞 warning 与少量既有 C# warning，但无失败
- **Manual Tests:** 仍建议下一轮重新截一帧新的 `.rdc` 做 runtime 视觉复核

### Technical Debt
- **Created:** 无新增实现债务
- **Paid Down:** current bottom 主采样路径与状态文档的语义漂移
- **TODOs:** 单独处理 pre-bottom payload / bank 泄漏问题

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新截修复后 frame，确认 runtime 结果与 hot-edit 结论一致
2. 独立分析并重建 pre-bottom seed payload，而不是继续把它当 scene copy 的小变体
3. 在 bank 代表像素上重新串一次 pre-bottom -> bottom -> surface pixel history

### Questions to Resolve
1. pre-bottom 是否需要新增 CK3 风格独立 payload pass？
2. bank 泄漏的剩余主因是 seed payload、surface 接收逻辑，还是两者叠加？

### Docs to Read Before Next Session
- [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md) - 根因证据链
- [2026-06-17-river-bottom-worlduv-capture-alignment-design.md](../../../superpowers/specs/2026-06-17-river-bottom-worlduv-capture-alignment-design.md) - 本轮设计约束

---

## Session Statistics

**Files Changed:** 5
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `RiverBottom.sdsl` 现在是 `worldUv` 主采样、`tangentUv` depth/profile、capture 对齐 alpha
- Critical decision: 本轮刻意不碰 pre-bottom payload
- Active pattern: RenderDoc 热替换证明 -> 文本测试固化 -> shader 落地 -> Stride asset compile
- Current status: bottom 主路径已对齐到 CK3 capture，bank 泄漏仍是后续项

**What Changed Since Last Doc Read:**
- Architecture: bottom 主采样语义已经从旧 advanced 描述切到 capture 对齐实现
- Implementation: `RiverBottom.sdsl` 已完成最小主路径修复
- Constraints: 还没有新的修复后 `.rdc` 做 runtime 复核

**Gotchas for Next Session:**
- Watch out for: 不要再把 current bottom 当成 advanced tangent-UV 主路径
- Don't forget: pre-bottom payload 还没修，不能把 bank 问题视为已完结
- Remember: `get_cbuffer_contents` 在这批 capture 上仍不可靠

---

## Links & References

### Related Documentation
- [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)
- [2026-06-17-river-bottom-worlduv-capture-alignment-design.md](../../../superpowers/specs/2026-06-17-river-bottom-worlduv-capture-alignment-design.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`

### Code References
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`

---

## Notes & Observations

- 这轮修的是 current 与 CK3 capture 的主采样分支差异，不是河流系统的全部剩余差异。
- pre-bottom payload 仍然是解释 bank 泄漏的关键上游问题，不能因为 bottom 主路径收敛就忽略它。

---
