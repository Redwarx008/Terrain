# 地形编辑器架构概览
**从这里开始了解整个系统**

---

## TL;DR - 30 秒核心架构

**三层系统：**
- **Core（核心）**: 地形数据、高度图、流式加载
- **Rendering（渲染）**: GPU 实例化、LOD、虚拟纹理
- **Editor（编辑器）**: 笔刷系统、Avalonia UI、编辑操作

**关键原则：** 分离数据层、渲染层、编辑层

**当前状态：** Core ✅ | Rendering ✅ | Editor ✅ | Vegetation 🚧 | Path/River ✅

---

## 系统状态概览

### 核心层

| 系统 | 状态 | 文档 |
|------|------|------|
| **地形组件** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md) |
| **高度数据** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md) |
| **流式加载** | ✅ 已实现 | [terrain-streaming-design](../plans/terrain-streaming-design.md)；runtime preload 的顶层 Height/Detail fallback pages 必须 pinned。Quadtree 检查候选子节点是否 resident 时会刷新对应 Height/Detail VT page 的 LRU recency，避免子节点等待 sibling 凑齐期间被页表淘汰，导致漫游时回退到低细节或闪烁。 |
| **LOD 系统** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md)；`LOD delta > 1` 是当前算法允许的拓扑，渲染路径必须支持；若某个 RenderView 选择不到节点，`TerrainRenderFeature` 必须清空 `InstanceCount`，避免 shadow/临时视图沿用上一轮 terrain 实例。 |

### 渲染层

| 系统 | 状态 | 文档 |
|------|------|------|
| **地形渲染** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md) |
| **实例化渲染** | ✅ 已实现 | [instance-buffer-refactor](../plans/instance-buffer-refactor.md) |
| **材质系统** | ✅ 已实现 | - |
| **map TOML 规格** | ✅ 已记录 | [map-data-toml-formats](design/map-data-toml-formats.md)；2026-06-21 CK3 runtime cbuffer 对照确认 terrain `HeightScale=50.0`、`MaxHeight=50` refraction camera clamp 是两个不同常量。本项目默认 `height_scale=200` 可保留；2026-06-22 `debug2.rdc` 进一步证明 terrain `HeightScale` 也不能作为实际河面高度代理，河流 refraction clamp 现由 generated river mesh `Bounds.Maximum.Y` 推导为 `ceil(maxY + 1)`，shader 端继续用 `max(_, 50)` 保留 CK3 低地兜底。 |
| **虚拟纹理** | 🚧 进行中 | HeightMap VT 的高度 mip 必须保留偶数对齐源样本，不能用 2x2 平均；相邻 LOD patch 的 crack snap 依赖跨 mip 共享边界高度一致。 |

### 路径与河流层

| 系统 | 状态 | 文档 |
|------|------|------|
| **路径编辑** | ✅ 已实现 | [Phase 6](design/terrain-editor-design-phase-6.md) |
| **道路渲染** | ✅ 已实现 | [adr-013-vic3-path-rendering](log/decisions/adr-013-vic3-path-rendering.md) |
| **河流网格生成** | ✅ 已实现 | [2026-06-05-1](log/2026/06/05/2026-06-05-1-river-mesh-generation-fix.md)；2026-06-19 `RiverMapService.TracePath` 已改为在分支终点优先踏入唯一相邻的 semantic marker，避免真实 `rivers.png` 中 branch 在 junction 前一格提前断开并丢失 `Confluence/Bifurcation` endpoint；同日 `NormalizeDirection` 也补齐为 `Source/None -> Confluence/Bifurcation` 的有序归一，修正 `Confluence->None` / `Bifurcation->None` 段把 mesh tangent、parallax 与流向整体反过来的问题。2026-06-21 `river-mesh.rdc` 对比 CK3 `ck3-river.rdc` 确认当前中心线内部存在 `60°~77°` 单点折角，而 CK3 非端点多在约 `12°~26°`；`RiverMeshService.SmoothCenterline` 现对两轮以上平滑后的中心线追加更强的 bend relaxation，保留端点和 ribbon 拓扑，同时用回归测试把连续低分辨率河道折弯角限制到 `15°` 内。随后真实 `RiverMapService -> BuildCenterlines -> BuildRiverMesh` 集成测试证明可见硬角主要还来自最终 Catmull-Rom 采样过稀：固定 `1.0/0.5` 间距会分别留下约 `33°/17.44°` 边界角；当前改为按局部曲率自适应采样，直线段保留 `1.0`，急弯加密到 `0.25`，并在最终中心线按 smoothed XZ 双线性重新采样 terrain 高度，避免崎岖地形上最近邻高度量化形成河面台阶；回归测试覆盖 `<=12°` mesh 边界、长直/长曲河采样预算、长曲河边界质量、默认半宽 corridor 偏移上限、非平坦地形高度一致性和 invalid/overflowing height cache 降级。2026-06-21 `debug1.rdc` 热替换证明 bottom bank 的尖端收束会同时体现在 `RiverUV.y`、see-through attenuation 和 `RefractionDepth` 诊断信号上；后续把它直接归因到 mesh 并尝试五横截面 lane / miter clamp 是错误方向，源码已撤回到原二列 ribbon。后续 `debug2.rdc` 热替换确认真正尖端断裂来自 surface 对半分辨率 refraction alpha distance payload 的线性过滤，而不是 mesh 生成。2026-06-21 河流宽度生成现在从 `game/map/default.toml` 读取 `[settings].river_min_width` / `river_max_width` 作为 full-width 地图单位，并把每个 `rivers.png` palette index 线性映射到该范围；mesh 生成会把局部 half-width samples 带过中心线平滑和插值，因此浅蓝到绿色的局部宽度渐变会被保留，不再折叠为单一 `AvgHalfWidth`。 |
| **河流多 pass 渲染** | ✅ 已实现 | [adr-014-river-rendering-architecture](log/decisions/adr-014-river-rendering-architecture.md)；当前河流链路按 CK3 对齐为 `bottom -> refraction -> surface` 三段。bottom pass 已从早期的 world-UV/non-advanced 近似推进到更接近 CK3 advanced 的语义：`scaledRiverUv.x * _TextureUvScale`、tangent-UV 主采样、fixed 2/10 layer steep parallax、`bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2` alpha、scene-driven directional sun 与 scene skybox cubemap；2026-06-18 已移除旧 `* 3.0f` final gain，并把 lighting 改为 CK3 material BRDF（`0.25 * specular`、metalness diffuse/spec split、GGX direct、dominant specular IBL、Burley roughness-to-mip），使用 fake-depth 前的 submerged `bottomLightingPosition` 而不是 water-surface `streams.PositionWS` 计算 view/light，同时删除 `_BottomSpecularIntensity` river-local 参数链。2026-06-19 已把 bottom shadow 改为“Stride 负责 cascade 选择 + CK3 bottom shadow 投影/随机 disc kernel/bias/fade”的组合路径，旧 Stride 5x5 filter 与 normal-offset helper 已移除；同日 `RiverCommon` 的 refraction distance pack/unpack 补齐 CK3 `MaxHeight=50` camera clamp，避免俯视相机高度直接把 bottom alpha / surface refraction depth 带偏。2026-06-21 进一步确认高 `height_scale=200` 下固定 50 clamp 会截断 camera-relative payload；2026-06-22 `debug2.rdc` 证明 terrain `HeightScale` 仍可能低于实际 generated river mesh，高处右侧河面 `Y≈105` 已超过旧 `_RefractionMaxCameraHeight=100`，现通过 mesh `Bounds.Maximum.Y` 推导 `ceil(maxY + 1)`，再由 shader `max(_, 50)` 保留 CK3 低地兜底，并同步绑定到 scene seed、bottom、surface。bottom direct 读取 scene sun color，IBL 读取 scene skybox intensity，当前 editor scene 的 sun/environment 均为 `20`，因此 direct/IBL 的 scene-scale 比例与 CK3 保持 `1:1`。editor scene 现在把 CK3 warm sun 和 Jomini `environment_terrain_sunny` cubemap 作为 scene-level directional/skybox 输入，river bottom 继续只读取 scene light。terrain 现在默认参与 shadow caster，`RiverSceneSeed` 用 Presenter depth 写 camera-relative seed alpha；surface pass 使用目标截帧实际命中的 `CalcRiverAdvanced -> CalcWater` 分支：单次 flow normal、三层 ambient water-wave normal、base/refraction-depth 分离的 `CalcRefraction` 与 `WaterFade`、water-color Y 翻转/重采样、dedicated `WaterColorSampler` 与 cubemap reflection。2026-06-21 `debug2.rdc` 热替换确认 refraction RGB 可线性过滤，但 RT alpha 是 camera-distance payload，不能线性过滤后再 `DecompressWorldSpace`；`RiverSurface` 现在用 `_RefractionTextureSize` + `Texture2D.Load` 读取 alpha payload，base/offset/WaterFade 的 refraction depth 均走未过滤 payload。`debug-river-target-after.rdc` 已确认 bottom/surface 新 shader 和 cbuffer 参数进入 GPU，旧双 flow、old depth adapter、`safeDenom` parallax、旧 refraction offset 公式均不再出现。 |
| **河流摄像机高度可见性** | ✅ 已实现 | `map/default.toml` `[settings].river_max_visible_camera_height` 默认 `3000.0`，Editor Settings 以 Slider 可调并随 Save 写回。`RiverRenderFeature.Draw` 在分配 river seed/bottom/surface render targets 前读取当前 `RenderView` 摄像机 world Y，达到或超过该阈值时跳过整条 river 渲染链路，避免高空视角继续产生 river pass GPU 开销。 |

**2026-06-23 河流高度采样补充：** `C:\Users\Redwa\Desktop\debug.rdc` 中 EID 276/323 与 290/343 的 post-VS 河流 mesh 仍可见高度斜率突跳；源码复核确认 editor `IRiverTerrainHeightSource.SampleHeight` 通过 `TerrainManager.GetHeightAtPosition` 走整数 `Round` 采样。`RiverMeshService` 现在对 `IRiverTerrainHeightSource` 和 runtime `Func<int,int,float>` 两类离散高度源都在 mesh 服务内部做四点双线性重采样，避免真实 editor 高度源绕过此前只在测试 stub 中覆盖的双线性路径。`C:\Users\Redwa\Desktop\debug2.rdc` 随后证明 editor 中 `RiverMeshService` 会先于地形加载创建，构造期缓存的 `HeightmapWidth/HeightmapHeight` 仍为 0，导致河流 mesh 只落在 `SurfaceOffset` 高度；现在 `IRiverTerrainHeightSource` 路径在每次采样时读取当前宽高，避免延迟加载后继续使用 stale dimensions。`GetMapWorldSize` 同样对 height source 读取当前宽高，保证 reload 后 `MapWorldSize/MapExtent/Width` 与高度采样使用同一尺寸。新增回归测试 `curved river centerline bilinearly resamples nearest height source`、`river centerline uses height source dimensions loaded after mesh service construction` 和 `river mesh map extent uses reloaded height source dimensions` 锁定该行为。

**2026-06-18 复核补充：** 旧 `debug.rdc` 显示 `RiverSceneSeed` 的 RGB 已压到 CK3 seed 类似范围，但 alpha 恒定为 near clip `0.1`；原因是 editor 路径使用 `commandList.DepthStencilBuffer` 时没有读到有效 scene depth。`RiverRenderFeature` 现明确使用 `GraphicsDevice.Presenter.DepthStencilBuffer` 作为窗口化 editor/runtime 的 scene seed depth；Presenter、depth 或尺寸一致性前置条件用直接 `Debug.Assert` 表达，不再保留 `SelectSceneDepthSource` 或 command-list depth fallback。2026-06-18 03:00 新 `debug.rdc` 复核确认 Presenter depth 绑定后 EID 248 seed alpha 已变为 `12.3984..24.4375`，不再是 near clip 常量；随后 `RiverSceneSeed` 进一步改为写 camera-relative distance payload。2026-06-18 03:16 新 `debug.rdc` 复核确认该新版路径已进入截帧：EID 248 shader 使用 `ProjectionInverse/ViewInverse/Eye` 重建 world position 并写 alpha `4.82031..8.66406`，pixel `(471,282)` 为 `5.23047`。离屏/render-target presenter 若没有 depth，需要后续显式提供 scene-depth source。

**2026-06-23 1x1 scene color 保护补充：** `RiverRenderFeature` 的 seed pass 现在以实际 `commandList.RenderTargets[0]` 尺寸分配半分辨率 `SceneSeedColor/BottomColor`，并在 scene color 只有 `1x1` 时跳过整条 river pass。该尺寸常见于启动、窗口最小化或嵌入式 viewport 临时 0 尺寸被 swapchain/backbuffer clamp；如果继续执行，半分辨率目标也会是 `1x1`，Stride `ImageMultiScaler` 会因输入输出尺寸相同而抛出 `Input and output texture cannot have same size [(1,1,1)]`。

**2026-06-23 runtime 图形 API 配置补充：** 当前工作区 `Terrain.Windows` 实际设置为 `<StrideGraphicsApi>Direct3D11</StrideGraphicsApi>`，本地 `Stride.Graphics.4.3.0.2743.nupkg` 已用 `StrideGraphicsApis=Direct3D11` 重打，并确认 runtime 输出目录中的 `Stride.Graphics.dll` hash 匹配 `lib/net10.0/Direct3D11/Stride.Graphics.dll`。如果后续在不变更包版本的情况下切换后端，需要清理精确版本的 NuGet 全局缓存再 restore。Runtime 现不启用 `GameDrawFPS` profiling overlay。

**2026-06-18 bottom lighting 复核补充：** CK3 `ck3-river.rdc` 的 bottom PS EID 332 最终 `o0.xyz` 为 `mad o0.xyz, r0.xyz, r1.xyz, r2.xyz`，没有全局 `* 3.0f` 增益；本地旧 `debug.rdc` EID 290 曾在 `CalculateRiverBottomLighting(...)` 后追加 `* 3.0f`，导致代表像素从未放大的约 `[0.167, 0.142, 0.103]` 被推到 `[0.501, 0.427, 0.314]`。该增益已从 `RiverBottom.sdsl` 移除。随后用 `debug1.rdc` 与 CK3 trace 分解确认：CK3 代表像素 direct 为 `[0.1436,0.0920,0.0437]`，diffuse IBL 只加 `[0.0141,0.0118,0.00935]`，specular IBL 只加 `[0.0009,0.0010,0.0016]`；本地旧 shader 的 specular IBL 为 `[0.0110,0.0174,0.0233]`，偏高且偏蓝。当前 `RiverBottom` 已改为 CK3 material BRDF / dominant specular IBL，并改用 submerged bottom position 计算 view/light，而不是继续用 water-surface `streams.PositionWS` 或 `_BottomSpecularIntensity`。

**2026-06-18 scene lighting 补充：** 新 `debug.rdc` 复核确认 bottom pass 已绑定真实 shadow 和 scene cubemap，但 current scene 输入仍是白色太阳 `[20,20,20]` 与 HDR/blue Stride skybox，和 CK3 的 warm sun `[20,17.3568,15.0970]`、`CubemapIntensity=20`、低值 Jomini environment 不等价。Editor scene 现在加载 `Scene/Environment/jomini-environment-terrain-sunny`，用 `LightSkybox.Skybox.SpecularLightingParameters[SkyboxKeys.CubeMap]` 直接暴露该预过滤 cubemap，并把 directional light 设置为 CK3 `SunDiffuse/SunIntensity/ToSunDir`，不通过 river-local fallback 或 shader 常量补偿。后续 RenderDoc 复核确认 Stride `LightComponent.SetColor` 需要 gamma-space 输入；代码将 CK3 线性 `SunDiffuse` 先 `ToSRgb()` 再交给 `SetColor`，最终 bottom cbuffer 为 `_SceneSunColor=[20,17.3568,15.0970]`、`_EnvironmentIntensity=20`、`_EnvironmentMipCount=10`，并绑定 Jomini cubemap `ResourceId::284` 与 shadow atlas `ResourceId::7764`。后续 `debug-river-after-surface-alpha_frame798.rdc` 热替换证明当前 Stride cascade shadow helper 不是 CK3 bottom shadow 的等价实现，直接乘入会让 bottom RT 近黑；正式 shader 现保留 scene sun/IBL，但 direct light 暂时使用 unshadowed term，直到目标 shadow 投影路径移植完成。

**2026-06-18/19 river ribbon basis 与 segment direction 补充：** CK3 bottom 像素输入显示 river tangent 保留中心线 Y 坡度，normal 是与水平横向 side 和 sloped tangent 正交的 ribbon normal；当前旧 mesh 则把 `Tangent.Y` 清零并从 terrain height 差分采 normal，代表像素 normal 约 `[-0.0006,0.8536,-0.5209]`，会把 CK3 的黄棕河床 lighting 压暗。`RiverMeshService` 现改为保留 sloped centerline tangent，并用 `cross(side, tangent)` 生成向上的 ribbon normal。`debug-current-codex-ribbon-normal_frame870.rdc` 一度让固定 `-normalize(streams.RiverTangent)` 看似正确，但后续 `C:\Users\Redwa\Desktop\debug.rdc` 证明这是单帧/单方向误判：current 黑点的 `surfaceNormal` 直接打光有 `nDotL≈0.55`，而 current segment 经过 normal-map + TBN 后只有 `1e-5`；RenderDoc 热替换进一步证明单独翻 `bitangent` 可把 `nDotL` 拉到 `0.76`，整条 `tangent` 链翻转可到 `0.85`。根因不是 shader shadow，也不是 bottom diffuse，而是 `Confluence->None` / `Bifurcation->None` 段保持了错误的拓扑方向，导致 mesh tangent、parallax 和 surface/bottom 流向整体反向。`RiverBottom` 继续保持 CK3 源码的 `normalize(Input.Tangent)` 语义；修复点落在 `RiverMapService.NormalizeDirection`，现按 `Source/None -> Confluence/Bifurcation` 的方向等级归一 segment。CK3 bottom cbuffer 还确认 river material `_BankFade=0.025`，当前 `RiverRenderSettings`、`RiverRenderObject`、`RiverBottom/RiverSurface` SDSL 默认值和生成 key 已同步。

**2026-06-18 surface waterFade 补充：** CK3 `jomini_river_surface.fxh` 先计算 `Params._Depth = CalcDepth(Input.UV) * Input.Width + 0.1f`，再由 `jomini_water_default.fxh` 在 refraction 开启时重采 base refraction，并用 `Depth = min(Input._Depth, RefractionDepth)` 套用 `WaterFade = 1 - saturate((_WaterFadeShoreMaskDepth - Depth) * _WaterFadeShoreMaskSharpness)`。早期 `debug1.rdc` 热替换曾用 cross-section visual depth adapter 缓解本地窄 ribbon 的 fade 过暗，但这不是目标 shader 语义。当前 `RiverSurface` 已删除该 adapter 与 `ComputeRiverWaterFade(physicalDepth, depthFactor)` 旧路径，恢复为 CK3 的 base-refraction depth 路径；`debug-river-target-after.rdc` 的 surface disasm 已确认 `WaterFade` 单独走 base refraction sample 和 `min(InputDepth, RefractionDepth)`。

**2026-06-18 surface refraction offset 补充：** `debug2.rdc` 证明 surface 黑岸的直接来源不是 waterDiffuse、cubemap reflection 或 bottom RT 未写入，而是 `SampleRefractionSeeThrough` 在浅岸仍使用 distorted refraction。代表暗侧像素 base refraction 约 `[0.28,0.25,0.20]`，distorted refraction 约 `[0.02,0.02,0.02]`，且 `useDistorted=1`；CK3 `CalcRefraction` 会先用 base refraction depth 计算 `_WaterRefractionShoreMaskDepth=3` / sharpness `1` 的 `RefractionShoreMask`，这些像素 mask 为 `0`，因此 offset 应为 `0`。后续新 `debug.rdc` 复核确认已编译 shader 仍保留旧 `normalOffset * (0.0025 + depthFactor * 0.0035)` 公式，和 CK3 的 `mul(ViewMatrix, Normal.xz) * float2(-1/1920, 1/1080) * _WaterRefractionScale * RefractionShoreMask * _WaterRefractionFade` 不等价。`RiverSurface` 现显式绑定 `_ViewMatrix`，用最终 `waterNormal` 计算 view-space refraction offset，并直接乘 CK3 refraction scale / shore mask / fade；offset sample 后使用 `step(WorldSpacePos.y, OffsetRefractionWorldSpacePos.y)` 回退 base refraction，see-through 使用 offset 后的 `RefractionDepth`，旧 river-local offset magnitude、`/500` 归一化和 capped-depth see-through 已移除。

**2026-06-18 surface CalcWater 端口补充：** `debug1.rdc` 与 `ck3-river.rdc` 的逐 pass 复核确认，旧 surface pass 不是 CK3 `jomini_river_surface.fxh -> CalcWater` 语义等价实现：current `305` 河心输出约 `[0.26..0.29,0.50..0.53,0.63..0.66]`，direct-refraction hot-replace 后回到 `[0.27..0.30,0.19..0.22,0.12..0.14]`，说明偏蓝主要来自 surface 手写 composition。随后 RenderDoc energy gate 证明 CK3 cbuffer 暗水色和 see-through attenuation 可把同一 draw 压到目标低能量范围；replacement 必须输出 alpha `1.0`，因为 refraction alpha 是 camera-relative distance payload。`RiverSurface.sdsl` 现已改为 SDSL 可编译的 CK3-style 结构：`PSMain -> CalcRiverAdvanced -> CalcWater`，补齐 `CalcFoamFactor`、`CalcRefraction`、`CalcReflection`、`ComposeLight`、`GetNonLinearGlossiness` 等边界。`debug-river-target-after.rdc` 复核确认 `_GlobalTime`、`_FlattenMult`、三层 `_WaterWave*`、`_WaterFlowNormalFlatten`、`_WaterHeight`、`_WaterColorMapTintFactor` 等 cbuffer 参数已进入 GPU，surface disasm 只保留单次 flow normal 采样、三层 ambient normal 和 CK3 `CalcRefraction/WaterFade` 路径；旧 `flowUv1`、`SampleRefractionSeeThrough`、`effectiveDepth` 等模式搜索结果为 0。

**2026-06-19 surface lighting 复核补充：** 新 `C:\Users\Redwa\Desktop\debug.rdc` 复核确认正式 bottom 修正已进入 GPU：bottom PS 绑定中已无 `SceneShadowMapTexture`，bottom RT 统计范围约 `[0.1866..1.2168, 0.1320..1.1201, 0.0873..0.9922]`，实际代表像素不再是近黑；direct-refraction hot replacement 证明 surface 仍会把正常 refraction 压低。对照 CK3 `ck3-river.rdc` EID 460 cbuffer 后确认 surface 的水色、see-through、refraction、wave 参数一致，剩余不等价点在 `CalcWater` lighting 组合：本地仍用旧 `cloudShadowMask` helper 生成 gloss/specular/cubemap intensity，并用 `sunIntensityMask=smoothstep(0.05,0.1,glossMap)` 门控太阳光；CK3 源码直接使用 `_WaterZoomedInZoomedOutFactor`、`_WaterToSunDir`、`_WaterGlossScale`、`_WaterSpecularFactor`、`_WaterCubemapIntensity`，且不按 gloss map 掐掉 direct sun。`RiverSurface.sdsl` 已改为该目标语义，并新增文本测试防止旧 helper 与 glossMap sun gate 回归。

**2026-06-19/20 surface 后段资源/参数链补充：** CK3 surface 在 `CalcRiverAdvanced -> CalcWater` 后还会执行 cloud shadow、terrain shadow tint、strategy fog-of-war 与 map distance fog；本项目曾分阶段接入这些 wrapper 后段，并删除了 strategy-layer FOW 依赖。2026-06-20 `debug3.rdc` / `debug4.rdc` 证明 procedural cloud shadow 的 `_GlobalTime` 相位会让同一 river surface 在 capture 对比中从亮色跳到暗色；随后曾按 debug4 恢复完整 `ApplySurfacePostProcessing`。同日对 `C:\Users\Redwa\Desktop\debug4.rdc` 做 RenderDoc 热替换评估后，EID 149/176 删除 wrapper 的导出差异只有 16 个 RGB 像素、最大 1 LSB；按用户最终要求，当前源码已删除 `ApplySurfacePostProcessing` 以及它独占的 `GetCloudShadowMask`、terrain shadow tint、map distance fog、editor height slice 输入、`shadow_color.dds` 加载、`_HasCloudShadowEnabled` 和 `_InverseWorldSize` 绑定。`RiverSurface` 现在直接输出 `CalcRiverAdvanced` 的结果；策略层 FOW 仍不接入 river surface。这个决定牺牲 CK3 wrapper 后段语义，换取更少的时间相位敏感性、资源绑定和 shader 复杂度。

**2026-06-19 surface foam ramp 修正补充：** 最新 `C:\Users\Redwa\Desktop\debug.rdc` 证明 surface 白色块不是 bottom/refraction，也不是 shadow/fog，而是 `FoamRampTexture` 使用 wrap sampler 在 `u=0` 发生左右边缘双线性混合；current `u=0,y=0.5` 为 `[0.323,0.325,0.290]`，CK3 同点约 `[0,0.0003,0]`。`RiverSurface` 现对 foam ramp lookup U 做半 texel clamp 并用 `SampleLevel(..., 0)`，避免 `FlowFoamMask=0` 时仍产生 foam；`CalcFoamFactor` 的 detail/noise 坐标也改回 CK3 的 world-space XZ，而不是本地 map-unit XZ。

**2026-06-19/21/22 surface alpha、water-color 与高度 clamp 补充：** 更新后的 `debug.rdc` 证明 foam 白块已消失，但 bank-edge 仍被 surface 以 alpha `1.0` 写入暗 see-through 色。早期曾按 `jomini_river_surface.fxh` 里的 `CalcRiverSurface` 误判为 depth alpha；2026-06-21 复查 CK3 `river_surface.shader` 后确认实际入口是 `CalcRiverAdvanced(Input)._Color`，`Depth = CalcDepth(Input.UV)` 只进入 `CalcWater` / `WaterFade` / refraction，最终 alpha 由 `Transparency`、`DistanceToMain` 与 `_BankFade` 双边 edge fade 决定。`debug.rdc` 热替换证明 raw `RefractionTexture` 在坏点仍有棕色 bottom，临时调整 `_WaterSeeThroughDensity` 可改变透底程度，但 `debug2.rdc` 左右对照进一步证明本轮高处缺 bottom 的根因不是 density：右侧 dark pixel 的 surface/terrain `Y≈105`，而 surface draw cbuffer 仍为 `_RefractionMaxCameraHeight=100`。因此 `_WaterSeeThroughDensity=0.4f` 源码改动已撤回，默认保持 `0.8f`；当前正式修复是让 `RiverMeshService` 用 generated mesh `Bounds.Maximum.Y` 生成 `ceil(maxY + 1)`，并由 shader 端 `max(_RefractionMaxCameraHeight, 50.0f)` 保留 CK3 `MaxHeight=50` 低地兜底。CK3 源码确认真正控制链是：`CalcTerrainUnderwaterSeeThrough(RefractionDepth, RefractionWorldSpacePos, RefractionWaterColorMap, RefractionSample.rgb)` 使用 `_WaterSeeThroughDensity` 和 `Depth / ToCameraDir.y` 控制透底；`WaterFade` 另用 `min(Input._Depth, RefractionDepth)` 控制 lit water color 强度；最终 `FinalColor += lerp(Refraction, Reflection, Fresnel * WaterFade)`。当前 `RiverSurface` 已移除上一轮 `_SurfaceBankFade` workaround，恢复 `CalcRiverAdvanced` 的 `_BankFade` edge alpha，同时保持 CK3 的 `RefractionDepth` see-through 路径。bottom/water DDS 统一绕过 Stride 内容系统，从 `game/map/water` 直接 `Texture.Load`；`water_color.dds` 当前使用 `loadAsSrgb:false` 创建 UNorm/linear GPU texture view，`RiverSurface` 不再手动 `DecodeWaterColorSrgb`。

**2026-06-19 updated `debug.rdc` 复核补充：** 新 capture 的事件号已漂移到 current `223 -> 248 -> 276 -> 305`。其中 `223` 的透明阶段 scene RT 已是高亮 HDR 缓冲，代表像素约 `[2.1289,1.3848,0.8491]`；`248` 的 `RiverSceneSeed` 只是基于这个 HDR 源重建 depth payload，`276` 才是 bottom，`305` 是 surface。也就是说，这一轮排查确认 current pre-bottom/refraction source 仍不是 CK3 `ck3-river.rdc` 里那种独立暗色 payload，`commandList.RenderTargets[0]` 在当前 transparent stage 上不能直接等价成 CK3 `JominiRefraction` 输入。这是现在 river 与 CK3 仍有大幅观感差异的主要剩余根因之一；shader 语义已继续收敛，但 refraction source/timing 仍需后续专门处理。

**2026-06-20 editor ToneMap 复核补充：** `C:\Users\Redwa\Desktop\debug.rdc`（无地形）与 `debug1.rdc`（有地形）对比确认 river bottom/surface draw 的 shader output 基本一致；有地形时变黑发生在后续 Stride fullscreen ToneMap。地形 HDR 把 `LuminanceAverageGlobal` 从约 `0.344` 推到 `1.447`，自动曝光从约 `0.263` 降到 `0.133`，最终把河心从约 `[0.196,0.282,0.271]` 压到 `[0.106,0.165,0.153]`。Editor compositor 现在关闭 ToneMap `AutoExposure/AutoKeyValue/TemporalAdaptation` 并固定 `Exposure=-2.0 EV`，使 CK3 scene light intensity `20` 不再通过 terrain 平均亮度压暗河流。

**2026-06-21 river Stride lighting 补充：** `debug1.rdc`（无地形）与 `debug2.rdc`（有地形）进一步确认 terrain 的 HDR 输出显著高于 river，是因为 terrain 走 Stride standard material lighting，而 river bottom/surface 仍各自使用较低能量的自定义 lighting。当前已新增 `RiverStrideLighting.sdsl` 共享 mixin，集中实现 Stride-style direct Lambert、GGX direct specular、Schlick Fresnel、Smith-Schlick visibility、polynomial DFG、scene skybox specular、scene skybox diffuse IBL 与共享 scene shadow helper；diffuse IBL 采 scene cubemap 最低频 mip，避免高低地形边界处进入 terrain shadow 的 river bottom 只剩 specular IBL 而近黑。`RiverBottom` / `RiverSurface` 均 mix in 该 helper。`RiverRenderFeature` 现在通过 `RiverStrideLightingKeys` 把同一 scene directional light、shadow cascade、shadow atlas、skybox cubemap、skybox matrix/intensity/mip count 绑定给 bottom 和 surface。Surface 不再声明 `_DefaultEnvironmentSunDiffuse`、`_DefaultEnvironmentSunIntensity`、`_WaterToSunDir`，也不再使用 `ImprovedBlinnPhong`；bottom 不再保留本地 GGX/dominant-reflection IBL。河流仍保持自定义 bottom/refraction/surface pass、refraction、foam、waterFade、reflection 与 depth payload 语义。

**2026-06-20 river surface depth-bias 复核补充：** `C:\Users\Redwa\Desktop\debug.rdc` 的一整段河流在 terrain 打开时出现块状露地形，RenderDoc pixel/debug 对比确认根因是 surface 与 terrain depth 近共面：失败点 `(1180,640)` terrain depth `0.99582636`，surface `SV_Position.z=0.99584526`，水面只远约 `1.9e-5` 就被拒绝；通过点 `(800,620)` water 只近约 `3e-6`。CK3 `ck3-river.rdc` 中 surface draw 460 不输出 `SV_Depth`，通过后 depth history 仍保留 terrain draw 366 的值，但 surface `SV_Position.z` 稳定比 terrain depth 近约 `0.00297~0.002998`。因此当前修正点不是改 `RiverMeshService` 抬高顶点，而是让 river surface 保持 read-only depth、不写主 depth，并使用独立的负向 rasterizer depth bias；bottom pass 保留原小 bias。后续 `ck3-hidden-river.rdc` 与本地新 `debug.rdc` 的 raw D3D11 state 对比进一步确认：CK3 可见水面使用 `DepthBias=-50000`、`SlopeScaledDepthBias=0`、`DepthFunc=Less`，本地旧 `DepthStencilStates.DepthRead` 默认 `LessEqual` 会让等深 hidden river fragment 通过；`RiverRenderFeature` 现改为显式 read-only depth state 且 `DepthBufferFunction=Less`。更新后的 `debug.rdc` 又证明山体透河已经不是等深问题，而是完整 `-50000` bias 在旧 editor near=0.1、far=100000 的深度分布下可跨过约 `0.0015` 的遮挡深度差；CK3 同 raw 参数成立的前提是有效 near 约 `10`，其 hidden river 与 terrain 的 depth 差为 `0.12~0.15`，且 `DepthBias=-50000` 的可见水面 draw 不覆盖 hidden 像素。随后的新版 `debug.rdc` 仍可见前景山体横穿河线，pixel history 确认写入来自 surface draw `3346`；terrain `(800,413)` 的 `w=134.076`、depth `0.9992555` 反推实际 near 仍是 `0.1`，说明前一次只改运行时代码没有让资产相机/实际 RenderView 进入 CK3 深度分布。当前修正进一步把 `Terrain/Assets/MainScene.sdscene` 相机显式序列化为 `NearClipPlane=10`、`FarClipPlane=100000`，并且 asset scene 进入 editor 时剥离运行时 `BasicCameraController` 等非 editor 组件，只保留 transform/camera/light/background，避免运行时相机脚本与 embedded editor camera controller 竞争。最新 `debug.rdc` 的 surface draw `1458` 仍显示实际 near≈`0.1`，坏点 `(974,416)` 的无 bias water 约为 `0.999226`、terrain depth 为 `0.998260`，只有完整 `-50000` 才会把它推到山前；因此 surface rasterizer 现在按实际 `RenderView.NearClipPlane` 连续缩放 bias：`abs(-50000) * pow(actualNear / 10, 0.5)` 并上限到 CK3 raw magnitude。near=`10` 保持 `-50000`，当前 near=`0.1` 推导为 `-5000`，仍足够覆盖原始 `1e-5` 共面差但不会跨过当前约 `0.0009` 的山体遮挡差。

### 编辑器层

| 系统 | 状态 | 文档 |
|------|------|------|
| **高度编辑** | ✅ 已实现 | [terrain-editor-design-phase-2](design/terrain-editor-design-phase-2.md) |
| **笔刷系统** | ✅ 已实现 | [terrain-editor-design-phase-2](design/terrain-editor-design-phase-2.md) |
| **Avalonia UI** | ✅ 已实现 | 原 ImGui 编辑器已迁移 |
| **气候蒙版（ClimateMask）** | ✅ 已实现 | R8 格式，1/4 高度图分辨率，规则驱动材质索引 |
| **季节过滤（Season）** | ✅ 已实现 | EditorState.ActiveSeason 驱动规则求值 |
| **纹理刷** | ✅ 已实现 | [2026-04-06-3](log/2026/04/06/2026-04-06-3-terrain-texture-brush-implementation.md) |
| **纹理导入增强** | ✅ 已实现 | [texture-auto-normal-import-and-inspector](design/texture-auto-normal-import-and-inspector.md) |
| **数据同步机制** | ✅ 已实现 | [2026-04-07-1](log/2026/04/07/2026-04-07-1-unified-terrain-data-sync.md) |
| **材质索引图增强** | ✅ 已实现 | [2026-04-07-2](log/2026/04/07/2026-04-07-2-index-map-enhancement.md) |
| **Undo/Redo（Chunk事务）** | ✅ 已实现 | [2026-04-07-5](log/2026/04/07/2026-04-07-5-chunk-based-undo-redo-implementation.md) |
| **Editor 作者态启动** | ✅ 已实现 | 自动补齐 `default.toml` / `descriptor.toml` / `biome_settings.toml`，并在这些 TOML 顶部保留固定注释模板；缺失 `heightmap.png` 时以待补资源模式进入；缺失 `material_id` 时为每个缺失项创建运行时默认槽位并禁止 `Save` / `Export`；缺失贴图文件时仅该槽位逐通道降级：`albedo` 回退洋红缺失材质纹理、`normal` 回退 flat normal、`properties` 仅记录诊断 |
| **旧项目持久化（TOML）** | ❌ 已移除 | Editor 固定 Terrain 工作区；旧 ProjectManager/TomlProjectConfig 已删除 |
| **植被编辑** | 🚧 进行中 | [terrain-editor-design-phase-3](design/terrain-editor-design-phase-3.md) |
| **导出系统（IExporter）** | ✅ 已实现 | 当前保留 Terrain `.terrain` 导出；旧 Biome Config 导出已移除；Export 进度使用 owned top-level window，避免嵌入式 Stride viewport child HWND 遮挡 |

### 未来系统

| 系统 | 状态 | 文档 |
|------|------|------|
| **植被 LOD** | 📋 规划中 | [terrain-editor-design-phase-5](design/terrain-editor-design-phase-5.md) |
| **路径系统** | 📋 规划中 | [terrain-editor-design-phase-6](design/terrain-editor-design-phase-6.md) |
| **GPU 优化** | 📋 规划中 | [terrain-editor-design-phase-7](design/terrain-editor-design-phase-7.md) |

**图例：** ✅ 已实现 | 🚧 进行中 | 📋 规划中 | ❌ 未开始

---

## 关键架构决策

### 0. 统一数据同步机制
**问题：** 多种笔刷需要同步不同类型的数据到 GPU
**方案：** 使用 `TerrainDataChannel` 枚举和统一的 `MarkDataDirty(channel)` 接口
**权衡：** 抽象层 vs 直接调用
**参考：** Godot heightmap 插件的 `notify_region_change(p_map_type)` 设计

### 1. 材质索引图数据格式 (RGBA)
**问题：** 传统 splatmap 受通道数限制，且缺少投影和旋转控制
**方案：** 使用 R8G8B8A8_UNorm 格式，R=索引, G=权重, B=投影方向, A=旋转角度
**权衡：** 内存翻倍 vs 功能增强
**参考：** Unity IndexMapTerrain 项目的 Index Map 设计
**优势：**
- 支持 256 种材质
- 3D 投影解决悬崖纹理拉伸
- 随机旋转打破平铺重复

### 1.5 气候蒙版驱动材质索引 (R8, 1/4 高度图)
**问题：** 直接绘制材质索引图效率低、难以表达海拔/坡度/季节规则
**方案：** ClimateMask（R8, 1/4 高度图分辨率）存储气候 ID，通过规则栈（海拔/坡度/季节）求值生成 MaterialIndexMap（1/2 分辨率）
**权衡：** 间接映射 vs 直接绘制 — 间接映射更适合程序化规则，1/4 分辨率节省内存
**关键：** 1 个 ClimateMask 像素映射到 2x2 MaterialIndex 像素，坐标转换均需 ×4（ClimateMask→Heightmap）或 ×2（ClimateMask→MaterialIndex）

### 2. 四叉树 LOD
**问题：** 大地形需要不同细节级别
**方案：** 四叉树分割，GPU 选择 LOD
**权衡：** 内存 vs 视觉质量

### 2. 流式加载
**问题：** 地形太大无法全部加载
**方案：** 按需加载地形块
**权衡：** 实现复杂度 vs 内存占用

### 3. GPU 实例化
**问题：** 植被对象太多
**方案：** 实例化渲染，一次绘制调用
**权衡：** GPU 内存 vs CPU 开销

### 4. ImGui 编辑器
**问题：** Stride 原生编辑器限制
**方案：** 使用 ImGui 自定义编辑器
**权衡：** 学习曲线 vs 灵活性

### 5. Undo/Redo Chunk 事务模型
**问题：** 区域快照在笔触开始阶段容易退化为整图复制，导致 Paint Mode 卡顿
**方案：** 参考 Godot heightmap 插件，采用“笔触期间标记 chunk，提交时抓取 before/after”的事务模型
**权衡：** 命令结构更复杂 vs 明显更稳定的交互性能与更干净的历史栈
**参考：** [2026-04-07-5](log/2026/04/07/2026-04-07-5-chunk-based-undo-redo-implementation.md)

### 6. Editor/Runtime 共用本地 LaunchSetting 与 SVN Game 入口
**问题：** `game/` 将由 SVN 管理，`LaunchSetting.json` 不应继续作为 `game/` 根目录判定条件，也不应再由 Git 跟踪。
**方案：** 生产路径优先使用编译进 `Terrain.dll` 的 `TerrainWorkspaceRoot` 元数据向上扫描工作区同级 `game/`，避免 Stride Game Studio 宿主进程的 `AppContext.BaseDirectory` 或按字节加载项目程序集后不可靠的 `Assembly.Location` 指向引擎源码输出目录时找不到项目资源；元数据不可用时才回退到 `Terrain` 程序集所在目录。如果起点本身已经位于目录名为 `game` 且包含 `map/` 的合法根，也会直接接受该根。`LaunchSetting.json` 固定放在生产入口解析出的有效 app 目录旁，缺失时自动生成默认文件。Editor 与 Runtime 都先通过共享的 `GameResourceResolverBootstrap` 构建 `base(gameRoot) + enabled absolute-path mods`，再进入各自 bootstrap。
**关键：** `mods[*].Root` 保持绝对路径语义；Editor 仍允许缺失 `.terrain` 并把 `biome_mask.png` / `biome_settings.toml` 作为作者态 Export 输入；Runtime 只严格要求 `.terrain`、`default.toml` 与 runtime material descriptor，不再要求 biome authoring 资源。`CreateForAppDirectory(appRoot)` 保留给测试和显式工具入口，生产代码使用 `CreateForTerrainAssemblyDirectory()` / `FindFromTerrainAssembly()`，其中 `FindFromTerrainAssembly()` 优先消费 `TerrainWorkspaceRoot` 元数据。

### 7. 导出系统（IExporter 模式）
**问题：** 编辑器中的修改无法直接导出为运行时 .terrain 文件，需依赖独立的 TerrainPreProcessor
**方案：** IExporter 接口 + ExportManager 单例，每种导出类型实现接口并注册；TerrainExporter 从内存状态直接导出
**权衡：** 在 Editor 内重写导出逻辑 vs 引用 TerrainPreProcessor 库；选择重写以避免跨项目依赖
**关键：** `.terrain` v8 写入 `HeightMap VT + DetailIndex VT + DetailWeight VT`。HeightMap 使用 padding=2；两个 detail stream 使用 padding=1、RGBA8 packed `DetailControlPixel`，由 Editor Export 读取作者态 `biome_mask.png` / `biome_settings.toml` 后 bake。2026-06-23 RenderDoc 复核确认地形接缝可由相邻 LOD 在同一世界 XZ 读取不同高度 mip 值触发；HeightMap mip 生成现在保留偶数对齐源样本，保证 crack-snap 后共享边界的高度跨 mip 一致。Detail mip 不能平均 RGBA 数值，当前解包 2x2 source detail index/weight、按 material id 聚合权重、选 top4 后重新归一化并打包。2026-06-22 Export bake 热路径已移除逐 detail texel 的 `List`/`Dictionary`/LINQ top4 聚合，改为固定小缓冲以降低分配和排序开销，同时用行为测试锁定输出语义；大图 detail texel bake 与 detail mip 聚合现在按行并行，并以阈值避免小图并行调度开销。Exporter 先写同目录临时文件，完整成功后 replace/move 到目标，失败或取消时删除临时文件并保留旧目标。Export 期间 `EditorShellViewModel.IsExporting` 驱动模态进度 owned top-level window，并与 Save 共用可变更命令/视口输入阻断。

### 8. 虚拟资源系统驱动 Runtime 地形加载
**问题：** Runtime 依赖组件上的显式文件路径和旧 BiomeConfig TOML，无法表达 base + mod 覆盖顺序
**方案：** Runtime 固定通过 `TerrainWorkspaceRoot` 元数据优先定位工作区 `game/` 资源根；如果元数据不可用，则回退到从 `Terrain` 程序集所在目录向上定位。如果起点本身已在目录名为 `game` 且包含 `map/` 的合法根，也会直接接受该根。随后从有效 app 目录旁的 `LaunchSetting.json` 读取或自动生成本地 mod 配置；base 作为隐式根，按启用 mod 顺序构建 `GameResourceResolver`，再通过 `GameRuntimeResourceBootstrap` 解析 `map/default.toml` 与固定 companion 资源
**权衡：** 不保留旧路径兼容，迁移更直接但资源入口更统一
**关键：** `TerrainComponent` 不再保存资源路径；`.terrain` 仍由 `bundle.TerrainDataPath` 直接读取；Runtime 会忽略 `default.toml` 中的 `heightmap` 声明，并直接从 `.terrain` v8 读取 HeightMap / DetailIndex / DetailWeight virtual texture payload。`biome_mask.png` 和 `biome_settings.toml` 只属于 Editor authoring/export，不再进入 Runtime bootstrap 或启动时 detail 构建。若 `terrain.terrain` 缺失或 `.terrain` payload/header 无效，`TerrainProcessor` 记录错误日志并保持 terrain 未初始化；同配置失败后不会逐帧重复重试。

### 9. 河流渲染采用 RiverComponent → RiverProcessor → RiverRenderObject → RiverRenderFeature
**问题：** 仅靠 editor service 或临时 `ModelComponent` 预览无法承载河流的独立 mesh 生命周期、双 pass 渲染和视口调试模式。
**方案：** 将河流作为独立渲染子系统接入 Stride 渲染管线：
- `RiverRenderingService` 仅负责 editor façade（接收 `RiverSegment`、驱动 mesh 生成、同步可见性）
- `RiverComponent` 持有快照化 `RiverMeshData`
- `RiverProcessor` 负责版本同步与 `RiverRenderObject` 生命周期
- `RiverRenderFeature` 负责河底/水面双 pass、分离的 `SceneSeedColor`/`BottomColor` 折射缓冲与调试光栅状态
- 河流 shader 统一使用 `TransformationWAndVP` 生成 `PositionWS / PositionH / DepthVS`
- `RiverResourceLoader` 负责从 `game/map/water` 直接加载 bottom diffuse/normal/properties/depth 与 water flow/foam/ambient/water-color 等 DDS 文件；这些 Bottom/Water 纹理不再通过 Stride `.sdtex`、`Terrain.Editor.sdpkg` `RootAssets` 或 `ContentManager` 管理，缺失本地文件时先写 `Terrain` 日志再让原始文件异常冒泡。`River/Environment/reflection-specular` 仍保持 Stride 内容资源；`RiverRenderFeature` 将这些资源绑定到 `RiverBottom` / `RiverSurface`
- Runtime 河流也接入同一套 `Terrain.Rendering.River` core：`Terrain/Assets/MainScene.sdscene` 持有运行时 `RiverSystem` entity 和 `RiverComponent`，`Terrain/Assets/GraphicsCompositor.sdgfxcomp` 持有 `RiverRenderFeature` 注册。`RiverProcessor` 等待 runtime terrain height data 初始化后加载可选 `game/map/rivers.png`，通过 `RiverMapService` / `RiverMeshService` 生成 mesh 并写回 `RiverComponent`；`TerrainRuntimeResourceBundle` 只承载资源路径和宽度配置，不创建 scene object。
- Runtime 不在 `TerrainComponent` 上常驻完整 `ushort[]` heightmap。`TerrainComponent` 只暴露离散 `GetHeight(int sampleX, int sampleZ)`；内部经 `TerrainQuadTree -> TerrainStreamingManager` 从 `.terrain` 按需读取对应 height tile。CPU height page cache 属于 streaming manager：height page 上传 GPU 后继续保留，直到对应 GPU resident page 因 `MaxResidentChunks` 淘汰时同步释放。Runtime detail VT 不再启动时生成：`TerrainStreamingManager` 直接从 `.terrain` reader 读取 `ReadDetailIndexPage` / `ReadDetailWeightPage`，并上传到两个 RGBA8 detail texture arrays。顶层 Height/Detail fallback pages 在 preload 后常驻 pinned；quadtree child residency 检查会 touch 已 resident 的候选子节点页，避免这些页在 sibling 未全齐、尚未成为 render node 前被 LRU 当作冷页淘汰。River 需要连续高度时自己对 4 个离散 sample 做双线性插值，Terrain 不承担 river-specific 插值策略。
- 河底 pass 使用 CK3-style dual-source blending：RT0 RGB 和 RT0 alpha 都按 `src1_alpha / inv_src1_alpha` 与目标折射缓冲混合，RT1 alpha 作为 coverage 权重；bottom 当前对齐到 CK3 这帧实际使用的 non-advanced 分支，`BottomDiffuse/Normal/Properties` 主采样使用 parallax 后的 `worldUv`，depth/profile 继续从 `tangentUv` 计算，steep parallax 使用固定 2/10 layer 与 CK3 插值公式，alpha 为 `fadeOut * connectionFade * saturate(depth * 13.0f)`；bottom lighting 绑定当前 `LightingView` 的 directional light/skybox、shadow cascade 数据、shadow atlas、scene cubemap intensity/rotation，shadow 路径使用 Stride cascade 选择叠加 CK3 bottom shadow 的投影/随机 disc kernel/bias/fade，旧 5x5 filter 与 normal-offset helper 已移除。Editor scene 会提供 CK3 warm sun 与 Jomini terrain sunny cubemap 作为实际 scene light 输入；当前 scene sun intensity 与 skybox intensity 都是 `20`，因此 bottom direct/IBL 的 scene-scale 比例保持 `1:1`。2026-06-18 已移除 `* 3.0f` final gain，并改用 CK3 material BRDF / dominant specular IBL，删除 `_BottomSpecularIntensity` river-local 参数链；`SceneSeedColor` 由 `RiverSceneSeed` image shader 写半分辨率场景种子，RGB 做 Reinhard 压缩，alpha 从 Presenter scene depth 重建 world position 后写 camera-relative distance payload；surface pass 读取折射缓冲并走 `CalcRiverAdvanced -> CalcWater`：单次 flow normal、三层 ambient water-wave normal、water-color、foam 与 cubemap reflection，water-color map UV 执行 CK3 Y 翻转并在 refraction world position 重采样，seed-only 边缘像素 `a <= 0` 会回退到当前水面 world position，`CalcRefraction` 先用 base refraction depth 计算 shore mask，再用最终 `waterNormal` 经 `_ViewMatrix` 转 view-space 后乘 `float2(-1/1920, 1/1080)`、CK3 refraction scale / shore mask / fade，offset 后用 `step(WorldSpacePos.y, OffsetRefractionWorldSpacePos.y)` 回退 base refraction；`WaterFade` 独立重采 base refraction 并使用 CK3 `min(InputDepth, RefractionDepth)` 公式；顶点流中的宽度是归一化 half-width，shader 给 CK3 depth / flow 使用前会还原为 full-width。2026-06-21 复核 CK3 `jomini_river_bottom.fxh` 后确认早期 RT0 alpha direct-write 是本地偏差；若后续再出现边缘 payload 混合问题，应优先修 pre-bottom/refraction payload writer，而不是改偏 bottom blend state。
- 河流 mesh 顶点 basis 对齐 CK3：tangent 保留中心线 Y 坡度，normal 使用水平横向 side 与 sloped tangent 的正交 ribbon normal，不再用 terrain height 差分 normal 驱动 bottom lighting。
**权衡：** 架构更复杂，但换来可维护的渲染生命周期、与 Stride render stage 的正确集成，以及后续扩展空间。
**参考：** [adr-014-river-rendering-architecture](log/decisions/adr-014-river-rendering-architecture.md)

---

## 关键文件

### 核心运行时
| 文件 | 职责 |
|------|------|
| `Terrain/Core/TerrainComponent.cs` | 地形组件主入口 |
| `Terrain/Rendering/TerrainRenderFeature.cs` | 渲染特性 |
| `Terrain/Streaming/TerrainStreaming.cs` | 流式加载 |

### 编辑器
| 文件 | 职责 |
|------|------|
| `Terrain.Editor/ViewModels/EditorShellViewModel.cs` | 主窗口状态与命令；`Save` 使用 `AuthoringSaveProgress` 驱动模态进度，打开进度窗口后先让 UI 调度一次，再由 UI 线程捕获保存快照并在后台执行作者态资源写回；`Export Terrain` 使用 `ExportProgress` 驱动独立导出进度窗口；Save/Export 期间禁用可变更命令 |
| `Terrain.Editor/Views/SaveProgressWindow.axaml` | Save 进度 owned top-level window；避免 Avalonia inline overlay 被嵌入式 Stride native child HWND 遮盖 |
| `Terrain.Editor/Views/ExportProgressWindow.axaml` | Export Terrain 进度 owned top-level window；避免 Avalonia inline overlay 被嵌入式 Stride native child HWND 遮盖 |
| `Terrain.Editor/Services/TerrainManager.cs` | 地形管理服务 |
| `Terrain.Editor/Services/HeightEditor.cs` | 高度编辑服务 |
| `Terrain.Editor/Services/PaintEditor.cs` | 材质绘制服务 |
| `Terrain.Editor/Services/ClimateEditor.cs` | 气候蒙版笔刷服务 |
| `Terrain.Editor/Services/ClimateMask.cs` | 气候蒙版数据（R8, 1/4 高度图） |
| `Terrain.Editor/Services/ClimateRuleService.cs` | 气候定义和规则栈管理 |
| `Terrain.Editor/Services/Commands/HistoryManager.cs` | Undo/Redo 历史事务管理 |
| `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs` | 笔触 Chunk 跟踪与去重 |
| `Terrain.Editor/Services/MaterialSlotManager.cs` | 材质槽位管理；缺失 normal fallback 贴图上传每个 mip 时显式传入 mip-sized `ResourceRegion`，与 runtime fallback 路径一致，避免 D3D12 压缩 mip 上传使用 full-texture 默认 region |
| `Terrain.Editor/Services/RiverRenderingService.cs` | 河流渲染 façade（mesh 同步、显隐控制、桥接编辑器与渲染组件） |
| `Terrain.Editor/Services/EditorDirtyState.cs` | 编辑器 dirty 状态跟踪（不携带项目路径） |
| `Terrain.Editor/Services/Resources/EditorBootstrapService.cs` | 启动时按 `TerrainWorkspaceRoot` / `Terrain` 程序集目录解析出的有效 app 目录旁 `LaunchSetting.json` 构建 Editor 资源会话 |
| `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs` | descriptor + biome settings 的作者态材质恢复、缺失 `material_id` 补位与诊断聚合 |
| `Terrain.Editor/Services/Resources/EditorResourceSession.cs` | 当前命中的虚拟资源实体路径与写回目标 |
| `Terrain.Editor/Services/Resources/EditorAuthoringSaveSnapshot.cs` | 作者态保存的不可变快照，由 `TerrainManager.CreateAuthoringSaveSnapshot` 在 UI 线程捕获，后台保存只消费快照并写文件 |
| `Terrain.Editor/Services/Resources/AuthoringSaveProgress.cs` | 作者态保存进度报告，驱动 Save 模态进度覆盖层 |
| `Terrain.Editor/Services/Resources/*Writer.cs` | 作者态资源写回到当前命中的实体文件 |
| `Terrain/Resources/GameResourceRootLocator.cs` | 从 `TerrainWorkspaceRoot` 元数据优先定位工作区 `game/` 资源根，元数据不可用时回退 `Terrain` 程序集目录 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | 地形实体（含统一数据同步接口） |
| `Terrain/Rendering/River/RiverComponent.cs` | 河流 mesh 快照组件 |
| `Terrain/Rendering/River/RiverProcessor.cs` | 河流组件到渲染对象的同步处理器 |
| `Terrain/Rendering/River/RiverRenderObject.cs` | 河流 GPU 顶点/索引缓冲与 bounds |
| `Terrain/Rendering/River/RiverRenderFeature.cs` | 河流河底/水面双 pass 渲染特性 |
| `Terrain/Streaming/TerrainStreaming.cs` | Runtime height/detail VT streaming；height、DetailIndex、DetailWeight 均来自 `.terrain` v8 reader；CPU height page cache 随 GPU resident page 生命周期释放；顶层 fallback pages 在 preload 时 pinned，保证子页缺失或流式加载 churn 时仍可回退到粗 LOD；child residency 检查可 touch resident pages，防止候选子页等待 sibling 期间被 LRU 淘汰 |
| `Terrain/Materials/RuntimeMaterialManager.cs` | Runtime descriptor-driven material texture array 管理；缺失 normal 贴图时生成 flat normal fallback，并对每个 mip 使用显式 `ResourceRegion` 上传，避免 D3D12 压缩 mip 默认 region 问题 |
| `Terrain.Editor/Rendering/NativeViewport/NativeStrideViewportHost.cs` | 原生 Stride 视口宿主；`SetInputBlocked` 在 Save/Export 模态期间阻断视口输入并要求 Stride game flush 当前输入状态 |
| `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` | 注册河流 RenderFeature 并创建编辑器侧 RiverSystem；Save/Export 模态期间响应输入阻断，释放相机/笔刷状态并 flush 鼠标锁定与笔触输入 |
| `Terrain.Editor/Brushes/` | 笔刷系统 |
| `Terrain.Editor/Services/Export/IExporter.cs` | 导出器接口（可扩展） |
| `Terrain.Editor/Services/Export/ExportManager.cs` | 导出管理器（注册、执行、错误回滚） |
| `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` | `.terrain` v8 文件导出实现，写 HeightMap + baked DetailIndex/DetailWeight VT |
| `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs` | Editor authoring biome 规则到 baked DetailIndex/DetailWeight RGBA8 control buffers 的转换；热路径使用固定小缓冲做材质贡献聚合，避免逐 texel 集合分配；大图按行并行 |

### 着色器
| 文件 | 职责 |
|------|------|
| `Terrain/Effects/Build/` | LOD 构建；`TerrainComputeDispatcher` 写当前帧节点、生成 `LodMap` 与 neighbor mask；`TerrainRenderFeature` 在空选择时清空实例数，避免非主视图复用 stale draw state |
| `Terrain/Effects/Material/` | 材质着色器 |
| `Terrain/Effects/River/RiverBottom.sdsl` | 河底 pass（底部 diffuse/normal/properties/depth 采样、dual-source alpha、折射缓冲底色） |
| `Terrain/Effects/River/RiverSurface.sdsl` | 水面 pass（flow normal、ambient normal、water-color、foam/foam ramp/foam map、reflection/specular、折射采样） |
| `Terrain/Effects/River/RiverVertexStreams.sdsl` | 河流自定义顶点语义 |
| `Terrain/Effects/River/RiverWaterCommon.sdsl` | 河流水体共用函数 |

---

## 设计阶段索引

| 阶段 | 内容 | 状态 |
|------|------|------|
| [Phase 1](design/terrain-editor-design-phase-1.md) | 基础地形渲染 | ✅ 完成 |
| [Phase 2](design/terrain-editor-design-phase-2.md) | 高度编辑工具 | ✅ 完成 |
| [Phase 3](design/terrain-editor-design-phase-3.md) | 植被系统基础 | ✅ 设计完成 |
| [Phase 4](design/terrain-editor-design-phase-4.md) | 高级功能 | 📋 规划中 |
| [Phase 5](design/terrain-editor-design-phase-5.md) | 植被扩展 | 🆕 新增 |
| [Phase 6](design/terrain-editor-design-phase-6.md) | 路径系统 | 🆕 新增 |
| [Phase 7](design/terrain-editor-design-phase-7.md) | 渲染优化 | 🆕 新增 |

---

## 参考项目

- **Godot MTerrain Plugin** (`E:\reference\Godot-MTerrain-plugin`)
  - 贝塞尔曲线网络
  - 地形变形适配
  - 草地系统

- **Unity GPU Indirect** - GPU 实例化渲染参考
- **Terrain3D** - LOD 系统参考

---

## 快速 FAQ

**"我在哪里添加 [功能]？"**
→ 检查上面的文件表，找到对应的系统

**"如何修改地形高度？"**
→ 通过 `HeightEditor` 服务，使用笔刷系统

**"如何添加新的笔刷类型？"**
→ 继承 `IBrush` 接口，在 `Terrain.Editor/Brushes/` 添加

**"这是 Core 还是 Editor 逻辑？"**
→ Core = 运行时数据；Editor = 编辑时操作；Rendering = GPU 渲染

---

*最后更新: 2026-06-23*
*状态: 反映当前实现状态*
