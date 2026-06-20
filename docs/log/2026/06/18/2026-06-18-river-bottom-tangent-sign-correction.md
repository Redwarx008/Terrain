# River Bottom Tangent Sign Correction
**Date**: 2026-06-18
**Session**: debug.rdc follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 解释为什么 `C:\Users\Redwa\Desktop\debug.rdc` 在 scene-driven lighting、ribbon normal 和固定 tangent flip 后仍然很黑。

**Success Criteria:**
- 确认黑色来自 bottom pass 还是 surface pass。
- 用 RenderDoc 事实验证固定 `-normalize(streams.RiverTangent)` 是否仍然成立。
- 修正 SDSL 与文档，避免继续沿用错误 tangent sign 结论。

---

## Context & Background

**Previous Work:**
- `RiverSceneSeed` 已改用 Presenter depth 写 camera-relative seed alpha。
- bottom lighting 已绑定 CK3 warm sun、scene shadow、Jomini cubemap 和 CK3 material BRDF。
- `RiverMeshService` 已保留 sloped centerline tangent，并生成 CK3 风格 ribbon normal。
- 前一帧 `debug-current-codex-ribbon-normal_frame870.rdc` 让固定 `-normalize(streams.RiverTangent)` 看似能显著提高 `nDotL`。

**Current State:**
- 新 `debug.rdc` 文件时间晚于当前 exe，不是旧 capture。
- EID 276 bottom cbuffer 已绑定 `_SceneSunColor=[20,17.3568,15.0970]`、`_EnvironmentIntensity=20`、Jomini cubemap 和 shadow atlas。
- EID 276 disassembly 已包含此前的 fixed tangent inversion，所以当前“仍黑”不是 stale shader。

---

## What We Did

### 1. RenderDoc 像素复核
**Capture:** `C:\Users\Redwa\Desktop\debug.rdc`

**Findings:**
- Bottom EID 276，半分辨率 RT `ResourceId::7812`。
- Surface EID 305，全分辨率 RT `ResourceId::4057`。
- Bottom pixel `(562,239)` 输出 `[0.0742,0.0553,0.0416,4.569]`，post-blend 基本相同。
- 对应 surface pixel `(1124,478)` 输出 `[0.0765,0.0594,0.0404,1.0]`。
- Surface 主要透传暗 bottom/refraction，根因仍在 bottom pass。

### 2. 验证 tangent sign 不是固定取反
**Evidence:**
- 同一 bottom pixel PS 输入 normal `[-0.000515,0.9999996,-0.000028]`，tangent `[0.9993387,0.0005159,0.0292324]`。
- 旧 fixed shader 把该 `+X` tangent 取反后，normal-map X 分量进入背光方向，direct-light `nDotL≈0.17`。
- RenderDoc hot-replace no-flip 后，同一 capture 的代表像素 `nDotL≈0.72/0.82`。
- CK3 源码 `jomini_river_bottom.fxh` 直接使用 `normalize(Input.Tangent)`，没有 shader 固定取反。

**Hot-replace 复核:**
- 2026-06-18 追加 replacement PS，输出 `R=no-flip nDotL`、`G=flip nDotL`。
- Pixel `(562,239)`：`R=0.8949`、`G=0.0297`。
- Pixel `(528,301)`：`R=0.8595`、`G=0.1122`。
- 再用 no-flip TBN + BottomDiffuse + CK3 warm sun 的简化 direct-light replacement，`(562,239)` 从原始 `[0.074,0.055,0.042]` 变为 `[0.233,0.146,0.065]`，`(528,301)` 变为 `[0.168,0.101,0.049]`。

**Conclusion:**
- 前一个 capture 的 `-X` tangent 让固定取反看似正确，但这是单帧/单方向误判。
- Shader 层不能硬编码 tangent sign；否则相反方向的河段必然变黑。

### 3. 落地修复
**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`
- `docs/log/2026/06/18/2026-06-18-river-bottom-tbn-tangent-sign.md`

**Implementation:**
```hlsl
float3 tangent = normalize(streams.RiverTangent);
```

**Rationale:**
- 这与 CK3 `normalize(Input.Tangent)` 语义一致。
- 文本测试禁止重新引入 `-normalize(streams.RiverTangent)`。
- 如果后续仍出现局部方向错误，应查 `RiverMapService` / `RiverMeshService` 的 segment ordering，而不是 shader fallback。

---

## Decisions Made

### Decision 1: Shader 不再固定翻转 river tangent
**Context:** 两个 capture 的 river tangent 方向相反，固定取反只对其中一个方向有效。

**Decision:** `RiverBottom.sdsl` 使用 CK3 等价的 `normalize(streams.RiverTangent)`，测试禁止 `-normalize(streams.RiverTangent)`。

**Rationale:** CK3 源码直接使用输入 tangent，固定 shader flip 会让相反方向河段背光。正确边界是 mesh/segment 语义，而不是 river-local lighting fallback 或 shader sign heuristic。

---

## Verification

- RenderDoc `debug.rdc` pixel history confirmed bottom is already dark before surface composition.
- RenderDoc hot-replace confirmed no-flip raises representative direct-light `nDotL` from about `0.17` to `0.72/0.82`.
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCleanAsset /p:Configuration=Debug` ✅
- `dotnet msbuild "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" /t:StrideCompileAsset /p:Configuration=Debug` ✅
- `dotnet run --project "E:\Stride Projects\Terrain\Terrain.Editor.Tests\Terrain.Editor.Tests.csproj" -c Debug` ✅
- `dotnet build "E:\Stride Projects\Terrain\Terrain.Editor\Terrain.Editor.csproj" -c Debug` ✅

---

## Next Session

1. Capture a fresh frame after rebuilding and verify EID 276 no longer contains hard-coded tangent inversion.
2. If any river segment remains black, compare `RiverTangent` signs across connected segments and trace `RiverMapService` ordering.
3. Keep lighting/shadow/cubemap constants unchanged while isolating tangent direction; they are already scene-driven in this capture.

---

## Quick Reference

**What changed:**
- Reverted `RiverBottom` TBN tangent from fixed `-normalize(streams.RiverTangent)` to CK3-style `normalize(streams.RiverTangent)`.

**Critical evidence:**
- `debug.rdc` bottom pixel `(562,239)` has `+X` tangent and remains dark with fixed inversion.
- No-flip hot-replace raises `nDotL` to the expected lit range.

**Gotcha:**
- A single RenderDoc hot-replace result can be direction-dependent for river meshes. Compare opposite tangent directions before turning a sign experiment into shader code.
