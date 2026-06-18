# River Surface CalcWater Strict Parity Design

**Date**: 2026-06-18  
**Status**: Draft - awaiting review  
**Author**: Codex

---

## Goal

把当前 `RiverSurface.sdsl` 从“局部相似的水面近似”推进到严格的 `CalcWater` 语义对齐。

目标优先级：

1. shader 源码语义一致优先。
2. 用户提供的目标 RDC 单帧作为第一道硬门。
3. 资源、参数、scene 输入、sampler 和 pass 绑定必须逐项可追踪。

这不是调色任务。当前已验证 raw refraction/bottom 输入与目标帧同量级，主要差距集中在 surface 层缺少完整 `CalcWater` 语义和 water 参数组。

## Confirmed Constraints

- 采用“完整语义边界，分层激活”路线。
- 缺失输入必须补齐，不能长期使用中性 fallback。
- 临时 fallback 只允许用于 RenderDoc hot-replace 或迁移过程中的短期验证。
- 外部游戏安装目录可作为本机调试源，用于提取 shader、cbuffer、纹理、sampler、资源格式和数值。
- 复制到项目内的资源必须放入现有 `Terrain.Editor/Assets/River/` 结构。
- 不新增带外部产品名、产品缩写或“参考来源”含义的目录、类型、模式、URL、日志标签。
- 项目内代码、资源名、目录名、shader 参数名、C# 类型名都使用中性河流语义。

## Existing Resource Layout

沿用现有路径：

- `Terrain.Editor/Assets/River/Water/`
- `Terrain.Editor/Assets/River/Bottom/`
- `Terrain.Editor/Assets/River/Environment/`

沿用现有内容 URL：

- `River/Water/flow-normal`
- `River/Water/ambient-normal`
- `River/Water/foam`
- `River/Water/foam-ramp`
- `River/Water/foam-map`
- `River/Water/foam-noise`
- `River/Water/water-color`
- `River/Bottom/bottom-diffuse`
- `River/Bottom/bottom-normal`
- `River/Bottom/bottom-properties`
- `River/Bottom/bottom-depth`
- `River/Environment/reflection-specular`

新增资源时按现有语义归类到 `Water`、`Bottom` 或 `Environment`，文件名使用中性语义，例如 `cloud-mask`、`fog-lookup`、`shadow-mask`、`environment-*`。具体名称在实施时根据外部 shader 实际 SRV 用途确定。

## Resource Copy Strategy

资源处理规则：

1. 同名语义资源优先覆盖现有 `.dds`，保留 sibling `.sdtex` 和 asset URL。
2. 覆盖前记录 hash、尺寸、mip、format、cube/2D 类型和 sRGB 语义。
3. 如果外部资源与当前项目文件逐字节一致，只记录一致，不制造文件改动。
4. 如果缺少新的 surface 语义输入，新增中性 `.dds` 和 `.sdtex`，并加入 `Terrain.Editor.sdpkg` `RootAssets`。
5. `.sdtex` 继续使用现有 sibling source 模式，例如 `Source: !file flow-normal.dds`。
6. 资源加载失败必须带 asset URL 报错，不能静默回落到白图、黑图或旧 cubemap。

## Shader Port Boundary

`RiverSurface.sdsl` 最终必须保留完整 water surface 语义边界，包括：

- water shallow/deep color
- water diffuse multiplier / map tint
- specular / specular factor
- gloss base / gloss factor / gloss map usage
- flow normal / ambient normal / normal flatten
- multi-layer wave scale / speed / strength
- foam texture / foam ramp / foam map / foam noise
- refraction scale / fade / shore mask / distorted sample
- see-through color / depth attenuation
- fresnel bias / power / scale
- reflection/environment sampling
- cloud / fog / FoW / shadow / scene lighting inputs required by the external shader path

缺失系统不能通过删除接口来规避。短期无法接真实 scene 数据时，实施必须：

1. 在严格模式下 fail fast 或明确标红。
2. 在临时验证模式下用命名占位输入。
3. 在 TODO 中记录真实来源、目标 SRV/CBV、期望格式和当前阻塞原因。

## C# Binding Strategy

绑定层保持中性命名：

- `RiverResourceLoader` 继续暴露 `River/Water/...`、`River/Bottom/...`、`River/Environment/...` URL。
- 参数组命名使用 `RiverWaterParameters` 或现有等价类型。
- 严格开关使用 `RiverStrictMode` 或 `StrictRiverRendering`。
- 日志标签使用 `RiverSurface`、`RiverWater` 或 `RiverStrict`。

严格模式行为：

1. Editor 调试默认开启严格模式。
2. surface 所需纹理、sampler、cbuffer 参数和 scene 输入必须完整绑定。
3. 缺任何一项直接报错或输出清晰诊断。
4. 严格模式关闭时允许项目级兜底继续跑，但 shader 接口仍保持完整，避免两套语义分叉。

## RenderDoc Hot-Replace Gate

在修改 SDSL/C# 前，先完成 hot-replace 验证：

1. 从目标 RDC 导出 surface pass 的 cbuffer、SRV、sampler 和关键采样点。
2. 从外部游戏目录提取 surface shader 依赖的水面资源和参数语义。
3. 在当前 `debug1.rdc` 的 surface pass 上 hot-replace PS。
4. hot-replace PS 使用完整 `CalcWater` 语义和外部参数/资源。
5. 确认当前输出从蓝青高能量水色回到目标帧暗水能量范围。
6. 通过后再迁入 `RiverSurface.sdsl` 和 C# 绑定。

如果 hot-replace 未能把输出拉回目标能量范围，不进入 SDSL 落地，继续在 RenderDoc 中拆分变量验证。

## Implementation Phases

### Phase 0 - Baseline Extraction

- 列出目标 surface pass 的 draw/event、RT、SRV、sampler、cbuffer。
- 导出 water cbuffer 参数。
- 导出关键像素点 current original、current refraction-only、target bottom、target surface。
- 建立资源清单：已有、同名覆盖、缺项新增。

### Phase 1 - RenderDoc Shader Verification

- 用 hot-replace 实现完整 surface 语义草案。
- 用目标参数和项目内复制资源验证 surface 输出。
- 逐项隔离 water color、normal、foam、refraction、reflection、shadow/fog/cloud/FoW 对输出的贡献。

### Phase 2 - SDSL and Asset Binding

- 更新 `RiverSurface.sdsl`，保留完整参数边界。
- 新增或更新 `.sdtex` 和 `Terrain.Editor.sdpkg` RootAssets。
- 更新 `RiverResourceLoader` / `RiverRenderFeature` / `RiverRenderObject` 绑定。
- 按 Stride shader 工作流运行 shader key 生成与 asset rebuild。

### Phase 3 - Scene Input Completion

- 把临时占位输入替换为真实 scene 数据。
- shadow/cloud/fog/FoW/environment 每项都要有真实来源或明确阻塞。
- 重新截当前项目帧，确认 cbuffer、SRV 和输出趋势与 hot-replace 一致。

## Verification

必须执行的验证：

1. RenderDoc hot-replace：surface 输出进入目标帧同量级暗水范围。
2. SDSL 落地后重新截帧：draw 顺序、RT、SRV、sampler、cbuffer 与设计一致。
3. 关键采样点对比：current original、current refraction-only、落地 surface、目标 surface。
4. 文本测试覆盖资源 URL、`.sdtex` source、sRGB/linear 语义、cubemap/2D 类型、关键 shader 语义片段。
5. 按 Stride shader workflow 执行：
   - `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles`
   - `StrideCleanAsset`
   - `StrideCompileAsset`
   - `dotnet build`

验收门槛：

- shader 语义路径逐项存在。
- 关键点 RGB 先进入目标帧同量级范围，再追逐更小误差。
- 缺失输入不能藏在 fallback 里。
- 项目内命名不出现外部游戏缩写、完整产品名或“参考来源”类英文命名。

## Non-Goals

- 不在设计阶段复制资源或修改 SDSL/C#。
- 不用单纯调暗水色替代完整 `CalcWater` 端口。
- 不为了快速接近单帧截图删除 cloud/fog/FoW/shadow/environment 接口。
- 不提交外部品牌命名的项目路径或代码符号。

## Risks

- 外部资源和当前 Stride 内容管线的 format/sRGB/cubemap 解释可能不一致，需要逐张校验。
- 一次性补齐 surface 输入会增加 C# 绑定和 asset rebuild 风险，因此必须先 hot-replace 验证。
- 当前项目可能没有完整 fog/FoW/cloud 系统；这些项需要真实 scene 数据或明确实现任务，不能长期假装等价。
- 现有仓库有大量未提交改动，实施时必须只处理相关文件，不能回滚用户改动。

## Next Step

用户审阅并批准本设计后，再进入 implementation plan。实施计划应从 Phase 0 的 RenderDoc / resource extraction 开始，而不是直接改 `RiverSurface.sdsl`。
