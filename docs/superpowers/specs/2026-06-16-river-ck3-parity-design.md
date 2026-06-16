# River CK3 对标渲染设计

**Date**: 2026-06-16  
**Status**: Approved Draft  
**Author**: Codex

---

## 1. 背景

当前 Editor 河流渲染已经具备独立的 `bottom pass` 与 `surface pass`，并且资源也已补齐：

- `bottom pass` 在当前截帧中对应 `debug.rdc` 的 `184`
- `surface pass` 在当前截帧中对应 `debug.rdc` 的 `213`
- 对照参考为 `ck3-river.rdc` 的 `332` 与 `460`

但从 RenderDoc 对比与热替换验证来看，当前结果与 CK3 仍有本质差异：

1. 当前 `bottom pass` 输出虽然已经可见，但河床整体过暗，岸边没有 CK3 那种暖棕色 bank 过渡。
2. 当前 `surface pass` 透出的 `refraction` 大部分只是把错误的 `bottom/refraction` 缓冲原样显示出来，因此最终差异主要不是水面最后一层颜色，而是更早的链路就已经错了。
3. 当前实现虽然加载了和 CK3 一致的底部贴图资源，但 shader 采样语义、深度塑形、alpha 计算、以及 refraction seed 路径都没有真正对齐 CK3。

已经确认的关键事实：

- `bottom-diffuse.dds`、`bottom-normal.dds`、`bottom-properties.dds`、`bottom-depth.dds` 与 CK3 原始资源逐字节一致。
- 当前 `184` 只输出 `BottomDiffuse.rgb` 时，河床立即显示为暖棕色，说明贴图本身没有问题。
- 当前 `184` 输出 `BottomDiffuse.a` 时，河道区域几乎为常数 `1.0`，因此不能依赖该贴图 alpha 复现 CK3 的 advanced bank fade。
- 当前 `184` 改成按河道 UV 采样 `BottomDiffuse` 时，画面立刻出现明显沿河条带纹理，说明当前 `worldPos.xz` 采样语义是核心偏差。

因此，本次方案 C 的目标不是局部调参数，而是把整条链路按 CK3 的语义重新对齐：

- `bottom pass`
- `refraction seed / buffer`
- `surface pass`

---

## 2. 目标

1. 让当前 Editor 河流的 `bottom -> refraction -> surface` 结果在结构和视觉趋势上对齐 CK3，而不是继续依赖临时倍增、提亮或屏蔽黑边等补丁。
2. `bottom pass` 输出应具备 CK3 类似的河床暖棕色、边缘过渡与深度塑形。
3. `refraction` 缓冲应主要承载正确的河床信息，而不是被错误的 seed/downsample 污染。
4. `surface pass` 应建立在正确的 `bottom/refraction` 基础上，而不是试图用最后一层颜色掩盖上游错误。
5. 每一层都要能在 RenderDoc 中独立验证：
   - 当前 `184` 对 CK3 `332`
   - 当前 `213` 对 CK3 `460`

---

## 3. 非目标

- 不在本轮追求 100% 像素级复刻 CK3 的所有水体细节。
- 不在本轮引入与 CK3 完全一致的全部外部 lighting/shadow/fog 系统。
- 不在本轮顺手重构与河流无关的 terrain 或 renderer 模块。
- 不在本轮为了追结果继续使用 `_BottomDiffuseMultiplier` 之类的“遮羞参数”。
- 不把本轮工作限定为单个 shader 小修；本轮明确覆盖完整河流链路。

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| 总体策略 | 按 CK3 语义整体对齐 `bottom -> refraction -> surface` 三段链路 |
| `bottom pass` 采样语义 | 不再使用单纯 `worldPos.xz * _BottomUvScale` 作为唯一底图采样语义，改为引入与 CK3 相同的 `TangentUV + parallax` 主路径 |
| 河床深度塑形 | 对齐 CK3 的 `BottomNormal.b` 参与深度塑形、`_BankAmount` 与 `_DepthWidthPower` 联合作用 |
| 河岸 alpha | 不再只用 `depth * 13` 逻辑；改为 `FadeOut * FadeToConnection * smoothstep(bank)`，并在 advanced 版本中允许底图 alpha 参与 |
| refraction seed | 不再把“场景缩小一份”视为正确语义；seed 路径必须服务于正确 bottom 信息透传 |
| 验证顺序 | 先验证 `bottom pass` raw 输出，再验证 `refraction`，最后验证 `surface` |
| 调试方法 | 先用 RenderDoc 热替换锁定根因，再落工程实现 |

---

## 5. 现状与 CK3 的关键差异

### 5.1 当前 `RiverBottom.sdsl`

当前实现位于 [RiverBottom.sdsl](</E:/Stride Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:107>)。

当前主要特征：

- 通过 `ComputeBottomWorldUv(...)` 使用 `worldPosition.xz` 采样底图
- `BottomDepthTexture` 独立参与深度因子
- `alpha = bottomEdgeFade * connectionFade * transparency`
- 没有 `_BankAmount`
- 没有 `_OceanFadeRate`
- 没有 advanced 版本中的 `smoothstep` 河岸淡出
- 没有 advanced 版本中的 `Input.UV.x * _TextureUvScale`

这导致两个问题：

1. 河床采样缺乏 CK3 的河道局部 UV 语义，表现成沿世界坐标平铺。
2. 河岸过渡完全由程序曲线控制，缺少 CK3 advanced 版本的 bank fade 语义。

### 5.2 CK3 `CalcRiverBottomAdvanced`

CK3 参考位于 [jomini_river_bottom.fxh](</E:/SteamLibrary/steamapps/common/Crusader Kings III/jomini/gfx/FX/jomini/jomini_river_bottom.fxh:282>)。

其关键语义：

1. `Input.UV.x` 先乘 `_TextureUvScale`
2. 基于 `BottomNormal` 参与的 parallax 计算出 `WorldUV` 与 `TangentUV`
3. `Depth = CalcDepth(TangentUV, BottomNormal)`
4. `Diffuse/Properties/Normal` 使用 `TangentUV` 采样
5. `Alpha = Diffuse.a * FadeOut * FadeToConnection * EdgeFade1 * EdgeFade2`
6. `EdgeFade1/2` 来自 `smoothstep(0, _BankFade, ...)`

这意味着 CK3 的河床不是“世界坐标平铺的一条带”，而是“河道 UV 驱动、带 parallax 的局部材质层”。

### 5.3 当前 `surface pass`

当前实现位于 [RiverSurface.sdsl](</E:/Stride Projects/Terrain/Terrain.Editor/Effects/RiverSurface.sdsl:141>)。

虽然已经具备：

- flow normal
- ambient normal
- foam
- `RefractionTexture`

但此前在 RenderDoc 中已经验证：

- 当前 `213` 的最终输出与 raw `RefractionTexture` 高度一致
- 因此最终差异主要不是水面最后一层，而是前面的 `bottom/refraction` 链路本身

### 5.4 当前 `RiverRenderFeature`

当前实现位于 [RiverRenderFeature.cs](</E:/Stride Projects/Terrain/Terrain.Editor/Rendering/River/RiverRenderFeature.cs:45>)。

当前关键语义：

- `BottomColor` 在绘制 bottom 前，会先由 `ImageScaler(SamplingPattern.Linear)` 从 scene color 下采样 seed 一份
- `surface pass` 读取 `BottomColor` 时使用 `LinearClamp`

这会带来两个风险：

1. 河流较窄时，half-res 下采样和再次线性采样会把河岸周围 terrain 颜色混进 refraction。
2. `BottomColor` 既被当成“scene seed”，又被当成“bottom pass 混合后的 refraction 结果”，语义不够清晰。

---

## 6. 方案 C 的总体架构

方案 C 不是单点修正，而是对齐整条渲染链。

### 6.1 bottom 阶段

bottom 阶段负责生成“可用于河底透视”的正确河床信息。

设计要求：

1. 对齐 CK3 advanced 版本的 UV 语义。
2. 对齐 CK3 advanced 版本的深度塑形语义。
3. 对齐 CK3 advanced 版本的河岸 alpha 语义。
4. 输出仍维持：
   - `ColorTarget.rgb = 河床颜色`
   - `ColorTarget.a = 压缩后的河底世界空间信息`
   - `ColorTarget1 = dual-source alpha`

### 6.2 refraction seed / buffer 阶段

此阶段负责构建 surface 读取的底层颜色缓冲。

设计要求：

1. 明确区分“场景 seed”与“bottom 结果”。
2. 不允许 seed/downsample 语义污染窄河岸边的颜色。
3. 必须能在 RenderDoc 中直接验证 raw refraction 是否接近 CK3。

### 6.3 surface 阶段

surface 阶段在正确的 raw refraction 之上叠加水体法线、泡沫、颜色与反射。

设计要求：

1. 与 CK3 的河岸 alpha 和连接 fade 语义一致。
2. refraction 的使用建立在正确 bottom 输出基础上。
3. 若 surface 仍有风格差异，只能在 bottom/refraction 已正确后再调。

---

## 7. bottom pass 详细设计

### 7.1 新增或对齐的参数

当前 bottom shader 需要引入或对齐以下 CK3 语义参数：

- `_TextureUvScale`
- `_BankAmount`
- `_OceanFadeRate`
- `_ParallaxIterations`
- `_Depth`
- `_DepthWidthPower`
- `_DepthFakeFactor`
- `_BankFade`

其中：

- `_TextureUvScale` 用于拉伸河道纵向材质频率
- `_BankAmount` 用于控制河岸深度压缩
- `_OceanFadeRate` 用于河流与大水体连接时的 fade
- `_ParallaxIterations` 用于 steep parallax 近似

### 7.2 深度函数改造

当前实现使用独立 `BottomDepthTexture` 驱动一部分深度因子，但 CK3 advanced 的核心深度函数在 [jomini_river.fxh](</E:/SteamLibrary/steamapps/common/Crusader Kings III/jomini/gfx/FX/jomini/jomini_river.fxh:88>)：

- 使用 `BottomNormal.b` 作为 sampled depth
- 用 `_BankAmount` 改变横截面分布
- 用 `_DepthWidthPower` 控制曲线宽度

本轮设计要求：

1. `CalcDepth` 主逻辑改为对齐 CK3 advanced。
2. `BottomDepthTexture` 不再作为主深度塑形来源。
3. 若仍保留 `BottomDepthTexture`，只能作为兼容或调试用途，不能继续主导结果。

### 7.3 Parallax 与采样 UV

本轮 `bottom pass` 必须引入 CK3 的两级 UV：

- `TangentUV`
- `WorldUV`

对齐规则：

1. `Input.UV.x` 乘 `_TextureUvScale`
2. 用 `BottomNormal` 参与 steep parallax
3. `Diffuse/Properties/Normal` 主采样使用 `TangentUV`
4. `WorldSpacePos.xz` 通过 parallax 偏移修正

这是本轮最关键的语义对齐点之一。

### 7.4 Alpha 语义

当前 alpha 在 [RiverBottom.sdsl](</E:/Stride Projects/Terrain/Terrain.Editor/Effects/RiverBottom.sdsl:133>) 为：

```text
depth fade * connection fade * transparency
```

本轮改为 advanced 语义：

```text
Diffuse.a * FadeOut * FadeToConnection * EdgeFade1 * EdgeFade2
```

其中：

- `FadeOut = min(UnderOceanFade, Input.Transparency)`
- `EdgeFade1 = smoothstep(0, _BankFade, UV.y)`
- `EdgeFade2 = smoothstep(0, _BankFade, 1 - UV.y)`

注意：

- 当前 CK3 贴图 alpha 是否在运行时总是承载 bank mask，并不是本轮唯一前提
- 即使 `Diffuse.a` 在当前导入版本里为常数，也必须先把 shader 语义对齐
- 之后再根据运行结果决定是否补充显式 bank mask 资源策略

### 7.5 Lighting

本轮不把 lighting 当作主根因，但仍要保证其语义不继续偏离：

1. 保留当前 `CalculateRiverBottomLighting(...)` 结构作为第一阶段实现基础。
2. 优先修正采样 UV、深度与 alpha；只有在 raw bottom 仍明显偏离时，才继续对照 CK3 `GetMaterialProperties / CalculateSunLighting` 深挖 lighting。
3. 禁止使用额外 multiplier 强行提亮。

---

## 8. refraction seed / buffer 详细设计

### 8.1 语义问题

当前 [RiverRenderFeature.cs](</E:/Stride Projects/Terrain/Terrain.Editor/Rendering/River/RiverRenderFeature.cs:141>) 把 scene color 下采样到 `BottomColor`，再在其上叠加 bottom。

这条路径的问题在于：

1. `BottomColor` 同时承担“场景 seed”和“最终 refraction 缓冲”两种语义。
2. `SamplingPattern.Linear` 与后续 `LinearClamp` 会在窄河岸边产生颜色串扰。

### 8.2 设计方向

方案 C 中，refraction 路径需要显式分成两部分：

1. 场景 seed 的生成
2. bottom pass 写入后的 refraction 结果

设计要求：

- 必须保留一个可供 surface 使用的 `RefractionTexture`
- 但不再允许其结果被“为了图省事的 scene downsample”主导

### 8.3 实现口径

本轮采用以下实施顺序：

1. 先保留现有资源结构，避免一次性拆太多 render target。
2. 调整 seed 与读取 sampler 语义，减少岸边串色。
3. 若结果仍不接近 CK3，则再把 seed 与 bottom 结果分拆成两个独立 RT。

优先级判断：

- 若 `bottom pass` 自身修正后 raw refraction 已接近 CK3，则不强制本轮重构全部 RT 结构
- 若 `bottom pass` 修正后仍被 scene seed 明显污染，则必须分拆 RT

### 8.4 GPU 路径要求

用户已经明确指出不应把缩放逻辑理解为 CPU 方案。

本轮实现要求：

- 保持 GPU 路径
- 若继续使用 `ImageScaler`，则明确它只是 GPU image effect
- 若需要更严格控制采样语义，可以替换为更明确的 blit/copy 路径

---

## 9. surface pass 详细设计

### 9.1 保留的内容

当前 `RiverSurface.sdsl` 已经补齐以下 CK3 水面资源：

- `FlowNormalTexture`
- `FoamTexture`
- `FoamRampTexture`
- `FoamMapTexture`
- `FoamNoiseTexture`
- `WaterColorTexture`
- `ReflectionSpecularTexture`

这些资源可以继续作为当前 surface 风格实现的基础。

### 9.2 需要对齐的内容

需要优先对齐的不是所有花纹，而是：

1. 河岸 alpha 语义
2. connection fade 语义
3. refraction 深度与透视链路
4. 与正确 bottom 结果的衔接方式

### 9.3 Alpha 规则

surface alpha 应遵循：

- river transparency
- connection fade
- bank fade
- zoom/flatmap 场景下的现有兼容项

但河岸淡出的主语义应与 CK3 相同，即以 bank fade 为主，而不是继续依赖错误的底层黑边去“显得更细”。

### 9.4 bottom / refraction 优先级

surface 不负责掩盖上游错误。

执行规则：

1. 如果 raw refraction 错，先修 `bottom/refraction`
2. 只有 raw refraction 正确后，才允许细调 surface 的水色、foam、reflection

---

## 10. 参数绑定与运行时改造

### 10.1 `RiverBottom.sdsl`

需要新增或调整的 stage 参数：

- `_TextureUvScale`
- `_BankAmount`
- `_OceanFadeRate`
- `_ParallaxIterations`

并重写：

- 深度函数
- parallax 逻辑
- 采样 UV 逻辑
- alpha 逻辑

### 10.2 `RiverRenderFeature`

需要在 [RiverRenderFeature.cs](</E:/Stride Projects/Terrain/Terrain.Editor/Rendering/River/RiverRenderFeature.cs:201>) 扩展参数绑定：

1. 为 `bottomEffect` 传入新增 river 参数。
2. 为 `surfaceEffect` 传入与 CK3 语义对应的 bank/depth/fade 参数。
3. 明确 `RefractionTexture` 的来源与 sampler 语义。

### 10.3 `RiverRenderObject`

若现有 `RiverRenderObject` 尚未承载这些参数，需要新增运行时字段，至少包括：

- `MapExtent`
- `TextureUvScale`
- `BankAmount`
- `OceanFadeRate`
- `ParallaxIterations`
- 与 water/bottom 相关的基础风格参数

参数来源可以先走 editor 默认值，后续再外露到 authoring 配置。

---

## 11. 验证计划

本轮验证必须基于 RenderDoc 证据，不接受只看主观截图就判断“差不多了”。

### 11.1 bottom pass 验证

目标：

- 当前 `debug.rdc` 的 `184`
- 对齐 CK3 `332`

验证内容：

1. raw RT 导出结果是否出现 CK3 类似的暖色河床
2. 岸边是否由 bank fade 控制，而不是黑边
3. `pick_pixel` 的河道内部像素亮度与色相是否接近 CK3 趋势
4. 热替换与落地 shader 结果是否一致

### 11.2 refraction 验证

目标：

- 当前 `213` 中读取的 `RefractionTexture`
- 对齐 CK3 `460` 中的 `RefractionTexture`

验证内容：

1. raw refraction 是否仍被地表颜色严重污染
2. 岸边 refraction 是否能透出正确河床
3. 若 surface 直接输出 `RefractionTexture`，画面是否接近预期

### 11.3 surface 验证

目标：

- 当前 `213`
- 对齐 CK3 `460`

验证内容：

1. 最终水色是否建立在正确河床之上
2. 河流是否仍是“线条状”
3. 泡沫、流向与反射是否只表现为上层细节，而不是掩盖底层错误

### 11.4 自动化验证

至少保留以下验证流程：

1. `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug`
2. 若自动 capture 可稳定工作，补充固定帧抓取并复核 draw
3. 若自动 capture 仍不稳定，则记录人工 RenderDoc 验证点

---

## 12. 风险与取舍

### 12.1 为什么不继续靠提亮参数修

因为热替换已经证明：

- 当前河床过暗并非单一 lighting 参数错误
- 更核心的偏差是 UV、深度、alpha 与 refraction 链路语义

继续加 multiplier 只会把错误结果提亮，不会得到 CK3 的结构。

### 12.2 为什么本轮允许分阶段实现

方案 C 是整链路方案，但实现仍需分阶段验证：

1. 先修 bottom
2. 再修 refraction
3. 最后修 surface

这样做不是回退到小修，而是为了保持证据链清晰，避免再出现“改了很多但不知道哪一项生效”的问题。

### 12.3 为什么不先全面重写 water shading

因为当前最大偏差已证实发生在更早的 `bottom/refraction` 层。

若先大改水面层，只会掩盖根因。

---

## 13. 实施顺序

1. 重写 `RiverBottom.sdsl` 到 advanced 语义：
   - 新参数
   - parallax
   - `TangentUV`
   - 新深度函数
   - 新 alpha
2. 扩展 `RiverRenderFeature` / `RiverRenderObject` 参数绑定。
3. 校正 refraction seed / sampler 语义，必要时拆分 scene seed 与 bottom result。
4. 让 `RiverSurface.sdsl` 读取修正后的 refraction，并对齐河岸 alpha 规则。
5. 用 RenderDoc 逐层验证 `184/213` 对 `332/460`。

---

## 14. 成功标准

满足以下条件即可认为方案 C 落地成功：

1. `bottom pass` 自身就能输出接近 CK3 的暖色河床，不再主要表现为黑暗带状区域。
2. 岸边能看到正确的河床/河岸过渡，而不是依赖黑边制造轮廓。
3. raw `RefractionTexture` 透出的颜色与 CK3 趋势一致，不再被明显的 terrain seed 污染。
4. 最终河面建立在正确河床之上，和 CK3 的差距主要收敛到高层风格细节，而不是底层语义错误。
5. 整个修复过程有明确的 RenderDoc 证据链支撑，而不是仅靠截图主观判断。
