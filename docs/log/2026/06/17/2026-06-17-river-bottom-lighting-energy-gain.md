# River Bottom Lighting Energy Gain
**Date**: 2026-06-17
**Session**: River bottom lighting energy gain
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不直接猜测 SDSL 改法的前提下，先根据 RenderDoc hot-edit 结果判断 current river bottom 的主问题是 UV 采样路径还是 lighting energy。

**Secondary Objectives:**
- 用红绿测试锁住最终选中的 shader 改动。
- 按 Stride shader workflow 完成资产重编译，避免只在前端编译器里通过。

**Success Criteria:**
- 给出 `tangent_diffuse_only` / `worlduv_diffuse_only` / `lighting_x3` 三组热替换的定量结论。
- 只做一个最小 SDSL 改动，并让 `Terrain.Editor.Tests`、Stride asset compile、`dotnet build` 全部通过。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- See: [2026-06-17-river-bottom-env-binding-renderdoc-verification.md](./2026-06-17-river-bottom-env-binding-renderdoc-verification.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 新 `debug.rdc` 已经证明 bottom/surface cubemap 绑定分离正确：bottom 读 scene skybox，surface 读 `reflection-specular`。
- 剩余问题收敛到 `RiverBottom` 自身：到底是 advanced UV 路径不对，还是 lighting energy 太低。

**Why Now:**
- 用户要求在修改 SDSL 前优先做 RenderDoc 热验证；本轮已有完整 hot-edit 结果，足够进入最小实现。

---

## What We Did

### 1. 先用热替换排除“仅仅是 UV 路径错了”
**Files Changed:** 无

**Implementation:**
- 读取 `C:\Users\Redwa\Desktop\renderdoc-mcp-export\debug_20260617_052228\hotedit\results.json`
- 三组代表像素结果：
  - `tangent_diffuse_only`
    - bottom `RGB = (0.04599, 0.03053, 0.01520)`，亮度约为原来的 `0.468x`
    - surface 亮度约为原来的 `0.972x`
  - `worlduv_diffuse_only`
    - bottom `RGB = (0.04028, 0.02934, 0.01520)`，亮度约为原来的 `0.438x`
    - surface 亮度约为原来的 `0.972x`
  - `lighting_x3`
    - bottom `RGB = (0.20374, 0.21338, 0.19348)`，亮度约为原来的 `3.001x`
    - surface 亮度约为原来的 `1.143x`

**Rationale:**
- 两个 diffuse-only 变体都更暗，说明单独切 `worldUv` 或 `tangentUv` 不能把河床拉向 CK3。
- 只有 `lighting_x3` 具备量级正确的提升，因此优先级应当放在 bottom lighting energy，而不是先重写 UV 分支。

### 2. 用红绿测试锁最小 shader 改法
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
```csharp
TestHarness.Run("river bottom shader applies renderdoc-validated lighting energy boost", BottomShaderAppliesRenderDocValidatedLightingEnergyBoost);
```

```csharp
AssertContains(
    shader,
    "CalculateRiverBottomLighting(bottomDiffuse.rgb, bottomNormal, viewDir, bottomProperties) * 3.0f",
    "RiverBottom should apply the RenderDoc-validated 3x lighting gain to the fully lit bottom color");
```

**Rationale:**
- 现有 river shader 测试已经是文本/编译双层验证；这次继续沿用同一模式，最小成本把 hot-edit 结论变成回归约束。

### 3. 在 `RiverBottom.sdsl` 落地最小 energy gain
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`

**Implementation:**
```csharp
float3 color = CalculateRiverBottomLighting(bottomDiffuse.rgb, bottomNormal, viewDir, bottomProperties) * 3.0f;
```

**Rationale:**
- 这是和 RenderDoc `lighting_x3` 完全同构的最小实现。
- 本轮不额外引入新参数、不顺手改更多 lighting 公式，避免把“验证过的修正”再次混成猜测。

### 4. 按 Stride shader workflow 做资产重编译与构建验证
**Files Changed:** 无

**Implementation:**
- 运行：
  - `dotnet msbuild 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build 'E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj' -c Debug`
- 由于本轮没有新增/重命名 shader 参数，因此没有执行 `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles`

**Rationale:**
- 这一步确保修改不只是 `RiverShaderCompileTests` 里的前端编译通过，而是真的进入 Stride 资产编译链。

---

## Decisions Made

### Decision 1: 本轮先做最小 `3.0f` lighting gain，而不是继续追 UV 分支
**Context:** `worldUv` 与 `tangentUv` 的 diffuse-only 热替换都明显更暗，`lighting_x3` 才能把河床抬到正确量级。

**Options Considered:**
1. 继续优先重写 `worldUv` / non-advanced 路径
2. 先做最小 bottom lighting energy gain

**Decision:** 选择 2
**Rationale:** 这是唯一已被当前 RenderDoc hot-edit 直接验证有效的改动。
**Trade-offs:** 它先解决的是“量级”，不是最终全部 parity。
**Documentation Impact:** 更新 `ARCHITECTURE_OVERVIEW.md`、`CURRENT_FEATURES.md`、`stride-river-rendering-patterns.md`

### Decision 2: 不在本轮引入新的 shader 参数
**Context:** 可以把 `3.0f` 做成新 stage 参数并一路暴露到 C#，但这会扩大改动面。

**Options Considered:**
1. 新增可调参数并更新绑定链
2. 直接落地最小常量 gain

**Decision:** 选择 2
**Rationale:** TDD 和 hot-edit 都指向“先做最小通过实现”；参数化可以等下一轮确认视觉结果后再做。

---

## What Worked ✅

1. **hot-edit 三分法**
   - What: 同时比较 `tangent_diffuse_only`、`worlduv_diffuse_only`、`lighting_x3`
   - Why it worked: 一轮内就把“采样路径问题”和“lighting energy 问题”分开了
   - Reusable pattern: Yes

2. **文本测试 + Stride 编译测试**
   - What: 先写红测锁定目标行，再依赖现有 `RiverShaderCompileTests`
   - Impact: 既防回归，又保证 shader 改动仍能通过 Stride effect compiler

---

## What Didn't Work ❌

1. **继续把主要精力放在 UV 分支**
   - What we tried: 先前多轮分析持续怀疑 `worldUv` / `tangentUv`
   - Why it failed: 两种 diffuse-only 变体都无法提供正确量级的河床能量
   - Lesson learned: 当 diffuse-only 变体一起变暗时，优先怀疑 lighting energy
   - Don't try this again because: 会把根因排序重新搞反

---

## Problems Encountered & Solutions

### Problem 1: 当前 `RiverBottom` 缺的不是“采样颜色”，而是“最终受光能量”
**Symptom:** current river bottom 仍然明显偏黑，且 skybox cubemap 绑定已经被证明正确。
**Root Cause:** 现阶段 `CalculateRiverBottomLighting(...)` 产出的最终能量不足以接近 CK3 河床亮度区间。
**Investigation:**
- Tried: `tangent_diffuse_only`
- Tried: `worlduv_diffuse_only`
- Tried: `lighting_x3`
- Found: 只有 `lighting_x3` 能把 bottom 从原来的亮度拉到约 `3x`

**Solution:**
```csharp
float3 color = CalculateRiverBottomLighting(bottomDiffuse.rgb, bottomNormal, viewDir, bottomProperties) * 3.0f;
```

**Why This Works:** 它和 RenderDoc 中已验证有效的热替换完全一致。
**Pattern for Future:** 如果 hot-edit 里只有最终 lighting gain 有量级正确的提升，就先在最终 lit color 上做最小能量校准。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 记录 bottom 当前带有 RenderDoc 验证过的 `3.0f` lighting gain
- [x] Update `CURRENT_FEATURES.md` - 记录 river bottom 当前的最小能量校准状态
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 记录“diffuse-only 都暗时先修 lighting energy”的模式

### New Patterns/Anti-Patterns Discovered
**New Pattern:** `tangent/worldUv diffuse-only` 与 `lighting_x3` 的三分 hot-edit
- When to use: bottom 看起来既可能是采样路径错，也可能是 lighting 太弱
- Benefits: 一轮内分清一阶问题和二阶问题
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无新的架构层级变化
- **Reason:** 本轮是 shader 行为校准，不是子系统重构

---

## Code Quality Notes

### Testing
- **Tests Written:** 1 个新的 `RiverShaderTextTests` 断言
- **Coverage:** bottom 最终 lit color 的 `3.0f` energy gain；现有 shader compile test 继续覆盖 Stride effect compiler
- **Manual Tests:** 还需要新的 `.rdc` 抓帧确认真实画面是否已更接近 CK3

### Technical Debt
- **Created:** 当前 `3.0f` gain 仍是一个校准常量，尚未参数化
- **Paid Down:** 不再继续把主要问题误判为 UV 分支
- **TODOs:** 下一轮用新 capture 复核视觉结果；若仍有差距，再决定是否把 gain 参数化或继续调整 UV / shadow 语义

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 运行 editor 并截一张新 `.rdc` - 验证 `3.0f` gain 是否真的进入 GPU capture
2. 对比新 capture 和 `ck3-river.rdc` - 确认亮度接近后是否还存在暖色/阴影结构差距
3. 如果仍有明显差距，再决定是参数化 gain，还是继续补 `ShadowTexture`/UV 分支

### Questions to Resolve
1. `3.0f` gain 后的真实 bottom RT 是否已经足够接近 CK3？
2. 如果亮度接近但色相仍偏冷，下一步优先补 shadow 语义还是材质/IBL 语义？

### Docs to Read Before Next Session
- [2026-06-17-river-bottom-env-binding-renderdoc-verification.md](./2026-06-17-river-bottom-env-binding-renderdoc-verification.md) - 已确认 cubemap 绑定层无误
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 本轮新增了 lighting-energy 优先模式

---

## Session Statistics

**Files Changed:** 6
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Effects/RiverBottom.sdsl` 的最终 `color` 现在是 `CalculateRiverBottomLighting(...) * 3.0f`
- Critical decision: 当前主矛盾先按 lighting energy 处理，不先回到 UV 分支
- Active pattern: `tangent_diffuse_only` / `worlduv_diffuse_only` / `lighting_x3` 三分 hot-edit
- Current status: 测试、Stride asset compile、`dotnet build` 都已通过；还缺新 capture 的 GPU 侧复核

**What Changed Since Last Doc Read:**
- Implementation: bottom 最终 lit color 增加了 `3.0f` gain
- Constraints: 这还是一个最小常量校准，不是最终可调参数方案

**Gotchas for Next Session:**
- Watch out for: 不要因为 CK3 某帧可能走 `worldUv` 就重新把一阶问题判回 UV 分支
- Don't forget: 本轮没有跑 `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles`，因为没有新增 shader key
- Remember: 真正的下一步证据是新的 `.rdc`，不是只看本地代码

---

## Links & References

### Related Sessions
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- [2026-06-17-river-bottom-env-binding-renderdoc-verification.md](./2026-06-17-river-bottom-env-binding-renderdoc-verification.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\debug_20260617_052228\hotedit\results.json`

### Code References
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---

## Notes & Observations

- 本轮没有再依赖 RenderDoc MCP 在线会话，因为已有导出的 hot-edit 结果足够支撑实现决策。
- `3.0f` gain 是“已验证有效的最小修复”，不是宣布 river bottom 已完全对齐 CK3。

---

*Template Version: 1.0 - Based on Archon-Engine template*
