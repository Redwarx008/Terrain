# 当前功能清单

**最后更新：** 2026-06-21
**状态图例：** ✅ 完成 | 🚧 进行中 | 📋 规划中 | ❌ 未开始

> **注意：** 2026-04-15 至 2026-05-14 期间有大量开发但无会话日志记录。以下功能状态基于当前代码库实际验证。

---

## 核心层 (Core)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形组件 (TerrainComponent) | ✅ | `Terrain/Core/TerrainComponent.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 高度数据 (HeightData) | ✅ | `Terrain/Core/TerrainComponent.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 流式加载 (Streaming) | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [terrain-streaming-design](../plans/terrain-streaming-design.md) |
| LOD 系统 (QuadTree) | ✅ | `Terrain/Core/` | [Phase 1](design/terrain-editor-design-phase-1.md) |

## 渲染层 (Rendering)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形渲染 (TerrainRenderFeature) | ✅ | `Terrain/Rendering/TerrainRenderFeature.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 实例化渲染 (Instancing) | ✅ | `Terrain/Rendering/` | [instance-buffer-refactor](../plans/instance-buffer-refactor.md) |
| 材质系统 (IndexMap RGBA) | ✅ | `Terrain/Effects/Material/` | [Phase 2](design/terrain-editor-design-phase-2.md) |
| 虚拟纹理 (VT) | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [runtime-indexmap-streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| 路径渲染 — 道路 | ✅ | `Terrain.Editor/Effects/PathRoadSurface.sdsl` | [ADR-013](log/decisions/adr-013-vic3-path-rendering.md) |
| 路径渲染 — 河流 | ✅ | `Terrain.Editor/Rendering/River/`, `Terrain.Editor/Effects/River*.sdsl`, `game/map/water/`, `Terrain.Editor/Assets/River/Environment/`, `Terrain.Editor/Assets/Scene/Environment/` | [ADR-014](log/decisions/adr-014-river-rendering-architecture.md)；河流渲染使用 CK3 对齐的 `bottom -> refraction -> surface` 三段链路。bottom pass 现已切到更接近 CK3 advanced 的分支语义：`scaledRiverUv.x * _TextureUvScale`、tangent-UV 主采样、fixed 2/10 layer steep parallax、`bottomDiffuse.a * fadeOut * connectionFade * edgeFade1 * edgeFade2` bank fade、scene-driven directional sun 与 scene skybox cubemap；Bottom/Water DDS 现统一放在 `game/map/water` 并由 `RiverResourceLoader` 文件直读，不再通过 Stride `.sdtex` / RootAsset / `ContentManager` 管理；`River/Environment/reflection-specular` 仍保持 Stride 内容资源。2026-06-18 已移除旧 `* 3.0f` final gain，并改用 CK3 material BRDF（`0.25 * specular`、metalness diffuse/spec split、GGX direct、dominant specular IBL、Burley roughness-to-mip），lighting 使用 fake-depth 前的 submerged `bottomLightingPosition`，同时删除 `_BottomSpecularIntensity` river-local 参数链。2026-06-19 已把 bottom shadow 改为“Stride 负责 cascade 选择 + CK3 bottom shadow 投影/随机 disc kernel/bias/fade”的组合路径，旧 Stride 5x5 filter 与 normal-offset helper 已移除；同日 `RiverCommon` 的 refraction distance pack/unpack 也补齐了 CK3 `MaxHeight=50` camera clamp，避免俯视相机下 alpha/depth payload 与 CK3 偏离。2026-06-21 高 `height_scale=200` 复核确认固定 50 clamp 会截断高地形/高相机下的 distance payload；当前 `_RefractionMaxCameraHeight=max(50, HeightScale)` 已由 `RiverMeshService -> RiverRenderObject -> RiverRenderFeature` 绑定到 `RiverSceneSeed`、bottom 和 surface。bottom direct 读取 scene sun color，IBL 读取 scene skybox intensity，当前 editor scene 的 sun/environment 均为 `20`，因此 direct/IBL 的 scene-scale 比例与 CK3 保持 `1:1`。Editor scene 现在提供 CK3 warm sun 与 Jomini terrain sunny cubemap 作为 scene-level lighting 输入；Editor compositor 同时固定 ToneMap `Exposure=-2.0 EV` 并关闭自动曝光，避免高亮 terrain HDR 平均亮度把河流/bottom 压黑。`RiverSceneSeed` 使用 Presenter depth 写 camera-relative seed alpha；surface pass 采用目标截帧实际使用的 `CalcRiverAdvanced -> CalcWater` 路径：单次 flow normal、三层 water wave ambient normal、base/refraction-depth 分离的 `WaterFade` 与 `CalcRefraction`、water-color Y 翻转/重采样、dedicated `WaterColorSampler`、cubemap reflection。`C:\\Users\\Redwa\\Desktop\\debug.rdc` 新复核还确认 current `event 223` 的 transparent-stage scene RT 本身就是高亮 HDR 源，因此 current pre-bottom/refraction source 仍不等价于 CK3 的暗色 pre-bottom payload；这条链仍需后续继续校正。 |
| 河流 scene seed depth 绑定 | ✅ | `RiverRenderFeature.SeedSceneColorFromScene`, `RiverSceneSeed.sdsl` | 2026-06-18 旧 `debug.rdc` 复核确认 seed shader 已运行且 RGB 压缩有效，但仅使用 `commandList.DepthStencilBuffer` 时 alpha 恒定为 near clip `0.1`；现已明确改为窗口化 editor/runtime 的 `GraphicsDevice.Presenter.DepthStencilBuffer`，Presenter/depth/尺寸一致性用直接 `Debug.Assert` 约束，不再使用 `SelectSceneDepthSource` 或 command-list fallback；2026-06-18 03:00 新 `debug.rdc` 复核确认 EID 248 seed alpha 已变为 `12.3984..24.4375`；随后 `RiverSceneSeed` 已改为从 scene depth 重建 world position 并写 camera-relative distance，与 bottom alpha 解码语义一致；2026-06-18 03:16 新 `debug.rdc` 已确认 EID 248 编译并运行 `ProjectionInverse/ViewInverse/Eye` 路径，seed alpha 为 `4.82031..8.66406`，pixel `(471,282)` 为 `5.23047`；离屏/render-target presenter 若没有 depth，需要后续显式注入 scene-depth source |
| 河流 bottom lighting final gain / BRDF | ✅ 已修正 | `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | 2026-06-18 RenderDoc 复核确认 CK3 bottom PS 没有全局 `* 3.0f`，本地旧增益会把代表像素从约 `[0.167,0.142,0.103]` 推到 `[0.501,0.427,0.314]`，因此已移除并用测试防回归。随后 trace 分解确认 CK3 direct `[0.1436,0.0920,0.0437]`、diffuse IBL `[0.0141,0.0118,0.00935]`、specular IBL `[0.0009,0.0010,0.0016]`；当前 shader 已改为 CK3 material BRDF / dominant specular IBL，删除 `_BottomSpecularIntensity`，并改用 submerged bottom position 参与 lighting。 |
| 河流 CK3 scene lighting 输入 | ✅ 已实现 | `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`, `Terrain.Editor/Assets/Scene/Environment/jomini-environment-terrain-sunny.sdtex` | 2026-06-18 新 `debug.rdc` 证明 shader 与 shadow/cubemap 绑定已进入 GPU，但旧 scene 输入仍不等价于 CK3：白色太阳与 HDR/blue skybox 会让 bottom 偏冷。Editor scene 现在直接加载 CK3 `environment_terrain_sunny.dds` 作为 `LightSkybox` specular cubemap，并设置 CK3 `SunDiffuse=[1,0.867838,0.754852]`、`SunIntensity=20`、`ToSunDir=[-0.818182,0.545455,-0.181818]`、cubemap intensity `20`；由于 Stride `LightComponent.SetColor` 在 linear color space 下会把输入从 gamma 转 linear，代码将 CK3 线性 diffuse 先 `ToSRgb()` 再设置。RenderDoc 复核 `debug-current-codex-fixed.rdc` 确认 bottom cbuffer 已为 `_SceneSunColor=[20,17.3568,15.0970]`、`_EnvironmentIntensity=20`、`_EnvironmentMipCount=10`，bottom 读取 Jomini cubemap 与真实 shadow atlas。后续热替换确认当前 shadow helper 不能代表目标 bottom shadow，因此现阶段 scene shadow 只保持绑定，不参与 bottom direct light 能量。 |
| 河流 ribbon basis / bottom TBN | ✅ 已修正 | `Terrain.Editor/Services/RiverMeshService.cs`, `Terrain.Editor/Effects/RiverBottom.sdsl` | `RiverMeshService` 保留中心线 Y 坡度并生成向上的 ribbon normal，不再使用 terrain height 差分 normal。`debug-current-codex-ribbon-normal_frame870.rdc` 曾让固定 `-normalize(streams.RiverTangent)` 看似能变亮，但后续 `debug.rdc` 证明固定取反会在 `+X` tangent 河段把 normal-map X 分量投到背光：代表像素 shader 已编译取反仍只有 `nDotL≈0.17`，no-flip hot-replace 为 `0.72/0.82`。CK3 源码使用 `normalize(Input.Tangent)` 直接构建 bottom TBN，因此 `RiverBottom` 使用 `normalize(streams.RiverTangent)`，不再硬编码 tangent 符号；若仍出现局部黑段，应查 river segment/mesh 方向一致性。 |
| 河流 surface waterFade / refraction | ✅ 已修正并复核 | `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | CK3 surface 路径为 `Params._Depth = CalcDepth(UV) * Input.Width + 0.1`；`CalcRefraction` 先采 base refraction，用 `Depth = min(Input._Depth, RefractionDepth)` 只计算 refraction shore mask，再用最终 `waterNormal` 的 view-space offset 做 distorted sample，并用 `step(WorldSpacePos.y, OffsetRefractionWorldSpacePos.y)` 拒绝高于水面的 offset；see-through 使用 offset 后的 `RefractionDepth`。`WaterFade` 另行重采 base refraction，并用同一 CK3 `min(Input._Depth, RefractionDepth)` 公式，不再保留 cross-section visual depth adapter 或 `SampleRefractionSeeThrough` 旧路径。2026-06-21 `debug2.rdc` 热替换确认尖端 bank 消失来自半分辨率 refraction alpha distance payload 被线性过滤：坏点 `(1100,540)` 的 linear depth 饱和到 `>=12`，point/Load depth 只有约 `3.33`。当前 `RiverSurface` 保留 RGB 线性采样，但用 `_RefractionTextureSize` + `Texture2D.Load` 读取 alpha payload，base/offset/WaterFade 的 `DecompressWorldSpace` 都不再吃 filtered alpha。`debug-river-target-after.rdc` 复核确认 GPU surface disasm 已包含该 base/offset/refraction-depth 分离路径，且 `flowUv1`、`normalOffset * (0.0025 + depthFactor * 0.0035)`、`effectiveDepth` 旧模式均不存在。 |
| 河流 surface CK3 `CalcWater` 语义等价 | ✅ 已复核 | `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | 2026-06-18 `debug1.rdc` / `ck3-river.rdc` 复核确认旧 surface 手写 composition 会把 raw refraction 暖棕 `[0.27..0.30,0.19..0.22,0.12..0.14]` 推到高饱和蓝青 `[0.26..0.29,0.50..0.53,0.63..0.66]`；direct-refraction hot-replace 与 CK3 surface pixel history 证明问题在 surface water path。当前 `RiverSurface.sdsl` 已改为 SDSL 可编译的 `PSMain -> CalcRiverAdvanced -> CalcWater` 结构：单次 `FlowNormalTexture` 采样、三层 `_WaterWave1/2/3` ambient normal、CK3 `CalcRefraction`、独立 `WaterFade` base-refraction 路径、`ImprovedBlinnPhong`/map sun inputs/water gloss-spec-IBL composition。`debug-river-target-after.rdc` 复核 surface cbuffer 中 `_GlobalTime`、`_FlattenMult`、三层 wave 参数、`_WaterFlowNormalFlatten`、`_WaterHeight`、`_WaterColorMapTintFactor` 与目标值已绑定，disasm 已无旧双 flow、旧 see-through capped depth 和旧 handwritten composition。2026-06-19 新 `debug.rdc` 进一步确认 bottom 不再是黑源，surface 参数与 CK3 EID 460 基本一致；剩余 darkening gap 来自 surface lighting 仍保留旧 `cloudShadowMask` gloss/spec/reflection helper 和 `sunIntensityMask` glossMap 门控。现已改为直接使用目标 `_WaterZoomedInZoomedOutFactor`、`_WaterToSunDir`、`_WaterGlossScale`、`_WaterSpecularFactor`、`_WaterCubemapIntensity`，并用测试禁止旧 helper 和 glossMap sun gate 回归。 |
| 河流共享 Stride 标准材质光照 | ✅ 已实现 | `Terrain.Editor/Effects/RiverStrideLighting.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs` | 2026-06-21 `debug1.rdc` / `debug2.rdc` 复核后确认 terrain 输出高而 river 输出低的根因是 lighting model 不一致。当前新增 `RiverStrideLighting` 共享 SDSL mixin，bottom/surface 均使用 Stride-style direct Lambert、GGX direct specular、Schlick Fresnel、Smith-Schlick visibility、polynomial DFG、scene skybox specular 与 scene skybox diffuse IBL；diffuse IBL 采 scene cubemap 最低频 mip，避免高低地形边界处进入 terrain shadow 的 river bottom 只剩 specular IBL 而近黑。scene sun、shadow cascade、shadow atlas、skybox cubemap/matrix/intensity/mip count 通过 `RiverStrideLightingKeys` 同时绑定到 bottom 和 surface。Surface 已移除 `_DefaultEnvironmentSunDiffuse`、`_DefaultEnvironmentSunIntensity`、`_WaterToSunDir` 与 `ImprovedBlinnPhong`，bottom 已移除本地 GGX/dominant-reflection IBL；river pass/refraction/foam/waterFade/reflection/depth payload 保持自定义实现。 |
| 河流 surface 后段 map lighting/fog/cloud 链 | ❌ 已移除 | `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | 2026-06-20 对 `C:\Users\Redwa\Desktop\debug4.rdc` 做 RenderDoc 热替换评估，EID 149/176 删除 `ApplySurfacePostProcessing` 后导出图只有 16 个 RGB 像素变化、最大 1 LSB。按用户最终要求，当前删除完整后段 wrapper：不再有 `GetCloudShadowMask`、terrain shadow tint、map distance fog、editor height slice 输入、`shadow_color.dds` 加载、`_HasCloudShadowEnabled` 或 `_InverseWorldSize` surface 绑定。`RiverSurface` 直接输出 `CalcRiverAdvanced`，测试改为禁止这些 wrapper 依赖回归。策略层 FOW 仍不接入 river surface。 |
| 河流 surface foam ramp 采样 | ✅ 已修正 | `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | 2026-06-19 最新 `debug.rdc` 热修改确认 surface 内大块白斑来自 foam ramp wrap bleed：current `FoamRampTexture(t4)` 在 `u=0,y=0.5` 采样为 `[0.323,0.325,0.290]`，CK3 同一点为 `[0,0.0003,0]`；因此 `FlowFoamMask=0` 仍会产生强 foam。`CalcFoamFactor` 现对 ramp U 做半 texel clamp `[0.5/256, 1-0.5/256]` 并使用 `SampleLevel(..., 0)`，同时 foam detail/noise 坐标改回 CK3 的 world-space XZ。 |
| 河流 surface alpha / water-color 导入 | ✅ 已修正 | `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, `game/map/water/water_color.dds`, `Terrain.Editor.Tests/RiverShaderTextTests.cs` | 2026-06-19 更新后的 `debug.rdc` 复核确认 foam 白块已消失，剩余浅岸发黑曾被归因到 surface alpha 不等价；随后 2026-06-21 复查 CK3 源码确认 `river_surface.shader` 实际入口是 `CalcRiverAdvanced(Input)._Color`，不是同文件里的 `CalcRiverSurface`。`Depth = CalcDepth(Input.UV)` 来自 `jomini_river.fxh`，但只进入 `Params._Depth = Depth * Input.Width + 0.1f` 供 `CalcWater` / `WaterFade` / refraction 使用；最终 alpha 在 refraction 分支下是 `Transparency * connectionFade * _BankFade` 双边 edge fade。对 `C:\Users\Redwa\Desktop\debug.rdc` surface draw `309` 做 hot-replace 诊断后，左侧坏点 `(500,204..219)` 的 advanced alpha 仍为 `1.0`，因为代表点 `RiverUV.y=0.1321603` 已远超 `_BankFade=0.025`；因此当前源码移除上一轮 `_SurfaceBankFade=0.30` workaround，恢复 CK3 advanced alpha 分支，并把 `RiverSurfaceKeys._BankFade` 绑定到 `riverObject.BankFade`。继续 hot-replace 证明 raw refraction 已含棕色 bottom，但 see-through attenuation 只有 `0.02~0.03`；短暂尝试用 `min(refractionDepth, Depth)` 限制 see-through 深度后会让全水面丢失水体颜色，已撤销。再次热替换只调 `_WaterSeeThroughDensity`：`0.6` 改善很小，`0.4` 能更明显露出 bottom 且仍保留水色，`0.2` 已偏 bottom-heavy；源码尚未落该调参。CK3 控制水色/透底的路径是：`CalcTerrainUnderwaterSeeThrough` 直接吃 `RefractionDepth` 和 `_WaterSeeThroughDensity`，`WaterFade` 才吃 `min(Input._Depth, RefractionDepth)`，最终用 `FinalColor += lerp(Refraction, Reflection, Fresnel * WaterFade)` 组合。bottom/water DDS 均绕过 Stride 内容系统从 `game/map/water` 直接读取；`water_color.dds` 当前按 UNorm/linear view 加载（`loadAsSrgb:false`），surface shader 不做手动 sRGB decode。 |
| 河流 surface depth bias / depth compare | ✅ 已修正 | `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`, `Terrain/Assets/MainScene.sdscene`, `Terrain.Editor.Tests/RiverRenderFeatureRuntimeTests.cs` | 2026-06-20 `debug.rdc` 与 CK3 `ck3-river.rdc` 对比确认，块状露地形不是整段 river 缺失，也不是 CK3 不写 terrain；当前 surface 和 terrain depth 只差 `1e-5` 量级，水面局部落到 terrain 后面即被 depth test 拒绝。CK3 surface 不输出 `SV_Depth`，通过后主 depth history 仍保留 terrain 值。后续 `ck3-hidden-river.rdc` 与本地新 `debug.rdc` raw state 对比确认 CK3 水面是 strict `Less`，本地旧 `DepthStencilStates.DepthRead` 的 `LessEqual` 会让等深 hidden river fragment 透过山体；当前已改为显式 `DepthBufferFunction=Less`。更新后的本地 `debug.rdc` 进一步证明完整 CK3 raw `DepthBias=-50000` 在旧 editor near=0.1、far=100000 的深度分布下会跨过约 `0.0015` 的山体遮挡差；CK3 effective near 约 `10`，hidden river 和 terrain depth 差为 `0.12~0.15`。后续新版 `debug.rdc` 在 surface `3346` / `1458` 仍透山，terrain pixel `w/depth` 反推实际 near 仍是 `0.1`；当前因此同步修改 `MainScene.sdscene` 相机资产为 `NearClipPlane=10`，并在 asset scene clone 进入 editor 时移除运行时 `BasicCameraController` 等非 editor 组件，避免资产默认/运行时相机脚本把实际 RenderView 留在旧 near。同时 surface bias 改为按实际 `RenderView.NearClipPlane` 连续缩放：near=`10` 用 CK3 raw `-50000`，near=`0.1` 由公式推导为 `-5000`，保留 `SlopeScaleDepthBias=0`、strict `Less`。上一轮按 terrain 重新抬高 `RiverMeshService` 顶点的尝试已撤销。 |
| 路径深度偏移 | ✅ | `Terrain.Editor/Rendering/PathDepthBiasPipelineProcessor.cs` | - |

## 编辑器层 (Editor)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 高度编辑 (HeightEditor) | ✅ | `Terrain.Editor/Services/HeightEditor.cs` | [Phase 1](design/terrain-editor-design-phase-1.md) |
| 笔刷系统 (Brush System) | ✅ | `Terrain.Editor/Brushes/` | [Phase 2](design/terrain-editor-design-phase-2.md) |
| Avalonia UI | ✅ | `Terrain.Editor/Views/MainWindow.axaml` + `ViewModels/` | - |
| ~~ImGui UI~~ | ✅ 已替换 | ~~`Terrain.Editor/UI/`~~ | → 迁移至 Avalonia |
| 笔刷投影 (屏幕空间 Decal) | ✅ | `Terrain.Editor/Rendering/Decal/` | - |
| Biome 规则系统 | ✅ | `BiomeRuleService.cs`, `BiomeViewModel.cs` | - |
| Biome 蒙版绘制 | ✅ | `BiomeEditor.cs`, `BiomeMask.cs` | - |
| 纹理刷 (Texture Brush) | ✅ | `Terrain.Editor/Services/PaintEditor.cs` | [texture-brush](log/2026/04/06/2026-04-06-2-terrain-texture-brush-planning.md) |
| 纹理导入增强 | ✅ | `Terrain.Editor/Services/` | [texture-auto-normal](design/texture-auto-normal-import-and-inspector.md) |
| 统一数据同步 | ✅ | `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | - |
| 材质索引图增强 | ✅ | `Terrain.Editor/Services/MaterialIndexMap.cs` | - |
| Undo/Redo (Chunk事务) | ✅ | `Terrain.Editor/Services/Commands/` | - |
| 路径特征编辑 | ✅ | `PathFeatureService.cs`, `PathFeatureEditCommand.cs` | - |
| 河流网格生成 | ✅ | `RiverMapService.cs`, `RiverMeshService.cs`, `RiverViewModel.cs` | 启动或运行期加载 `rivers.png` 后会自动生成 mesh；宽度缩放仍可触发重建；不再暴露手动 Import/Generate UI；River inspector 仅保留资源路径、生成状态与宽度缩放。2026-06-19 `RiverMapService.TracePath` 已改为在 junction 邻域优先踏入唯一相邻的 `Source/Confluence/Bifurcation` marker，避免真实 `rivers.png` 中 branch 因 side continuation 提前以 `EndKind.None` 终止；最小回归测试 `branch honors adjacent confluence marker before side continuation` 已覆盖。同日 RenderDoc 热替换进一步确认 current `debug.rdc` 的黑线样本并非 shadow，而是 `Confluence->None` / `Bifurcation->None` 段保持错误拓扑方向，导致 mesh tangent、parallax 与流向整体反向；`RiverMapService.NormalizeDirection` 现按 `Source/None -> Confluence/Bifurcation` 归一 segment，新增 `confluence to none...` 与 `bifurcation to none...` 回归测试锁住该行为。2026-06-21 `river-mesh.rdc` 导出的 GPU centerline 显示 current 非端点折角可达 `60°~77°`，CK3 对照 `ck3-river.rdc` 非退化端点主要约 `12°~26°`；`RiverMeshService` 在两轮 Chaikin 后新增更强的 bend relaxation，并用 `centerline smoothing limits repeated river bend angles` 回归测试锁定连续折弯不超过 `15°`。随后新增 `curved river map publishes smooth mesh boundaries` 集成测试覆盖真实生成链路，证明最终 Catmull-Rom 固定 `1.0/0.5` 间距仍会分别留下约 `33°/17.44°` 边界角；当前改为按局部曲率自适应采样，直线段保留 `1.0`，急弯才加密到 `0.25`，并在最终中心线按 smoothed XZ 双线性重新采样 terrain 高度，避免崎岖地形上最近邻高度量化形成河面台阶；测试同时锁定最终 mesh 边界角不超过 `12°`、长直/长曲采样预算、长曲边界质量、默认半宽 corridor 偏移、非平坦高度一致性和 invalid/overflowing height cache 降级。2026-06-21 `debug1.rdc` 的 pointed bank convergence 诊断只说明尖端同时体现在 `RiverUV.y` / see-through attenuation / `RefractionDepth` 观测信号上；后续五横截面 lane 与 miter clamp 方向已判定为过度推断并撤回，当前河流 mesh 仍保持原二列 ribbon 生成。`debug2.rdc` 已把该尖端进一步定位到 surface refraction alpha payload 线性过滤。 |
| 河流显隐/线框调试 | ✅ | `RiverRenderingService.cs`, `RiverWireframeModeController.cs` | - |
| 虚拟资源会话 | ✅ | `Terrain.Editor/Services/Resources/`, `Terrain/Resources/` | Editor/Runtime 优先扫描工作区 `game/` 作为 base；若起点本身已位于目录名为 `game` 且包含 `map/` 的合法根，也会直接接受该根，并从 `exe/LaunchSetting.json` 读取或自动生成本地 mod 配置；`game/` 不再由 Git 跟踪 |
| MapData 缺失骨架补齐 | ✅ | `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs` | 自动生成三个 TOML，并在文件顶部保留注释模板示例；`heightmap.png` 仍需人工补齐 |
| 作者态缺失材质降级加载 | ✅ | `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs`, `Terrain.Editor/Services/MaterialSlotManager.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs` | 缺失 `material_id` 时为每个缺失项创建运行时默认槽位并逐条打印错误；缺失贴图文件时按通道逐槽降级：`albedo` 回退洋红缺失材质纹理、`normal` 回退 flat normal、`properties` 仅记录诊断；仅缺贴图文件仍允许 `Save` / `Export`，缺失 `material_id` 时会阻止 `Save` / `Export` |
| `map` TOML 规格文档 | ✅ | `docs/design/map-data-toml-formats.md` | 记录 `default.toml`、`materials/descriptor.toml`、`biome_settings.toml` 的当前实现字段、约束、默认值与 Editor/Runtime 消费边界 |
| Save 作者态资源 | ✅ | `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor/Views/SaveProgressWindow.axaml`, `Terrain.Editor/Services/TerrainManager.cs`, `Terrain.Editor/Services/Resources/EditorResourceSaveService.cs` | `Save` 通过异步模态进度写回 `default.toml` / heightmap / biome mask / biome settings / materials descriptor；打开进度窗口后先让 UI 调度一次，再由 UI 线程捕获不可变 save snapshot，后台只做文件写入；保存期间禁用 Save/Export/Import/Undo/Redo 等可变更命令，并阻止 Stride 视口输入；由于 Stride viewport 是 native child HWND，进度条使用 owned top-level window 显示，避免 Avalonia inline overlay 被子窗口遮盖；缺失 `biome_mask.png` 时首次保存再生成；作者态保存使用事务化写回，后续 writer 失败时会回滚前面已 staged 的资源；当前不写回 `rivers.png` 与材质贴图文件；若存在缺失 `material_id` 的运行时临时槽位则禁止保存 |
| TOML 项目持久化 | ❌ 已移除 | 旧 `ProjectManager.cs` / `TomlProjectConfig.cs` 已删除 | Editor 固定 Terrain 工作区 |
| 导出系统 (IExporter) | ✅ | `Terrain.Editor/Services/Export/` | - |
| Biome 配置导出 | ❌ 已移除 | 旧 `BiomeConfigExporter.cs` 已删除 | Runtime 改用 `map/biome_settings.toml` |
| 设置模式 (HeightScale) | ✅ | `SettingsViewModel.cs`, `game/map/default.toml`, `game/map_data/default.toml` | 默认作者态/运行时 map descriptor 保持 `height_scale=200`，用于支持本项目更高的地形起伏；2026-06-21 对照 CK3 `ck3 river2.rdc` terrain cbuffer 确认 CK3 `HeightScale=50`、`OriginalHeightmapToWorldSpace≈0.5`、`MapSize=9216x4608`。河流 refraction 不再要求降低全局高度尺度，而是用 `_RefractionMaxCameraHeight=max(50, HeightScale)` 让 CK3 `MaxHeight=50` camera clamp 在高地形高度下仍保持有效距离精度。 |
| 资产浏览器 | ✅ | `AssetBrowserItemViewModel.cs` | - |
| 原生 SDL 视口 | ✅ | `NativeStrideViewportHost.cs` | - |
| 植被编辑 | 🚧 | - | [Phase 3](design/terrain-editor-design-phase-3.md) |

## 运行时 (Runtime)

| 功能 | 状态 | 关键文件 | 设计文档 |
|------|------|----------|----------|
| 地形加载 | ✅ | `Terrain/Core/TerrainProcessor.cs`, `Terrain/Resources/GameRuntimeResourceBootstrap.cs` | Runtime 从工作区 `game/` 根定位资源并读取 `.terrain`；忽略 `default.toml` 中的 `heightmap` 声明；缺失 `.terrain` 或 `biome_mask.png` 时记错误日志并保持未初始化；同配置失败后不逐帧重试 |
| 双 VT 流式加载 | ✅ | `Terrain/Streaming/TerrainStreaming.cs` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| IndexMap 材质混合 | ✅ | `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | [streaming](log/2026/04/10/2026-04-10-1-runtime-indexmap-streaming.md) |
| RuntimeMaterialManager | ✅ | `Terrain/Materials/RuntimeMaterialManager.cs` | descriptor 驱动 |
| 虚拟资源 Bootstrap | ✅ | `Terrain/Resources/` | `gameRoot` 扫描或 direct-hit 合法 `game/` 根 + `exe/LaunchSetting.json` + `GameResourceResolverBootstrap` + resolver/bootstrap |
| Editor 作者态写回器 | ✅ | `Terrain.Editor/Services/Resources/*Writer.cs` | 写回当前命中的 `default.toml` / heightmap / biome_mask / biome_settings / materials descriptor；rivers 当前仅可选读取，不写回 |
| Runtime DetailMap 构建 | ✅ | `Terrain/Materials/RuntimeDetailMapBuilder.cs` | 高度来源于 `.terrain` 内数据，而不是 `heightmap.png` |
| 半分辨率 SplatMap | ✅ | Editor + Runtime 均支持 | - |

## 规划中 (Planned)

| 功能 | 优先级 | 设计文档 |
|------|--------|----------|
| 侵蚀模拟 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 程序化地形生成 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 笔刷预设系统 | 低 | [Phase 4](design/terrain-editor-design-phase-4.md) |
| 植被 LOD (GPU Instancing) | 中 | [Phase 5](design/terrain-editor-design-phase-5.md) |
| Compute Shader 剔除 | 中 | [Phase 5](design/terrain-editor-design-phase-5.md) |
| GPU LOD 迁移 | 中 | [Phase 7](design/terrain-editor-design-phase-7.md) |
| Hi-Z 遮挡剔除 | 低 | [Phase 7](design/terrain-editor-design-phase-7.md) |

---

## 关键架构决策摘要

| 决策 | 日期 | 备注 |
|------|------|------|
| Biome 规则层体系 | 2026-05 | [ADR-012](log/decisions/adr-012-biome-rule-layer-system.md) |
| 路径特征系统 (Road/River) | 2026-05~06 | [ADR-013](log/decisions/adr-013-vic3-path-rendering.md), [ADR-014](log/decisions/adr-014-river-rendering-architecture.md) |
| Avalonia UI 迁移 | 2026-04 | [ADR-011](log/decisions/adr-011-avalonia-sdl-viewport-hosting.md) |
| 半分辨率 SplatMap/BiomeMask | 2026-05 | 待创建 ADR |

> **注意**：2026-04-15 之前的旧 ADR 已删除（基于过时日志）。下次会话应基于当前代码状态重新创建 ADR。

---

*此文件应随功能完成状态变化而更新。详见 [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) 获取完整架构说明。*
