# Stride 河流渲染分层与标准变换链

**Topic**: River rendering subsystem pattern for Stride
**Date**: 2026-06-06
**Related Sessions**: [2026-06-06 河流渲染架构落地与顶点变换修复](../2026/06/06/2026-06-06-1-river-rendering-architecture-and-transform-fix.md), [2026-06-16 河流水面黑色输出修复](../2026/06/16/2026-06-16-river-surface-black-output-fix.md), [2026-06-16 河流 bottom pass 暗色根因诊断](../2026/06/16/2026-06-16-river-bottom-pass-refraction-scale-fix.md), [2026-06-16 河流水面 bank fade 黑边根因诊断](../2026/06/16/2026-06-16-river-surface-bank-fade-renderdoc-diagnosis.md), [2026-06-16 河流水面 waterFade 热替换验证与 MapExtent 修正](../2026/06/16/2026-06-16-river-surface-waterfade-hotedit-and-mapextent.md), [2026-06-16 河流 water-color UV 与折射位置修正](../2026/06/16/2026-06-16-river-watercolor-uv-refraction-fix.md), [ADR-014 河流渲染架构](../decisions/adr-014-river-rendering-architecture.md)

---

## Problem / Context

- 河流 mesh 已经能在 editor 侧生成，但正式渲染一度卡在 service-only / 临时预览路径。
- 河流还出现过屏幕空间闪烁和全屏大三角，说明 draw 虽然在跑，但 shader 没有遵守 Stride 标准位置流约定。
- 这类对象同时具备动态 mesh、独立 GPU buffer 生命周期、多 pass 和调试模式需求，不能按普通材质对象处理。

---

## Solution / Pattern

把河流作为独立渲染子系统接入 Stride：

```csharp
RiverRenderingService -> RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature
```

并在 shader 侧回到 Stride 标准 transformation 链：

```csharp
shader RiverSurface : ShaderBase, TransformationWAndVP, RiverVertexStreams, RiverWaterCommon
```

---

## Key Insights

### 1. editor façade 与正式渲染链要分开
- `RiverRenderingService` 适合承担编辑器桥接职责：接收 `RiverSegment`、驱动 mesh 生成、同步显隐。
- 正式 draw 生命周期应放到 `RiverComponent / RiverProcessor / RiverRenderObject / RiverRenderFeature`。

### 2. 多 pass 水体更适合独立 RenderFeature
- 河底 pass、水面 pass、折射中间缓冲和调试 rasterizer 状态属于 render pipeline 责任，不应塞回 service。
- 只有走 render stage / visibility group 正式链路，后续扩展才清晰。

### 3. 位置流问题优先回到 Stride 标准 mixin
- 当 shader 依赖 `PositionWS / PositionH / DepthVS` 时，优先使用 `TransformationWAndVP`。
- 如果 draw 在跑但画面表现为屏幕空间错乱，首先怀疑的是变换链缺失，而不是材质参数。

### 4. 外部 DDS 必须有 Stride asset descriptor
- Stride runtime 内容加载走 asset URL，例如 `River/Water/flow-normal`，不是直接扫描 `.dds` 文件。
- 对 CK3 这类 packed-channel DDS，`.sdtex` 应显式使用 `!ColorTextureType` 和 `UseSRgbSampling: false`，避免 normal-map importer 或 sRGB 转换破坏通道含义。
- 动态 `Content.Load` 的资产必须列入 `.sdpkg` `RootAssets`，否则 shader 反射会显示 texture slot，但实际 GPU 绑定可能为空。
- 完成标准是：源 DDS 存在、`.sdtex` 存在、`RootAssets` 存在、shader key 生成、`StrideCompileAsset` 成功，并且截帧中资源 usage 出现在对应 draw。

### 5. HDR 目标上的水面 alpha 不能沿用低动态范围假设
- RenderDoc pixel history 能直接区分 shader output 和 post-blend color。
- 如果地形 HDR 颜色很高，例如 `[5.1, 3.8, 2.6]`，即使水面 alpha 为 `0.85`，剩余 `15%` 目标颜色仍会把深色水面冲成沙色。
- 中心水面 opacity 应接近 1，边缘/连接处再用 `edgeFade`、`connectionFade` 和作者态 transparency 做过渡。

### 6. DynamicEffect shader 需要显式绑定相机位置
- `Eye` 在一些 Stride material shader 组合中可见，但自定义 `DynamicEffectInstance("RiverBottom"/"RiverSurface")` 不应依赖这个裸全局。
- 如果 shader 需要相机世界坐标，用 `Matrix.Invert(ref renderView.View, out var viewInverse)` 和 `viewInverse.TranslationVector` 在 `RenderFeature` 中显式绑定。
- bottom/surface 这种成对 pass 必须共享同一个 camera parameter，否则 bottom distance packing 和 surface decompression 会不一致。

### 7. 河流宽度流和 CK3 depth/fade 语义不完全等价
- 当前 `RiverMeshService` 写入顶点流的宽度是 `halfWidth / mapExtent`，这是归一化半宽，不是 CK3 shader 中参与 depth / flow UV 的 full-width。
- `RiverBottom` / `RiverSurface` 中用于 CK3 depth、flow scale 的宽度应先还原为 `halfWidth * 2`，否则 `worldDepth` 会系统性偏小。
- CK3 的 raw shore fade 阈值直接套在窄 Stride ribbon 上，会让 `waterFade` 把中心主水色清零，只剩暗 bottom/refraction；水面 shader 应用 cross-section `depthFactor` 提供视觉深度下限。

### 8. 岸边 alpha ramp 必须覆盖 waterFade 黑色 ramp
- `waterFade` 可能先把近岸 RGB 压到 0；如果 `_BankFade` 太窄，`edgeFade` 会在同一区域已经变成 1。
- RenderDoc pixel history 的典型症状是 terrain 先写入亮 HDR 颜色，随后 river surface draw 自己输出 `RGB=(0,0,0), A=1`，不是 blend 后变黑。
- 对当前窄 ribbon，`_BankFade=0.02f` 会让 `UV.y≈0.04` 完全不透明；`0.15f` 这类更宽的 bank fade 能让 alpha 过渡覆盖 near-shore waterFade 黑斜坡。

### 9. 区分“黑边症状修复”和 CK3 bottom-bank 目标
- 早期 RenderDoc hot-edit 证明过：强行让 `waterFade` 跟随 alpha-visible 岸内区域，可以缓解 opaque black edge。
- 但 CK3 参考目标要求岸边能看到 bottom/refraction 河床颜色；如果把 `edgeFade` 反推成 depth floor 长期保留，可能把本该露出的河床改成水色。
- 最终判断顺序应是：先修正 bottom/refraction buffer、water-color UV 和 refraction world-position 采样；只有确认仍是 `waterFade≈0` 且输出高 alpha 黑色时，再考虑 depth-floor 方案。

### 10. 地图尺寸传世界坐标跨度，不传采样数
- 地形世界坐标范围是 `0..width-1` / `0..height-1`，因此 river shader 的 `_MapExtent` 应使用 `max(width - 1, height - 1)`。
- `RiverWidth` 归一化和 `PositionWS / _MapExtent` world UV 必须使用同一尺度。
- SDSL 默认值只是 fallback；RenderDoc 中应从 shader trace 寄存器或 disasm 反推实际绑定值，不要只看默认 `4096.0f`。

### 11. CK3 water-color map UV 必须 Y 翻转并在折射位置重采样
- CK3 `jomini_river_surface.fxh` 先设置 `Params._WorldUV = WorldSpacePos.xz / MapSize`，再执行 `Params._WorldUV.y = 1.0f - Params._WorldUV.y`。
- `jomini_water_default.fxh` 在 refraction 路径里会反解 `RefractionWorldSpacePos`，再用该位置重新采 `WaterColorTexture` 作为 see-through tint。
- RenderDoc 热替换验证显示，同一河面像素未翻转采样约为 `(0.031,0.031,0.031)`，翻转后约为 `(0.180,0.176,0.129)`；未翻转会直接把浅岸/河床染成近黑。

### 12. CK3 bottom 贴图是 tileable world-space UV，不是 ribbon UV
- CK3 `river_bottom.shader` 调用 `CalcRiverBottom`，该路径中 `BottomDiffuse/BottomProperties/BottomNormal` 采样 `WorldUV = Input.WorldSpacePos.xz + WorldSpaceParallax * Input.Width`。
- `Input.UV` 只用于 depth、edge fade、connection fade 和 parallax offset；如果把 Bottom 贴图采样也绑定到 `riverUv`，河床会出现沿河方向的黑色条纹，且无法看到 CK3 参考中的棕色河床。
- RenderDoc 热替换验证：在当前 `debug.rdc` event `184` 中直接输出 `BottomDiffuse.Sample(worldPosition.xz)` 后，河床从黑色纵向条纹变成连续棕色世界锁定纹理；而归一化 `worldPosition.xz / _MapExtent` 基本变成大块平色，说明 Bottom DDS 不是 water-color 那类 map texture。

### 13. 对比 bottom RT 时先比较“river vs seed”的相对对比度
- 当前工程的 bottom pass 是先把 scene color 缩放 seed 到 half-res RT，再用 dual-source blend 写河床；因此“河床看起来很黑”可能不是 bottom shader 输出过低，而是 seed 场景过亮。
- RenderDoc 对比显示：CK3 `332` 在 `UV.y≈0.49` 的河心像素，seed 约 `(0.049, 0.057, 0.024)`，bottom shader 输出约 `(0.118, 0.082, 0.045)`；当前 `184` 在相近 `UV.y≈0.46` 的河心像素，seed 约 `(2.96, 3.55, 0.94)`，bottom shader 输出约 `(0.160, 0.151, 0.108)`。
- 结论是：当前 bottom shader 绝对输出并不比 CK3 更暗，甚至数值更高；视觉上的“黑沟”主要来自 copied scene seed 比 CK3 亮一个数量级以上，以及 IBL/场景光照语义不同。

### 14. bottom 热替换若破坏 RT0 alpha 的 bottom-distance，surface 结果就不能用于颜色归因
- `RiverSurface` 会读取 bottom RT，其中 `RT0.rgb` 是河床/折射底色，`RT0.a` 是给 surface 反解折射位置和深度用的 bottom distance。
- 在 RenderDoc 里热替换 `RiverBottom` 做颜色实验时，如果只顾着改 `o0.rgb`，却没有保持原始 `o0.a` 打包语义，surface pass 的 refraction / water-color / see-through 路径会一起跑偏。
- 实际验证：把 `184` 热替换成“直接输出 `BottomDiffuse`”后，bottom RT 本身明显变亮，但 `213` 的最终水面同时出现与颜色实验无关的大幅变化；根因不是 bottom RGB，而是错误的 `o0.a` 让 surface 误解了 bottom distance。
- 因此用 RenderDoc 热替换验证 bottom 明暗时，优先相信 `184` 的 bottom RT 和 pixel history；除非能精确保留原始 distance packing，否则不要直接用 `213` 的最终图去判断 bottom 颜色逻辑。

---

## When to Use

- 需要为动态生成 mesh 建立独立 GPU buffer 生命周期。
- 需要多 pass、自定义 render stage 或中间缓冲。
- 需要 editor/runtime 桥接，但又不希望编辑器 service 直接承担正式渲染逻辑。

---

## When NOT to Use

- 只是普通静态模型，且无需独立 render stage / 多 pass。
- 只是一次性预览对象，没有长期维护的渲染生命周期需求。

---

## Common Mistakes

### ❌ Mistake 1: 继续停留在 service-only 渲染
**What to avoid:**
- 在 `RiverRenderingService` 里直接管理 GPU buffer、渲染 pass 和 draw。

**Why it's bad:**
- 编辑器逻辑和渲染生命周期耦合，后续扩展多 pass、调试模式时边界会越来越乱。

**Correct approach:**
- 保留 service 作为 façade，把正式渲染链下放到 `Component -> Processor -> RenderObject -> RenderFeature`。

### ❌ Mistake 2: 用输入布局补丁替代 shader 标准变换链
**What to avoid:**
- 在 C# 输入布局侧加入 `POSITION -> POSITION_WS` 特殊映射，或按 reflection 动态拼输入布局来兜底。

**Why it's bad:**
- 这只能掩盖问题，不能替代 shader 中缺失的标准 world/view/projection 变换。

**Correct approach:**
- 让 shader 混入 `TransformationWAndVP`，回到 Stride 标准位置流输出。

### ❌ Mistake 3: 把排障代码留在正式渲染路径里
**What to avoid:**
- 在 `RiverRenderFeature` 长期保留日志、反射分支和临时输入语义映射。

**Why it's bad:**
- 会制造额外噪音，掩盖正式实现的真实边界。

**Correct approach:**
- 根因确认后，恢复固定 `RiverVertex.Layout.CreateInputElements()` 路径。

### ❌ Mistake 4: 只复制 DDS，不创建 `.sdtex`
**What to avoid:**
- 把 CK3 DDS 放进 `Assets/River/` 后就认为 `Content.Load<Texture>("River/...")` 可以工作。

**Why it's bad:**
- 没有 `.sdtex` 时，Stride 内容库没有对应 asset URL；shader 和 C# 绑定看起来完整，运行期仍可能加载不到资源。

**Correct approach:**
- 每张外部 DDS 都提交同目录 `.sdtex`，用 asset compiler 验证 bundle 构建，并用测试锁住 descriptor 存在。

### ❌ Mistake 5: 只看 shader 反射名，误以为贴图已绑定
**What to avoid:**
- 看到 `get_bindings` 里有 `FlowNormalTexture_id44` / `WaterColorTexture_id49` 就认为 GPU 已经绑定了实际 texture。

**Why it's bad:**
- 这些名字来自 shader reflection；如果 C# loader 返回 null，RenderDoc 仍能显示 slot 名，但 resource usage 不会出现实际 texture。

**Correct approach:**
- 用 `get_resource_usage` / pixel debug / 截帧资源表确认 texture resource 在目标 draw 上有 `PS_Resource` 使用。

### ❌ Mistake 6: 在 DynamicEffect shader 里直接使用 `Eye.xyz`
**What to avoid:**
- 在 `RiverBottom.sdsl` / `RiverSurface.sdsl` 这类自定义 DynamicEffect 中直接写 `Eye.xyz`。

**Why it's bad:**
- 运行时 effect compile 会报 `E0237 The variable [Eye] is not defined`；asset compile 未必能提前覆盖这种组合。

**Correct approach:**
- 声明显式 stage 参数，例如 `_CameraWorldPosition`，并在 `RiverRenderFeature.Draw` 中从 `renderView.View` 逆矩阵提取后绑定到 bottom/surface effect。

### ❌ Mistake 7: 用归一化半宽直接驱动 CK3 water fade
**What to avoid:**
- 把 `streams.RiverWidth * _MapExtent` 当成 CK3 `Input.Width`，并直接使用 `1 - saturate((_WaterFadeShoreMaskDepth - worldDepth) * sharpness)`。

**Why it's bad:**
- 对当前窄 ribbon，raw `worldDepth` 低于 CK3 shore fade 阈值，RenderDoc pixel history 会显示 surface draw 自己输出接近黑色，而不是 blend 后变黑。

**Correct approach:**
- shader 内部先把归一化半宽还原为 full-width：`streams.RiverWidth * max(_MapExtent, 1.0f) * 2.0f`。
- `RiverSurface` 的 water fade 应优先使用 refraction buffer 反解出的深度：`effectiveDepth = min(surfaceDepth, refractionDepth)`；如果出现黑岸，再用 RenderDoc 证明是 `waterFade` 问题后单独处理。

### ❌ Mistake 8: 用 diffuse multiplier 掩盖 bottom lighting 缺失
**What to avoid:**
- 看到河流偏黑后，在 `RiverBottom.sdsl` 里加入 `_BottomDiffuseMultiplier` 或把 `bottomDiffuse.rgb * depthTint` 直接乘大。

**Why it's bad:**
- RenderDoc 对比 CK3 event `336` 表明 bottom pass 是 lit material pass：`BottomDiffuse/Normal/Properties` 之后还会经过 direct sun、shadow、diffuse IBL 和 specular IBL。
- 当前 `RiverBottom` 是自定义 `DynamicEffectInstance`，不会自动接入 Stride ForwardLighting；直接调 multiplier 只能补数值，不能补缺失的 shader 输入。

**Correct approach:**
- 先用 pixel history 确认暗色发生在哪个 pass，再对照参考 shader 的绑定和 disasm。
- bottom pass 应显式绑定 environment cubemap，并把 diffuse/normal/properties 作为 material input 进入 lighting 函数；shadow 暂时缺失时也应作为 lighting fallback 输入，而不是混入 albedo multiplier。

### ❌ Mistake 9: 让 near-shore waterFade 黑色区域变成完全不透明
**What to avoid:**
- 把 `_BankFade` 设得比 waterFade 的黑色过渡区更窄，例如 `0.02f`，导致 `UV.y≈0.04` 已经 `edgeFade=1`。

**Why it's bad:**
- Pixel history 会显示 surface pass 自己输出 `RGB=(0,0,0), A=1` 覆盖亮地形；这不是 bottom 亮度或 blend state 能正确修复的问题。

**Correct approach:**
- edge alpha ramp 必须覆盖 near-shore waterFade 黑斜坡；当前默认 `_BankFade=0.15f` 与岸边 foam mask 宽度一致，并能避免 opaque black edge。

### ❌ Mistake 10: 把 waterFade depth-floor 当成最终 CK3 方案
**What to avoid:**
- 只因为黑岸变亮，就长期保留 `edgeFade * _WaterFadeShoreMaskDepth` 或反函数 `edgeVisibleDepth` 这类 depth floor。
- 在没有检查 water-color UV、refraction world-position 和 bottom RT alpha/RGB 前，把所有暗岸问题都归咎于 `waterFade`。

**Why it's bad:**
- CK3 参考岸边能看到 bottom/refraction 河床；depth floor 可能把本该露出的 bottom 改成水色。
- 新截帧证明另一类根因是 water-color 未翻转采样命中近黑区域：同一像素未翻转约 `(0.031,0.031,0.031)`，翻转后约 `(0.180,0.176,0.129)`。

**Correct approach:**
- 先用 RenderDoc pixel history/debug_pixel 证明暗色来自哪个阶段：bottom RT、water-color tint、refraction see-through、waterFade 或最终 alpha blend。
- 对 CK3 surface，map UV 需要 Y 翻转，refraction tint 需要在 `RefractionWorldSpacePos` 重新采 `WaterColorTexture`。

### ❌ Mistake 11: 把 SDSL 默认 `_MapExtent` 当作运行时事实
**What to avoid:**
- 看到 `stage float _MapExtent = 4096.0f` 就判断运行时没传地图尺寸。

**Why it's bad:**
- `RiverRenderFeature` 会绑定 `RiverSurfaceKeys._MapExtent`；当前 capture 的 shader trace 反推实际值是 `18432.0f`。
- `get_cbuffer_contents` 在这些 capture 上可能返回全 0，不能作为唯一证据。

**Correct approach:**
- 同时检查 C# binding、mesh data source、shader trace 寄存器值；`RiverMeshService` 应传世界坐标跨度 `max(width - 1, height - 1)`。

### ❌ Mistake 12: 用 `riverUv` 或归一化 map UV 采样 BottomDiffuse
**What to avoid:**
- `BottomDiffuseTexture.Sample(..., riverUv)`、`riverUv * _BottomUvScale`，或 `worldPosition.xz / _MapExtent`。

**Why it's bad:**
- CK3 bottom diffuse/normal/properties 是随世界坐标平铺的 tileable texture，不是沿河 ribbon 拉伸的纹理，也不是整图 water-color map。
- 错误 UV 会让河床先在 bottom pass 输出阶段就变成黑线或大块平色，后续 surface/refraction 无法恢复 CK3 的岸边河床颜色。

**Correct approach:**
- Bottom 贴图用 `worldPosition.xz` 采样；depth/alpha 仍使用 `riverUv.y`。
- 修改前后必须用 RenderDoc hot-replace 或 pixel history 验证 bottom RT 本身，而不是只看最终水面截图。

### ❌ Mistake 13: 只看 bottom shader 的绝对 RGB，忽略 scene seed
**What to avoid:**
- 看到河床在最终 RT 里发黑，就直接把原因归结为 bottom shader 太暗。

**Why it's bad:**
- bottom RT 是 scene seed + bottom draw 的组合结果；如果 seed 地表已经是 HDR 亮沙地，哪怕 bottom shader 输出与 CK3 相当，也会因为相对对比度过大而看成黑沟。
- CK3 和当前工程如果使用不同的 terrain/albedo/shadow 环境，单看 `o0.rgb` 不能解释视觉差距。

**Correct approach:**
- 先用 pixel history 同时记录 seed event 和 bottom event 的 `postMod/shaderOut`。
- 把“河床 RGB 是否过暗”和“河床相对周围地表是否过暗”拆开分析；前者看 shader，后者看 scene seed 与 lighting 语义。

### ❌ Mistake 14: 用破坏了 bottom-distance 的热替换结果去判断 surface 颜色
**What to avoid:**
- 在 `RiverBottom` 热替换里随手写一个 `o0.a` 常数、`length(PositionWS)`，或者任何与原始压缩距离不一致的值，然后拿 `213` 的最终图判断“bottom 颜色是否正确”。

**Why it's bad:**
- `RiverSurface` 依赖 bottom RT alpha 反解折射世界位置和水深；distance packing 一旦错，surface 输出的明暗、色相、岸边过渡都会一起失真。

**Correct approach:**
- 如果只是验证 bottom 颜色逻辑，优先比较 `184` 的 `shaderOut/postMod/export_render_target`。
- 只有在能保持原始 `o0.a` 语义时，才用 `213` 的最终图做结论。

### ❌ Mistake 15: 把颜色贴图也按线性数据贴图导入
**What to avoid:**
- 给 `bottom-diffuse.dds`、`water-color.dds` 这类颜色贴图的 `.sdtex` 也统一写 `UseSRgbSampling: false`，和 normal/properties/depth/foam-noise 一起一刀切处理。

**Why it's bad:**
- CK3 的 `BottomDiffuse` 和 `WaterColorTexture` 在 shader 里承担的是 albedo / tint 语义，不是 packed data 语义。
- 这类颜色贴图如果不做 sRGB 解码，shader 里拿到的是偏亮、偏灰、偏洗掉的中间调；RenderDoc 对比会表现为当前 refraction/bottom 缓冲在近岸显著高于 CK3，同位置更难出现暖棕河床。

**Correct approach:**
- 区分 color map 和 data map：
- `bottom-diffuse`、`water-color` 应优先按颜色贴图语义导入并验证是否需要 sRGB sampling。
- `bottom-normal`、`bottom-properties`、`bottom-depth`、`flow-normal`、`foam-map`、`foam-noise` 这类 packed data 继续保持线性采样。
- 修正前后要用 RenderDoc 直接比 `184`/`332` 的 raw refraction sample，而不是只看最终截图。

### ❌ Mistake 16: 忽略 half-res refraction seed 的线性缩放污染
**What to avoid:**
- 只盯 shader 公式，不检查 full-res scene 是如何 seed 到 half-res refraction 链路里的。

**Why it's bad:**
- 如果把 scene seed 和 working bottom/refraction buffer 混成同一张纹理，RenderDoc 很难区分“场景种子”与“bottom pass 结果”。
- 如果 surface 再对 refraction 使用线性采样，窄河岸边缘的 terrain 亮色会在“下采样一次 + 上采样一次”后提前泄漏进河床颜色；即使 bottom pass 自己写了河床，screen-space sample 也可能已经被周围地表抬亮。
- `ImageScaler` 在 Stride 里是 GPU image effect，不是 CPU 缩放；这里的问题是线性滤波语义，不是 CPU 性能。

**Correct approach:**
- 先用 RenderDoc 热替换证明 surface 岸边是否几乎等于 raw `RefractionTexture` sample。
- 如果成立，再追 seed/downsample/upsample 这条链，而不是继续调 surface 水色。
- 让 `SceneSeedColor` 与 `BottomColor` 分离：先把 scene seed 写进 `SceneSeedColor`，再 `CopyRegion` 到 `BottomColor`，最后让 surface 用 `PointClamp` 读取 working refraction buffer。

### ✅ Pattern 17: CK3 河床要先对齐 Tangent-UV + Parallax 语义
**What to do:**
- 看到河岸发黑、河床像线条或大块平色时，先检查 `RiverBottom` 是否已经切到 tangent-UV + steep parallax + `BottomNormal.b` depth shaping，而不是继续调亮度参数。

**Why it works:**
- CK3 的河床颜色资源与本仓库导入的 DDS 一致；多数“看不到河床颜色”的问题来自 shader 采样语义错误，而不是贴图本身错误。
- 如果 bottom 仍直接按 `worldPosition.xz` 或归一化 map UV 去驱动主采样，很容易得到黑色条纹、过度平铺或根本不像 CK3 的岸边河床。

**Correct approach:**
- 先在 `RiverBottom` 里确认 `CalcBottomDepth`、`CalculateParallaxOffsetSteep`、`CalcParallaxedBottomUvs`、显式 bank fade 和分量式 `bottomWorldPosition` 都已接通。
- 只有在这些语义都对齐后，再继续看 lighting、scene seed 或 surface water-color。

### ❌ Mistake 18: 看到 CK3 源码里存在 `CalcRiverBottomAdvanced`，就默认实际 capture 也走 advanced 路径
**What to avoid:**
- 只因为 `jomini_river_bottom.fxh` 里有 `CalcRiverBottomAdvanced`，就直接把当前实现、测试和 spec 全部锁死到 `tangentUv + advanced parallax`。

**Why it's bad:**
- CK3 同一份 shader 源里同时存在 `CalcRiverBottom` 与 `CalcRiverBottomAdvanced` 两条 bottom 路径。
- `C:\\Users\\Redwa\\Desktop\\ck3-river.rdc` 的实际 bottom draw（代表像素命中 `336`）在 disasm 上明确表现为：
- 先用 `worldPos.xz + worldSpaceParallax * width` 生成 `worldUv`
- `BottomDiffuse / BottomProperties / BottomNormal` 都用 `worldUv` 采样
- 主采样链路里没有实际参与的 `UV.x`
- 如果不先用 capture disasm / input signature 验证真实运行分支，就可能把一个已经验证过的 world-UV bottom 路径，又被后续 parity 计划和测试错误地覆盖回 advanced `tangentUv` 路径。

**Correct approach:**
- 当参考项目同一 shader 文件里存在多个分支实现时，移植优先级是：
- 先看当前 capture 的 disasm / input signature / bound resources
- 再用源码定位对应函数体
- 最后才把结论写进项目测试和 spec
- 如果 capture 与既有 spec 冲突，优先修 spec，不要先为旧 spec 补更多实现。

### ✅ Pattern 18: Scene Seed 与 Working Refraction Buffer 必须分离
**What to do:**
- 保留独立的 half-res `SceneSeedColor` 保存下采样场景种子，再复制到 `BottomColor` 作为 bottom pass 的 working refraction buffer。

**Why it works:**
- 同一张纹理同时承担“scene seed”与“bottom 结果”会让 RenderDoc 分析变得含糊，也更容易把 downsample/filter 污染误判成 shader 着色问题。
- 分离之后，bottom pass 的写入边界、surface 的 refraction 读取路径，以及河岸颜色串扰来源都能独立验证。

**Correct approach:**
- `SceneSeedColor` 只负责 scene downsample。
- `BottomColor` 只负责承接 `CopyRegion` 结果与后续 dual-source bottom pass。
- `RiverSurface` 对 refraction 使用 `PointClamp`，避免把河岸附近的 terrain 颜色再次线性模糊进水体。

### ❌ Mistake 17: 在像素着色器的动态 parallax 循环里继续用 `Texture.Sample`
**What to avoid:**
- 在 `RiverBottom` 这类 steep parallax 搜索循环里，用 `Texture.Sample(...)` 做循环内的 depth/normal 采样，同时循环次数又依赖视角或材质参数。

**Why it's bad:**
- `Sample` 依赖隐式梯度；D3D11 像素着色器遇到“带隐式梯度采样的可变迭代循环”时，会强行尝试展开循环。
- 一旦循环上界或 `break` 条件不够静态，编译器很容易报 `X3570 gradient instruction used in a loop with varying iteration` 和 `X3511 unable to unroll loop`，运行时就会以 `Could not compile shader` 失败。

**Correct approach:**
- 在循环外先算好 `ddx/ddy`，循环内改用 `SampleGrad(...)`。
- 如果只是初始化临时向量，避免再写 `float4(0.0f)` 这种单参数构造；显式写成四分量，减少 HLSL 前端兼容性问题。

### ✅ Pattern 19: 多 draw 共用同一 RT 的 pass，要看“最后一个 draw 的 RT 状态”
**What to do:**
- 当一个 river pass 由多个 draw 连续写入同一张 RT 时，先用 `get_resource_usage` 和 `pixel_history` 找出整条 pass 的 draw 组，再用该组最后一个 draw 导出 RT。

**Why it works:**
- `184 -> 197 -> 210 -> 223` 这类序列不是 4 个独立 pass，而是同一 bottom pass 的 4 个分段 draw；`252 -> 270 -> 288 -> 306` 同理属于同一 surface pass。
- 如果直接把首个 draw 当成“整条 pass 的结果”，会把“首段 river section 的局部输出”误判成“完整 bottom/surface RT”，后续 pixel sample、导图和 CK3 对位都会错位。
- CK3 参考帧同样如此：bottom 组应看 `338`，surface 组应看 `466`，而不是只盯 `332` / `460`。

**Correct approach:**
- 先查 RT usage：确认哪些 event 以 `ColorTarget` 写同一张 RT，哪些 event 以 `PS_Resource` 读取它。
- 导整条 pass 的最终 RT 时，用该组最后一个 draw：当前 capture 的完整 bottom/surface 分别是 `223` / `306`，CK3 分别是 `338` / `466`。
- 做 shader hot-replace 时，仍可挂在组内任一 draw 上替换同一 shader；但验证整条 pass 结果时，必须回到最后一个 draw 导图。

### ❌ Mistake 20: 让 bottom pass 继续吃专用 river reflection cubemap，而不是 scene skybox
**What to avoid:**
- 在 `RiverRenderFeature.BindRiverTextures` 里，把 `River/Environment/reflection-specular` 同时绑定给 bottom 的 `EnvironmentMapTexture` 和 surface 的 `ReflectionSpecularTexture`。

**Why it's bad:**
- RenderDoc 热替换表明 current bottom 的能量主要来自 IBL，而不是直射太阳；当 bottom cubemap 来源偏暗或语义错误时，河床会整体落在近黑量级。
- surface 的 `reflection-specular` 资源承担的是水面反射/高光变化语义，不等于 editor scene 的环境光 cubemap；把两者混成一个来源，会让 bottom lighting 和 scene skybox 脱节。

**Correct approach:**
- bottom 的 `EnvironmentMapTexture` 优先绑定 editor scene 的 `Skybox texture`。
- surface 继续绑定 `reflection-specular`，只负责水面反射/高光变化。
- 如果 skybox 资源缺失，再允许 bottom 回退到 river reflection cubemap，而不是默认把它当主来源。

### ✅ Pattern 20: bottom lighting 控制量必须走 `Settings -> RenderObject -> RenderFeature`
**What to do:**
- 让 `_BottomNormalStrength`、`_BottomSunDirection`、`_BottomSunColor`、`_BottomSunIntensity`、`_BottomEnvironmentIntensity`、`_BottomSpecularIntensity` 先进入 `RiverRenderSettings`，再经 `RiverRenderObject.ApplySettings(...)` 和 `RiverRenderFeature.ApplyBottomParameters(...)` 绑定到 shader。

**Why it works:**
- 这样可以把 RenderDoc 热替换里验证过的 lighting 参数，稳定地变成运行时可控输入，而不是继续依赖 shader 默认值。
- bottom 明暗问题如果只靠 shader 内默认常量，会让 scene light、skybox 和 bottom pass 永远脱钩。

**Correct approach:**
- `RiverRenderSettings` 存作者态/场景默认值。
- `RiverRenderObject` 在 mesh snapshot 生命周期内缓存这些值。
- `RiverRenderFeature` 在 draw 前统一绑定，确保 bottom/surface 和 camera parameter 一样都吃到显式输入。

### ❌ Mistake 21: 用两次 `SetTexture` 把 bottom skybox 绑定自己覆盖掉
**What to avoid:**
- 在 `RiverRenderFeature.BindRiverTextures` 里先调用一次
- `SetTexture(..., RiverBottomKeys.EnvironmentMapTexture, riverResources.BottomEnvironment)`
- 再无条件调用一次
- `SetTexture(..., RiverBottomKeys.EnvironmentMapTexture, riverResources.ReflectionSpecular)`

**Why it's bad:**
- `SetTexture` 只是“如果 texture 非 null 就直接写参数”，第二次调用会无条件覆盖第一次绑定。
- RenderDoc 会表现为：bottom pass 和 surface pass 同时读取同一张 `reflection-specular` cubemap，看起来像“已经加载了 skybox”，但实际 shader 根本没有吃到它。

**Correct approach:**
- 先在 CPU 侧解析 fallback：`riverResources.BottomEnvironment ?? riverResources.ReflectionSpecular`
- 然后只绑定一次 `EnvironmentMapTexture`。
- 验证时不要只看 `get_bindings` 的 slot 名称，要再查 cubemap resource usage：正确状态下 bottom 应读 scene skybox 的预滤波 cubemap，surface 才读 river reflection cubemap。

### ✅ Pattern 21: `worldUv` 和 `tangentUv` 的 diffuse-only 热替换都暗时，先修 bottom lighting energy
**What to do:**
- 在 RenderDoc 里对同一颗代表像素至少做三组 bottom hot-edit：`tangent_diffuse_only`、`worlduv_diffuse_only`、`lighting_x3`。
- 先量化 bottom/surface 代表像素，而不是只看导图主观印象。

**Why it works:**
- 如果 `tangent_diffuse_only` 和 `worlduv_diffuse_only` 都把 bottom 压到原亮度的大约一半，而 `lighting_x3` 能把 bottom 直接抬到约 `3x`、surface 只小幅跟着变亮，那么主矛盾就是 bottom lighting energy，不是 UV 分支。
- 这种情况下继续先追 `worldUv` / `tangentUv` 路径，只会把时间花在二级问题上。

**Correct approach:**
- 先用 hot-edit 证明“采样路径变体”和“最终 lighting energy”谁的影响量级更大。
- 如果只有 `lighting_x3` 接近 CK3，就优先在 `RiverBottom` 的最终 lit color 上做最小能量校准，再决定是否继续回头重写 UV 路径。

### ✅ Pattern 22: 用 seed-alpha 常数热替换区分“bank-edge 泄漏”和“主河道主体色”
**What to do:**
- 在 seed pass 上做最小 hot-edit：保留原 RGB，只把 alpha 从 `0` 强制改到一个 CK3 量级的非零值，例如 `50.0f`。
- 同时比较三类证据：
- `157` 自身像素是否真的从 `a=0` 变成了非零
- `184` bottom RT 的河道中心 RGB 是否变化
- `213` surface 的整图差分是否只沿河岸边缘分布

**Why it works:**
- 如果 `184` 只有 alpha 变、RGB 不变，说明 bottom pass 的主体着色不是 seed alpha 驱动。
- 如果 `213` 的主河道中心像素不变，但河岸/折射边界出现细带状大幅变化，说明 seed alpha 只是在修 refraction/bank-edge leak，而不是主水色。
- 这样可以避免把 surface/bottom 的主问题继续误归因到 seed pass。

**Correct approach:**
- 代表像素不要只取河心；至少再取一颗由整图差分找出的 bank-edge 最大变化像素。
- 结论要和 surface shader 的 `RefractionTexture.a -> DecompressWorldSpace -> waterColor/refraction` 路径一起看，而不是只看单个 pick pixel。

### ✅ Pattern 23: 复杂 surface 热替换先做 “current-like exact clone”
**What to do:**
- 在验证 `_BankFade`、`WaterColorShallow/Deep`、`refractionColor` 这类单变量前，先手写一份与 current pass 逐像素完全等价的 HLSL replacement。
- 用 RenderDoc `get_shader mode=reflect` 先拿到真实 input semantics 和资源绑定，再写 replacement。
- 只有在整图 diff 为 0 后，才开始改单个变量。

**Why it works:**
- 如果不先证明 replacement 和当前 shader 等价，后续“改一个参数后图变了”无法区分是参数影响，还是手写 HLSL 本身已经偏了。
- 对 `RiverSurface` 这种包含 refraction、foam、reflection、waterDiffuse 的 shader，这一步尤其重要。

**Correct approach:**
- 顺序固定为：
- `reflect -> exact clone -> single-variable variant -> center pixel + edge pixel + full-frame diff`
- 不要直接从一版“差不多像 current”的 replacement 开始做参数实验。

### ✅ Pattern 24: 对 refraction / see-through 链用“packed-channel replacement”一次输出多个中间值
**What to do:**
- 当 `selected refraction`、`waterColorMap`、`seeThrough final` 之间关系复杂时，不要继续分三次 hot-edit 人工拼结论。
- 直接在同一份 replacement 里打包输出，例如：
- `R = selectedRefraction.r`
- `G = waterColorMap.r`
- `B = seeThroughFinal.r`

**Why it works:**
- 这样所有中间值都来自同一次 shader 执行，能消掉跨 run 的定义漂移和替换误差。
- 在 current `RiverSurface` 中，这个方法直接证明了：
- 某些中心像素的 `seeThrough.r == waterColorMap.r`
- 而且明显不等于 `selectedRefraction.r`
- 从而把“see-through 已经完全塌回 water-color map”钉死。

**Correct approach:**
- 对标量逻辑再写一份 scalar-pack：
- `R = attenuation`
- `G = shoreMask`
- `B = refractionDepth / _WaterSeeThroughShoreMaskDepth`
- 先用同执行 pack 证明链路关系，再决定是否值得继续拆更多中间量。

### ❌ Mistake 24: 跨 capture 直接拿 refractionDepth / attenuation 数值下结论，不先检查 `MaxHeight` 分支
**What to avoid:**
- 看见 CK3 scalar pack 的 `refractionDepth` 比 current 小，就直接归因为 current shader 公式错了。

**Why it's bad:**
- CK3 `jomini_water.fxh` 的 `CompressWorldSpace / DecompressWorldSpace` 带有 `MaxHeight = 50` 相机高度钳制。
- 如果参考 capture 的 `CameraPosition.y > 50`，这个分支会显著改变 refraction-depth / attenuation 结果。
- 实测 `ck3-river.rdc` `event 466`：
- 开启 clamp：`attenuation ≈ 0.48~0.52`，`refractionDepth ≈ 0.52~0.65`
- 禁用 clamp：同一像素直接变成 `attenuation = 1`、`refractionDepth = 0`

**Correct approach:**
- 做 current/CK3 depth 对照前，先记录两边 capture 的相机高度。
- 如果参考实现存在条件分支，优先做一个 `noclamp` / `nofeature` 热替换，量化该分支到底是不是主变量。
- 报告结论时显式区分：
- “同一 capture 内的硬结论”
- “跨 capture 的建议性对照”

### ✅ Pattern 25: RenderDoc HLSL replacement 要按 packed register 还原 Stride 顶点语义
**What to do:**
- 当 Stride 自定义顶点流把多个语义 pack 到同一个 PS input register 时，不要直接按字段名写最自然的 HLSL 输入声明。
- 先用 `debug_pixel summary` 或 shader disasm 看真实输入寄存器布局，再决定 replacement 的输入 struct。

**Why it works:**
- 当前 river bottom capture 里，`v3.x` 是 transparency，而 `v3.yzw` 才是 `RiverNormal`。
- 如果在 replacement 里直接写 `float3 RiverNormal : TEXCOORD3;`，HLSL 会把它读成 `v3.xyz`，相当于把 transparency 混进 normal，导致 exact clone 看起来“公式不对”，其实是输入解释错了。

**Correct approach:**
- 对这种 packed 输入，优先整寄存器读取，再手工拆分分量。
- 例如 bottom pass replacement 中，用：
- `float4 RiverPacked3 : TEXCOORD3;`
- 然后把 normal 写成 `RiverPacked3.yzw`。
- 在 exact clone 为 0-diff 前，不要基于 replacement 结果继续判断 shader 公式。

### ✅ Pattern 26: bottom root fix 与 surface guard 要分开验证、再组合验证
**What to do:**
- 先单独热替换 `RiverBottom`，看 `worldSpaceOffset`、`compressedDistance` 和 surface scalar/R-pack 是否回到合理区间。
- 再单独热替换 `RiverSurface` 的 `effectiveDepth` guard。
- 最后把两者叠加，看最终色和 CK3 方向是否一致。

**Why it works:**
- current 这次问题里，bottom root fix 解决的是“错误外插导致的假深水”，而 surface guard 解决的是“即使 refractionDepth 仍偏大，也不要让 see-through 继续按更深值衰减”。
- 两者作用层级不同；如果不拆开，就很容易把 guard 修正误当成 root cause，或者低估 root fix 对 depth semantics 的意义。

### ❌ Mistake 25: 把 CK3 pre-bottom 当成“scene copy + 非零 alpha”
**What to avoid:**
- 看到 current seed pass alpha 是 `0`，就把问题简化成“只要补一个 CK3 量级的 alpha，就能把 bank 颜色修对”。

**Why it's bad:**
- `debug.rdc` 的 current `event 157` 本质是 bright HDR scene seed copy；代表像素约为 `(2.248, 2.594, 0.755, 0)`。
- `ck3-river.rdc` 的 `event 304` 已经是独立暗 pre-bottom payload；代表像素约为 `(0.021, 0.038, 0.016, 80.69)`。
- 实测只把 current alpha 从 `0` 改到 `50`，bank 最终颜色会从约 `(4.18, 5.02, 1.45)` 继续恶化到 `(4.31, 5.16, 1.50)`。

**Correct approach:**
- 把 pre-bottom 视为独立 payload pass，而不是 scene copy 的一个小变体。
- 诊断时必须同时记录：
- pre-bottom 像素
- bottom `shaderOut/postMod`
- surface `shaderOut/postMod`
- 只有这条完整证据链都对上，才能判断 bank 泄漏到底是不是 pre-bottom 语义错误。

### ✅ Pattern 27: 用“河心像素 + bank 像素 + seed-alpha 热替换”分离主色问题与岸边泄漏
**What to do:**
- 至少固定两颗 representative pixels：
- 一颗河心
- 一颗 bank/highlight 最大变化点
- 先做 bottom 主采样热替换，再做 seed alpha 单变量热替换。

**Why it works:**
- 河心像素回答的是“bottom path/lighting 是否决定主水色”。
- bank 像素回答的是“pre-bottom payload 是否在 downstream 被错误保留下来”。
- 如果 alpha 热替换几乎不影响河心，却显著改变 bank，就说明 seed 问题属于岸边泄漏层，不属于主色层。

**Correct approach:**
- 当前 `debug.rdc` 可以用：
- bottom/seed half-res `(172,299)` 作为河心
- half-res `(176,352)` / full-res `(352,705)` 作为 bank
- 报告结论时，把“河心颜色根因”和“bank 泄漏根因”分开写，不要混成一个参数问题。

### ✅ Pattern 28: scene-driven bottom light selection 要绑定当前 `LightingView` 的真实可见光
**What to do:**
- river bottom 需要读取 scene sun / skybox 时，优先从当前 `LightingView` 对应的 forward-lighting per-view light data 选灯。
- directional 优先选择“真的有 shadow map”的候选，再按强度选；skybox 优先选择“真的有 scene cubemap”的候选，再按强度选。

**Why it works:**
- `CurrentLights` 只是 visibility group 的全局光集合；直接取第一盏 directional / skybox，在多灯或多 view 场景下很容易拿错太阳或错过真正参与 shading 的 skybox。
- 先用当前 view 的 `VisibleLights` / `VisibleLightsWithShadows`，可以让 river bottom 和 scene forward lighting 尽量对齐到同一份语义。

**Correct approach:**
- 优先读取 `ForwardLightingRenderFeature` 的 per-view light cache。
- 读不到 cache 时，再退回本地 frustum 过滤后的 visible-light fallback，而不是直接扫全局 lights。
- shadow map 也优先复用 per-view `RenderLightsWithShadows`，取不到时才调用 `FindShadowMap(lightingView, light)`。

### ❌ Mistake 26: optional fallback cubemap 加载失败后仍在 render loop 里反复重试
**What to avoid:**
- 把 scene skybox 缺失时的 legacy cubemap 当成“每帧都可以再试一次”的 optional 资源。
- 失败后不记录 attempt 状态，导致每帧再次走 `Content.Load` 和异常路径。

**Why it's bad:**
- 这会把一个本应一次性确定的 fallback 资源错误地放进热路径，持续制造异常、日志噪音和帧时间抖动。
- 对 river 这种每帧都会 draw 的 feature，这类失败重试会被无限放大。

**Correct approach:**
- optional fallback 资源也要做 one-shot memoization：成功缓存成功，失败也缓存“已经尝试过”。
- 只有在 loader 生命周期重建或显式 unload/reload 后，才允许再次尝试加载。

### ✅ Pattern 29: `MapExtent` 只负责河宽/深度，矩形地图 UV 要保留独立 `MapWorldSize`
**What to do:**
- river shader 里如果既要用地图跨度做河宽/深度归一化，又要用 world position 去采 `water-color` / `foam-map` / refraction tint，就把这两类量拆开：
- `MapExtent = max(width - 1, height - 1)` 仅给宽度和 depth/profile 使用
- `MapWorldSize = float2(width - 1, height - 1)` 专门给 map-space UV 使用

**Why it works:**
- 宽度归一化确实需要单个标量上界，但 map texture UV 需要按轴独立归一化。
- 如果把 `worldPosition.xz` 也除以 `max(width - 1, height - 1)`，矩形地图会把短边压扁，只能采到半张甚至更少的 `water-color` / `foam` / refraction tint。

**Correct approach:**
- mesh/build 阶段同时保存 `MapExtent` 与 `MapWorldSize`。
- surface shader 的 `ComputeMapWorldUv()` 用 `MapWorldSize`；`ComputeWorldWidth()` 仍然用 `MapExtent`。

### ❌ Mistake 27: 让 refraction payload alpha 和 bottom coverage 共用同一条 blend 语义
**What to avoid:**
- bottom pass 用 dual-source blending 写颜色时，把 RT0 alpha 也继续按 `SecondarySourceAlpha` 和 seed alpha 混合。
- surface 侧再无条件把 `RefractionTexture.a` 当成有效的 `CompressWorldSpace` payload 去解压。

**Why it's bad:**
- 边缘/连接区域一旦只有部分 coverage，RT0 alpha 就会被混成“compressed distance * coverage + seed alpha”。
- 如果 seed alpha 是 `0`，surface 会把这些像素错误地解压到 camera space，随后把 refraction depth、world UV 和 see-through tint 一起带偏。

**Correct approach:**
- RT0 alpha 直接写 payload，不参与 coverage 混合；coverage 只留在 RT1 alpha 控制颜色混合。
- surface decode 时把 `a <= epsilon` 视为“没有有效 payload”，回退到当前水面 world position，而不是强行 `DecompressWorldSpace(...)`。

### ✅ Pattern 30: map-space water-color 要有独立 sampler，但不要把“独立语义”误解成“必须 Clamp”
**What to do:**
- 把 `WaterColorTexture` 从 flow/foam/ambient normal 的共享 sampler 里拆出来，单独声明并绑定 `WaterColorSampler`。
- sampler 的地址模式要跟参考实现和资产语义对齐，而不是仅凭“这是 map-space UV”就默认改成 `Clamp`。

**Why it works:**
- CK3 `jomini_water_default.fxh` 里的 `WaterColorTexture` 本身就是 `Wrap`；真正的问题不是它用了 wrap，而是如果和 flow/foam 继续共用同一个 sampler，后续很难区分“map texture 采样语义”与“tileable noise/normal 采样语义”。
- 把 sampler 拆开之后，既能保持当前 CK3 parity，又能避免下次把 sampler 地址模式问题和 shader 采样语义问题混在一起。

**Correct approach:**
- `WaterColorTexture` / refraction tint 统一通过 dedicated `WaterColorSampler` 采样。
- `FlowNormal` / `AmbientNormal` / `Foam*` 继续走 tileable `WaterTextureSampler`。
- 如果未来要改地址模式，先拿 RenderDoc 或参考 shader 证明该纹理的参考行为真的不是 `Wrap`。

### ✅ Pattern 31: bottom pass 已绑定 scene shadow 但结果仍全亮时，先查 atlas 是否真的有 caster 写入
**What to do:**
- 如果 bottom PS 已经绑定了 `SceneShadowMapTexture` / cascade 参数，但 `EvaluateSceneShadow()` 仍处处返回 `1`，先看 shadow atlas 的 resource usage 和 texture stats，再回头查 caster flags。
- 对 editor terrain 或自定义 `RenderMesh`，优先确认 processor 是否把 `CastShadows` 正确透传到 `RenderMesh.IsShadowCaster`。

**Why it works:**
- SRV 绑定存在只说明 river shader 能读到一张 atlas，不说明 atlas 里已经有有效深度内容。
- 如果 atlas usage 只有 `Clear + PS_Resource`，并且纹理统计是 `min=max=1.0`，问题通常在 shadow caster stage 没有任何对象写入，而不是 river shader 的 world-to-shadow 矩阵或 sampler。

**Correct approach:**
- 先用 RenderDoc 验证 shadow atlas 是否出现 `DepthStencilTarget` 写入，再检查具体 caster 的 `IsShadowCaster` 和 shadow render stage selector。
- 本项目这次根因就是 `EditorTerrainProcessor` 把 terrain 写死成 `IsShadowCaster = false`，导致 river bottom scene shadow 绑定齐全但采到的始终是空 atlas。

---

## Code Examples

### ✅ Good Example
```csharp
pipelineState.State.InputElements = RiverInputElements;
effect.Parameters.Set(TransformationKeys.World, riverObject.World);
effect.Parameters.Set(TransformationKeys.WorldViewProjection, riverObject.World * renderView.ViewProjection);
```

### ❌ Bad Example
```csharp
if (inputAttribute.SemanticName == "POSITION_WS")
{
    positionElement.SemanticName = "POSITION_WS";
    inputElements.Add(positionElement);
}
```

---

## Performance Considerations

- `RiverComponent` 通过 snapshot + `Version` 同步，避免每帧无差别重建 GPU 资源。
- 多 pass 渲染会增加一次额外河底绘制与中间缓冲开销，因此更需要清晰的 render object 生命周期来控制重建频率。

---

## Related Patterns

- [ADR-014 河流渲染架构](../decisions/adr-014-river-rendering-architecture.md)
- [vic3-road-river-rendering](vic3-road-river-rendering.md)

---

## References

- [2026-06-06 河流渲染架构落地与顶点变换修复](../2026/06/06/2026-06-06-1-river-rendering-architecture-and-transform-fix.md)
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`

---

*Learning Document Version: 2.0*
