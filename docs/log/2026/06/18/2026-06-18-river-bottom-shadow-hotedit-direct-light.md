# 河流 bottom shadow 热替换与 direct light 修正
**Date**: 2026-06-18
**Session**: RenderDoc hot-replace diagnosis and SDSL update
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按用户要求先在 RenderDoc 中热修改 `C:\Users\Redwa\Desktop\debug-river-after-surface-alpha_frame798.rdc`，确认 bottom/surface 发黑是否来自 bottom pass。

**Success Criteria:**
- 证明黑色是在 bottom/refraction RT 里产生，还是 surface pass 继续压黑。
- 修改 SDSL 前先拿 hot-replace 结果验证方向。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/18/2026-06-18-river-surface-hotedit-waterfade-wrapper.md`
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- 本地 capture: `C:\Users\Redwa\Desktop\debug-river-after-surface-alpha_frame798.rdc`
- 目标 capture: `C:\Users\Redwa\Desktop\ck3-river.rdc`
- CK3 loose shader 目录缺少 `jomini/jomini_river_bottom.fxh` 和 `cw/shadow.fxh`，bottom shadow 语义以 RenderDoc disasm 为准。

---

## What We Did

### 1. 用 RenderDoc 复核 bottom 到 surface 的黑色传播
**Files Changed:** none

**Implementation:**
- 打开本地 capture，复核 bottom event `290` 和 surface event `351`。
- 对 bottom pixel `(500,250)` 与 surface pixel `(1000,500)` 执行 `debug_pixel` / `pixel_history`。

**Findings:**
- bottom event `290` 原始输出约 `[0.0157, 0.0179, 0.0198, 9.447]`。
- surface event `351` 原始输出约 `[0.0095, 0.0180, 0.0208, 1.0]`。
- bottom/refraction RT 在 surface 采样前已经近黑，surface 不是主要压黑点。

### 2. 热替换 bottom direct light
**Files Changed:** none

**Implementation:**
- 编译 HLSL replacement，只输出 bottom diffuse * scene sun * `NdotL / pi`。
- 第一次 replacement 只让 `shaderOut` 变亮，但由于 dual-source secondary alpha 不正确，post-blend 变成负值。
- 第二次 replacement 将 `SV_Target1.a` 固定为 `1.0`。

**Findings:**
- bottom `(500,250)` post-blend 从 `[0.0157,0.0179,0.0198]` 变为约 `[0.1599,0.1021,0.0397]`。
- surface `(1000,500)` 从 `[0.0095,0.0180,0.0208]` 变为约 `[0.1463,0.0953,0.0396]`。
- 导出图：`C:\Users\Redwa\Desktop\renderdoc-mcp-export\rt_369_0.png`

### 3. 对照目标 bottom shadow disasm
**Files Changed:** none

**Implementation:**
- 打开目标 capture，读取 bottom event `338` 的 PS disasm 和 bindings。
- 对照 CK3 loose shader `river_bottom.shader`、`jomini_lighting.fxh`、`map_lighting.fxh`。

**Findings:**
- 目标 bottom PS 使用 `ShadowMapTextureMatrix`、water-surface intersection、`ShadowScreenSpaceScale` 随机旋转 kernel、`KernelScale`、`NumSamples`、`Bias` 和边界 fade 后，再乘 `SunDiffuse * SunIntensity`。
- 当前 `RiverBottom.sdsl` 的 `EvaluateSceneShadow` 是 Stride cascade selector + 5x5 PCF helper，不是目标 shadow 函数的等价实现。

### 4. 落地 SDSL 修正
**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverBottom.sdsl.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- `CalculateRiverBottomLighting` 中 direct light 的 `shadow` 改为 `1.0f`，不再调用当前非等价 `EvaluateSceneShadow`。
- 文本测试改为锁住该非等价路径不得再次进入 bottom direct light。
- 文档标记目标 shadow projection / kernel / bias / fade 仍待移植。

**Rationale:**
- 这是热替换验证过的方向：先恢复 bottom direct energy，避免 surface 继续采到近黑 refraction。
- 这不是完整 shadow parity；完整修复需要移植目标 `ShadowMapTextureMatrix + CalculateShadow` 语义。

---

## Decisions Made

### Decision 1: 暂不把 Stride cascade shadow 乘进 bottom direct light
**Context:** 当前 helper 会把 bottom/refraction 写黑，且目标 disasm 的 shadow 语义不同。
**Decision:** bottom direct light 暂时使用 unshadowed term。
**Rationale:** 热替换证明该改动能直接修复当前黑色传播链。
**Trade-offs:** 暂时没有目标 bottom shadow；后续必须单独移植目标 shadow 投影。

### Decision 2: 保留 scene lighting/IBL 绑定状态
**Context:** scene sun、environment cubemap、shadow atlas 绑定仍是后续 parity 所需输入。
**Decision:** 不删除 shadow 参数和 C# 绑定，只禁止非等价 helper 进入 direct light。
**Rationale:** 降低本轮改动面，避免破坏后续目标 shadow 移植入口。

---

## What Worked

1. **dual-source aware hot-replace**
   - 同时控制 `SV_Target0` 和 `SV_Target1.a` 后，bottom RT 与 surface RT 均按预期变亮。

2. **先查 post-blend 而不是只看 shaderOut**
   - 第一次 replacement 的 shader output 是亮的，但 post-blend 是负值；pixel history 直接暴露了问题。

---

## What Didn't Work

1. **只输出单 target 或错误 secondary alpha**
   - bottom shader output 变亮不能代表 RT 真的变亮；dual-source blend 会使用 secondary alpha。

2. **把 Stride cascade helper 当作目标 shadow**
   - 绑定名相似不代表语义等价；目标 disasm 的投影和滤波路径不同。

---

## Testing

- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "-t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" -p:Configuration=Debug -p:Platform=x64`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj -t:StrideCleanAsset -p:Configuration=Debug -p:Platform=x64`
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj -t:StrideCompileAsset -p:Configuration=Debug -p:Platform=x64`
- `dotnet build Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug --no-build`

**Result:**
- Asset compile: 911 succeeded, 0 failed.
- Tests: all PASS.
- Remaining warnings: existing NuGet vulnerability warnings and existing C# warnings.

---

## Next Session

### Immediate Next Steps
1. 重新截当前工程帧，确认正式 SDSL 编译后的 bottom/surface 画面是否接近 hot-replace 结果。
2. 移植目标 bottom shadow：`ShadowMapTextureMatrix` 等价输入、water-surface min depth、random rotated kernel、`NumSamples`、`KernelScale`、`Bias`、edge fade。
3. 再查流动方向：当前修正只解决 bottom 发黑，不处理 surface flow direction。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`
- `docs/log/2026/06/18/2026-06-18-river-target-shader-semantics.md`

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本轮已用 RenderDoc 热替换证明 bottom direct energy 修复会让 surface 同点变亮。
- `RiverBottom.sdsl` 当前 direct light 不再调用 `EvaluateSceneShadow`。
- 这不是完整 CK3 bottom shadow parity；目标 shadow 仍需按 `ck3-river.rdc` disasm 单独移植。
- 热替换 bottom 时必须正确写 `SV_Target1.a`，否则 dual-source blend 会让结果失真。
