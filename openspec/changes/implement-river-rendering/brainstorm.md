<!--
Raw capture of superpowers:brainstorming output.

本檔原樣捕捉 brainstorming skill 的產出，不強制結構。
Skill 的自然產出通常是 decision log 格式（背景 → 決議鏈 Q1-Qn → 設計取捨），
但依對話內容可能有不同組織方式。

design.md 從本檔萃取並重新整理為結構化設計文件。

不要將本檔的內容複製到 design.md — design.md 是獨立的重組產物，
兩者互補但不重疊。
-->

# River Rendering Brainstorm Raw Capture

## 背景

当前河流 mesh 已生成，但渲染仍未按参考捕获实现。现有 `Terrain.Editor/Services/RiverRenderingService.cs` 使用普通 `MaterialDescriptor` 和 `VertexPositionNormalTexture` 创建透明材质；这无法承载参考河流渲染所需的多 pass、half-res refraction/bottom RT、dual-source blending、自定义顶点属性和 surface 采样链路。

本 change 目标从“简单可见水材质”收敛为：完整实现参考捕获中河流的 two-pass 渲染结构。用户明确要求：

- 以 `C:\Users\Redwa\Desktop\ck3-river.rdc` 中 event 460 / Draw(580) 及其对应 bottom draw 为事实基准。
- shader 源码和资源来自 `E:\SteamLibrary\steamapps\common\Crusader Kings III`。
- 实现完整 pass stack，不只是简化 MVP。
- 命名中不要包含外部产品或相关字样；代码、目录、shader、资源名使用中性项目命名。
- 不考虑 OpenGL 后端；目标以后端支持 dual-source blending 的 Windows/D3D 路径为主。

## RenderDoc 与 shader 事实

### RenderDoc pass 分层

RenderDoc 捕获中发现同一批河段重复绘制两组 draw：

- event 332 / 334 / 336 / 338：draw counts 580 / 950 / 340 / 510，写 half-res render target `ResourceId::49006`，分辨率 1280x720。
- event 460 / 462 / 464 / 466：draw counts 580 / 950 / 340 / 510，写 full-res render target `ResourceId::49000`，分辨率 2560x1440，并读取 `ResourceId::49006` 作为 `RefractionTexture`。

因此 event 460 不是完整河流渲染的第一步，而是 surface pass。它依赖 332..338 写出的 half-res bottom/refraction buffer。

### Bottom pass

参考源码：

- `game/gfx/FX/river_bottom.shader`
- `jomini/gfx/FX/jomini/jomini_river_bottom.fxh`
- `jomini/gfx/FX/jomini/jomini_river.fxh`

关键行为：

- 使用 `VS_INPUT_RIVER`。
- 输出 `PS_RIVER_BOTTOM_OUT`：`Color : PDX_COLOR0` 和 `Blend : PDX_COLOR0_SRC1`，即 dual-source output。
- BlendState 使用 `src1_alpha / inv_src1_alpha`。
- `Color.rgb` 是河底颜色。
- `Color.a` 写 `CompressWorldSpace(WorldSpacePos)`，不是普通 alpha。
- `Blend = vec4(Alpha)` 作为混合因子。
- DepthWrite off，DepthBias -50000。
- 使用 bottom diffuse / normal / properties、parallax、distance-to-main fade、edge fade、fake depth。

### Surface pass

参考源码：

- `game/gfx/FX/river_surface.shader`
- `game/gfx/FX/jomini/jomini_river_surface.fxh`
- `jomini/gfx/FX/jomini/jomini_river.fxh`

关键行为：

- 使用同一 `VS_INPUT_RIVER`。
- 读取 `RefractionTexture`（half-res bottom output）。
- 读取 WaterColor、AmbientNormal、FlowNormal、ReflectionCubeMap、Foam/FoamRamp/FoamMap/FoamNoise 等纹理。
- `CalcRiverAdvanced` 中使用 flow normal UV：`Input.UV.yx * float2(1, -1) * float2(Input.Width, 1) * _FlowNormalUvScale`，并沿 `GlobalTime * _FlowNormalSpeed` 滚动。
- alpha 使用 `Input.Transparency * saturate((Input.DistanceToMain - 0.1) * 5)`，再乘 edge fade。
- BlendState 使用 `src_alpha / inv_src_alpha`，DepthWrite off，DepthBias -50000，WriteMask RGB。

### Vertex contract

参考 `VS_INPUT_RIVER`：

```hlsl
float3 Position       : POSITION;
float  Transparency   : TEXCOORD0;
float2 UV             : TEXCOORD1;
float3 Tangent        : TEXCOORD2;
float3 Normal         : TEXCOORD3;
float  Width          : TEXCOORD4;
float  DistanceToMain : TEXCOORD5;
```

现有 `VertexPositionNormalTexture` 不够，必须改为自定义 `RiverVertex`。

## 主要设计分歧与决策

### Q1：实现档位

讨论过三档：

1. 简化双 pass：可见且可调的河底 + 水面。
2. 尽量忠实复刻参考 draw 的 shader / 资源 / 顶点属性。
3. 最小实现：简单半透明水材质。

用户选择 2。后续进一步明确：既然 RenderDoc、shader 源码、Stride 可行性都已经确认，就不要停在 MVP，而是直接实现完整参考链路。

### Q2：范围基准

选项：

1. 以 `ck3-river.rdc` event 460 / Draw(580) 为准。
2. 扫全部 river 相关 draw。
3. 以 shader 源码为主，RenderDoc 只验证。

用户选择 1。实现以 event 332/460 对应链路为主，不扩展到所有水体系统。

### Q3：资源来源

选项：

1. 复制必要资源到项目资源目录。
2. 运行时从外部安装目录读取。
3. 使用 RenderDoc 导出的临时资源。

用户选择 1。后续追加要求：命名中不要包含外部产品或相关字样。资源目录与文件名采用中性项目语义，例如 `Assets/River/Water/water_color.dds`、`Bottom/bottom_diffuse.dds`、`Environment/reflection_cube.dds`。来源路径可记录在 README，但不要进入代码/API/目录命名。

### Q4：Stride 实现路线

先比较过两种：

- 方案 A：保留 `ModelComponent/MeshRenderFeature`，通过 stage/selector 和自定义 renderer 编排 bottom/surface。
- 方案 B：专用 `RiverRenderFeature / DynamicEffectInstance / 手动 multi-pass draw`。

用户认为方案 B 更合适，因为项目已有 `TerrainRenderFeature`。随后并行 subagent 调研确认：

- `DynamicEffectInstance`、自定义 `RiverVertex`、手动 VB/IB 绑定、`DrawIndexed`、half-res RT、full-res transparent pass、MRT/dual-source 路线在 Stride 中可行。
- `TerrainRenderFeature` 的 `RootEffectRenderFeature + Prepare/Draw + ProcessPipelineState + 手动 draw` 架构骨架可复用，但不要照搬 terrain chunk/LOD/shadow proxy。
- dual-source blending OpenGL 不支持，但用户明确不考虑 OpenGL，因此 dual-source 是主线，不是 fallback。

最终选择方案 B。

### Q5：是否使用 RiverComponent

最初方案把 `RiverRenderingService` 作为 render data provider。用户质疑为什么不使用 `RiverComponent` 体系。

修正决策：采用更正统的 Stride 架构：

```text
RiverRenderingService -> RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature
```

职责：

- `RiverRenderingService` 仅作为 Editor façade，保留 `UpdateMeshes / SetVisible / ClearMeshes / Dispose` 外部 API，不直接拥有主渲染资源。
- `RiverComponent` 是 scene 中的河流状态入口，持有 CPU-side river mesh data、settings、version。
- `RiverProcessor` 负责 component 到 GPU render object 的同步。
- `RiverRenderObject` 持有 vertex/index buffers、bounds、segment draw metadata。
- `RiverRenderFeature` 负责 bottom/surface GPU pass、effects、pipeline states、half-res resources。

这个结构与现有 `TerrainComponent -> TerrainProcessor -> TerrainRenderObject -> TerrainRenderFeature` 更一致。

## 方案 B 完整可行性调研结论

### 能完整实现

- 专用 `RiverRenderFeature`
- 自定义 `RiverVertex`
- 手动 draw 提交
- 两阶段 river pipeline
- half-res bottom offscreen RT
- full-res surface transparent pass
- 连接段网格 + `DistanceToMain` 渐隐
- bottom/surface 各自独立 PSO
- custom flow/depth/parallax shader 参数
- per-view / per-pass / per-draw 参数更新
- dual-source blending（目标后端不考虑 OpenGL）

### 需要 neutral fallback 的外部系统

参考 shader 中存在一些项目当前没有完整等价系统的输入：

- fog of war
- cloud mask
- terrain shadow tint
- full water/refraction global system
- flat map / zoom integration

处理方式不是删掉结构，而是保留参数/函数边界并提供 neutral fallback：

- FoW = 1
- cloud mask = 0
- shadow term = 1
- flatmap lerp = 0
- zoom blend out = 1

后续项目有对应系统时可以接入。

## 最终架构

```text
RiverViewModel.Generate()
  -> RiverRenderingService.UpdateMeshes(...)
  -> RiverMeshService.BuildRiverMesh(...)
  -> RiverComponent.SetMeshes(...)
  -> RiverProcessor detects Version change
  -> creates/updates RiverRenderObject GPU buffers
  -> RiverRenderFeature.Draw()
       -> DrawBottomPass half-res dual-source
       -> DrawSurfacePass full-res transparent sampling bottom RT
```

可见性：

```text
EditorShellViewModel.Settings.ShowRivers
  -> RiverRenderingService.SetVisible(...)
  -> RiverComponent.Enabled or RiverComponent.Settings.Visible
  -> RiverProcessor/RiverRenderFeature skips draw
```

清理：

```text
RiverRenderingService.ClearMeshes()
  -> RiverComponent.Clear()
  -> RiverProcessor releases RiverRenderObjects
```

## Shader / resource / asset decisions

### Neutral naming

代码、目录、shader、资源名不包含外部产品/引擎名。使用：

- `RiverComponent`
- `RiverProcessor`
- `RiverRenderFeature`
- `RiverRenderObject`
- `RiverRenderResources`
- `RiverRenderSettings`
- `RiverVertex`
- `RiverMeshData`
- `RiverBottom.sdsl`
- `RiverSurface.sdsl`
- `RiverCommon.sdsl`
- `RiverWaterCommon.sdsl`
- `RiverVertexStreams.sdsl`

资源目录：

```text
Terrain.Editor/Assets/River/
  Water/
    water_color.dds
    ambient_normal.dds
    flow_normal.dds
    foam.dds
    foam_ramp.dds
    foam_map.dds
    foam_noise.dds
  Bottom/
    bottom_diffuse.dds
    bottom_normal.dds
    bottom_properties.dds
  Environment/
    reflection_cube.dds
  README.md
```

README 记录来源路径与用途，但文件/API 名称保持中性。

### Shader files

当前已有：

- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverEffect.sdfx`

新增共享文件：

- `Terrain.Editor/Effects/RiverVertexStreams.sdsl`
- `Terrain.Editor/Effects/RiverCommon.sdsl`
- `Terrain.Editor/Effects/RiverWaterCommon.sdsl`

`RiverVertexStreams` 显式声明 `TEXCOORD0..5`。`RiverCommon` 包含 `CalcRiverDepth`、`CompressRiverWorldSpace` 等 river 独立逻辑。`RiverWaterCommon` 包含 water color、ambient normal、flow normal、foam、reflection、neutral fallback 参数。

### Bottom shader

`RiverBottom.sdsl` 移植参考 bottom logic：

- TBN
- parallax offset
- bottom diffuse / normal / properties sampling
- depth and fake depth
- edge fade
- distance-to-main fade
- `CompressRiverWorldSpace`
- dual-source output

目标输出：

```hlsl
Color : SV_Target0
Blend : SV_Target0_SRC1
```

### Surface shader

`RiverSurface.sdsl` 移植参考 surface logic：

- depth formula
- flow normal animation
- water color lookup
- ambient normal
- reflection cubemap
- foam sampling
- transparency fade
- distance-to-main fade
- edge fade
- flatmap/zoom/shadow/fog/cloud neutral fallback
- sample bottom/refraction RT

### Parameters

`rivers.settings` 映射到 `RiverRenderSettings`：

- FlowNormalUvScale = 0.4
- FlowNormalSpeed = 0.075
- NoiseScale = 0.25
- NoiseSpeed = 2.0
- FlattenMult = 1.0
- DepthFakeFactor = 2.0
- OceanFadeRate = 0.8
- Depth = 0.15
- Bottom diffuse/normal/properties resources

`riverwater.settings` 映射 water resource and water parameters：

- water color
- ambient normal
- flow normal
- reflection cube
- foam textures
- water color shallow/deep
- cubemap/specular/fresnel/foam/see-through parameters

缺少默认值的参数应继续从参考 shader/settings 中定位，不随意拍脑袋。

### Asset workflow

新增/修改 SDSL 后必须运行：

```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug
dotnet build Terrain.sln -c Debug
```

新增 `.sdsl` 需要同步 `.csproj` shader generator 配置；`.sdpkg` 需确认包含 `Effects` 与资源目录所在 AssetFolder。

## 实现任务拆分

1. RiverComponent 数据入口
2. RiverVertex / RiverMeshData / mesh output migration
3. RiverProcessor / RiverRenderObject
4. RiverRenderResources half-res RT/depth
5. River shader streams/common files
6. RiverBottom shader full reference migration
7. RiverSurface shader full reference migration
8. Copy/load river resources with neutral names
9. RiverRenderFeature manual multi-pass
10. EmbeddedStrideViewportGame compositor/game integration
11. Wireframe/debug mode migration
12. Tests, shader workflow, RenderDoc verification

## RenderDoc acceptance

Bottom pass must show:

- river bottom draws
- half-res RT
- R16G16B16A16_FLOAT or equivalent
- POSITION + TEXCOORD0..5 vertex input
- secondary source PS output
- src1 alpha blend
- depth write off
- depth bias
- bottom diffuse/normal/properties bound

Surface pass must show:

- river surface draws corresponding to bottom draws
- bottom/refraction RT bound as shader resource
- water color / ambient normal / flow normal / foam / reflection bound
- src alpha blend
- depth write off
- edge fade
- flow normal animation
- distance-to-main fade

## Risks

- SDSL support for `SV_Target0_SRC1` must be verified. If blocked, temporarily use MRT to validate resource chain, but design target remains dual-source.
- Resource lifetime must be owned by `RiverRenderFeature` / `RiverProcessor`, not split ambiguously with `RiverRenderingService`.
- `UpdateMeshes` and render draw synchronization may need versioning or deferred GPU update.
- Compositor draw timing must place river bottom/surface in correct order relative to terrain and transparent overlays.
- Existing wireframe behavior must be migrated from `MeshRenderFeature` selector route to river-specific debug/wireframe rendering.

## User-approved direction

The user accepted:

- full reference-style implementation, not simplified MVP;
-方案 B with dedicated RiverRenderFeature;
- RiverComponent体系;
- no OpenGL consideration;
- dual-source as primary path;
- neutral naming without external product names in code/assets.
