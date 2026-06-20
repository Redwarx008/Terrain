# River Bottom Final Gain Removal
**Date**: 2026-06-18
**Session**: river-bottom-final-gain-removal
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用可用的 RenderDoc MCP 复核 CK3 bottom pass 与本地 bottom pass 的 final lighting 语义，并移除错误的全局亮度增益。

**Success Criteria:**
- CK3 bottom shader disassembly 明确确认是否存在 final `* 3.0f`。
- 本地改动前先用 RenderDoc hot-replace 验证效果。
- SDSL、测试和项目文档同步到新的结论。

---

## Context & Background

**Previous Work:**
- See: [2026-06-18-river-debug-rdc-camera-seed-validation.md](./2026-06-18-river-debug-rdc-camera-seed-validation.md)
- See: [2026-06-17-river-bottom-lighting-energy-gain.md](../17/2026-06-17-river-bottom-lighting-energy-gain.md)

**Current State:**
- `debug.rdc` 已确认 scene-driven shadow / cubemap / camera-distance seed 都绑定进入当前 pipeline。
- 代表 bottom 像素仍显著亮于 CK3。

---

## What We Did

### 1. RenderDoc MCP 复核 CK3 bottom final output
**Files Changed:** None

**Findings:**
- CK3 `ck3-river.rdc` EID 332 PS disassembly 最终 RGB 输出为 `mad o0.xyz, r0.xyz, r1.xyz, r2.xyz`。
- CK3 没有本地 shader 里的 final `mul o0.xyz, ..., 3.0`。
- CK3 代表 bottom 像素 `(770,615)` 输出为 `[0.1586, 0.1048, 0.0546, 46.88]`。

### 2. RenderDoc hot-replace 验证本地去掉 final gain
**Files Changed:** None

**Findings:**
- 本地 `debug.rdc` EID 290 原始代表像素 `(471,282)` 输出为 `[0.501, 0.427, 0.314, 7.178]`。
- 从 Stride HLSL log 抽取当前 `RiverBottom` HLSL，移除 `* 3.0f` 后通过 MCP `shader_build -> shader_replace` 热替换。
- 热替换后同像素输出变为 `[0.167, 0.142, 0.103, 7.178]`。

**Rationale:**
- 这证明全局 `* 3.0f` 是当前 bottom 过亮的一阶原因。
- 去掉后仍比 CK3 偏绿/蓝，说明后续应继续拆 direct/IBL/albedo 通道，而不是再加全局亮度补偿。

### 3. 移除 SDSL 中的 final `* 3.0f`
**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
```csharp
float3 color = CalculateRiverBottomLighting(streams.PositionWS.xyz, bottomDiffuse.rgb, bottomNormal, viewDir, bottomProperties);
```

**Testing:**
- 文本测试从“要求存在 3x gain”改为“禁止重新引入全局 3x gain”。

---

## Decisions Made

### Decision 1: 移除 final `* 3.0f`，不改成参数化 tuning
**Context:** CK3 capture 的真实 bottom PS 没有该 final gain。
**Options Considered:**
1. 保留 `* 3.0f`
2. 改成新的 stage 参数
3. 直接移除并继续查 per-channel 差异

**Decision:** 选择 3
**Rationale:** 参数化仍然保留了 CK3 中不存在的全局补偿语义。
**Trade-offs:** 去掉后 current bottom 仍偏绿/蓝，需要后续继续追 lighting 分项。
**Documentation Impact:** 已更新 `ARCHITECTURE_OVERVIEW.md`、`CURRENT_FEATURES.md` 和 river rendering learnings。

---

## What Worked ✅

1. **用 MCP hot-replace 验证 SDSL 改动前提**
   - What: 抽取 Stride HLSL log，移除 `* 3.0f` 后在 RenderDoc 里替换 PS。
   - Impact: 同像素从 `[0.501,0.427,0.314]` 降到 `[0.167,0.142,0.103]`，验证了改动方向。

2. **直接比较 CK3 disasm 而不是只看主观亮度**
   - What: 用 CK3 EID 332 disasm 确认最终输出没有 final gain。
   - Impact: 推翻了旧的 `lighting_x3` 固化结论。

---

## What Didn't Work ❌

1. **把 `lighting_x3` 作为长期实现**
   - What we tried: 之前把热替换中有效的 `* 3.0f` 固化进 `RiverBottom.sdsl`。
   - Why it failed: CK3 bottom shader 本身没有同构 final gain，且当前 bottom 被推得过亮。
   - Lesson learned: hot-edit 可用于量级诊断，但落地前必须用 reference disasm/pixel history 验证语义等价。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录 final gain 已移除和剩余绿/蓝偏差。
- [x] Update `docs/CURRENT_FEATURES.md` - 新增 bottom final gain 已移除状态。
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 将 `lighting_x3` 固化改为反模式。

### Technical Debt
- **Remaining:** 去掉 final gain 后 current bottom `[0.167,0.142,0.103]` 仍比 CK3 `[0.159,0.105,0.055]` 偏绿/蓝。
- **Next probe:** 用 PS trace 拆 direct diffuse、direct specular、diffuse IBL、specular IBL 与 sampled albedo/properties。

---

## Code Quality Notes

### Testing
- **Tests Updated:** `RiverShaderTextTests` 增加“不应有全局 3x gain”的回归断言。
- **Manual Tests:** RenderDoc MCP hot-replace + pixel debug。
- **Verification Commands:**
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`

---

## Next Session

### Immediate Next Steps
1. 截新 `debug.rdc`，确认 GPU capture 中 EID bottom PS 不再有 `mul o0.xyz, ..., 3.0`。
2. 对 hot-replaced 或新 capture 的 bottom PS 做 per-term trace，优先查绿/蓝偏高来自 albedo、diffuse IBL 还是 specular IBL。
3. 若继续改 SDSL，仍先用 RenderDoc hot-replace 验证。

### Docs to Read Before Next Session
- [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md)
- [stride-river-rendering-patterns.md](../../../learnings/stride-river-rendering-patterns.md)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- CK3 bottom EID 332 没有 final `* 3.0f`。
- 本地 EID 290 原始输出 `[0.501,0.427,0.314]`，去掉 final gain 后 `[0.167,0.142,0.103]`。
- `RiverBottom.sdsl` 现在直接写 `CalculateRiverBottomLighting(...)`。

**Gotchas for Next Session:**
- 不要重新引入全局 brightness multiplier 来追 CK3 黄色底色。
- 剩余问题是通道构成偏差，不是 bottom pass 没有 scene shadow/cubemap 绑定。
