# River CK3 Current Pass Semantic Audit
**Date**: 2026-06-19
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 对比 `C:\Users\Redwa\Desktop\debug.rdc` 与 `C:\Users\Redwa\Desktop\ck3-river.rdc`，逐个 pass 判断当前河流和 CK3 的 shader / 资源 / cbuffer 是否一致或等价。

**Secondary Objectives:**
- 在修改 `.sdsl` 前，优先用已有 RenderDoc 工件和可恢复的 MCP 工具确认方向。
- 找出当前与 CK3 差距最大的实际环节，而不是继续围绕水色常量做无效调整。

**Success Criteria:**
- 明确 bottom 与 surface 哪部分已经接近 CK3。
- 明确当前最主要的不等价点。
- 给出下一步修改优先级。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md](./2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md)
- See: [2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md](./2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 当前 river 实现仍是 `scene seed -> bottom/refraction -> surface`。
- 用户要求停止凭源码猜测，直接基于 capture、shader loose source、资源绑定与热修改证据判断。

**Why Now:**
- 当前河流视觉和 CK3 仍有明显差距，需要明确到底是 bottom、surface 本体，还是 surface 后段输入语义不等价。

---

## What We Did

### 1. 复核 current / CK3 的关键 capture 证据
**Files Changed:** 无

**Implementation:**
- 复核已有日志和工件，确认：
- current `debug.rdc`
  - bottom 关键事件：`276`
  - surface 关键事件：`397`
  - `426` 是 FXAA，不是新的 river pass
- target `ck3-river.rdc`
  - bottom 关键事件：`338`
  - surface 关键事件：`466`
- 用 `diff_open` / `diff_summary` 再次核对两个 capture 的总体规模：
  - current `69 draws / 82 events / 293 resources`
  - CK3 `424 draws / 441 events / 710 resources`

**Rationale:**
- 这确认了两帧不是“只差几个常量”的同规模链路，后续必须按 pass 语义对齐，而不是只比单个 RT 截图。

### 2. 复核 current `RiverSurface` 与 `RiverRenderFeature` 的真实输入集合
**Files Changed:** 无

**Implementation:**
- 检查 [RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:95) 到 [RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:109)：
  - 当前 surface 绑定 `FoamTexture`、`FoamRampTexture`、`WaterColorTexture`、`HeightmapSlice0..7`、`ReflectionSpecularTexture`
- 检查 [RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:487)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:522)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:566)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:633)、[RiverSurface.sdsl](E:\Stride Projects\Terrain\Terrain.Editor\Effects\RiverSurface.sdsl:674)
  - 当前完整 PS 仍是 `CalcRiverAdvanced -> CalcWater -> ApplyTerrainShadowTintWithClouds -> ApplyMapDistanceFogWithoutFoW -> PSMain`
- 检查 [RiverRenderFeature.cs](E:\Stride Projects\Terrain\Terrain.Editor\Rendering\River\RiverRenderFeature.cs:596) 到 [RiverRenderFeature.cs](E:\Stride Projects\Terrain\Terrain.Editor\Rendering\River\RiverRenderFeature.cs:654)
  - current C# 明确绑定 `FoamTexture`、`FoamRampTexture`、`ShadowNoiseTexture`、`WaterColorTexture`、`ReflectionSpecularTexture`
  - `BindSurfaceRequiredInputs()` 强制走 `TryBindEditorTerrainInputs()`
  - `TryBindEditorTerrainInputs()` 明确把 Editor 地形 `HeightmapSlice0..7`、`SliceCount`、`HeightScale`、`_WorldSpaceToTerrain0To1`、`_InverseWorldSize` 塞进 river surface

**Rationale:**
- 这把“当前只是 shader 公式偏了”排除掉了一半：current surface 后段输入本来就不是 CK3 的那一套。

### 3. 对照 CK3 loose shader source 与 capture 绑定
**Files Changed:** 无

**Implementation:**
- 复核 `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\river_surface.shader`
  - wrapper 明确是 `CalcRiverAdvanced( Input )._Color`
  - 然后继续走 `ShadowMap -> ApplyTerrainShadowTintWithClouds -> ApplyFogOfWar -> ApplyMapDistanceFogWithoutFoW`
- 复核 `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_river_surface.fxh`
  - advanced river flow normal 路径仍是单次 `FlowNormalTexture` 采样
  - edge fade 与 connection fade 逻辑和当前 SDSL 前半段基本同类
- 复核现有 target 工件 `artifacts/renderdoc/river_surface_calcwater_gate/target-surface-bindings.json`
  - CK3 surface capture 明确绑定：
    - `HeightLookupTexture_Texture`
    - `PackedHeightTexture_Texture`
    - `FogOfWarAlpha_Texture`
    - `ShadowMap_Texture`
    - `FoamTexture_Texture`
    - `FoamRampTexture_Texture`
    - `WaterColorTexture_Texture`

**Rationale:**
- CK3 的完整 river surface 不是只有 `CalcWater` 前半段；它的 wrapper 和后段依赖的资源集合本身就是语义的一部分。

### 4. 复核当前剩余差距不是“常量没对齐”
**Files Changed:** 无

**Implementation:**
- 复核 [2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md](./2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md)
  - current `event 397` 关键 water cbuffer 常量已与 CK3 target surface 对齐：
    - `_WaterDiffuseMultiplier = 1.0`
    - `_WaterColorMapTintFactor = 0.0106959343`
    - `_WaterSeeThroughDensity = 0.8`
    - `_WaterFresnelBias = 0.01`
    - `_WaterFresnelPow = 4.3`
    - `WaterColorShallow/Deep` 也已对齐
- 同时确认 `event 426` 只是 FXAA

**Rationale:**
- 因此当前不该继续优先调 `WaterColorShallow/Deep`、Fresnel、see-through 标量。

### 5. 复核资源元数据剩余差异
**Files Changed:** 无

**Implementation:**
- 复核 `artifacts/renderdoc/river_surface_calcwater_gate/target-surface-bindings.json`
  - CK3 `FoamTexture_Texture` 是 `3072x512 BC3_SRGB`
  - 当前项目 `foam.dds` 不是一一对应资源
  - CK3 `FoamRampTexture_Texture` 是 `1024x1024 BC3_SRGB`
  - 当前项目 `foam-ramp.dds` 是另一种语义资源，不是一一对应
- 复核本地 asset descriptor：
  - [water-color.sdtex](E:\Stride Projects\Terrain\Terrain.Editor\Assets\River\Water\water-color.sdtex:1) 已经是 `UseSRgbSampling: true`
  - [foam.sdtex](E:\Stride Projects\Terrain\Terrain.Editor\Assets\River\Water\foam.sdtex:1) 与 [foam-ramp.sdtex](E:\Stride Projects\Terrain\Terrain.Editor\Assets\River\Water\foam-ramp.sdtex:1) 仍是本地资源语义

**Rationale:**
- 这说明“water-color 一定还是 sRGB 错”不是当前最强根因；更大的资源级风险落在 foam/foam-ramp 选型不等价。

---

## Decisions Made

### Decision 1: 不再把 bottom 当本轮主修方向
**Context:** 多轮 capture 与热修改都表明 bottom 已接近 CK3 同类分支。
**Options Considered:**
1. 继续追 bottom lighting 和 shadow
2. 把重心切到 surface 完整 PS 和输入集合

**Decision:** Chose Option 2
**Rationale:** current bottom 与 CK3 bottom 在 draw/disasm/绑定层面已经是同类问题；surface 才是当前主要差距。
**Trade-offs:** 如果后续仍有局部黑块，可能还要回头看 mesh/tangent 方向，但那不是本轮主线。
**Documentation Impact:** 记录在本日志即可，架构文档不需要额外更新。

### Decision 2: 下一步优先改输入语义和资源槽位，不先调水色常量
**Context:** current `event 397` 的关键 water cbuffer 常量已和 CK3 对齐。
**Options Considered:**
1. 继续调 `WaterColorShallow/Deep`、Fresnel、see-through
2. 优先解决 `HeightmapSlice` 替代链和 foam/foam-ramp 资源不等价

**Decision:** Chose Option 2
**Rationale:** 这才是 capture 与源码同时能证明的不等价点。
**Trade-offs:** 需要改动范围可能大于单纯调常量，但方向正确。
**Documentation Impact:** 记录在本日志即可。

---

## What Worked ✅

1. **把 loose shader、capture 绑定和本地代码放到同一张表里看**
   - What: 同时核对 CK3 `river_surface.shader`、`jomini_river_surface.fxh`、target bindings、current SDSL/C#
   - Why it worked: 快速区分了“前半段公式接近”和“完整 PS 输入语义等价”这两个不同层级
   - Reusable pattern: Yes

2. **先排除已经对齐的常量**
   - What: 直接引用 current `event 397` 与 target cbuffer 工件
   - Impact: 避免继续把时间浪费在水色、Fresnel、see-through 常量上

---

## What Didn't Work ❌

1. **继续尝试 `renderdoc-mcp` 的 `diff_draws`**
   - What we tried: 在已打开的 diff session 上跑 `diff_draws`
   - Why it failed: MCP transport 直接崩溃，后续 `open_capture` 也无法恢复
   - Lesson learned: diff summary 可用，但 diff draws 在当前工具版本上不稳定
   - Don't try this again because: 会中断整轮 RenderDoc 分析

---

## Problems Encountered & Solutions

### Problem 1: 表面上看像“shader 已经很像 CK3”，但效果仍差很大
**Symptom:** `CalcRiverAdvanced` / `CalcWater` 前半段和 CK3 看起来很接近，但最终画面仍不对。
**Root Cause:** 当前仍用 editor terrain `HeightmapSlice0..7` 去近似 CK3 的 `HeightLookup/PackedHeight/FoW/ShadowMap` 语义链；同时 foam / foam-ramp 资源也不一一对应。
**Investigation:**
- Tried: 对照 loose shader source
- Tried: 对照 current/target bindings 工件
- Tried: 对照本地 `RiverSurface.sdsl` 与 `RiverRenderFeature.cs`
- Found: 公式前半段接近不代表完整 PS 等价

**Solution:**
- 本轮不给运行时代码打补丁，先把根因收敛到：
  - surface 输入语义不等价
  - surface 资源槽位语义不等价
  - 已对齐常量不是主问题

**Pattern for Future:** 先区分“公式段对齐”与“完整 PS + 资源语义对齐”。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update ARCHITECTURE_OVERVIEW.md - 本轮无系统状态变化
- [ ] Update CURRENT_FEATURES.md - 本轮无实现状态变化

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 editor terrain `HeightmapSlice` 近似路径当成 CK3 river surface 后段等价物
- What not to do: 看到 `ApplyTerrainShadowTintWithClouds` / `ApplyMapDistanceFogWithoutFoW` 已存在，就认为 current surface 已经和 CK3 完整等价
- Why it's bad: capture 绑定和 loose source 都证明后段依赖的资源语义完全不同
- Add warning to: 本日志即可，已有学习文档已覆盖“完整 PS 边界优先于 include 边界”

### Architectural Decisions That Changed
- 无

---

## Code Quality Notes

### Testing
- **Manual Tests:** 本轮仅做 capture、shader 源码、绑定工件与本地代码对照
- **Automated Tests:** 无新增

### Technical Debt
- **Created:** 无运行时代码债
- **TODOs:**
  - 下一轮需要先在 RenderDoc 热修改里验证“切掉 editor terrain 后段/替换资源槽位”哪一个更接近 CK3

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 先用 RenderDoc 热修改验证 surface 后段输入替换方案
2. 优先检查并修正 `FoamTexture` / `FoamRampTexture` 的资源选型或绑定语义
3. 再决定是继续沿 editor terrain 近似路径修，还是显式建立更接近 CK3 的 surface 输入 provider

### Blocked Items
- **Blocker:** `renderdoc-mcp` 在 `diff_draws` 后会崩 transport
- **Needs:** 新会话恢复 MCP，或改走 `renderdoc-cli` / GUI 热修改
- **Owner:** 调试会话环境

### Questions to Resolve
1. surface 画面主差距里，`HeightmapSlice` 近似链和 `foam/foam-ramp` 资源不等价各占多大比例？
2. 是否值得为 runtime/editor 统一做一条更接近 CK3 的 river surface terrain provider？

### Docs to Read Before Next Session
- [2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md](./2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md)
- [2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md](./2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +221/-0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current bottom 不是本轮主差距
- current surface 关键 water 常量已经和 CK3 对齐
- 当前最稳定的不等价点是 surface 后段输入语义和 foam / foam-ramp 资源语义

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无
- Constraints: `renderdoc-mcp diff_draws` 仍会崩 transport

**Gotchas for Next Session:**
- 不要再先调 `WaterColorShallow/Deep`、Fresnel、see-through 标量
- 不要把 `HeightmapSlice` 路径误当成 CK3 `HeightLookup/PackedHeight/FoW/ShadowMap` 的等价实现
- 先做热修改验证，再改 `.sdsl`

---

## Links & References

### Related Sessions
- [2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md](./2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md)
- [2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md](./2026-06-19-river-surface-cbuffer-fxaa-and-transport-crash.md)

### Code References
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Assets/River/Water/water-color.sdtex`
- `Terrain.Editor/Assets/River/Water/foam.sdtex`
- `Terrain.Editor/Assets/River/Water/foam-ramp.sdtex`

### External Resources
- `C:\Users\Redwa\Desktop\debug.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\river_surface.shader`
- `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_river_surface.fxh`

---

## Notes & Observations

- `water-color.sdtex` 已经启用 `UseSRgbSampling: true`，因此“water color 仍然因为 sRGB 错误”不是当前最强结论。
- `FoamTexture` / `FoamRampTexture` 的 target metadata 与本地资产仍然明显不一一对应。

---
