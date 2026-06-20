# River Bottom CK3 BRDF Alignment
**Date**: 2026-06-18
**Session**: river bottom debug1 RenderDoc follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 用新截帧 `C:\Users\Redwa\Desktop\debug1.rdc` 复核 bottom pass 是否已经去掉 final gain，并查明 current bottom 为什么仍不像 CK3 的黄橙河床。

**Success Criteria:**
- 用 RenderDoc MCP 证明差异来自哪一段 lighting。
- 先用 shader hot-edit 验证方向，再修改 `RiverBottom.sdsl`。
- 不新增 fallback/try-catch/全局增益常量。

---

## What We Did

### 1. RenderDoc 分解 bottom lighting
**Captures:** `debug1.rdc`, `ck3-river.rdc`

**Findings:**
- `debug1.rdc` EID 290 已确认无 final `* 3.0f`，代表像素 `(471,282)` 输出约 `[0.1400,0.1129,0.0857,6.3867]`。
- 当前 bottom pass 已绑定 scene cubemap `ResourceId::276` 和 shadow atlas `ResourceId::7727`。
- CK3 EID 332 trace 显示最终累加结构是 direct + diffuse IBL + specular IBL，无全局 gain。
- CK3 代表像素分项：direct `[0.1436,0.0920,0.0437]`，diffuse IBL delta `[0.0141,0.0118,0.00935]`，specular IBL delta `[0.0009,0.0010,0.0016]`。
- current 旧 shader 的 specular IBL 约 `[0.0110,0.0174,0.0233]`，比 CK3 高一个数量级且偏蓝。

### 2. Hot-edit 验证
**Files:** `artifacts/renderdoc/shader_RiverBottom_debug1_ck3_lighting.hlsl`

**Result:**
- CK3-like BRDF + warm sun 的 direct-only keep 变体输出 `[0.136,0.087,0.053]`，通道比例接近 CK3。
- full IBL 仍偏蓝，说明剩余差异不是贴图本身，而是旧 shader 的 specular IBL/BRDF 语义不等价。

### 3. 工程落地
**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverBottom.sdsl.cs`
- `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
- `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `CalculateRiverBottomLighting` 改为 CK3 material BRDF：`0.25 * properties.g` spec remap、metalness diffuse/spec split、GGX direct、dominant specular IBL、Burley roughness-to-mip。
- lighting position 改为 fake-depth 前的 submerged `bottomLightingPosition`；fake depth 只影响 refraction payload。
- 删除 `_BottomSpecularIntensity` shader key 和 C# settings/render-object/render-feature 参数链。
- 文本测试锁定无 final gain、CK3 BRDF helpers、bottom-position lighting，以及删除 river-local bottom specular intensity。

---

## Decisions Made

### Decision 1: 不再保留 `_BottomSpecularIntensity`
**Context:** CK3 trace 证明 specular IBL 是很小的补项；current 旧 shader 的 river-local specular multiplier 会把蓝色 cubemap 放大。

**Decision:** 删除该参数链，而不是保留但不使用。

**Rationale:** 死参数会误导后续调试，也违反“不要靠 river 专用 fallback 常量”的约束。

### Decision 2: fake depth 不参与 lighting
**Context:** CK3 在 fake-depth 压缩前计算 bottom lighting，fake depth 只用于后续世界位置压缩/输出。

**Decision:** `bottomLightingPosition` 使用 `streams.PositionWS.y - worldDepth`，随后才对 `bottomWorldPosition` 扣 fake depth。

---

## Verification

- `dotnet run --project "E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj" -c Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCleanAsset /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCompileAsset /p:Configuration=Debug` ✅
- `dotnet build "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" -c Debug` ✅

**Warnings:**
- 既有 NuGet vulnerability warnings。
- 既有 Stride HLSL loop-unroll warning。

---

## Architecture Impact

**Updated:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**New Pattern:**
- 对齐 CK3 bottom lighting 时必须拆 direct / diffuse IBL / specular IBL，不能只看 final RGB。

---

## Next Session

1. 重新运行 editor 截取新 `debug.rdc`，确认 GPU shader 中 CK3 BRDF 已进入 EID 290。
2. 对同一代表像素重新分解 current direct/diffuse IBL/specular IBL，和 CK3 trace 对齐。
3. 若仍偏蓝，优先检查 scene sun color 是否仍是白色 `[20,20,20]`，而 CK3 是 warm `[20,17.3568,15.0970]`。

---

## Quick Reference

**Key implementation:**
- `Terrain.Editor/Effects/RiverBottom.sdsl` now computes CK3 material BRDF and lights `bottomLightingPosition`.

**Critical RenderDoc evidence:**
- CK3 EID 332 direct dominates; specular IBL is tiny.
- current old shader specular IBL was too large and blue.
