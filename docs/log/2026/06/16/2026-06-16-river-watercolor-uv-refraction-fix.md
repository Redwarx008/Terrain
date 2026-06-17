# 河流 water-color UV 与折射位置修正
**Date**: 2026-06-16
**Session**: 5
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 分析 `C:\Users\Redwa\Desktop\terrain-river-seed-check_frame1258.rdc` 中黑边消失后仍看不到 bottom 河床颜色的问题。

**Success Criteria:**
- 用 RenderDoc 证明问题发生在哪个 pass。
- 不使用 `_BottomDiffuseMultiplier` 等亮度补偿。
- 按 CK3 shader 语义修正源码，并通过测试与 Stride asset workflow。

---

## What We Found

### 1. bottom pass 已经写入 refraction RT
**RenderDoc Evidence:**
- 新截帧为 D3D11，75 events / 63 draws，无 HIGH 日志。
- `157` 是 refraction seed fullscreen draw，写 `ResourceId::7822`。
- `184/198` 是 river bottom draws，写同一半分辨率 RT `ResourceId::7822`。
- `227/245` 是 river surface draws，写 full-res RT `ResourceId::4059`。

在 bottom half pixel `(425,215)`：
- seed event `157` 写入亮地形 `RGB≈(2.99,3.44,0.99), A=0`。
- bottom event `198` 写入 `RGB≈(0.166,0.159,0.123), A≈11.64`。
- `debug_pixel` 显示 bottom dual-source `o1=(1,1,1,1)`，说明该点 bottom 完全覆盖 seed。

结论：这次不是 bottom pass 没写，也不是贴图未绑定。

### 2. water-color 坐标方向与 CK3 不一致
**RenderDoc Hot-edit:**
- 替换 surface PS，让同一像素输出 `WaterColorTexture` 的不同 UV 采样。
- full-res pixel `(850,430)`：
  - 未翻转 `PositionWS.xz / _MapExtent`：`RGB≈(0.031,0.031,0.031)`。
  - CK3 Y-flipped UV：`RGB≈(0.180,0.176,0.129)`。

**Reference:**
- CK3 `jomini_river_surface.fxh` 设置 `Params._WorldUV = Input.WorldSpacePos.xz / MapSize` 后执行 `Params._WorldUV.y = 1.0f - Params._WorldUV.y`。
- CK3 `jomini_water_default.fxh` 在 refraction path 中反解 `RefractionWorldSpacePos`，再用该位置重新采 `WaterColorTexture` 作为 see-through tint。

结论：当前 surface 用未翻转 surface world UV 采到近黑区域，会把河床/浅岸透视染暗；同时 refraction tint 没有在反解出的 bottom/refraction world position 重采样。

---

## Implementation

### 1. `RiverSurface.sdsl`
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`

- 新增 `ComputeMapWorldUv(float3 worldPosition)`，统一执行 `uv.y = 1.0f - uv.y`。
- surface `waterColorAndSpec` 改为用 `ComputeMapWorldUv(streams.PositionWS.xyz)` 采样，保留 alpha 做 gloss/spec。
- `SampleRefractionSeeThrough` 反解 `refractionWorldPosition` 后，用该位置重新采 `WaterColorTexture`。
- `ApplyTerrainUnderwaterSeeThrough` 按 CK3 使用 `refractionDepth / ToCameraDir.y` 作为水下视距衰减。
- see-through color 使用真实 `refractionDepth`，water fade 仍返回 `effectiveDepth = min(surfaceDepth, refractionDepth)`。

### 2. Tests
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`

- 增加对 `ComputeMapWorldUv`、Y 翻转、refraction world-position water-color 重采样的文本约束。
- 更新 see-through 断言：颜色路径用真实 refraction depth，shore fade depth 用 effective depth。

### 3. Docs
**Files Changed:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

文档已更新为当前 CK3-compatible 语义：不要把旧的 `edgeVisibleDepth` hot-edit 当成最终方案；先确认 water-color/refraction 坐标。

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore`：通过。
- `dotnet build Terrain.Editor\Terrain.Editor.csproj --no-restore -v minimal`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`：通过。
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`：907 succeeded, 0 failed。

Warnings:
- 仍有既有 NuGet vulnerability warnings。
- Stride asset compile 仍有既有 `X3557 loop doesn't seem to do anything` warning。

---

## Next Session

### Immediate Next Steps
1. 重新运行 Editor 并截帧，确认 `RiverSurface` disasm 中出现 `uv.y = 1.0f - uv.y` / refraction world-position water-color 采样。
2. 对新帧抽样旧点 `(850,430)` 或实际河岸点，确认未翻转近黑 water-color 不再参与 surface 输出。
3. 如果仍缺 bottom 河床颜色，再继续查 bottom RT alpha seed 是否需要像 CK3 一样写入 terrain compressed world distance，而不是当前 seed alpha 0。

### Questions to Resolve
1. 是否需要专门的 refraction seed shader 写 terrain/world distance alpha，替代 `ImageScaler` 仅复制 RGB 的方案？

---

## Quick Reference for Future Claude

**Key Evidence:**
- bottom RT 已写：`(425,215)` bottom post `RGB≈(0.166,0.159,0.123), A≈11.64`。
- surface water-color 未翻转采样几乎黑：`(0.031,0.031,0.031)`。
- CK3 Y-flipped 采样为棕绿：`(0.180,0.176,0.129)`。

**Root Cause:**
- surface water-color/refraction 坐标未对齐 CK3：缺少 map UV Y 翻转，且 see-through tint 没在 `RefractionWorldSpacePos` 重新采样。

**Current Status:**
- 源码、测试、文档已更新，asset compile 通过。
- 视觉最终结果仍需新 `.rdc` 验证。

---

*Template Version: 1.0 - Based on Archon-Engine template*
