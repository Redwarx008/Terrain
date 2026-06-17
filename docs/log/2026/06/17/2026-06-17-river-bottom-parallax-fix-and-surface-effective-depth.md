# River Bottom Parallax Fix And Surface Effective Depth
**Date**: 2026-06-17
**Session**: river-bottom-parallax-fix-and-surface-effective-depth
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不先改仓库代码的前提下，用 RenderDoc 热替换验证 current `RiverBottom` 的 steep parallax 是否是河心过暗的 root cause，并在证据充分后把最小修正落到 `RiverBottom.sdsl` / `RiverSurface.sdsl`。

**Secondary Objectives:**
- 验证 `RiverBottom` hot-edit 的 exact clone，确保 replacement 结果可信。
- 对比 root fix、surface guard、以及两者叠加后的实际像素变化。
- 按 Stride shader 工作流执行资产重编译和项目验证。

**Success Criteria:**
- 能用 RenderDoc 证明 `RiverBottom` 的 parallax 插值失稳会把 `bottomWorldPosition` 横向外插。
- 能用热替换证明 `RiverSurface` 的 `effectiveDepth` guard 仍然有效。
- 仓库代码、Stride 资产编译、项目构建和 `Terrain.Editor.Tests` 全部通过。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-surface-refraction-attenuation-hotedit.md](./2026-06-17-river-surface-refraction-attenuation-hotedit.md)
- See: [2026-06-17-river-surface-bankfade-watercolor-hotedit.md](./2026-06-17-river-surface-bankfade-watercolor-hotedit.md)
- See: [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 已知 current `RiverSurface` 河心像素 `(344,598)` 的 `attenuation≈4.77e-07`，see-through 已完全塌回 `water-color map`。
- 已知 current `RiverBottom` 代表像素 `(172,299)` 通过 trace 算出了约 `(16.62, -14.10)` 的 `worldSpaceOffset`，导致 `bottomWorldPosition` 横向漂移约 `21.8m`。

**Why Now:**
- 用户要求“认真分析每一个 pass 的输出，热修改后要和参考对比再落到代码，不要动不动让我来验证”。
- 之前的 packed debug 已经把问题收敛到 `bottom parallax + surface see-through`，这一轮应该从诊断收束到最小实现修正。

---

## What We Did

### 1. 先把 `RiverBottom` 的 RenderDoc replacement 做成 exact clone
**Files Changed:** 桌面临时 HLSL，不改仓库

**Implementation:**
- 新增：
  - `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\tools\bottom_current_like.hlsl`
  - `...\tools\bottom_input_probe.hlsl`
- 用探针先确认 replacement 输入语义：
  - `float3 RiverNormal : TEXCOORD3` 会读成 `v3.xyz`
  - 而当前 capture 的真实布局是：
    - `v3.x = RiverTransparency`
    - `v3.yzw = RiverNormal`
- 修正为：
  - `float4 RiverPacked3 : TEXCOORD3`
  - `surfaceNormal = normalize(RiverPacked3.yzw)`
- 修正后 `bottom_current_like.hlsl` 在 `event 184` / `(172,299)` 与原始 shader 0-diff：
  - bottom RT `0.110596 / 0.120422 / 0.120789 / 29.03125`
  - surface `event 213` / `(344,598)` 也保持 `0.069641 / 0.178223 / 0.226685`

**Rationale:**
- 只有 exact clone 成立，后续 `weight` 热修改才有可信度。

### 2. 验证 `RiverBottom` root fix：parallax `weight` 改成保符号分母 + `saturate`
**Files Changed:** 桌面临时 HLSL，不改仓库

**Implementation:**
- 新增：
  - `...\tools\bottom_parallax_weight_clamped.hlsl`
  - `...\tools\bottom_parallax_weight_clamped_probe.hlsl`
- 唯一逻辑改动：
```hlsl
float denom = nextDepth - prevDepth;
float safeDenom = abs(denom) > 0.0001f ? denom : (denom >= 0.0f ? 0.0001f : -0.0001f);
float weight = saturate(nextDepth / safeDenom);
```
- `event 184` / `(172,299)`：
  - baseline bottom RT：`0.110596 / 0.120422 / 0.120789 / 29.03125`
  - hot bottom RT：`0.089722 / 0.101624 / 0.100342 / 13.21094`
- probe 直接输出 `worldSpaceOffset.xy / bottomWorldPosition.y / compressedDistance`：
  - hot probe：`(0.113159, -0.096008, 16.0, 13.21094)`
- 与之前 current trace 对比：
  - baseline `worldSpaceOffset ≈ (16.6247, -14.1027)`
  - baseline `bottomWorldPosition.y ≈ 18.261`
  - baseline `compressedDistance ≈ 29.03`

**Rationale:**
- 这个热替换直接证明 `weight` 外插被收住了：
  - 横向偏移从二十米量级降到亚米量级
  - `compressedDistance` 从 `29.03` 降到 `13.21`
  - `bottomWorldPosition` 不再是“几乎同高但横向跑飞”的假底点

### 3. 在 bottom root fix 上叠加 surface scalar/R-pack，对比 current 与 CK3
**Files Changed:** 桌面临时 HLSL，不改仓库

**Implementation:**
- 组合替换：
  - bottom: `bottom_parallax_weight_clamped.hlsl`
  - surface: `surface_refraction_scalar_pack.hlsl`
- `event 213` 代表像素：
  - center `(344,598)`：`attenuation=0.18799`, `shoreMask=0`, `normalizedDepth=0.09027`
    - 即 `refractionDepth ≈ 1.81`
  - mid `(392,606)`：`attenuation=0.18579`, `shoreMask=0`, `normalizedDepth=0.09241`
    - 即 `refractionDepth ≈ 1.85`
- 对比 baseline：
  - center 原本 `attenuation≈4.77e-07`, `refractionDepth≈15.65`
- 再组合替换：
  - bottom: `bottom_parallax_weight_clamped.hlsl`
  - surface: `surface_refraction_r_pack.hlsl`
- `event 213`：
  - center `(344,598)`：
    - baseline `selectedRefraction.r / waterColorMap.r / seeThrough.r = 0.13013 / 0.03229 / 0.03229`
    - hot `= 0.09674 / 0.03564 / 0.04712`
  - mid `(392,606)`：
    - baseline `= 0.12140 / 0.03577 / 0.05026`
    - hot `= 0.09973 / 0.03580 / 0.04767`
- CK3 对照（上一轮已测）：
  - `(110,738)`：`attenuation≈0.4805`, `refractionDepth≈0.52`, `seeThrough.r≈0.1366`

**Rationale:**
- bottom root fix 已经把 current center 像素从“完全塌回 water-color map”拉回到“至少保留部分 bottom/refraction”。
- 但它单独还不足以让 current 达到 CK3 的明亮区间，说明 surface 侧 guard 仍然有价值。

### 4. 验证 `bottom root fix + surface effectiveDepth guard` 的组合
**Files Changed:** 桌面临时 HLSL，不改仓库

**Implementation:**
- 新增：
  - `...\tools\surface_effective_depth_see_through.hlsl`
- 组合替换：
  - bottom: `bottom_parallax_weight_clamped.hlsl`
  - surface: `surface_effective_depth_see_through.hlsl`
- `event 213`：
  - center `(344,598)`：
    - baseline final `0.069641 / 0.178223 / 0.226685`
    - hot final `0.104126 / 0.203369 / 0.264404`
  - mid `(392,606)`：
    - baseline `0.074768 / 0.178467 / 0.222900`
    - hot `0.102478 / 0.192505 / 0.247437`
  - edge `(352,705)`：
    - baseline `1.73828 / 1.81933 / 0.78271`
    - hot `1.73535 / 1.81738 / 0.78076`

**Rationale:**
- 组合热修在河心明显变亮，而岸边几乎不变。
- 与之前“只改 surface effectiveDepth”的结果相比，这个组合略暗，说明 bottom root fix 修正了距离语义，但 current 底部 refraction 能量本身仍然低于 CK3。

### 5. 把已验证的最小修正落到仓库，并执行 Stride 资产工作流
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
```csharp
// RiverBottom.sdsl
float denom = nextDepth - prevDepth;
float safeDenom = abs(denom) > 0.0001f ? denom : (denom >= 0.0f ? 0.0001f : -0.0001f);
float weight = saturate(nextDepth / safeDenom);

// RiverSurface.sdsl
return float4(
    ApplyTerrainUnderwaterSeeThrough(effectiveDepth, refractionWorldPosition, refractionWaterColorMap, refraction.rgb),
    effectiveDepth);
```
- 同步更新文本测试，要求：
  - bottom parallax 使用 `safeDenom + saturate(weight)`
  - surface see-through 调用改为 `effectiveDepth`

**Architecture Compliance:**
- ✅ 保持 `bottom -> refraction -> surface` 架构不变
- ✅ 先用 RenderDoc 热替换确认，再改 SDSL
- ✅ 按 `stride-shader-asset-workflow` 执行 Stride 资产重编译

---

## Decisions Made

### Decision 1: 先落 `RiverBottom` root fix，再保留 `RiverSurface` guard
**Context:** root cause 已经被证明在 `RiverBottom` 的 parallax 外插，但 root fix 单独带来的最终亮度提升不足。
**Options Considered:**
1. 只改 `RiverBottom`
2. 只改 `RiverSurface effectiveDepth`
3. 两者都改

**Decision:** 选择 3
**Rationale:** `RiverBottom` 修的是错误 distance semantics，`RiverSurface` guard 修的是错误距离进入 see-through 衰减时的灾难性后果；两者作用层级不同。
**Trade-offs:** 视觉上仍未完全达到 CK3，后续还要继续对齐 bottom/surface 能量与参数。

### Decision 2: 不继续猜 HLSL replacement 输入，先做 packed-input probe
**Context:** 第一版 `bottom_current_like.hlsl` 输出严重偏离原 shader。
**Options Considered:**
1. 继续改公式猜差异
2. 直接做 input probe 看寄存器布局

**Decision:** 选择 2
**Rationale:** probe 直接证明 `float3 TEXCOORD3` 会把 transparency 读进 normal，省掉大量无效排查。
**Trade-offs:** 多写一份临时 HLSL，但换来 exact clone 可信度。

---

## What Worked ✅

1. **packed-input probe**
   - What: 用最小 replacement 输出 `TEXCOORD3` 的真实分量映射
   - Why it worked: 直接钉死 `v3.x`/`v3.yzw` 的 packed 语义
   - Reusable pattern: Yes

2. **bottom probe + surface scalar/R-pack 组合替换**
   - What: 同时替换 bottom 与 surface，两边都输出中间量
   - Why it worked: 先证明 root fix 改了底层距离语义，再证明 see-through 不再塌回 `water-color map`
   - Reusable pattern: Yes

3. **Stride 强制资产重编译**
   - What:
     - `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles`
     - `StrideCleanAsset`
     - `StrideCompileAsset`
   - Impact: 确认仓库改动进入 Stride 资产流水线，而不是停留在热替换或缓存里

---

## What Didn't Work ❌

1. **第一版 `bottom_current_like.hlsl`**
   - What we tried: 直接用 `float3 RiverNormal : TEXCOORD3`
   - Why it failed: 读成了 `v3.xyz`，把 transparency 混进了 normal
   - Lesson learned: Stride packed vertex semantics 不能只看字段名，必须先看 capture 里的寄存器布局
   - Don't try this again because: exact clone 不成立时，后面所有 hot-edit 结论都会被污染

---

## Problems Encountered & Solutions

### Problem 1: bottom exact clone 与原 shader 差异很大
**Symptom:** first exact clone 把 bottom RT `(172,299)` 从 `0.1106 / 0.1204 / 0.1208 / 29.03` 变成了 `0.3003 / 0.2986 / 0.2703 / 30.20`。
**Root Cause:** replacement 错读了 packed `TEXCOORD3`，把 transparency 当成了 normal.x。
**Investigation:**
- Tried: `bottom_current_like.hlsl`
- Tried: `bottom_input_probe.hlsl`
- Found: `float3 RiverNormal : TEXCOORD3` 读到的是 `v3.xyz`

**Solution:**
```hlsl
float4 RiverPacked3 : TEXCOORD3;
float3 surfaceNormal = normalize(RiverPacked3.yzw);
```

**Why This Works:** 它按 capture 的真实 packed register 解释输入，而不是按字段名猜语义。
**Pattern for Future:** RenderDoc HLSL replacement 先用 probe 确认 packed semantics，再写 exact clone。

### Problem 2: root fix 单独视觉收益不够
**Symptom:** bottom root fix 把 center 最终色从 `0.06964 / 0.17822 / 0.22668` 只抬到 `0.08044 / 0.18616 / 0.23645`，仍偏暗。
**Root Cause:** root fix 修的是错误 depth semantics，不会自动提高 bottom/refraction 的颜色能量；surface 仍需要 guard 避免 see-through 按更深值衰减。
**Investigation:**
- Tried: `bottom_parallax_weight_clamped.hlsl`
- Tried: `bottom + surface scalar pack`
- Tried: `bottom + surface effectiveDepth guard`
- Found: bottom fix 让 `attenuation` 从 `~0` 回到 `~0.188`，但 guard 仍能进一步抬亮河心

**Solution:**
```text
仓库同时落：
- RiverBottom safeDenom + saturate(weight)
- RiverSurface ApplyTerrainUnderwaterSeeThrough(effectiveDepth, ...)
```

**Why This Works:** root fix 负责纠正错误底距，surface guard 负责避免 residual over-depth 继续把 see-through 衰减到接近 0。
**Pattern for Future:** root cause 修正和 symptom guard 不一定互斥，先拆层验证，再一起落地。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `ARCHITECTURE_OVERVIEW.md` - 记录 bottom parallax clamp 与 surface effectiveDepth guard
- [x] Update `CURRENT_FEATURES.md` - 记录 current river core darkening 修正路径

### New Patterns/Anti-Patterns Discovered
**New Pattern:** packed-input probe
- When to use: RenderDoc replacement 命中 Stride 自定义顶点流、且 exact clone 不成立时
- Benefits: 能快速识别 packed semantic 误读
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Pattern:** root fix 与 guard 分层验证
- When to use: 同一视觉问题同时涉及 bottom distance semantics 和 surface attenuation
- Benefits: 避免把 guard 错当 root cause，也避免低估 root fix 的必要性
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** 无新的架构分层
- **Scope:** 仅 river bottom/surface shader 行为
- **Reason:** 在现有三段链路内修正错误距离语义和 see-through 衰减

---

## Code Quality Notes

### Testing
- **Tests Written:** 无新测试文件；更新现有 `RiverShaderTextTests`
- **Coverage:**
  - RenderDoc hot-edit exact clone / probe / scalar pack / R-pack / final color
  - `StrideAssetUpdateGeneratedFiles`
  - `StrideCleanAsset`
  - `StrideCompileAsset`
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore`
- **Manual Tests:** 未启动编辑器做人工画面回归；本轮主要依赖 RenderDoc 和测试程序集

### Technical Debt
- **Created:** 无
- **Paid Down:** `RiverBottom` parallax 外插导致的假深水语义错误
- **TODOs:**
  - 继续对齐 current 与 CK3 的 bottom/refraction 能量
  - 继续评估是否需要 CK3 `MaxHeight` camera clamp parity

### Warnings Observed
- `StrideCompileAsset` 出现现有 shader 编译告警：`warning X3557: loop doesn't seem to do anything, forcing loop to unroll`
- `dotnet build` / `dotnet run` 仍有既有 NuGet 漏洞告警与既有 C# warning；本轮未新增构建错误

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 用新构建出来的 editor/runtime 再截一帧，确认仓库版本与热替换结果一致
2. 继续对齐 current 与 CK3 的 bottom/refraction 能量差，优先看 bottom RT 相对 seed 的对比度
3. 再决定是否引入更多 CK3 `JominiWater` 参数，而不是继续只调色

### Questions to Resolve
1. current 在 root fix + guard 后仍比 CK3 暗，下一刀应该放在 bottom lighting 还是 surface water-color/reflection 能量？
2. 是否需要让 current 在更高相机位也做一次 `MaxHeight` parity 对照？

### Docs to Read Before Next Session
- [2026-06-17-river-cbuffer-semantic-comparison.md](./2026-06-17-river-cbuffer-semantic-comparison.md) - current/CK3 water 常量差异
- [2026-06-17-river-surface-refraction-attenuation-hotedit.md](./2026-06-17-river-surface-refraction-attenuation-hotedit.md) - packed debug 结论

---

## Session Statistics

**Files Changed:** 7（仓库）+ 若干桌面分析临时 HLSL/JSON/PNG
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation:
  - `Terrain.Editor/Effects/RiverBottom.sdsl` 现在使用 `safeDenom + saturate(weight)` 防止 steep parallax 外插
  - `Terrain.Editor/Effects/RiverSurface.sdsl` 现在用 `effectiveDepth` 做 see-through attenuation
- Critical decision: root fix 和 surface guard 都保留，因为两者作用层级不同
- Active pattern: `packed-input probe -> exact clone -> root-fix hot-edit -> scalar/R-pack -> repo patch`
- Current status: 仓库改动、Stride 资产编译、项目构建、测试都已通过

**What Changed Since Last Doc Read:**
- Implementation:
  - river bottom parallax 权重修正已落代码
  - river surface effectiveDepth guard 已落代码
- Docs:
  - 架构总览、功能总览、learnings 已同步

**Gotchas for Next Session:**
- Watch out for: RenderDoc replacement 里 `TEXCOORD3` 可能吃到 packed transparency
- Don't forget: 热替换验证完后一定要跑 `StrideCleanAsset` / `StrideCompileAsset`
- Remember: bottom root fix 单独不等于视觉 parity，还要继续看 bottom/refraction 能量

---

## Links & References

### Related Documentation
- [2026-06-17-river-surface-refraction-attenuation-hotedit.md](./2026-06-17-river-surface-refraction-attenuation-hotedit.md)
- [2026-06-17-river-surface-bankfade-watercolor-hotedit.md](./2026-06-17-river-surface-bankfade-watercolor-hotedit.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\hotedit_bottom_parallax_weight_clamped\summary.json`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\bottom_fix_plus_surface_scalar_pack.json`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\bottom_fix_plus_surface_r_pack.json`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\analysis_20260617_latest\hotedit_bottom_fix_plus_surface_effective_depth\summary.json`

### Code References
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---

## Notes & Observations

- bottom probe 说明这次 root cause 非常干净：热修后横向偏移直接从二十米量级掉到亚米量级。
- surface effectiveDepth guard 仍然是值得保留的安全带，因为 current 的 bottom/refraction 颜色能量还没追上 CK3。

---

*Template Version: 1.0 - Based on Archon-Engine template*
