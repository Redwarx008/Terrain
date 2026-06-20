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
- CK3 的 raw shore fade 阈值直接套在窄 Stride ribbon 上，会让 `waterFade` 把中心主水色清零，只剩暗 bottom/refraction；水面 shader 应用 cross-section `depthFactor` 提供视觉深度下限，同时保持 CK3 的 fade 公式本身不变。

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

### 11. 路径提取在 junction 邻域要优先踏入 semantic marker
- 真实 `rivers.png` 不保证 `Confluence/Bifurcation` marker 恰好落在唯一拓扑分叉中心；最后一个 branch river cell 可能同时邻接 marker 和 side continuation。
- 如果 `TracePath` 只按“排除来路后剩余 filled neighbor 必须唯一”前进，segment 会在 junction 前一格提前终止，真实资产里会把大量 `EndKind` 退化成 `None`。
- 更稳妥的规则是：当且仅当存在唯一相邻 `Source/Confluence/Bifurcation` marker 时，优先踏入该 marker；只有没有 marker 可踏时，才要求普通 `River` 邻居唯一。
- 这条规则不会直接证明 river tangent 方向已经和 CK3 完全一致，但它能先恢复真实 branch 的 semantic endpoint，让后续 flow/taper/tangent 诊断建立在更可信的 graph 上。
- SDSL 默认值只是 fallback；RenderDoc 中应从 shader trace 寄存器或 disasm 反推实际绑定值，不要只看默认 `4096.0f`。

### 11.5 segment 方向必须和 mesh/taper/flow 语义对齐
- `RiverMapService.TracePath()` 从 special pixel 往外追踪时，天然会生成一批 `Confluence->None`、`Bifurcation->None` 这类 segment。
- `RiverMeshService` 的 tangent、parallax、surface flow 和 `RiverBottom` 的 TBN 都直接依赖 `segment.Cells` 顺序；如果方向没归一，RenderDoc 上会看到：
  - `surfaceNormal` 直接打光正常，但经过 bottom normal-map + TBN 后 `nDotL` 接近 0；
  - 单独翻 `bitangent` 能变亮，而整条 `tangent` 链翻转会更亮，同时流向也一起纠正。
- 当前项目里更稳定的归一规则是：`Source/None` 作为上游端，`Confluence/Bifurcation` 作为下游端；实现上可用一个简单 rank（`Source=0, None=1, Confluence/Bifurcation=2`）来决定是否反转 segment。
- 这条规则和现有 `RiverMeshGenerator` 的 `TaperStart = Source/None`、`TaperEnd = Confluence/Bifurcation` 完全一致；如果两者不一致，就会出现 taper、flow 和 lighting 各修各的现象。

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

### 15. 纹理采样诊断图必须按 draw 覆盖 mask 统计
- `C:\Users\Redwa\Desktop\debug-river-after-surface-alpha_frame798.rdc` 和 `ck3-river.rdc` 的 FoamMap 诊断一度因全图均值误判：本地背景是亮地形，CK3 背景是暗地形，全图统计会把背景差异算进 `1 - FoamMap.r`。
- 正确方法是先用纯红 shader replacement 导出目标 draw 的覆盖 mask，再只在 mask 内统计诊断图。
- 2026-06-18 复核后，本地河面区域 `1 - FoamMap.r` 均值约 `173/255`，CK3 河面区域约 `184/255`，同量级；因此不能把当前黑水面主因归结为 FoamMap UV 错。

### 16. CK3 pass wrapper 也是 shader 语义，不能只移植 include 函数
- CK3 `river_surface.shader` 在 `CalcRiverAdvanced(Input)._Color` 后继续执行 shadow map、cloud shadow、terrain shadow tint、fog of war、map distance fog。
- 本地如果只移植 `jomini_river_surface.fxh -> CalcWater`，但省略 `river_surface.shader` 的 wrapper 后处理，就不能称为 surface pass 语义完全等价。
- RenderDoc disasm 中这些 wrapper 逻辑和 `CalcWater` 在同一个 surface PS 内；排查差异时要按完整 PS 边界对齐，而不是按源码 include 文件边界对齐。

### 17. 先比较 refraction RT 的 writer，再比较 river surface 的 reader
- 当本地 `RiverSurface.sdsl` 和 CK3 `CalcWater` 文本上已经非常接近，但画面仍明显不同，下一步不要继续盯 surface 常量。
- 应先找“surface 读的那张 RT 是谁写出来的”，再比较这个 writer pass 的 bindings、资源语义和像素 payload。
- 2026-06-19 的对位里，current `248` 只绑定 scene color + depth，而 CK3 `304` 已绑定 `HeightLookup/PackedHeight/FogOfWarAlpha`、terrain 材质纹理、shadow/environment 等；这证明两边喂给 surface 的 `RefractionTexture/JominiRefraction` 不是同一种 payload。
- 只有 writer pass 也对齐后，再去判断 surface reader 是否还存在真正的公式差异。

### 18. bank 明暗先热改 bottom `RT0.a`，不要先猜 surface
- 如果 river surface 主公式已经和参考项目基本同构，但 bank 仍偏亮，最快的验证不是继续调 `WaterFade` 或 `see-through`，而是直接热改 bottom pass 写进 refraction RT 的 `RT0.a`。
- 2026-06-19 的实验里，current `248` seed alpha 即使被强行改到 `80`，`276` 仍会把 `RT0.a` 重写回自己的值；而只要把 `276` 的 `RT0.a` 从 `9.67` 提到约 `12`，`305` bank 就会从 `0.333/0.239/0.154` 跳到明显更暗的分支。
- 这类现象说明问题在 bottom payload 的阈值翻支，而不是 seed RGB 或 surface 常量的线性偏差。

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
- `RiverSurface` 的 physical depth 应来自 refraction buffer 反解出的深度：`effectiveDepth = min(surfaceDepth, refractionDepth)`。
- 对当前窄 ribbon，`ComputeRiverWaterFade(physicalDepth, depthFactor)` 应使用 `max(physicalDepth, depthFactor * _WaterFadeShoreMaskDepth)` 作为输入深度下限，再套用 CK3 `1 - saturate((maskDepth - depth) * sharpness)` 公式。

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
- 把这条早期 surface 黑边经验直接当成 CK3 bottom/material 的默认参数结论。

**Why it's bad:**
- Pixel history 会显示 surface pass 自己输出 `RGB=(0,0,0), A=1` 覆盖亮地形；这不是 bottom 亮度或 blend state 能正确修复的问题。

**Correct approach:**
- edge alpha ramp 是否需要覆盖 near-shore waterFade 黑斜坡，必须由 surface pixel history 证明，不能从 bottom pass 亮度问题反推。
- CK3 当前 bottom material cbuffer 复核到的 `_BankFade` 是 `0.025`；如果 surface 仍出现 opaque black edge，应单独诊断 `waterFade` / water-color / refraction，而不是把 `_BankFade=0.15f` 固化为 CK3 对齐默认。

### ❌ Mistake 10: 把 edge-alpha waterFade depth-floor 当成最终 CK3 方案
**What to avoid:**
- 只因为黑岸变亮，就长期保留 `edgeFade * _WaterFadeShoreMaskDepth` 或反函数 `edgeVisibleDepth` 这类 depth floor。
- 在没有检查 water-color UV、refraction world-position 和 bottom RT alpha/RGB 前，把所有暗岸问题都归咎于 `waterFade`。

**Why it's bad:**
- CK3 参考岸边能看到 bottom/refraction 河床；depth floor 可能把本该露出的 bottom 改成水色。
- 新截帧证明另一类根因是 water-color 未翻转采样命中近黑区域：同一像素未翻转约 `(0.031,0.031,0.031)`，翻转后约 `(0.180,0.176,0.129)`。

**Correct approach:**
- 先用 RenderDoc pixel history/debug_pixel 证明暗色来自哪个阶段：bottom RT、water-color tint、refraction see-through、waterFade 或最终 alpha blend。
- 对 CK3 surface，map UV 需要 Y 翻转，refraction tint 需要在 `RefractionWorldSpacePos` 重新采 `WaterColorTexture`。
- 如果证据显示中心水面只是因为窄 ribbon physical depth 偏小而 `WaterFade≈0`，使用 cross-section `depthFactor` 作为视觉深度下限；不要用 `edgeFade` 或 alpha 反推 depth。

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

### ❌ Mistake 15: 把所有颜色贴图套同一种色彩空间规则
**What to avoid:**
- 看到 `bottom-diffuse.dds` 是颜色贴图后，就把 `water-color.dds` 也一起按 sRGB view 加载。
- 反过来，也不要把 normal/properties/depth/foam-noise 这类 data map 和水色图混为一谈。

**Why it's bad:**
- `WaterColorTexture` 虽然用于 tint/spec lookup，但当前目标语义要求从 `water_color.dds` 创建 UNorm/linear view；如果按 sRGB view 读，采样值会被硬件额外 decode。
- `BottomDiffuse` 仍可作为 albedo color map 单独验证；它的结论不能自动套到 `WaterColorTexture`。

**Correct approach:**
- 逐张资源按 capture 绑定和 shader 语义定色彩空间，不按文件夹或“看起来是颜色图”批量归类。
- 当前 `water_color.dds` 使用 `Texture.Load(..., loadAsSrgb:false)`，shader 不做手动 `DecodeWaterColorSrgb`。
- `bottom-diffuse` 需要继续用 RenderDoc 单独验证 albedo 色彩空间。
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

### ❌ Mistake 22: 把 `lighting_x3` hot-edit 当成 CK3 等价实现固化
**What to avoid:**
- 在 RenderDoc 里看到 `lighting_x3` 能把 current bottom 亮度推近目标后，直接把 `CalculateRiverBottomLighting(...) * 3.0f` 写进 `RiverBottom.sdsl`。
- 只比较 surface 主观亮度，不复核 CK3 bottom PS disasm 和 bottom RT 代表像素。

**Why it's bad:**
- 2026-06-18 复核 `ck3-river.rdc` EID 332 表明 CK3 bottom 最终输出没有全局 `* 3.0f`；本地 `debug.rdc` EID 290 的 `* 3.0f` 只是把未放大的 `[0.167,0.142,0.103]` 推到 `[0.501,0.427,0.314]`，造成 bottom 过亮。
- 这种增益会掩盖真正的 channel/lighting 组成差异：去掉它以后，current 仍比 CK3 `[0.159,0.105,0.055]` 偏绿/蓝，说明后续应拆 direct/IBL/albedo，而不是继续调全局亮度。

**Correct approach:**
- hot-edit 可以用于量级诊断，但落地前必须和 CK3 disasm/pixel history 对齐。
- 如果 CK3 没有同构的 final gain，就不要把它固化；改为保留 scene-driven lighting 结构，继续用 per-term trace 查通道偏差。

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
- 2026-06-19 更新后的 `C:\Users\Redwa\Desktop\debug.rdc` 里，这个结论仍成立，只是事件号漂移到了 `223 -> 248 -> 276 -> 305`：`223` 的 transparent-stage scene RT 代表像素约为 `(2.1289, 1.3848, 0.8491, 1)`，说明问题首先出在 current 取到的源 RT 就不是 CK3 那种独立暗 payload，而不只是 alpha 缺了一个 camera-distance。

**Correct approach:**
- 把 pre-bottom 视为独立 payload pass，而不是 scene copy 的一个小变体。
- 如果 current 是在 transparent stage 里直接读 `commandList.RenderTargets[0]`，先验证这个 RT 的能量级和语义，再决定要不要继续追 seed shader 或 alpha。
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

### ✅ Pattern 32: 对齐 CK3 bottom lighting 时要拆 direct / diffuse IBL / specular IBL
**What to do:**
- 对 CK3 bottom shader 先用 RenderDoc trace 找到最终累加点，而不是只比较 `o0.rgb`。
- 本轮 CK3 EID 332 的关键累加是：direct 后约 `[0.1436,0.0920,0.0437]`，diffuse IBL 只增加 `[0.0141,0.0118,0.00935]`，specular IBL 只增加 `[0.0009,0.0010,0.0016]`。

**Why it works:**
- current 旧 shader 的 specular IBL 在代表像素约 `[0.0110,0.0174,0.0233]`，比 CK3 高一个数量级且明显偏蓝；单看 final RGB 容易误判成“整体亮度不够”或“贴图颜色不对”。
- CK3 bottom 的黄橙感主要来自 warm direct sun + material albedo，IBL 是小的修饰项，不能让 river-local specular multiplier 把蓝色 cubemap 放大。

**Correct approach:**
- `RiverBottom` 应使用 CK3 material BRDF：`0.25 * properties.g` spec remap、metalness diffuse/spec split、GGX direct、dominant specular IBL、Burley roughness-to-mip。
- lighting/view position 要用 fake-depth 前的 submerged bottom position；fake depth 只影响 refraction payload 压缩，不应参与 lighting。
- 不要再落地 `_BottomSpecularIntensity` 或全局 `* 3.0f` 这类 river-local energy workaround。

### ❌ Mistake 28: 把 Background skybox、river fallback cubemap 和 CK3 JominiEnvironmentMap 当成同一个输入
**What to avoid:**
- 看到 `Skybox texture`、`River/Environment/reflection-specular` 或 CK3 `environment_terrain_sunny.dds` 都是 cubemap，就认为它们对 river bottom lighting 等价。
- 只改 river fallback cubemap 或 `_BottomEnvironmentIntensity`，期望影响已经绑定 scene skybox 的 bottom pass。

**Why it's bad:**
- 当前 Stride bottom pass 绑定的是 `LightSkybox.Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]`，不是 `BackgroundComponent.Texture`，也不是 river fallback `reflection-specular`。
- Stride `SkyboxGenerator` 会对 skybox source 做 GGX specular prefilter 并生成 runtime cubemap；CK3 `JominiEnvironmentMap` 是已经用于 shader 的 prefiltered environment resource，不能默认等价于 Stride 对同一 DDS 再处理后的结果。
- `debug.rdc` EID 276 证明 current bottom 使用的是 HDR/blue scene cubemap `ResourceId::276`，而 CK3 EID 332 使用低值 `BC1_SRGB` cubemap `ResourceId::6427` 再乘 `CubemapIntensity=20`。

**Correct approach:**
- 诊断 bottom IBL 时必须记录三件事：
- `EnvironmentMapTexture` 的实际 `ResourceId` / format / mip stats。
- scene skybox light 的 `Intensity` 和 sky rotation。
- CK3 对应 cbuffer 的 `CubemapIntensity` / `CubemapYRotation` / environment resource stats。
- 如果要贴近 CK3，优先修 scene-level light/environment setup；不要把差异塞回 river-local fallback 常量。

### ❌ Mistake 29: 把 CK3 cbuffer 里的 SunDiffuse 线性值直接传给 Stride LightComponent.SetColor
**What to avoid:**
- 看到 CK3 cbuffer 里 `SunDiffuse=[1,0.867838,0.754852]`，就直接调用 `light.SetColor(new Color3(1,0.867838,0.754852))`。
- 只看 C# 常量正确，不用 RenderDoc 复核最终 `_SceneSunColor`。

**Why it's bad:**
- CK3 cbuffer 里的值已经是 shader 使用的线性 GPU 输入。
- Stride `LightComponent.SetColor` 存的是 gamma-space provider 值；`LightProcessor` 在 linear color space 下会调用 `Color3.ToLinear()` 再乘 intensity。
- 直接传 CK3 线性值会被二次转 linear，最终从目标 `[20,17.3568,15.0970]` 变成约 `[20,14.505,10.602]`，bottom direct lighting 会明显偏暗偏红/缺黄。

**Correct approach:**
- 保留 CK3 目标线性 diffuse 常量用于对照。
- 传给 `LightComponent.SetColor` 前先执行 `ToSRgb()`，让 Stride 后续 `ToLinear() * intensity` 回到 CK3 cbuffer 值。
- 每次改 scene light 后，用 RenderDoc 直接读 bottom pass Globals cbuffer，确认 `_SceneSunColor` 而不是只确认 C# 常量。

### ❌ Mistake 30: 把单帧 tangent hot-replace 结论固化成 shader 固定取反
**What to avoid:**
- 只用一个河段/一个 capture 的 `nDotL` hot-replace 结果，就把 `RiverBottom` 写死为 `-normalize(streams.RiverTangent)`。
- 看到某个像素翻转 tangent 会变亮，就跳过 CK3 源码和更多河段方向验证。

**Why it's bad:**
- `debug-current-codex-ribbon-normal_frame870.rdc` 证明 ribbon normal 修复后，bottom pass 代表像素仍然只有 `[0.013,0.016,0.017]`。
- 该帧同一像素 tangent 为 `-X`，所以 shader 里固定取反看似能把 direct sun `nDotL` 从近零提升到 `0.86/0.88`。
- 后续 `C:\Users\Redwa\Desktop\debug.rdc` 的同类 bottom 像素输入 tangent 为 `+X`；已编译进 GPU 的固定取反让代表像素仍只有 `nDotL≈0.17`，而 no-flip hot-replace 提升到 `0.72/0.82`。
- 追加同帧 replacement PS 输出 `R=no-flip nDotL`、`G=flip nDotL`：`(562,239)` 为 `0.8949/0.0297`，`(528,301)` 为 `0.8595/0.1122`；no-flip direct-light replacement 将 `(562,239)` 从原始 `[0.074,0.055,0.042]` 提升到黄棕 `[0.233,0.146,0.065]`。
- CK3 `jomini_river_bottom.fxh` 的实际 TBN 是 `float3 Tangent = normalize(Input.Tangent);`，固定 shader 取反并不等价。

**Correct approach:**
- mesh 层保留中心线 tangent 的真实方向和坡度，供几何/未来逻辑使用。
- `RiverBottom` 按 CK3 源码直接使用 `float3 tangent = normalize(streams.RiverTangent);`，不要在 shader 中硬编码方向修正。
- 每次怀疑 bottom 过暗时，先用 RenderDoc hot-replace 输出 `nDotL` 或 TBN-normal，并至少比较相反 tangent 方向的河段。
- 如果 no-flip 仍在部分河段变暗，查 `RiverMeshService` / `RiverMapService` 的 segment 方向语义，而不是再加 shader fallback 或常量补偿。

### ✅ Pattern 33: surface 变黑时先拆 base/distorted refraction，再判断水色和反射
**What to do:**
- 对 `RiverSurface` 的黑岸/暗岸问题，先用 packed replacement 输出：
- `waterFade / depthFactor / foam`
- `baseRefraction.rgb`
- `distortedRefraction.rgb`
- `useDistorted`
- `refractionShoreMask`

**Why it works:**
- `debug2.rdc` 证明代表像素的 `waterFade=0`，因此 `WaterColorShallow/Deep`、fresnel 和 cubemap reflection 都没有实际贡献；最终颜色几乎完全来自 `CalcRefraction/SampleRefractionSeeThrough`。
- 同一像素 base refraction 约 `[0.28,0.25,0.20]`，distorted refraction 约 `[0.02,0.02,0.02]`，且 `useDistorted=1`，这才是暗岸直接原因。
- CK3 `jomini_water_default.fxh` 在 distorted sample 前会用 base refraction depth 计算 `_WaterRefractionShoreMaskDepth/_Sharpness`，浅岸 mask 为 `0` 时 offset 应被清零。

**Correct approach:**
- `RiverSurface` 应暴露并绑定 `_WaterRefractionScale`、`_WaterRefractionShoreMaskDepth`、`_WaterRefractionShoreMaskSharpness`、`_WaterRefractionFade`。
- 先用 undistorted base refraction payload 得到 `refractionShoreDepth = min(worldDepth, baseRefractionDepth)`，再计算 `ComputeRefractionShoreMask(refractionShoreDepth)`。
- distorted offset 必须使用 CK3 的 view-space normal + 1080p 归一化 basis，再乘 `_WaterRefractionScale * refractionShoreMask * _WaterRefractionFade`；不要只把 shore mask 接到旧的 river-local offset 常量公式上。
- `RiverRenderFeature` 必须给 surface 绑定 `_ViewMatrix`，否则 shader 无法等价 CK3 的 `ViewMatrix` offset 语义。
- 只有证明 base/distorted/refraction shore-mask 都正确后，才继续查 water diffuse、water-color map 或 cubemap reflection。

### ❌ Mistake 31: 看到 surface 黑就先调 `WaterColorShallow/Deep` 或 cubemap
**What to avoid:**
- 发现 `RiverSurface` 最终很黑时，先把 `WaterColorShallow/Deep` 改成 CK3 cbuffer 值，或把 cubemap reflection intensity 改成 0。
- 只看 cbuffer 里水色差异很大，就推断水色是主因。

**Why it's bad:**
- 如果 `waterFade=0`，surface diffuse 和 fresnel/reflection 都被清掉，调水色或 cubemap 不会改变这些像素。
- 当前问题里 `reflection=0` 与 `CK3 water colors + reflection=0` 的 hot-replace 对代表像素完全不变；真正有效的是 CK3 refraction shore mask。

**Correct approach:**
- 先用 RenderDoc 证明 `waterFade` 是否为 0，以及 final 是否等于 processed refraction。
- 若 final 跟 refraction 相等，继续拆 base/distorted/refraction world depth；不要继续调水色常量。

### ❌ Mistake 32: 只补 CK3 shore mask 但保留旧 refraction offset basis
**What to avoid:**
- 把 `_WaterRefractionShoreMask*` 接进 shader 后，继续使用 `normalOffset * (0.0025 + depthFactor * 0.0035)` 和 `_WaterRefractionScale / 500`。
- 用 `flowNormal.xz` 代替 CK3 传入 `CalcRefraction` 的最终 water normal。

**Why it's bad:**
- CK3 offset 是 `mul(ViewMatrix, float4(Normal.x, 0, Normal.z, 0)).xy * float2(-1/1920, 1/1080) * _WaterRefractionScale * RefractionShoreMask * _WaterRefractionFade`。
- 旧公式的方向、尺度、resolution normalization 都不是 CK3 语义；浅岸 mask 只能阻止一部分黑 texel，不能让整体 refraction 行为对齐。

**Correct approach:**
- Surface shader 显式声明并绑定 `_ViewMatrix`。
- 用最终 `waterNormal` 计算 view-space offset，直接乘 CK3 scale / shore mask / fade。
- 文本测试必须反向禁止旧 `0.0025 + depthFactor * 0.0035` 与 `/500` normalized scale 公式。

### ✅ Pattern 34: bottom/refraction 已接近后，用 direct-refraction hot-replace 重新归因 surface
**When to use:**
- `RiverBottom` 已经对齐到 CK3 的 world-UV/non-advanced branch，且 raw refraction 代表像素接近 CK3 量级，但最终水面仍明显偏色。
- 旧日志坐标不再稳定命中当前 capture 的 surface draw，需要先从导出的 RT 或 pixel history 重新选实际河心点。

**Technique:**
- 在 RenderDoc 中把 current surface PS 热替换成只输出 `RefractionTexture.Sample(RefractionSampler, SV_Position.xy / ViewSize).rgb`。
- 对同一组 surface 命中点比较原始 `shaderOut` 与替换后 `shaderOut`，同时查 half-res bottom/refraction 对应点。
- 再用 CK3 `466/464` 的 pixel history 对比 CK3 surface 如何从同类底层颜色生成最终水色。

**Observed 2026-06-18 result:**
- current `debug1.rdc` surface `305` 的河心点原始输出约 `[0.26..0.29,0.50..0.53,0.63..0.66]`。
- direct-refraction hot-replace 后同点回到 `[0.27..0.30,0.19..0.22,0.12..0.14]`，与 CK3 bottom/refraction 代表点 `[0.271,0.185,0.100]` 同量级。
- CK3 surface `466` 代表点 `(110,738)` 输出 `[0.0223,0.0280,0.0305]`，`464` 代表点 `(930,810)` 输出 `[0.00874,0.01793,0.02160]`，说明 CK3 surface 是完整 `CalcWater` lighting/foam/refraction composition 后的低能量水色，不是 current 的高饱和蓝色叠加。

**Correct follow-up:**
- 不要只改 `WaterColorShallow/Deep`；那只能降低饱和度，不能补齐 CK3 `pdx_hlsl_cb11` 的 `_WaterSpecular/_WaterGloss*/_WaterWave*/_WaterFoam*/_WaterFlowNormalFlatten/_WaterReflectionNormalFlatten` 等语义。
- hot-replace gate 通过后，`RiverSurface.sdsl` 应按 `CalcRiverAdvanced -> CalcWater` 结构改，而不是继续在 `PSMain` 里手写组合。
- 端口后仍要重新截帧，因为 SDSL 可编译实现可能只能用 out-param 表达目标结构，且 FlowMap/FoW/cloud/fog 这类 scene-dependent 输入可能还未完全接入。

### ❌ Mistake 35: surface replacement shader 透传 refraction alpha
**What to avoid:**
- 在 RenderDoc hot-replace 里直接 `return RefractionTexture.Sample(...);`，把 refraction RT 的 alpha 原样传给 surface blend state。
- 看到 replacement 结果黑屏后，误判为 RGB 采样、water color 或 target 常量无效。

**Why it's bad:**
- 当前 refraction alpha 是 camera-relative distance payload，不是普通透明度。
- Surface blend path 会把这个 payload 当作输出 alpha 消费，导致 replacement 控制实验本身污染最终颜色。

**Correct approach:**
- replacement shader 验证 RGB 时显式输出 `float4(sample.rgb, 1.0f)`。
- 如果要检查 distance payload，单独打包到 RGB debug 通道或导出原 RT，不要把它作为最终 surface alpha 透传。

### ✅ Pattern 35: 河流 pass 分支必须以目标截帧的实际 draw/disasm 为准
**What to do:**
- 对 CK3 这类同一 shader 源同时包含 advanced/non-advanced 路径的 pass，先从目标 `.rdc` 的实际 draw、cbuffer 和 disasm 判断命中的函数，再端口 SDSL。
- bottom 和 surface 可以命中不同分支：本轮目标截帧中 bottom 使用 `CalcRiverBottom` non-advanced，surface 使用 `CalcRiverAdvanced -> CalcWater`。

**Why it works:**
- 只看源码文件名或相邻函数很容易把未使用路径误当目标路径，后续会引入额外参数、补偿项或错误采样链。
- `debug-river-target-after.rdc` 证明正确端口后的 surface 只有单次 flow normal 采样、三层 `_WaterWave*` ambient normal、CK3 `CalcRefraction/WaterFade` 路径；bottom 则是 hard power=2 depth profile、fixed 2/10 layer parallax、water-surface shadow exclusion。

**Correct approach:**
- 先在 RenderDoc 里记录 event、shader hash、关键 cbuffer 值和 disasm 特征。
- 再写文本测试锁住“应该出现”的目标模式和“不得出现”的旧模式，例如 `flowUv1`、`SampleRefractionSeeThrough`、`safeDenom`、`effectiveDepth`。
- 修改 SDSL 后必须跑 shader key 生成、`StrideCleanAsset`、`StrideCompileAsset`，再重新截帧确认 GPU 里实际运行的是新 shader。

### ❌ Mistake 36: 用临时 depth/fade adapter 代替目标 shader 语义
**What to avoid:**
- 热替换发现某个 depth floor、visual depth adapter 或 brightness multiplier 能把当前截图推近目标后，就直接把它当成最终实现。
- 在用户要求完全参考 CK3 时，继续保留 `ComputeRiverWaterFade(physicalDepth, depthFactor)`、全局 `* 3.0f`、旧 refraction offset scale、`safeDenom` parallax 等本地补偿。

**Why it's bad:**
- 这些 adapter 可能只修复当前帧的能量或岸边症状，但会破坏目标 shader 的数据流，导致下一帧、另一条河段或另一个 pass 继续偏离。
- 本轮 CK3 surface 的 `WaterFade` 和 `CalcRefraction` 是两条独立 base-refraction 路径；把它们合并成 capped/effective depth 会让 see-through、shore mask 和 final fade 的语义都不等价。

**Correct approach:**
- 热替换只能作为归因和风险门禁；落地代码必须回到目标 disasm 和 cbuffer 已证明的公式。
- 如果必须保留非目标 adapter，必须在文档和测试里明确标成当前项目限制；本轮已选择删除这些 adapter，直接对齐 CK3 语义。

### ✅ Pattern 36: bottom pass 热替换必须同时控制 dual-source blend 权重
**What to do:**
- 对 `RiverBottom` 做 RenderDoc shader replacement 时，输出不仅要包含 `SV_Target0` 的 RGB/alpha，还要给 `SV_Target1` 写入合理 alpha。
- 如果目标是验证 bottom RGB 能量，优先把 secondary alpha 固定为 `1.0`，让 shader output 直接进入 RT0，避免被 seed color 反向混合。

**Why it works:**
- 当前 bottom pass 使用 dual-source blending，RT0 RGB 的 source blend 依赖 secondary source alpha。
- 本轮第一次 hot-replace 只把 bottom direct diffuse 算亮，但 secondary alpha 误落到 camera-distance payload 附近，post-blend 变成负值；第二次把 `SV_Target1.a=1` 后，bottom `(500,250)` post-blend 从 `[0.0157,0.0179,0.0198]` 变为 `[0.1599,0.1021,0.0397]`，surface 对应点从 `[0.0095,0.0180,0.0208]` 变为 `[0.1463,0.0953,0.0396]`。

**Correct approach:**
- 先用 `pixel_history` 同时看 `shaderOut` 和 `postMod`，不要只看 shader debug output。
- replacement shader 需要输出双 target；如果只验证颜色，`SV_Target0.a` 继续保持 distance payload，`SV_Target1.a` 控制 coverage。
- 热替换结果必须在 bottom RT 和 surface RT 两级都复核，才能证明暗色传播链。

### ✅ Pattern 37: half-res / full-res river 链必须先做差分交点，再锁同一颗像素
**What to do:**
- 当 capture 链路同时包含 half-res seed/bottom 和 full-res surface 时，不要一上来直接 pick pixel。
- 先分别导出：
- half-res：例如 `248 -> 276`
- full-res：例如 `223 -> 305`
- 对两组 RT 做整图差分。
- 先在 half-res 图上找最强变化点，再按 `x2` 映射到 full-res，对照 full-res 差分确认落在同一条 river 变化带内。
- 然后再对这两组坐标执行 `pick-pixel` / `debug pixel`。

**Why it works:**
- 这类 pass 最容易出现的假阴性不是 shader 没生效，而是像素点到了河外、coverage 边界，或者 half-res / full-res 根本没对上同一逻辑位置。
- 先用差分锁住“确实被 river 改写过”的点，能直接把 `scene seed -> bottom -> surface` 串成同一颗代表像素证据链。

**Correct approach:**
- 顺序固定为：
- `export RT -> image diff -> half-res strongest point -> x2 map to full-res -> pick/debug`
- 如果 full-res 对应点没有变化，不要急着下 shader 结论；先回去检查是不是映射位置偏了，或者 half-res 点本身就落在 bank coverage 边缘。

### ✅ Pattern 38: bank-edge 要同时比较 `248 -> 276` 的 RGB 和 alpha，先判断 payload 是否存活
**What to do:**
- 当 bank 最终颜色明显接近 bottom，但又没有像 CK3 那样继续压暗时，不要只比较 `276 -> 305` 的 RGB。
- 先固定一颗 bank-edge 点和一颗 river interior 点，同时比较：
- `248` seed/pre-bottom
- `276` bottom
- `305` surface
- 尤其要看 `248 -> 276` 的 alpha 有没有像 interior 一样继续变化。

**Why it works:**
- 如果 bank 上 `248 -> 276` 只是 RGB 变成了 bottom 颜色，而 alpha 基本不变，就说明 surface 最终仍在吃 surviving seed/pre-bottom payload。
- 这种情况下继续调 `WaterFade`、water color 或 reflection 常量，通常都不会把 bank 拉回 CK3。

**Correct approach:**
- 先问两个问题：
- bank `305` 输出 alpha 是否已经接近 `1`？
- bank `248 -> 276` 的 alpha 是否几乎没变，而 interior `248 -> 276` 的 alpha 明显变了？
- 如果两个答案都是“是”，优先回头处理 bank payload/source，而不是先怀疑 surface fade。

### ❌ Mistake 37: 把 Stride cascade shadow helper 当成目标 bottom shadow
**What to avoid:**
- 因为 shader 已绑定 `SceneShadowMapTexture`、cascade matrix 和 shadow atlas，就认为 `EvaluateSceneShadow()` 与目标 bottom shadow 等价。
- 在没有验证 target disasm 的情况下，把 Stride 的 cascade 5x5 PCF helper 乘进 bottom direct light。

**Why it's bad:**
- 目标 bottom disasm 在 shadow 段使用 `ShadowMapTextureMatrix` 投影、水面交点和 bottom position 取 `min(depth)`、`ShadowScreenSpaceScale` 随机旋转 kernel、`KernelScale`、`NumSamples`、`Bias`、边界 fade 后再乘 `SunDiffuse * SunIntensity`。
- 当前 helper 是 Stride cascade selector + 5x5 filter，参数、投影边界和 fade 语义都不同；在 `debug-river-after-surface-alpha_frame798.rdc` 中它让 bottom RT 对应像素写成近黑，surface 只是继续采样这个近黑输入。

**Correct approach:**
- 如果 target shadow 投影还没有移植，bottom direct light 不应乘入这个非等价 helper；保持 scene sun / material BRDF / IBL 路径，再把 target shadow projection 作为独立缺口处理。
- 正式移植时需要补齐 `ShadowMapTextureMatrix` 等价输入、kernel sample 表、`Bias`、`KernelScale`、`NumSamples`、`ShadowScreenSpaceScale` 和 fade 逻辑，不能只复用 Stride cascade matrix 名义上替代。
- 代码变更前先用 RenderDoc replacement 证明 shadow term 改动会修复 bottom RT，再落地 SDSL 并跑 Stride asset rebuild。

### ❌ Mistake 38: 用本地 shadow/cloud helper 代替目标 water cbuffer 参数
**What to avoid:**
- 在 surface `CalcWater` 中保留本地 `cloudShadowMask -> GetWaterGlossScale/GetWaterSpecularFactor/GetWaterCubemapIntensity` 包装。
- 用 `sunIntensityMask = smoothstep(..., glossMap)` 按 water-color alpha 门控 direct sun，然后误以为 surface 已经和目标 `jomini_water_default.fxh` 等价。

**Why it's bad:**
- `ck3-river.rdc` EID 460 的 surface cbuffer 明确给出 `_WaterZoomedInZoomedOutFactor=0`、`_WaterGlossScale=1`、`_WaterSpecularFactor=0.01`、`_WaterCubemapIntensity=0`、`_WaterToSunDir=[-0.5439,0.5934,0.5934]`。
- 目标 `CalcWater` 直接使用这些参数：`Glossiness = lerp(_WaterGlossBase, GlossMap, _WaterZoomedInZoomedOutFactor)`，`NonLinearGlossiness *= _WaterGlossScale`，direct sun 使用 `_WaterToSunDir` 与 `SunDiffuse * SunIntensity`，没有 glossMap sun gate。
- cbuffer 数值一致不代表最终公式一致；本地 helper 会让 RenderDoc 看到参数对上了，但 shader energy path 仍偏离目标。

**Correct approach:**
- 对照 CK3 water shader 时，优先保留目标 cbuffer 参数的直接消费关系。
- 如果画面仍偏暗，先用 RenderDoc 确认 bottom/refraction 是否正常，再查 surface composition 是否还残留本地 helper 或额外门控。

### ❌ Mistake 39: 在 river shader 里直接继承 terrain height 参数 mixin
**What to avoid:**
- 为了复用 Editor terrain height sampling，直接让 `RiverSurface` 继承 `EditorTerrainHeightParameters`。

**Why it's bad:**
- `EditorTerrainHeightParameters` 继承 `Texturing`，会注入默认 `TexCoord` streams。
- `RiverVertexStreams` 已经使用 TEXCOORD0/2/3/4/5 承载 river transparency、tangent、normal、width、distance-to-main；两者同 semantic 不同类型会触发 Stride front-end `E2215`。

**Correct approach:**
- River shader 可以复用项目地形 GPU 数据，但不要继承会注入 vertex streams 的 terrain mixin。
- 在 river shader 内声明所需 height slice 资源、bounds、height scale 和采样函数；C# 侧从 `EditorTerrainRenderObject` 绑定真实 height slices。
- Runtime streaming terrain 需要独立 provider，不能把 Editor 8-slice 参数混到 runtime 路径里。

### ❌ Mistake 39.5: 把 Editor `HeightmapSlice0..7` 后段近似当成 CK3 surface 语义等价
**What to avoid:**
- 看到本地 `RiverSurface` 已经有 `ApplyTerrainShadowTintWithClouds`、`ApplyMapDistanceFogWithoutFoW`，就认为 current surface 和 CK3 `river_surface.shader` 的完整 PS 已经等价。

**Why it's bad:**
- CK3 `ck3-river.rdc` surface 事件的实际绑定明确依赖 `HeightLookupTexture`、`PackedHeightTexture`、`FogOfWarAlpha_Texture`、`ShadowMap_Texture`。
- 当前项目的 surface 后段实际绑定是 Editor terrain `HeightmapSlice0..7`、`SliceCount`、`HeightScale`、本地 `_WorldSpaceToTerrain0To1` 和 debug4-style procedural cloud 所需的 `_InverseWorldSize`；这些仍不是 CK3 原始 `HeightLookupTexture` / `PackedHeightTexture` / `FogOfWarAlpha_Texture` provider。
- wrapper 函数名相同，不代表资源语义相同；如果忽略这一层，就会在 `WaterColor/Fresnel` 常量上反复打转，而主差距根本不在那里。

**Correct approach:**
- 先同时对照 loose shader source、capture bindings 和本地 C#/SDSL 绑定点，再判断“后段是否等价”。
- 如果 CK3 目标 capture 依赖的 terrain/shadow/FoW 资源集合与本地不同，就把它视为输入 provider 问题，而不是先调 `CalcWater` 常量。

---

### ❌ Mistake 40: 把 `renderdoc-cli shader-replace` 当成跨命令持久热修会话
**What to avoid:**
- 在没有可用 MCP 或 GUI 持久会话时，先用 `renderdoc-cli shader-replace` 替换 shader，再指望下一条独立 CLI 命令继续读取替换后的像素、RT 或 pipeline 状态。

**Why it's bad:**
- 当前 `renderdoc-cli.exe` 是单命令单进程接口，替换状态不会自动跨下一条命令保留。
- 这条路径适合做 `shader-build`、pipeline/disasm/cbuffer 检查和资源导出，不适合做“替换后继续读回”的持续热修验证。

**Correct approach:**
- 把 CLI 当成 compile/inspection 工具，而不是持久热修会话。
- 需要验证 replacement 生效后的像素、RT 或 surface 传播链时，优先恢复 `renderdoc-mcp` 或使用 RenderDoc GUI。
- 在 CLI-only 条件下，最多把 replacement 用作“能否编译成目标 shader”与“理论公式是否成立”的门禁，不要把它当最终 GPU 回读证据。

### ❌ Mistake 41: 跨 `open_capture` 复用 `shader_build` 产出的 `shaderId`
**What to avoid:**
- 在一个 capture 里调用 `shader_build` 得到 replacement `shaderId` 后，切到另一个 capture，或重新 `open_capture` 当前 capture，再拿这个旧 `shaderId` 直接做 `shader_replace`。

**Why it's bad:**
- 当前 RenderDoc MCP 的 replacement 句柄只对生成它的 capture 会话有效，不能跨 capture 复用。
- 已确认这种误用会把 `renderdoc-mcp` 子进程直接打崩，Codex 侧通常只会看到 `Transport closed`。
- 代理日志 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618\bin\renderdoc-mcp-proxy.log` 可以直接确认根因；当前已复核到的崩溃退出码是 `3221225477`。

**Correct approach:**
- 始终在同一个 capture 里现编现替：`open_capture -> shader_build -> shader_replace -> pick/debug/pixel_history`。
- 只要切过 capture，或重新 `open_capture`，就重新 `shader_build`，不要复用旧 replacement `shaderId`。
- 如果 Codex 内置 MCP transport 已坏，但仍需继续做同样的 hot-edit 验证，可以直接启动底层 `renderdoc-mcp.exe`（同目录 `bin` 下）并走 stdio MCP 协议，不必退回只读的 CLI 检查流。

### ✅ Pattern 42: 用 replacement 直接 dump shader 输入和 cbuffer，避免从 disasm 反推错尺度
**When to use:**
- 需要确认 `RiverWidth`、`MapExtent`、camera position、view size 或其他 cbuffer 值是否与参考截帧同量级。
- 之前的结论来自手算、日志转述或未验证的寄存器映射。

**Technique:**
- 在目标 draw 上做最小 PS replacement，把待验证值直接输出到 `SV_Target0`。
- replacement 的输入签名必须先用 `get_shader(..., reflect)` 对齐；如果原 shader 的 `POSITION_WS` 占了整个 `reg0`，replacement 也要用 `float4 PositionWS` 占满，避免 HLSL 编译器把后续 semantic 重新打包。
- 输出后用 `pixel_history` 读 `shaderOut`，不要只看 post-blend，因为 blend state 可能改写诊断色。

**Observed 2026-06-19 result:**
- current bank 的真实 `MapExtent=9215.5`、`RiverWidth=8.283e-05`、`worldWidth=1.5267`。
- 这推翻了上一轮 `worldWidth≈0.339` 的错误推断；current 与 CK3 bank width 实际同量级。

**Correct follow-up:**
- 如果 replacement 输出和预期不符，先检查 replacement reflection 的 input register packing，再解释数值。

### ❌ Mistake 42: 在 surface texture probe 里猜 view size 或只信 `debug_pixel`
**What to avoid:**
- 在 surface replacement 中用 `800x600`、窗口尺寸或旧日志尺寸去采 `RefractionTexture`。
- 在多 primitive 覆盖同一像素时，只看 `debug_pixel` summary，不与 `pixel_history` 的实际 passing fragment 对照。

**Why it's bad:**
- current `debug.rdc` 的 surface RT `ResourceId::4059` 实际尺寸是 `1672x996`；错误 view size 会采到完全不同的 refraction payload。
- CK3 `event 466` 的 `debug_pixel` 曾返回与 pixel history 不一致的 raw output；pixel history 才显示该像素实际通过的 `shaderOut/post` 为 `[0.0223,0.0280,0.0305]`。

**Correct approach:**
- 先用 `get_resource_info` 或 pipeline state 确认 render target 尺寸，再计算 screen UV。
- 以 `pixel_history` 的 passing event/primitive/shaderOut/postMod 作为最终像素证据；`debug_pixel` 只作为辅助 trace。

### ✅ Pattern 43: 把 `CalcWater` 输出和完整 surface wrapper 分开热修改
**When to use:**
- `CalcRiverAdvanced -> CalcWater` 主体已经接近目标，但 CK3 最终 surface 仍比本地暗很多。
- 用户怀疑 `HeightLookup/PackedHeight/FoW` 这类 wrapper 输入不应影响水面主色。

**Technique:**
- 在 CK3 surface draw 上用 replacement 复现 base refraction/see-through，直接输出 `CalcWater` 中 `WaterFade=0` 时应返回的 refraction 分量。
- 另行采样 `FogOfWarAlpha`，确认 FOW 是否真的参与压暗。
- 在 current surface 上用最小 replacement 输出 CK3 shadow-tint 目标色，验证 wrapper 暗化量级是否足以解释最终差距。

**Observed 2026-06-19 result:**
- CK3 `event 466` bank 像素 `(110,738)` 的 base see-through 约为 `[0.098,0.095,0.072]`，完整 surface 输出为 `[0.022,0.028,0.030]`。
- 同点 `FogOfWarAlpha` 采样为 `[1,1,0,1]`，不是压暗来源。
- current `event 305` 同类像素 `(30,768)` 的 see-through 直出约 `[0.292,0.212,0.143]`；强制输出 CK3 shadow-tint 目标色 `[0.023,0.023,0.033]` 后落入 CK3 能量范围。

**Correct follow-up:**
- 允许 terrain shadow tint、cloud tint 和 map distance fog 参与 surface RGB 后处理。
- 继续禁止 strategy-layer `FogOfWarAlphaTexture` 作为本项目河流水体依赖；FOW 与 wrapper shadow/cloud 不是同一个问题。

### ❌ Mistake 43: 把 CK3 surface wrapper 当成可忽略的水色无关后段
**What to avoid:**
- 只对齐 `jomini_water_default.fxh` / `jomini_river_surface.fxh`，然后删除或禁用 `river_surface.shader` wrapper 的 RGB 修改。
- 因为 FOW 在本项目不需要，就顺带认为 terrain shadow tint、cloud tint、distance fog 都不应影响最终河流颜色。

**Why it's bad:**
- CK3 截帧里 `CalcWater` 的 refraction/see-through 与完整 surface final 可以差 4 倍以上。
- FOW 可以为 1 且不压暗，但 shadow/cloud tint 仍会把水色推向 `shadow_color.dds` 的低能量暗色。

**Correct approach:**
- 按完整 PS 边界比较：`CalcWater` 主体、shadow/cloud wrapper、FOW、distance fog 分别热修改或输出中间量。
- 移除 FOW 依赖时，只删除 FOW 资源和颜色调整，不要误删已验证会影响目标观感的 shadow/cloud/fog wrapper。

### ❌ Mistake 39: 把显隐切换后的河流变黑直接归因于 terrain 资源变化

**症状：**
- 多次切换地形显示后，两个 capture 的河流颜色不同。
- river surface 的输入、资源绑定、shader 和基础 `CalcRefraction` 输出都一致，但最终 RGB 一个很暗、一个较亮。

**实际原因：**
- 两帧之间 `_GlobalTime` 前进，`GetCloudShadowMask(worldPosition.xz)` 的 procedural cloud 相位不同。
- `debug3.rdc` 中多个河流点的 wrapper 探针为 `(shadowTintMask=0, cloudMask=1, terrainShadowTerm=1)`。
- `debug4.rdc` 同点 cloud mask 为 `0`。
- `ApplySurfacePostProcessing` 的 cloud tint：

```hlsl
color.rgb = lerp(color.rgb, float3(0.0f, 0.01f, 0.02f), cloudMask * 0.8f);
```

会把 `[0.146, 0.248, 0.235]` 级别的 refraction 色压到约 `[0.029, 0.058, 0.063]`。

**正确排查方式：**
- 先热替换输出 `_GlobalTime`、`cloudMask`、`shadowTintMask`、`terrainShadowTerm`。
- 再比较 `CalcWater` / `CalcRefraction` 主体和 wrapper 后段。
- 做地形显隐 A/B capture 时，冻结或记录 `_GlobalTime`；否则 time-driven cloud/fog 会伪装成显隐导致的资源差异。

**2026-06-20 处理结果：**
- 曾短暂删除 entire `ApplySurfacePostProcessing` wrapper 做隔离，但 `debug5.rdc` 证明直出 `CalcRiverAdvanced` 会让水面更黑；不要把删除 wrapper 当作修复。
- 曾短暂把 procedural cloud 固定为 `cloudMask=0` 以获得确定性 A/B 对比，但用户要求“完全恢复到 `debug4.rdc`”后，当前源码已恢复完整 debug4-style wrapper，包括 `GetCloudShadowMask`、`_HasCloudShadowEnabled`、`_InverseWorldSize`、`_GlobalTime * 0.01f`、`cloudMask * 0.8f` cloud tint、`_MapSadowTint*`、`_TerrainSunnySunDir`、`ApplyOvercastContrast`、`_FogBegin2/_FogEnd2/_FogMax`、relative fog color/height/noise 链。
- 复查 `debug4.rdc` 后确认不能用简化 normal-y tint / fixed smoothstep fog 代替旧 wrapper。
- `_GlobalTime` 现在再次影响 surface wrapper 的 cloud tint；做地形显隐 A/B capture 时必须冻结或记录 time phase，否则会再次把 cloud 相位差误判为 terrain 资源差异。

### ❌ Mistake 44: 用 wrap sampler 采 foam ramp 的黑色边缘
**What to avoid:**
- 直接用 `FoamRampTexture.Sample(WaterTextureSampler, float2(foamFactor * FlowFoamMask, 0.5f))`，其中 `WaterTextureSampler` 是 wrap。
- 看到 `flowFoamMask=0` 后就认为 foam 必然为 0，而不验证 ramp lookup 实际颜色。

**Why it's bad:**
- CK3 `foam_ramp.dds` 文件内容本身正确，左端应接近黑；但 wrap + linear 在 `u=0` 会把左端和右端亮色边缘混合。
- 2026-06-19 RenderDoc 热修改确认：current `FoamRampTexture(t4)` 在 `u=0,y=0.5` 采样为 `[0.323,0.325,0.290]`，CK3 同一点为 `[0,0.0003,0]`。
- 这会导致 `FlowFoamMask=0` 时仍产生强 foam，表现为 surface 内大块白斑；问题不是 DDS 哈希、RootAsset 或 foam map UV。

**Correct approach:**
- foam ramp 应使用 lod0，并把 lookup U clamp 到半 texel 内，例如 `[0.5/256, 1 - 0.5/256]`，或绑定等价的 clamp sampler。
- 热修改时先分别输出 `FoamRampTexture(u=0)`、完整 `CalcFoamFactor` 和目标 CK3 ramp 采样，再落地 SDSL。
- 同时确认 `CalcFoamFactor` 的 `WorldSpacePosXZ` 参数使用 CK3 的 world-space XZ，而不是 map-unit XZ。

### ❌ Mistake 45: 把 bottom bank-fade alpha 复用到 surface pass
**What to avoid:**
- 在 `RiverSurface` 最终输出 alpha 里使用 `_BankFade` 的 `edgeFade1 * edgeFade2`。
- 看到 surface RGB 很暗后只继续调 `WaterFade`、refraction 或 FOW，而不检查 surface alpha 是否让浅岸完全不透明。

**Why it's bad:**
- CK3 bottom 和 surface 的 alpha 语义不同：bottom advanced alpha 可以使用 diffuse alpha / bank fade；surface alpha 使用 `saturate(Depth * 2.0 / _Depth) * Transparency * connectionFade`。
- 2026-06-19 更新后的 `debug.rdc` 中，岸边 `(1000,620)` 的 current alpha 是 `1.0`，目标 depth alpha 约 `0.303`。同一 refraction RGB 改用目标 alpha 后，RenderDoc pixel history 的 post-blend 从暗水覆盖结果变为由亮场景底色托起的结果。
- 如果 surface alpha 错为 `1.0`，低 `WaterFade` 的浅岸 see-through 会完全盖住 terrain/scene，表现为“surface 发黑”，但根因不是 bottom RGB 黑。

**Correct approach:**
- 对 surface pass 按 CK3 `jomini_river_surface.fxh`：先 `Depth = CalcDepth(Input.UV)`，再设置 `Color.a = saturate(Depth * 2.0 / _Depth) * Transparency * saturate((DistanceToMain - 0.1) * 5.0)`。
- 用 RenderDoc replacement 分别输出 `ck3Alpha/currentAlpha/riverUv.y`，再用 `rawRefractionRGB + ck3Alpha` 验证 blend 是否真的改善岸边覆盖。
- 不要把 bottom alpha 的 `_BankFade` 经验迁移到 surface，除非目标 capture 的 disasm 也证明该 pass 使用同一公式。

### ❌ Mistake 46: 把无地形视角发黑继续归因到 alpha/FoW/height
**What to avoid:**
- 在 terrain 不绘制的 capture 中，看到 river surface 随视角发黑后继续优先怀疑 surface alpha、FogOfWarAlpha 或 terrain height wrapper。
- 只看最终 surface RGB，不拆 `CalcRefraction -> CalcTerrainUnderwaterSeeThrough` 的 attenuation 和 `WaterColorTexture` 采样值。

**Why it's bad:**
- 2026-06-19 更新后的无地形 `debug.rdc` 已确认 surface shader 包含 CK3 depth-based alpha 修正，旧 `_BankFade` alpha 只剩 bottom pass。
- 热替换直接输出 raw `RefractionTexture` 后河面明显变亮，说明 bottom/refraction 输入不是黑源。
- 代表暗像素的 see-through attenuation 只有约 `0.21-0.26`，即 70% 以上颜色会被替换为 `WaterColorTexture` 的水色图；同点 `WaterColorTexture` 采样只有约 `0.002-0.015`，足以解释发黑。

**Correct approach:**
- 先热替换输出 `RefractionTexture.rgb + CK3 alpha`，确认黑色是否来自输入 RT。
- 再输出 `R=seeThrough attenuation, G=refractionDepth, B=toCameraDir.y`，验证视角相关性是否来自水下透视距离。
- 最后分别输出 surface-world 和 refraction-world 的 `WaterColorTexture` 采样；若二者均近黑，问题是当前绑定水色图/无地形 fallback，而不是 alpha、FOW 或 height slices。

### ❌ Mistake 47: 把 CK3 最终 swapchain 颜色当作 river surface 输出
**What to avoid:**
- 看到 CK3 在 event/draw `1146` 从暗变成深蓝后，直接把 `1146` 的屏幕颜色拿来要求当前 `RiverSurface` 输出。
- 把 current surface/main-scene RT 的暗色与 CK3 final swapchain 色直接比较。
- 只加曝光或 tonemap，期待 brown-biased surface 输入自然变成 CK3 深蓝。

**Why it's bad:**
- `ck3-river.rdc` 中 event `1146` 是 `numIndices=3` 的 fullscreen final composite，输出 swapchain，并绑定 `MainScene_Texture`、`RestoreBloom_Texture`、`FogBlurTexture_Texture`、`TonyMcMapfaceLUT_Texture`、`ColorCube_Texture`。
- 同一 CK3 像素在 river/main-scene 阶段可以只有约 `[0.022, 0.028, 0.030]`，到 final pass 后才变成约 `[0.192, 0.235, 0.251]`；中间 buffer 很暗是预期。
- 当前 `debug.rdc` 的 final pass 是 Stride tonemap，只采样一个 `Texture0`，没有 CK3 mapface LUT / ColorCube 链。RenderDoc 热替换为简化 CK3-like 曝光/对比后，代表像素从 `[0.122,0.106,0.071]` 变为 `[0.647,0.631,0.588]`，只是变亮成米棕色，没有变蓝，说明 hue 已在 surface/main-scene 输入阶段偏离。
- 如果只隔离 `WaterColorTexture`，暗值本身不是错误：CK3 代表像素直出约 `[0.0117,0.0504,0.0564]`，经过 event `1146` 变为 `[0.0706,0.3333,0.3529]`；current 代表像素直出约 `[0.0032,0.0136,0.0150]`，经过 current 原始 final 几乎仍是黑，但经过简化 CK3-like lift 变为 `[0.0000,0.3412,0.3765]`。

**Correct approach:**
- 分两层比较：先把 CK3 river surface/main-scene buffer 对 current surface/main-scene buffer，再把 CK3 final swapchain 对 current final swapchain。
- 判断 surface shader 等价时，使用 CK3 对应 river draw 的中间 RT，不使用 event `1146`。
- 判断最终观感时，单独检查 current 是否实现了 CK3 的 final composite：exposure、contrast、fog blur、bloom、Tony mapface LUT、ColorCube。
- 如果 current 中间输出已经 `R > G > B` 偏棕，曝光只能变亮，不能证明 WaterColorTexture / refraction source / post wrapper 已经等价。
- 如果隔离后的 `WaterColorTexture` 是低线性蓝青值，优先检查 final postprocess 是否缺 CK3 lift/color-grade；不要直接改 DDS 或把采样值永久调亮，否则一旦补上 final pass 会过曝。
- 2026-06-20 `debug5.rdc` 热替换 `WaterColorTexture.rgb * 8` 把代表像素从约 `[0.059,0.080,0.070]` 拉到 `[0.089,0.414,0.439]`，证明删除 wrapper 后 raw water body 偏黑；但恢复 `ApplySurfacePostProcessing` 后不要保留 `_WaterColorSurfaceLift` 或 `visibleWaterColor` floor，否则会把诊断增益叠加到正式路径。

### ❌ Mistake 48: 把有地形时的自动曝光压暗误判为 river bottom/surface 变黑
**What to avoid:**
- 只看最终 swapchain 上“有地形时河流更黑”，就去调 river bottom diffuse、surface water color、alpha 或 lighting gain。
- 忽略 Stride ToneMap 的 `LuminanceAverageGlobal` / `AutoExposure`，直接比较有地形和无地形的最终颜色。

**Why it's bad:**
- `debug.rdc`（无地形）与 `debug1.rdc`（有地形）显示，同一河心点在 river bottom draw 的 shader output 约为 `[0.162,0.123,0.080]`，surface output 约为 `[0.152,0.238,0.224]`，两份捕获基本一致。
- 分叉发生在后续 fullscreen ToneMap：有地形时 terrain HDR 把平均亮度从约 `0.344` 提到 `1.447`，自动曝光从约 `0.263` 降到 `0.133`，最终河心从约 `[0.196,0.282,0.271]` 被压到 `[0.106,0.165,0.153]`。

**Correct approach:**
- 先用 pixel history 比较 river draw 的 shader output，再比较 final ToneMap 的 `avgLuminance` / exposure。
- Editor 视口需要 CK3 scene light intensity `20` 给 river bottom 读 scene light，但不应让 Stride 自动曝光随 terrain 可见性漂移；当前 editor compositor 固定 `ToneMap.Exposure=-2.0 EV` 并关闭 `AutoExposure/AutoKeyValue/TemporalAdaptation`。

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

*Learning Document Version: 2.1*
