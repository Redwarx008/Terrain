# Ocean CK3 RenderDoc 热替换验证
**Date**: 2026-06-25
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 在不修改 `Terrain/Effects/Ocean/OceanSurface.sdsl` 的前提下，用 RenderDoc 热替换验证 Ocean 与 CK3 海面差距的真实来源。

**Success Criteria:**
- 不再机械照搬 CK3 的亮度、太阳强度、曝光、对比度参数。
- 排除 Ocean-only 色彩补偿 trick。
- 把可复用结论记录下来，供下一轮源码修复使用。

---

## Context & Background

**Current State:**
- 本地 `C:\Users\Redwa\Desktop\debug.rdc`：
  - Ocean draw: EID 280, PS `ResourceId::7810`
  - Final pass: EID 3445
- CK3 台湾参考 `C:\Users\Redwa\Desktop\ck3-ocean-tw.rdc`：
  - Water draw: EID 429
  - Mapface final: EID 1172
- CK3 意大利参考 `C:\Users\Redwa\Desktop\ck3-cocean-ltaly.rdc`：
  - Water draw: EID 490, PS `ResourceId::46253`
  - Mapface final: EID 1263, PS `ResourceId::46900`

**Why Now:**
- 之前的 Ocean shader 引入了 display/close/far response 类 Ocean-only 补偿，能把颜色推近截图，但会压平区域差异和水面细节。
- 用户明确要求先热替换验证，不要继续做 trick，也不要复制 CK3 光照模型里的亮度参数。

---

## What We Did

### 1. 热替换验证 Ocean 源路径变体
**Files Changed:** `tmp/renderdoc/hot-ab/ocean-hot-common.hlsl` only

**Variants:**
- `HOT_WATERCOLOR_FLIP_ONLY`
- `HOT_CK3_NORMAL_SAMPLING`
- `HOT_CK3_NORMAL_SAMPLING + HOT_WATERCOLOR_FLIP_ONLY`
- `HOT_CK3_NORMAL_SAMPLING + HOT_WATERCOLOR_FLIP_ONLY + HOT_DISABLE_RESPONSES`

**Outputs:**
- `tmp/renderdoc/hot-ab/mcp-variants-20260625/*.png`

**Findings:**
- `water_color` 只做南北翻转会产生大块暗带，不能作为独立修复。
- 只切 CK3 normal/flow 取样路径能略微提升细节，但最终图仍远低于 CK3 的水面反射/波纹存在感。
- normal/flow + water flip 的组合同时引入暗带，视觉上更偏离 CK3。
- 禁用 Ocean-only response 后最终图明显变暗，说明当前本地可见颜色主要来自 Ocean response，而不是源路径里的 refraction/reflection/lit water。

### 2. 分量诊断
**Files Changed:** `tmp/renderdoc/hot-ab/ocean-hot-common.hlsl` only

**Diagnostics:**
- `HOT_VIS_REFRACTION`
- `HOT_VIS_REFLECTION`
- `HOT_VIS_FRESNEL`
- `HOT_VIS_LIT_WATER`
- `HOT_VIS_WATER_COLOR_MAP`
- `HOT_VIS_NORMAL_SLOPE`

**Outputs:**
- `tmp/renderdoc/hot-ab/diagnostics-20260625/*.png`
- `tmp/renderdoc/hot-ab/diagnostics-20260625/component-sheet.png`

**Findings:**
- 本地 normal slope 和 fresnel 分量里有水面结构。
- 本地 reflection/refraction/lit water 在水面区域偏暗、低对比，不能承载 CK3 那种最终可见的浪纹和高光。
- 当前 Ocean response 把颜色抬起来，但它不是 CK3 路径，会压平真实分量的细节和区域差异。
- CK3 意大利最终图的强水面细节来自水面源路径 + mapface/Tony/ColorCube/FogBlur/RestoreBloom 后处理链共同作用，不是单独把 Ocean raw 色彩硬映射到目标颜色。

---

## Decisions Made

### Decision 1: 不继续沿 Ocean-only response 补偿方向修
**Context:** response 能让颜色接近，但会把不同海域和波纹细节压到同一类显示色。

**Decision:** 后续源码修复不应继续扩大 `_OceanDisplay*`、`_OceanCloseWaterColor*`、`_OceanFarMap*` 这类补偿。

**Rationale:**
- 它绕过了 CK3 的真实路径：水面 draw 产生结构，mapface/post 负责最终显示。
- 它解释不了 CK3 在意大利、台湾、远景等不同海域的区域差异。

### Decision 2: 不照搬 CK3 亮度参数
**Context:** CK3 使用 `SunIntensity=20`、mapface fixed exposure/contrast、Tony LUT、ColorCube、FogBlur 等链路；本地 final pass 只有简单 tonemap 参数。

**Decision:** CK3 的亮度、曝光、对比度、太阳强度只能作为链路差异证据，不能直接搬到本地 Ocean shader。

**Rationale:**
- 两边光照模型和后处理链不同，数值同名不等价。
- 直接复制会在本地造成过亮或错误色彩。

---

## What Worked ✅

1. **同一会话 MCP 热替换**
   - `renderdoc-cli` 的 `shaderId` 不跨命令保持，批量导出的哈希与基准一致，结果作废。
   - MCP `shader_build` + `shader_replace` 在同一 RenderDoc 会话中有效，并通过哈希确认输出变化。

2. **按分量输出定位问题**
   - refraction/reflection/lit/fresnel/normal slope 分离后，能看出不是单纯 water_color 或 UV 问题。
   - 关键结论：细节在 normal/fresnel 中存在，但没有被亮度/反射/后处理链正确显现。

---

## What Didn't Work ❌

1. **只翻转 `water_color` 南北 UV**
   - 结果：出现大面积暗带。
   - 结论：UV 方向可能仍要单独核对，但不能当成颜色和细节差距的主修复。

2. **只切 CK3 normal/flow 取样路径**
   - 结果：细节略有提升，但 final 仍接近均匀色块。
   - 结论：normal path 不是唯一瓶颈；反射/高光/后处理承载不足才是更大的差距。

3. **禁用 response 后直接走当前本地 post**
   - 结果：最终水面明显变暗。
   - 结论：当前本地 post 链路不能像 CK3 mapface 那样把水面 raw 结构提出来。

---

## Next Session

### Immediate Next Steps
1. 设计源码修复方向：停止扩大 Ocean-only response，转向“源路径真实分量 + 正式 post/mapface 路径”的方案。
2. 热替换验证更接近 CK3 的非 trick 候选：
   - 保持当前光照强度模型，不复制 CK3 `SunIntensity` / exposure。
   - 优先验证 reflection/specular/IBL 与 fresnel/normal 的耦合是否能让现有 normal 细节显现。
   - 如需最终提亮，应放在正式后处理链路，而不是 Ocean shader 内做 display response。
3. 若开始改 SDSL，必须走 `stride-shader-asset-workflow`：shader key 生成、StrideCleanAsset、StrideCompileAsset、build/tests。

### Questions to Resolve
1. 本地是否要实现一个正式 mapface-like post pass，还是扩展现有 final pass 的色调映射资源绑定？
2. Ocean response 是否应完全移除，还是临时保留为调试参数但默认关闭？
3. `water_color` UV 方向要按世界坐标、贴图资产、CK3 vertex UV 三者重新做单独验证。

---

## Session Statistics

**Source Files Changed:** 0
**Temporary Files Changed:** `tmp/renderdoc/**`
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 不要继续把 CK3 final 颜色硬塞进 Ocean shader。
- 不要复制 CK3 亮度/曝光/太阳强度参数；它们属于不同后处理/光照链。
- 本地 `debug.rdc` 当前关键 EID：Ocean 280，final 3445。
- CK3 意大利关键 EID：water 490，mapface final 1263。
- MCP 热替换有效；`renderdoc-cli shader-build` 得到的 shaderId 不能跨命令使用。

**Gotchas for Next Session:**
- `water_color` 翻转不是主修复，单独翻转会产生暗带。
- `normal/flow` 取样改善有限，不能解决最终缺细节。
- 当前可见颜色主要来自 Ocean response，真实 reflection/refraction/lit 分量仍偏弱。

---

## Links & References

- Local capture: `C:\Users\Redwa\Desktop\debug.rdc`
- CK3 Taiwan capture: `C:\Users\Redwa\Desktop\ck3-ocean-tw.rdc`
- CK3 Italy capture: `C:\Users\Redwa\Desktop\ck3-cocean-ltaly.rdc`
- CK3 water shader: `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\FX\jomini\jomini_water_default.fxh`
- CK3 shared water helper: `E:\SteamLibrary\steamapps\common\Crusader Kings III\jomini\gfx\FX\jomini\jomini_water.fxh`

