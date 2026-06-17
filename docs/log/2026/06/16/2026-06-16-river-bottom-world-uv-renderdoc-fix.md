# 河流 bottom world-UV 修复
**Date**: 2026-06-16
**Session**: river-bottom-world-uv-renderdoc-fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对照 CK3 capture 与 shader 源码，定位当前河床 draw 输出与 CK3 差异巨大的根因，并修复 bottom pass 本身输出错误。

**Success Criteria:**
- RenderDoc 证明问题发生在 bottom pass 的 shader 输出阶段，而不是后续 surface/blend 阶段。
- 修复不使用 `_BottomDiffuseMultiplier` 这类亮度补偿。
- `RiverBottom.sdsl` 与 CK3 `CalcRiverBottom` 的 bottom 纹理 UV、depth profile、alpha 语义更一致。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-watercolor-uv-refraction-fix.md](2026-06-16-river-watercolor-uv-refraction-fix.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 用户提供 `C:\Users\Redwa\Desktop\debug.rdc`，指出河床 draw 在 event `184`，河床本身输出仍然和 CK3 差异巨大。
- CK3 参考 capture `C:\Users\Redwa\Desktop\ck3-river.rdc` 中 river bottom 事件为 `332/334/336/338`，surface 事件为 `460/462/464/466`。

---

## What We Did

### 1. RenderDoc 对照定位
**Files Changed:** none

**Evidence:**
- 当前 `debug.rdc` event `184` pipeline：RT0 `ResourceId::7789`，`R16G16B16A16_FLOAT`，`836x498`。
- 当前 `184` 命中河道像素 `(420,334)`：shaderOut `RGB≈(0.183,0.181,0.140)`，`Blend/o1=1`，说明不是 blend 把河床压黑。
- 当前 PS 反汇编显示 `bottomUv = riverUv * _BottomUvScale + worldPosition.xz * 0.001 * parallax`，BottomDiffuse/Normal/Properties/Depth 都用这个 ribbon-space UV 采样。
- CK3 `river_bottom.shader` 调用 `CalcRiverBottom`，不是 Advanced；`jomini_river_bottom.fxh` 中 BottomDiffuse/Properties/Normal 使用 `WorldUV = Input.WorldSpacePos.xz + WorldSpaceParallax * Input.Width` 采样。
- CK3 `332` PS 反汇编第 `57` 行生成 world UV，第 `71-73` 行用 world UV 采样 BottomDiffuse/Properties/Normal。

### 2. RenderDoc 热替换验证
**Files Changed:** none

**Implementation:**
- 在当前 `debug.rdc` event `184` 热替换 PS，直接输出 `BottomDiffuseTexture.Sample(BottomTextureSampler, input.PositionWS.xz)`。
- 结果：河床从黑色沿河条纹变成连续棕色世界锁定纹理。
- 再热替换为 `input.PositionWS.xz / 18431.0` 采样，结果变成大块平色，确认 Bottom DDS 是 tileable world-space texture，不是 water-color 那类 normalized map texture。
- 又热替换为保留 lighting、distance alpha 和 dual-source blend 的完整诊断 PS，只替换 world UV、CK3 cos depth 和 bottom alpha：event `184` 河道像素从旧 shaderOut `RGB≈(0.183,0.181,0.140)` 变为 `RGB≈(0.209,0.193,0.156)`，post-blend 变为 `RGB≈(1.185,1.087,0.563)`；event `213` 对应 surface 像素输出 `RGB≈(1.208,1.168,0.547), A≈1`。

**Rationale:**
- 这证明根因是 Bottom 贴图采样 UV 错，不是光照亮度不足，也不是需要额外 diffuse multiplier。
- 完整热替换仍显示岸边偏暗，说明后续差异应继续对齐 surface shading / CK3 lighting，而不是回退到 bottom UV 或亮度倍率。

### 3. Shader 修复
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverCommon.sdsl`

**Implementation:**
- `RiverCommon.RiverDepthFromCrossSection` 改为 CK3 的 cosine depth profile：`1 - pow(cos(UV.y * 2π) * 0.5 + 0.5, depthWidthPower)`。
- `RiverBottom` 新增 `ComputeBottomWorldUv(worldPosition)`，BottomDiffuse/Normal/Properties/Depth 改用 `worldPosition.xz` 采样。
- `RiverBottom` 新增 depth-shaped normal，按 CK3 使用 cross-section depth delta 构造 `ParallaxNormal`，再叠加 RRxG normal。
- bottom alpha 改为 `bottomEdgeFade * connectionFade * transparency`，对齐 CK3 `CalcRiverBottom` 路径，不再乘 `bottomDiffuse.a`。

### 4. 测试与文档
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `docs/*`

**Implementation:**
- 新增文本测试锁定 CK3 cos depth profile。
- 新增文本测试锁定 Bottom 贴图 world-space UV 采样，并禁止旧 ribbon UV/parallax hack。
- 更新河流渲染 learning、架构总览和功能清单。

---

## Decisions Made

### Decision 1: Bottom 贴图使用 worldPosition.xz，不使用 riverUv
**Context:** CK3 `CalcRiverBottom` 明确把 BottomDiffuse/Normal/Properties 绑定到 `WorldUV`，RenderDoc 热替换也证明 world UV 才能得到棕色河床。
**Decision:** `RiverBottom` Bottom 贴图采样统一使用 `streams.PositionWS.xz * _BottomUvScale`。
**Trade-offs:** 当前仍未完整实现 CK3 steep parallax offset 的 iterative world/tangent offset；本轮先修正根因 UV，并保留后续可继续补齐的结构。

### Decision 2: 不用亮度补偿修河床
**Context:** 当前像素 shaderOut 并非数值归零，且热替换证明纹理 UV 错误。
**Decision:** 不添加 `_BottomDiffuseMultiplier`，继续按 material lighting 和正确 texture input 修复。
**Trade-offs:** 需要后续继续对齐 CK3 direct sun/shadow/env 参数，而不是一次性用倍率调色。

---

## What Worked ✅

1. **先看 bottom RT，不看最终截图**
   - `event 184` 直接显示河床输出已错，避免误判为 surface pass 或 blend 问题。

2. **RenderDoc hot-replace 只验证单个假设**
   - 直接输出 `Sample(worldPosition.xz)` 与 `Sample(worldPosition.xz / _MapExtent)`，快速区分 tileable world UV 和 normalized map UV。

---

## Problems Encountered & Solutions

### Problem 1: Pixel history 初始取点未命中 184
**Symptom:** 早期取点只显示 terrain seed event `157`。
**Root Cause:** 导出 PNG 目测坐标与河道实际像素不一致。
**Solution:** 先用 PNG 扫描暗像素，再对 `(420,334)` 做 pixel history/debug_pixel，命中 event `184`。

### Problem 2: `get_cbuffer_contents` 返回全 0
**Symptom:** RenderDoc cbuffer 内容工具显示 `_MapExtent` 等变量为 0。
**Root Cause:** 该 capture/tool 对优化后常量 buffer 解码不可靠。
**Solution:** 从 shader trace 寄存器反推实际值，`_MapExtent≈18431`。

---

## Code Quality Notes

### Testing
- `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj --no-restore` ✅
- `dotnet build Terrain.Editor\Terrain.Editor.csproj --no-restore -v minimal` ✅
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug` ✅
- `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug` ✅，907 succeeded / 0 failed

### Technical Debt
- 还未完整实现 CK3 `CalculateParallaxOffsetSteep` 的 iterative parallax world/tangent offset。
- 当前 bottom lighting 仍是 CK3-inspired approximation，shadow 使用 fallback，不是 CK3 的完整 shadow sampling pipeline。

---

## Next Session

### Immediate Next Steps
1. 让用户重新截一帧新的 `debug.rdc`，检查 event `184` bottom RT 是否出现棕色 world-locked 河床。
2. 对照 CK3 继续补齐 `CalcParallaxedUvs` 的 iterative parallax offset。
3. 若 bottom RT 正确但最终画面仍不对，再转向 event `213` surface/refraction 调试。

### Docs to Read Before Next Session
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)
- [2026-06-16-river-watercolor-uv-refraction-fix.md](2026-06-16-river-watercolor-uv-refraction-fix.md)

---

## Session Statistics

**Files Changed:** 7
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverBottom.sdsl` Bottom 纹理必须用 `worldPosition.xz` 采样；`riverUv` 只用于 depth/edge/connection。
- 当前 `debug.rdc` event `184` hot-replace 已验证 `Sample(worldPosition.xz)` 会恢复棕色河床。
- 完整 hot-replace 已验证修正后的 bottom RT 能传到 event `213` surface 输出，但剩余视觉差异仍可能来自 surface shading/lighting。
- 不要用 `_BottomDiffuseMultiplier` 或其它亮度倍率掩盖 UV/lighting 问题。

**Gotchas for Next Session:**
- `get_cbuffer_contents` 可能显示全 0；用 trace/disasm 反推常量。
- Bottom DDS 和 water-color DDS 的 UV 语义不同：Bottom 是 tileable world UV，water-color 是 normalized map UV 且 Y 翻转。
