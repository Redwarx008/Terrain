# River Bottom Shader Compile Fix
**Date**: 2026-06-16
**Session**: River Bottom Shader Compile Fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 Editor 启动时 `RiverBottom` 运行时 shader 编译失败，消除 `error X3014: incorrect number of arguments to numeric-type constructor`。

**Secondary Objectives:**
- 把这次运行时编译故障转成可重复的自动回归测试。
- 记录本次 D3D11 shader 编译链的实际坑位，避免下次继续从 `AggregateException` 盲猜。

**Success Criteria:**
- `RiverBottom` 与 `RiverSurface` 都能通过 Stride `EffectCompiler` 的真实编译。
- `Terrain.Editor.Tests` 全部通过。

---

## Context & Background

**Previous Work:**
- Related: [2026-06-16-river-ck3-parity-implementation.md](2026-06-16-river-ck3-parity-implementation.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 今天的 CK3 河流 shader 改造后，Editor 运行时抛出 shader 编译异常，但现有测试主要锁文本语义，没有真正走 Stride 编译器。

**Why Now:**
- 这类错误如果不转成自动回归，只会在 Editor 启动时以 `AggregateException` 形式反复出现，反馈太慢。

---

## What We Did

### 1. 新增真实 shader 编译回归
**Files Changed:** `Terrain.Editor.Tests/Program.cs`, `Terrain.Editor.Tests/RiverShaderCompileTests.cs`

**Implementation:**
```csharp
TestHarness.Run("river bottom shader compiles through stride effect compiler", () => CompileShader("RiverBottom"));
TestHarness.Run("river surface shader compiles through stride effect compiler", () => CompileShader("RiverSurface"));
```

**Rationale:**
- 先把运行时故障变成确定性的红测，再修复。
- 直接调用 Stride `EffectCompiler`，而不是只检查 shader 文本。

**Architecture Compliance:**
- ✅ 保持现有 `Terrain.Editor.Tests` 轻量自定义 harness 结构
- ✅ 使用本地 Stride 引擎源码目录补足 mixin 查找路径

### 2. 修复 `RiverBottom` 的两个编译根因
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`

**Implementation:**
```c
float4 step = float4(0.0f, 0.0f, 0.0f, 0.0f);
float2 tangentUvDx = ddx(tangentUv);
float2 tangentUvDy = ddy(tangentUv);
float4 normalSample = BottomNormalTexture.SampleGrad(BottomTextureSampler, tangentUv, tangentUvDx, tangentUvDy);
```

**Rationale:**
- 第一处根因是 `float4(0.0f)` 这种单参数向量构造在当前 D3D11 编译链下会报 `X3014`。
- 第二处根因是 steep parallax 循环里使用了依赖隐式梯度的 `Texture.Sample`，触发 `X3570/X3511`。
- 用显式四分量构造 + `SampleGrad` 能最小范围修复，不改变现有 bottom 语义。

**Architecture Compliance:**
- ✅ 仅修正编译兼容性，不改变 `RiverBottom` 已落地的 CK3 parallax / lighting / bank fade 结构

---

## Decisions Made

### Decision 1: 用真实编译测试，不靠字符串断言猜测
**Context:** 现有 river text tests 能锁语义，但抓不到 D3D11 编译链特有的问题。

**Options Considered:**
1. 只看 shader 文本并人工猜根因
2. 写一次性临时脚本
3. 把真实编译接进 `Terrain.Editor.Tests`

**Decision:** 选择 3
**Rationale:** 能形成长期回归，而不是一次性排障脚本。
**Trade-offs:** 测试需要依赖本地 Stride 引擎源码目录。
**Documentation Impact:** 记录到本 session log 与 learnings。

### Decision 2: 用 `SampleGrad`，不靠 `[unroll]` 压编译器
**Context:** parallax 搜索循环因隐式梯度采样触发 D3D11 循环展开失败。

**Options Considered:**
1. 强制循环展开
2. 改成固定常量层数
3. 保留动态层数，循环里改用显式梯度采样

**Decision:** 选择 3
**Rationale:** 保留原有动态 parallax 行为，同时直接消除编译器对隐式梯度循环的限制。
**Trade-offs:** parallax depth 样本路径多了 `ddx/ddy` 显式梯度依赖。
**Documentation Impact:** 已补充到 `stride-river-rendering-patterns.md`。

---

## What Worked ✅

1. **真实编译回归先红后绿**
   - What: 先加 `RiverShaderCompileTests`，再修 shader
   - Why it worked: 把运行时异常转成了稳定、精确的测试反馈
   - Reusable pattern: Yes

2. **从生成 HLSL 反查 compile error**
   - What: 直接看 Stride 生成的 `shader_RiverBottom_*.hlsl`
   - Impact: 很快定位到 `float4(0.0f)` 和 parallax 循环采样问题

---

## What Didn't Work ❌

1. **直接 `dotnet build Terrain.Editor`**
   - What we tried: 直接编 Editor 项目
   - Why it failed: `Terrain.Editor.exe` 正在运行，apphost 被锁住
   - Lesson learned: 这次更适合用 `Terrain.Editor.Tests` + `/p:UseAppHost=false` 做验证
   - Don't try this again because: 在 Editor 正开着时，这条命令只会卡在复制 exe，而不是给 shader 根因

---

## Problems Encountered & Solutions

### Problem 1: `X3014 incorrect number of arguments to numeric-type constructor`
**Symptom:** `RiverBottom` 编译直接失败，报错指向内部 `Shader@...` 第 150 行。
**Root Cause:** `float4(0.0f)` 单参数构造在当前 HLSL 编译链下不被接受。
**Investigation:**
- Tried: 新增真实编译回归
- Tried: 打开 Stride 生成的 `shader_RiverBottom_*.hlsl`
- Found: 失败行对应 `float4 step = float4(0.0f);`

**Solution:**
```c
float4 step = float4(0.0f, 0.0f, 0.0f, 0.0f);
float4 offset = float4(0.0f, 0.0f, 0.0f, 0.0f);
```

**Why This Works:** 明确匹配四分量构造签名，消除 HLSL 前端歧义。
**Pattern for Future:** vector 初始化不要假设单参数广播在所有 Stride/D3D 组合下都成立。

### Problem 2: `X3570/X3511` parallax 循环无法展开
**Symptom:** 修完构造器后，编译继续在 steep parallax 循环里失败。
**Root Cause:** 循环内使用 `Texture.Sample`，需要隐式梯度；像素着色器的可变迭代循环会触发 D3D11 强制展开并失败。
**Investigation:**
- Tried: 再跑一次真实编译回归
- Found: 失败点落在 `for (int i = 0; i < maxNumLayers; ++i)` 内的 normal 采样

**Solution:**
```c
float2 tangentUvDx = ddx(tangentUv);
float2 tangentUvDy = ddy(tangentUv);
float4 normalSample = BottomNormalTexture.SampleGrad(BottomTextureSampler, tangentUv, tangentUvDx, tangentUvDy);
```

**Why This Works:** 显式梯度采样不再依赖编译器为循环体推导隐式梯度，从而允许动态 parallax 搜索继续保留。
**Pattern for Future:** 动态 pixel loop 里的纹理采样优先用 `SampleGrad`/`SampleLevel`，不要直接用 `Sample`。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 记录动态 parallax 循环的显式梯度采样坑位
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，本次没有架构变化
- [ ] Update `CURRENT_FEATURES.md` - 不需要，本次没有功能范围变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 动态 parallax 循环使用 `SampleGrad`
- When to use: 像素着色器里有依赖视角/参数的纹理搜索循环时
- Benefits: 避免 D3D11 因隐式梯度采样而展开失败
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 在 shader 循环里保留 `Texture.Sample`
- What not to do: 在可变迭代 parallax loop 中继续使用隐式梯度采样
- Why it's bad: 会直接把运行时 effect compile 打炸
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本次是编译兼容性修复，不是架构调整

---

## Code Quality Notes

### Performance
- **Measured:** 未做 GPU 性能基准
- **Target:** 本轮目标是恢复编译与运行
- **Status:** ⚠️ Close

### Testing
- **Tests Written:** 新增 `RiverShaderCompileTests`
- **Coverage:** `RiverBottom` / `RiverSurface` 的真实 Stride 编译链
- **Manual Tests:** 建议重启当前运行中的 Editor 进程后再验证一次场景加载

### Technical Debt
- **Created:** 无新的阻断债务
- **Paid Down:** 运行时 shader 编译故障现在有自动回归保护
- **TODOs:** 如果后续要提升可移植性，可把 Stride 引擎源码根目录从固定路径进一步抽象到统一配置

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重启 Editor 并确认运行时不再抛 shader 编译异常
2. 如有新的画面差异，再回到 RenderDoc 看 bottom/surface 颜色而不是编译链

### Blocked Items
- **Blocker:** 无代码阻断；仅当前本机有一个正在运行的旧版 `Terrain.Editor.exe`
- **Needs:** 重启进程以加载新二进制
- **Owner:** 用户 / Codex

### Questions to Resolve
1. 当前修复后的 `RiverBottom` 在真实场景里是否还有视觉偏差？ - 这关系到后续是否继续做 RenderDoc 对齐

### Docs to Read Before Next Session
- [2026-06-16-river-ck3-parity-implementation.md](2026-06-16-river-ck3-parity-implementation.md) - 承接当前 CK3 河流链路状态
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 避免再踩这次编译坑

---

## Session Statistics

**Files Changed:** 4
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Effects/RiverBottom.sdsl`
- Critical decision: parallax loop 改用 `SampleGrad`，不靠强制 unroll
- Active pattern: 用 `Terrain.Editor.Tests/RiverShaderCompileTests.cs` 做真实 shader 编译回归
- Current status: 编译回归已绿

**What Changed Since Last Doc Read:**
- Implementation: 新增 river shader compile tests；修复 `RiverBottom` 的构造器与循环采样
- Constraints: 当前正常 `dotnet build Terrain.Editor` 仍会受运行中的 Editor 进程锁定影响

**Gotchas for Next Session:**
- Watch out for: `Texture.Sample` 放进动态 pixel loop 会再次触发 D3D11 编译失败
- Don't forget: 当前用户机器的 Stride 引擎源码根默认在 `E:\WorkSpace\stride`
- Remember: 当前运行中的 Editor 进程需要重启才会加载这次修复

---

## Links & References

### Related Documentation
- [2026-06-16-river-ck3-parity-implementation.md](2026-06-16-river-ck3-parity-implementation.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

### Related Sessions
- [2026-06-16-river-bottom-hotreplace-validation.md](2026-06-16-river-bottom-hotreplace-validation.md)
- [2026-06-16-river-ck3-parity-implementation.md](2026-06-16-river-ck3-parity-implementation.md)

### Code References
- Key implementation: `Terrain.Editor/Effects/RiverBottom.sdsl`
- Regression test: `Terrain.Editor.Tests/RiverShaderCompileTests.cs`

---

## Notes & Observations

- 这次最关键的改进不是 shader 文本本身，而是把“运行时才会炸”的问题纳入了自动测试。
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug /p:UseAppHost=false` 适合作为 Editor 正在运行时的替代验证路径。

---

*Template Version: 1.0 - Based on Archon-Engine template*
