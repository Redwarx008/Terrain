# River CK3 Shader Pass Equivalence Analysis
**Date**: 2026-06-19
**Session**: 8
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续把 current `debug.rdc` 与 `ck3-river.rdc` 对位，确认本地 river shader 与 CK3 shader 在各 pass 上是否一致或等价。

**Secondary Objectives:**
- 区分“shader 主公式不等价”和“输入资源语义不等价”。
- 给出下一轮 RenderDoc 热修改的正确落点。

**Success Criteria:**
- 对 `scene seed/pre-bottom`、`bottom`、`surface` 三段分别给出源码与 GPU 证据。
- 明确剩余主因是否还在 `RiverSurface.sdsl`，还是在 `JominiRefraction` 输入链。

---

## Context & Background

**Previous Work:**
- See: [2026-06-19-river-updated-debug-rdc-pixel-chain-verification.md](./2026-06-19-river-updated-debug-rdc-pixel-chain-verification.md)
- See: [2026-06-19-river-bank-payload-survival-analysis.md](./2026-06-19-river-bank-payload-survival-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- current capture 的稳定链路已经确认是 `223 -> 248 -> 276 -> 305`。
- 已知 current 河心最终颜色主要由 `276` bottom 主导，bank-edge 剩余问题集中在 surviving payload。

**Why Now:**
- 如果不把 CK3 的 pre-bottom/refraction payload pass 单独拆出来，就会继续误把问题归因到 `RiverSurface.sdsl` 常量。

---

## What We Did

### 1. 对位 current render feature 与 CK3 pass 结构
**Files Changed:** 无

**Implementation:**
- current [RiverRenderFeature.cs](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Rendering/River/RiverRenderFeature.cs:140) 明确是：
  - `SeedSceneColorFromScene(...)`
  - `CopySceneSeedToBottomColor(...)`
  - bottom 写 `BottomColor`
  - surface 读 `BottomColor` 作为 `RefractionTexture`
- dual-source blend 仍是：
  - `ColorSourceBlend = SecondarySourceAlpha`
  - `ColorDestinationBlend = InverseSecondarySourceAlpha`

**Rationale:**
- 这说明 current 在拓扑上确实已经接近 CK3 的“pre-bottom payload -> bottom -> surface”链路，但 source payload 的生产方式仍需单独比较。

### 2. 比较 current `248` 与 CK3 `304` 的 pre-bottom payload pass
**Files Changed:** 无

**Implementation:**
- current `248`:
  - RenderDoc bindings 只绑定 `Texture0` 和 `DepthStencil`
  - disasm 对应 [RiverSceneSeed.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverSceneSeed.sdsl:8)：
    - `CompressSceneSeedColor`
    - `ComputeSceneDistanceFromUV`
- CK3 `304`:
  - RenderDoc bindings 绑定了 `HeightLookupTexture`、`PackedHeightTexture`、`FogOfWarAlpha`、terrain detail/normal/material textures、flat map、sunny/shadow environment、shadow map
  - 输出 RT 与 bottom 同为 `ResourceId::49006`

**Rationale:**
- current `248` 只是“scene color 压缩 + scene depth 重建距离”的 seed。
- CK3 `304` 则已经是 terrain-aware 的 refraction payload 生产 pass，不等价于 current `RiverSceneSeed`。
- 这直接解释了为什么 current bank 上 surviving alpha payload 只有 `~9.67`，而 CK3 bank 记录能到 `~81.75`。

### 3. 比较 current `276` 与 CK3 `338` 的 bottom advanced pass
**Files Changed:** 无

**Implementation:**
- CK3 源码 [jomini_river_bottom.fxh](/abs/path/e:/SteamLibrary/steamapps/common/Crusader%20Kings%20III/jomini/gfx/FX/jomini/jomini_river_bottom.fxh:282)：
  - `Input.UV.x * _TextureUvScale`
  - `CalcParallaxedUvs(..., BottomNormal)`
  - `Diffuse/Properties/Normal` 主采样 `TangentUV`
  - `Alpha = Diffuse.a * FadeOut * FadeToConnection * EdgeFade1 * EdgeFade2`
  - `Out.Color.a = CompressWorldSpace(WorldSpacePos)`
  - `Out.Blend = vec4(Alpha)`
- current 源码 [RiverBottom.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:375) 与之逐项同构。
- CK3 `338` RenderDoc bindings 也与 current `276` 一一对应：
  - `BottomDiffuse/BottomNormal/BottomProperties`
  - `EnvironmentMap`
  - `ShadowTexture`

**Rationale:**
- bottom advanced pass 的主公式和资源形状已经基本等价。
- 当前剩余差异不再值得优先归因给 `RiverBottom.sdsl` 主逻辑。

### 4. 比较 current `305` 与 CK3 `466` 的 surface / `CalcWater`
**Files Changed:** 无

**Implementation:**
- CK3 源码 [jomini_river_surface.fxh](/abs/path/e:/SteamLibrary/steamapps/common/Crusader%20Kings%20III/jomini/gfx/FX/jomini/jomini_river_surface.fxh:53) 与 [jomini_water_default.fxh](/abs/path/e:/SteamLibrary/steamapps/common/Crusader%20Kings%20III/jomini/gfx/FX/jomini/jomini_water_default.fxh:214)：
  - `Params._Depth = Depth * Input.Width + 0.1f`
  - 单次 `FlowNormalTexture`
  - `CalcRefraction` 先 sample base refraction，再算 shore mask，再算 offset
  - `Depth = min(Input._Depth, RefractionDepth)`
  - `WaterFade = 1 - saturate(...)`
- current [RiverSurface.sdsl](/abs/path/e:/Stride%20Projects/Terrain/Terrain.Editor/Effects/RiverSurface.sdsl:562) 与 `305` disasm 已确认：
  - 同样的 `worldDepth = depth * worldWidth + 0.1f`
  - 单次 flow normal
  - 1080p 归一化的 refraction offset
  - `ApplyTerrainUnderwaterSeeThrough`
  - `depth = min(depth, refractionDepth)`
  - `ComputeWaterFade(depth)`

**Rationale:**
- `CalcWater` 核心水面公式已经大体同构。
- CK3 `466` 虽然还绑定 `HeightLookup/PackedHeight/FogOfWarAlpha/ShadowMap`，但这些属于 wrapper 后段；当前项目已把 `ApplySurfacePostProcessing` 缩成只处理可见性，不再改写主 RGB，因此它们不是 current 与 CK3 主水色差距的第一根因。

---

## Decisions Made

### Decision 1: 认定剩余主根因在 pre-bottom/refraction payload 生产，而不是 surface 主公式
**Context:** current `305` 与 CK3 `466` 的 `CalcWater` 主路径已经基本对齐，但 bank-edge 仍明显更亮更棕。
**Options Considered:**
1. 继续调 `RiverSurface.sdsl` 的 `WaterFade/see-through/reflection` 常量
2. 回到底层 payload，比较 current `248` 与 CK3 `304`

**Decision:** 选择 2
**Rationale:** current `248` 只吃 scene color + depth；CK3 `304` 已经是 terrain-aware 的 pre-bottom payload pass，资源和语义都明显更重。

### Decision 2: 下一轮 RenderDoc 热修改优先动 source payload，不优先动 surface 常量
**Context:** current bank payload 仍沿用浅 seed alpha，surface 只是按这份浅 payload 计算出合理但偏亮的结果。
**Decision:** 优先验证“把 bank payload 做深”是否能让 `305` 靠近 CK3。
**Rationale:** 如果喂给 `CalcRefraction` 的输入还是 `~9.67` 量级，就算 surface 公式对了，也不会长出 CK3 那种暗 bank。

---

## What Worked ✅

1. **把 pass 结构、源码和 capture 绑定放在一起对位**
   - What: 同时比较源码、pipeline state、bindings、shader disasm。
   - Why it worked: 能把“实现看起来对”和“GPU 实际吃到的输入不对”分开。
   - Reusable pattern: Yes

2. **先比较 pre-bottom payload，再比较 bottom/surface**
   - What: 不直接跳到 `CalcWater`，而是先拆 `248` 和 `304`。
   - Impact: 直接锁定当前最大非等价点是 `JominiRefraction` 生产链。

---

## What Didn't Work ❌

1. **把 CK3 surface 多余绑定直接当成 current 主色差距来源**
   - What we tried: 之前优先怀疑 `HeightLookup/PackedHeight/FoW`
   - Why it failed: current 已经把 post-color 关闭；这些绑定在当前主 RGB 路径里不是第一影响因子。
   - Lesson learned: 要先区分 `CalcWater` 主体和 wrapper 后处理。
   - Don't try this again because: 会继续偏离真正的 root cause。

---

## Problems Encountered & Solutions

### Problem 1: 为什么 shader 源码看起来已经很像 CK3，画面还是差很多
**Symptom:** `RiverBottom.sdsl`、`RiverSurface.sdsl` 多数关键函数都能在 CK3 源码里找到对应语义，但 current bank 还是偏亮。
**Root Cause:** 对位只停留在 include/function 层面，没有拆到 pre-bottom payload 的生产 pass。
**Investigation:**
- Tried: 继续盯 `CalcWater`
- Tried: 继续看 bank `WaterFade`
- Found: current `248` 和 CK3 `304` 绑定资源与职责完全不同

**Solution:**
- 将比较边界改成：
  - `current 248` vs `CK3 304`
  - `current 276` vs `CK3 338`
  - `current 305` vs `CK3 466`

**Why This Works:** 这样比较的是完整 pass 语义，而不是孤立函数文本。
**Pattern for Future:** 对参考项目做 shader 对位时，必须先找“谁在写 surface 读取的 refraction RT”。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 补充“先比较 refraction RT writer，再比较 surface reader”
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，系统状态未变
- [ ] Update `CURRENT_FEATURES.md` - 不需要，功能状态未变

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先比较 refraction RT 的 writer，再比较 river surface 的 reader
- When to use: 参考项目和本地项目 surface shader 公式已经接近，但观感仍差很大
- Benefits: 能快速发现问题在输入 payload，而不是 surface 常量
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Code Quality Notes

### Testing
- **Manual Tests:** RenderDoc MCP `open_capture/get_pipeline_state/get_bindings/get_shader`
- **Coverage:** current `248/276/305`，CK3 `304/338/466`

### Technical Debt
- **Created:** 无
- **Paid Down:** 进一步排除了“继续调 surface 主公式”这条低价值方向
- **TODOs:** 需要下一轮 hot-edit 一个更接近 CK3 的 pre-bottom payload 生产实验

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在 RenderDoc 热修改里先改 current `248` 或 `7802` 上的 bank payload 深度量级，而不是先改 `305` 的水面常量
2. 继续确认 current `223 -> 248` 是否能构造出更接近 CK3 `304` 的暗色/深 payload 组合
3. 只有 source payload 改深后仍无效，才回头细拆 `ApplyTerrainUnderwaterSeeThrough`

### Blocked Items
- **Blocker:** 无
- **Needs:** 下一轮针对 bank 的最小 hot-edit
- **Owner:** Claude

### Questions to Resolve
1. current `248` 应该直接变成更像 CK3 `304` 的 terrain-aware payload，还是先在 `CopySceneSeedToBottomColor` 前后额外插一层 payload remap？
2. bank payload 只加深 alpha 是否足够，还是还需要同时压暗 pre-bottom RGB？

---

## Session Statistics

**Files Changed:** 2
**Lines Added/Removed:** +1 日志，+1 learning
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current `248` 只是 scene-color + depth 的 seed，不等价于 CK3 `304`
- current `276` 与 CK3 `338` 的 bottom advanced pass 已经基本同构
- current `305` 与 CK3 `466` 的 `CalcWater` 主公式已基本同构
- 剩余主根因是 pre-bottom/refraction payload 生产链

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无
- Constraints: 下一轮热修改不要先碰 `RiverSurface.sdsl` 主公式

**Gotchas for Next Session:**
- Watch out for: 不要把 CK3 wrapper 后段绑定误判成 current 主 RGB 根因
- Don't forget: `304`/`338` 共用 CK3 的 pre-surface RT；current 对应的是 `248 -> copy -> 276`
- Remember: 先修 `JominiRefraction` 输入语义，再谈 `CalcWater`

---
