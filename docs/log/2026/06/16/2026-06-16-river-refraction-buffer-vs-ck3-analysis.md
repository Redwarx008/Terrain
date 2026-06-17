# River refraction buffer 与 CK3 对比分析
**Date**: 2026-06-16
**Session**: river-refraction-buffer-vs-ck3-analysis
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 继续对比 `C:\Users\Redwa\Desktop\debug.rdc` 和 `C:\Users\Redwa\Desktop\ck3-river.rdc`，确认“当前河床颜色看不到 / 与 CK3 差异大”究竟卡在 bottom pass、surface pass，还是 refraction buffer 本身。

**Success Criteria:**
- 用 RenderDoc 给出 pass 级证据，而不是继续凭截图猜测。
- 缩小到可以直接落地的修复顺序。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-bottom-hotreplace-validation.md](./2026-06-16-river-bottom-hotreplace-validation.md)
- See: [2026-06-16-river-bottom-ck3-332-vs-debug-184-contrast-analysis.md](./2026-06-16-river-bottom-ck3-332-vs-debug-184-contrast-analysis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 用户反馈当前 bottom pass 虽然有输出，但整体和 CK3 仍差很大。
- 先前已经知道当前 surface 是简化版，但还不清楚最终差异主要发生在哪一层。

---

## What We Did

### 1. 重新对齐当前 capture 的关键 draw
**Files Changed:** 无

- 打开当前 `debug.rdc`，确认关键链路仍是：
- `157`：scene seed
- `184`：river bottom
- `213`：river surface
- 确认 `184` 写 `ResourceId::7770`，`213` 读同一张作为 `RefractionTexture_id52`。

### 2. 重算当前 bottom 混合权重，修正上一轮过度归因
**Files Changed:** 无

- 当前 `184`：
- `(420,250)`，`v2.y≈0.909`，混合权重 `k≈0.3005`
- `(420,255)`，`v2.y≈0.822`，混合权重 `k≈0.9456`
- `(420,260)`，`v2.y≈0.735`，混合权重 `k≈1.0000`
- CK3 `332`：
- `(540,468)`，`v1.y≈0.671`，`k≈0.9996`
- `(550,468)`，`v1.y≈0.835`，`k≈0.6250`
- `(554,468)`，`v1.y≈0.906`，`k≈0.2501`
- `(555,468)`，`v1.y≈0.927`，`k≈0.1618`

**Conclusion:**
- “当前 bottom 权重整体比 CK3 更低”这个说法不成立。
- 更准确的说法是：
- 当前在 `v≈0.82` 以内保留得更多，但最外沿掉得很陡。
- CK3 的 outer bank 也会快速掉权重。
- 因此不能再把问题单独归结为 `ColorTarget1 alpha`。

### 3. 直接热替换 current surface，验证它在岸边到底输出什么
**Files Changed:** 无

- 对当前 `213` 做最小热替换：直接输出 `RefractionTexture` 屏幕采样结果。
- 样本点：
- `(840,500)`：raw refraction `≈ (2.102, 2.343, 0.700)`
- `(840,510)`：raw refraction `≈ (0.328, 0.349, 0.186)`
- `(840,520)`：raw refraction `≈ (0.163, 0.154, 0.126)`
- 与原 `213` 对比：
- `(840,500)` 原始 shader output `≈ (2.096, 2.336, 0.698, 0.602)`
- `(840,510)` 原始 shader output `≈ (0.327, 0.348, 0.185, 1.0)`
- `(840,520)` 原始 shader output `≈ (0.161, 0.158, 0.127, 1.0)`

**Conclusion:**
- 当前岸边 `213` 基本就是把 raw `RefractionTexture` 原样透出来。
- 这说明当前“看不到 CK3 河床颜色”的主问题不在 surface 叠色，而在 surface 读到的 refraction/bottom 缓冲本身。

### 4. 用同样方法验证 CK3 的 raw refraction sample
**Files Changed:** 无

- 对 CK3 `460` 做最小热替换，但注意其 `RefractionTexture` 在 `t11`，不是 `t0`。
- 样本点：
- `(1100,936)`：raw refraction `≈ (0.1031, 0.0696, 0.0434)`
- `(1108,936)`：raw refraction `≈ (0.0581, 0.0458, 0.0373)`
- `(1110,936)`：raw refraction `≈ (0.0475, 0.0418, 0.0402)`

**Conclusion:**
- CK3 近岸 raw refraction 是暖棕、低亮度的河床色。
- 当前对应位置的 raw refraction 明显更亮、更灰、更偏绿，差距在 refraction buffer 层就已经形成。

### 5. 对源码与资源导入做根因交叉检查
**Files Changed:** 无

- 当前 `RiverSurface.sdsl` 与 CK3 `jomini_river_surface.fxh` / `jomini_water_default.fxh` 对比：
- 当前是简化版，缺少 CK3 的完整 water lighting / shadow / cloud / FoW / cubemap 路径。
- 当前 `RiverBottom.sdsl` 与 `snapshot_332/shader_ps.txt` 对比：
- 缺少 CK3 bottom 的 parallax、shadow map、`CubemapIntensity/CubemapYRotation` 语义和更完整的 material/light 结构。
- 当前资源描述检查：
- `Terrain.Editor/Assets/River/Bottom/bottom-diffuse.sdtex`
- `Terrain.Editor/Assets/River/Water/water-color.sdtex`
- 两者都设成了 `UseSRgbSampling: false`
- 当前 seed 路径检查：
- `RiverRenderFeature` 使用 `ImageScaler(SamplingPattern.Linear)` 把 scene color 缩到 half-res `BottomColor`
- `ImageScaler` 在 Stride 源码里是 GPU image effect，不是 CPU 缩放
- 当前 surface 采样 refraction 也使用 `LinearClamp`

### 6. 落地单变量修复：颜色贴图改回 sRGB 采样
**Files Changed:** `Terrain.Editor/Assets/River/Bottom/bottom-diffuse.sdtex`, `Terrain.Editor/Assets/River/Water/water-color.sdtex`

- 仅修改两张颜色贴图：
- `bottom-diffuse.sdtex`: `UseSRgbSampling: false -> true`
- `water-color.sdtex`: `UseSRgbSampling: false -> true`
- 不改 normal/properties/depth/foam 等 data map，避免一次改多个变量。

### 7. 构建验证
**Files Changed:** 无

- 运行：
- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
- 结果：
- C# 编译通过
- Stride AssetCompiler 成功
- `RootAssets` 内容构建成功
- 仅存在现有 NuGet / 未使用字段 / WinForms DPI 警告，无新错误

### 8. 自动截帧验证尝试
**Files Changed:** 无

- 使用 `capture_frame` 自动启动 `Bin/Editor/Debug/win-x64/Terrain.Editor.exe`
- 编辑器进程确实启动成功，但 RenderDoc 最终未生成 `.rdc`
- 因此这轮还没有得到“改完 sRGB 后”的新 capture 证据

---

## Conclusions

1. **当前问题首先发生在 refraction/bottom 缓冲，不是 surface 最后一步才出错。**
- 当前岸边 `213` 几乎等于 raw `RefractionTexture` sample。

2. **与 CK3 的关键差异，是当前 raw refraction 在近岸明显更亮、更灰。**
- current `(840,510)` `≈ (0.328, 0.349, 0.186)`
- ck3 `(1108,936)` `≈ (0.058, 0.046, 0.037)`

3. **导致 raw refraction 跑偏的根因至少有两层：**
- 当前 seed 地表 HDR 亮度远高于 CK3，outer bank 低权重像素会被 seed 直接冲亮。
- 当前颜色贴图导入把 `bottom-diffuse` / `water-color` 也按线性数据贴图处理，极可能破坏了 CK3 颜色语义。

4. **另外还有一个放大污染的工程实现差异：**
- 当前 `ImageScaler(SamplingPattern.Linear)` 先把 full-res scene 线性缩到 half-res bottom RT，surface 又对这张图线性采样。
- 对窄河岸边，这会额外把周围 terrain 颜色滤进 refraction sample。

5. **本轮已先落地最小修复变量，但还缺新的 RenderDoc 截帧确认效果。**
- 现在代码库状态已经可以重新运行并截帧验证。

---

## What Didn't Work ❌

1. **用最小 HLSL 重写 current bottom，试图只改 `BottomDiffuse` 解码**
- 失败原因：RenderDoc 自建 HLSL 的输入语义没有完整对上当前 bottom shader，导致 `o1` 掉成 0。
- 结论：这版热替换不能用于判断 final，只保留“不要再用不完整输入签名重写 dual-target bottom”这一经验。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新截帧验证 `184` / `213`，优先看 raw refraction sample 是否向 CK3 收敛
2. 若近岸仍被 terrain 污染，再处理 half-res seed/downsample 语义，而不是先调 surface 水色
3. 若需要自动验证，先解决 `capture_frame` 未产出 `.rdc` 的问题

### Questions to Resolve
1. `bottom-diffuse` / `water-color` 改成颜色贴图语义后，当前 raw refraction 是否已接近 CK3？
2. narrow river 的 half-res refraction seed 是否需要改成更保守的 copy/filter 方案？

---

## Session Statistics

**Files Changed:** 4
**Verification:** `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug` 通过；自动 RenderDoc 截帧失败，尚无新 `.rdc`
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前 `213` 岸边几乎等于 raw `RefractionTexture` sample，surface 不是主战场。
- CK3 对应位置的 raw refraction 明显更暖、更暗；当前 refraction buffer 本身就错了。
- 当前资产里 `bottom-diffuse` 和 `water-color` 已改为 `UseSRgbSampling: true`。
- `ImageScaler` 是 GPU image effect，但当前使用的是 `SamplingPattern.Linear`，会放大近岸污染。

**Gotchas for Next Session:**
- CK3 `460` 的 `RefractionTexture` 在 `t11`，不要再按 `t0` 采。
- 不要用没有完全对上输入语义的最小 bottom HLSL 去判断 `o1` / bottom-distance。
