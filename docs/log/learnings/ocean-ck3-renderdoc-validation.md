# Ocean CK3 RenderDoc 验证反模式

**Topic**: Ocean 与 CK3 海面视觉对齐的 RenderDoc 判据
**Date**: 2026-06-25
**Related Sessions**: [2026-06-25-ocean-ck3-renderdoc-hot-validation](../2026/06/25/2026-06-25-ocean-ck3-renderdoc-hot-validation.md)

---

## Problem / Context

- Ocean 与 CK3 海面颜色、区域差异、水面细节存在明显差距。
- CK3 和本项目的光照模型、HDR raw 输出、final post 链路不同，不能把 CK3 的亮度/曝光参数机械复制到本地。
- 之前的 Ocean-only display/close/far response 能把最终颜色推近截图，但它会压平区域差异和水面细节，属于不应继续扩大的补偿路径。

---

## Solution / Pattern

对 Ocean/CK3 视觉差距做 RenderDoc 验证时，按以下顺序拆分：

1. 定位同类区域和同类 draw：
   - 本地 Ocean draw / final pass。
   - CK3 water draw / mapface final pass。
2. 先用 hot replacement 验证，不直接改 SDSL。
3. 分量输出：
   - refraction
   - reflection
   - fresnel
   - lit water
   - water_color map
   - normal slope
4. 区分三类问题：
   - 源路径分量是否有结构。
   - 这些结构是否被 reflection/specular/refraction 承载。
   - final post 是否把结构提出来或压掉。

---

## Key Insights

### 1. CK3 的最终海面不是单一 Ocean 色彩映射
- CK3 water draw 先输出包含 refraction/reflection/normal/fresnel 的 raw。
- Mapface final pass 再通过 Tony LUT、ColorCube、FogBlur、RestoreBloom 等路径形成最终显示。
- 因此“把 Ocean shader 输出硬拉到截图颜色”不是 CK3 路径。

### 2. 本地当前可见颜色主要来自 Ocean response
- 禁用 Ocean-only response 后，本地最终海面明显变暗。
- 分量诊断显示 normal/fresnel 有结构，但 reflection/refraction/lit water 偏暗、低对比，不能承载最终可见浪纹。
- 继续调 `_OceanDisplay*` / `_OceanCloseWaterColor*` / `_OceanFarMap*` 会进一步压平区域差异。

### 3. `water_color` UV 翻转不能当成主修复
- 只翻转 `water_color` 南北 UV 会产生大块暗带。
- UV 方向仍可独立验证，但不能用它解释全部颜色和细节差距。

### 4. 细节修复优先调源路径承载，不继续扩展 response
- 2026-06-25 对 `debug.rdc` Ocean EID 280 / final EID 3445 做热替换后，保守有效候选是 `_WaterReflectionNormalFlatten=0.6`、`_WaterReflectionIntensity=0.25`、`_WaterSpecular=0.02`、`_WaterGlossScale=0.4`。
- `_WaterFlowNormalScale` 保持 `0.025`；试探 `0.04` 没有比 v6 带来更好细节，还会增加浪纹尺度/速度漂移风险。
- 更激进的 reflection/specular/gloss 候选能继续增加细节，但 final mean 漂移更明显，先不落地。

---

## When to Use

- 比较本地 Ocean 与 CK3/Vic3 等参考项目的水面视觉差异。
- 用户要求先 RenderDoc 热替换验证，而不是直接改 shader。
- 需要判断问题属于材质参数、normal/flow、reflection/specular、refraction，还是 post 链路。

---

## When NOT to Use

- 确定性 CPU 逻辑 bug；这类问题应优先测试或代码级调试。
- 已经明确是资源加载、shader 编译、cbuffer 绑定错误；这时先修工程/绑定链路。

---

## Common Mistakes

### ❌ Mistake 1: 复制 CK3 亮度参数
**What to avoid:**
- 直接搬 `SunIntensity`、fixed exposure、contrast、tonemap 参数到本地 Ocean。

**Why it's bad:**
- 两边光照和后处理链不等价，同名参数不代表同一能量尺度。
- 容易造成过亮、过黑或颜色错误。

**Correct approach:**
- 只把 CK3 参数当作链路差异证据。
- 在本地光照模型下重新验证 reflection/specular/post 的能量分配。

### ❌ Mistake 2: 用 Ocean-only response 匹配最终截图
**What to avoid:**
- 在 Ocean shader 内用 display response、close/far response 把最终颜色硬映射到截图。

**Why it's bad:**
- 它绕过 CK3 的真实 mapface/post 路径。
- 会压平不同海域的 water_color 区域差异和高频浪纹。

**Correct approach:**
- 先让 Ocean raw 的真实分量有可用结构。
- 最终显示差异放到正式 post/mapface-like 链路处理。

### ❌ Mistake 3: 只凭一张区域截图判断 UV 翻转
**What to avoid:**
- 看到南北颜色不对就直接翻转所有 world UV 或只翻 `water_color`。

**Why it's bad:**
- flow/foam/refraction/world position 可能需要不同验证。
- 单独翻 `water_color` 已验证会产生暗带，不能作为颜色主修复。

**Correct approach:**
- 分别验证 `water_color`、flow map、foam map、refraction world UV。
- 用同一地区 CK3 capture 对比，而不是台湾、意大利、远景混用同一采样框。

### ❌ Mistake 4: 用更强 close/far response 补细节
**What to avoid:**
- 通过降低/提高 `_OceanDisplay*`、`_OceanCloseWaterColor*` 或 `_OceanFarMap*` response 强度来制造浪纹、高光或区域差异。

**Why it's bad:**
- response 是低频颜色补偿，继续扩大只会把不同海域推向同一目标色，并压掉真实 reflection/specular/normal 结构。

**Correct approach:**
- 先在 RenderDoc 里热替换完整 Ocean PS，保持 final mean 基本不漂移。
- 优先调 `_WaterReflectionNormalFlatten`、`_WaterReflectionIntensity`、`_WaterSpecular`、`_WaterGlossScale` 这类源路径承载参数。

### ❌ Mistake 5: 用反号补偿 see-through shore mask 的零阈值
**What to avoid:**
- 因为 `_WaterSeeThroughShoreMaskDepth=0` 导致岸边全回到 water color，就把公式改成 `(refractionDepth - _WaterSeeThroughShoreMaskDepth)`。

**Why it's bad:**
- CK3 源码和本项目 River 都使用 `1 - saturate((_WaterSeeThroughShoreMaskDepth - Depth) * sharpness)`，再 `lerp(Color, WaterColorMap, mask)`。
- 这个公式要求 shore depth 是正阈值：浅水先走 see-through，超过阈值再回到 water color。
- 反号会把坡度翻过来，形成“贴岸是水色，稍深处才透底”的反向过渡。

**Correct approach:**
- 保留 CK3 公式：`(_WaterSeeThroughShoreMaskDepth - refractionDepth)`。
- 给 Ocean 一个正的 `_WaterSeeThroughShoreMaskDepth`；2026-06-25 `debug.rdc` 热替换验证 `3.0` 已能让岸边先透底、外侧回水体颜色。
- 如果岸边又被纯 water color 覆盖，先检查阈值是否为 `0`，不要先改公式方向。

---

## References

- Session log: `docs/log/2026/06/25/2026-06-25-ocean-ck3-renderdoc-hot-validation.md`
- Session log: `docs/log/2026/06/25/2026-06-25-ocean-source-detail-calibration.md`
- Session log: `docs/log/2026/06/25/2026-06-25-ocean-see-through-transition-direction.md`
- Superseded failed attempt: `docs/log/2026/06/25/2026-06-25-ocean-see-through-shore-mask-fix.md`
- Temporary diagnostics: `tmp/renderdoc/hot-ab/diagnostics-20260625/component-sheet.png`
- Temporary variants: `tmp/renderdoc/hot-ab/mcp-variants-20260625/*.png`

---

*Learning Document Version: 1.0*
