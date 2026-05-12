# vic3 风格道路河流重做

## Goal

基于现有 path feature 编辑闭环，把当前程序化纯色的道路/河流渲染重做到尽量贴近 Victoria 3 的实现方式。优先复用现有控制点、Catmull-Rom 采样、撤销重做、保存加载，但放弃当前“直接修改高度图塑形”的做法，转为依靠贴图、alpha、深度偏移和分层渲染来实现视觉效果；缺失素材从 `E:\Victoria.3.v1.2.7\game` 提取，若现有研究不够则继续分析 `C:\Users\Redwa\Desktop\vic3-river&road.rdc`。

## What I already know

* 用户要求参考归档研究 `archive/2026-05/05-06-editor-road-river-drawing/research/vic3-road-river-rendering.md`，按 Vic3 方向重做现有道路和河流实现。
* 用户已明确要求：不要 Properties 贴图；放弃当前通过修改高度图来塑形道路/河道的做法；尽量按 Vic3 的渲染实现思路来做，而不是做一个相似外观的简化版。
* 如果现有文字研究不足，应继续分析 `C:\Users\Redwa\Desktop\vic3-river&road.rdc` 捕获文件来确认具体实现。
* 现有编辑闭环已经存在：节点/路径数据模型、草绘、节点吸附、Catmull-Rom 采样、gizmo、撤销重做都集中在 `Terrain.Editor/Services/PathFeatures/PathFeatureService.cs`。
* 现有保存/加载已经支持 `path_nodes` 和 `path_features`，数据模型在 `Terrain.Editor/Services/TomlProjectConfig.cs`，TerrainManager 负责保存与恢复。
* 现有 mesh 生成已经是 ribbon 结构，UV 也是“横向 0..1 + 纵向累计距离”模式，这与研究里 Vic3 道路/河流的基本平铺方向兼容。
* 当前最大缺口不在编辑，而在渲染：`CreateMaterial` 仍然只是按道路/河流类型创建纯色 Stride 材质，没有使用 Diffuse/Normal 纹理，也没有河流双层结构、边缘淡出、流动法线或专门的深度偏移策略。
* Vic3 目标素材实际位于：
  * `E:\Victoria.3.v1.2.7\game\game\gfx\map\spline_network\`
  * `E:\Victoria.3.v1.2.7\game\game\gfx\map\rivers\`
  * `E:\Victoria.3.v1.2.7\game\game\gfx\map\water\`

## Assumptions (temporary)

* 首版尽量保留现有路径编辑拓扑与数据格式，不重写交互系统。
* 首版重点放在视觉/材质/渲染重做，而不是再次扩展拓扑编辑能力。
* 缺失纹理将导入到本项目可控路径中，而不是在运行时直接依赖 Vic3 安装目录。
* 首版不实现 Devastation / Pollution 覆盖系统。
* 首版先把道路完整跑通，河流后续按同一框架补齐。
* 首版支持两套道路材质：Vic3 土路与铺砌路。
* 首版不追求完整复刻 Vic3 的所有道路变体与铁路系统。
* 首版不使用 Properties 贴图作为必需输入，先围绕 Diffuse / Normal、alpha、深度偏移和分层效果搭建。
* 首版不再通过写回高度图来制造路床/河道视觉，而是尽量用渲染层手段实现贴地与层次效果。

## Technical Approach

保留现有 path feature 的控制点编辑、样条采样、保存加载和撤销重做闭环，但把当前基于高度图写回的道路塑形从主路径中移除。道路渲染改为贴近 Vic3 的 ribbon + 贴图 shader 路线：使用道路专用样式数据选择土路或铺砌路资产，接入 Vic3 来源的 Diffuse / Normal 贴图，补齐 alpha 淡出、深度偏移与贴地渲染状态。首版只做道路，河流后续复用同一框架再补底部/表面双层。

## Decision (ADR-lite)

**Context**: 用户希望尽量贴近 Vic3 的实现方式来重做道路/河流，但首版范围需要可执行，同时不能继续依赖当前改高度图的做法。

**Decision**: 首版只做道路，且同时支持土路与铺砌路两套 Vic3 材质；数据模型改为道路专用样式枚举/资产引用，不再复用通用 `MaterialSlotIndex`；渲染依赖 Diffuse / Normal、alpha 与深度偏移，不使用 Properties 贴图，也不再以高度图写回作为最终视觉手段。

**Consequences**: 需要调整路径数据模型、保存格式、编辑器 UI 与 shader/材质接入，但语义会更清晰，也更接近目标渲染系统。河流被推迟到下一阶段，可以减少首版并行变量。

## Requirements (evolving)

* 保留现有道路/河流编辑、保存加载、撤销重做闭环。
* 去掉当前基于高度图写回的道路/河流塑形依赖。
* 首版范围只做道路，河流在后续阶段按同一渲染框架补齐。
* 道路从“纯色材质”升级为“基于纹理的 spline ribbon 渲染”。
* 道路首版同时支持 2 套 Vic3 风格 Diffuse / Normal 贴图：土路与铺砌路。
* 每条道路都需要能选择具体道路样式类型，并按 Vic3 的思路处理 alpha 与深度偏移。
* 道路样式数据应使用道路专用枚举或资产引用建模，不再继续复用通用 `MaterialSlotIndex`。
* 渲染应使用合适的深度状态/偏移避免与地形 z-fighting，不能依赖修改高度图去盖住缝隙。
* 新素材的来源、导入路径和项目内引用方式必须可重复。
* 若归档研究不足以支撑实现，应继续分析 `C:\Users\Redwa\Desktop\vic3-river&road.rdc` 获取缺失细节。

## Acceptance Criteria (evolving)

* [ ] 现有道路数据在重做后仍可加载、编辑、保存。
* [ ] 道路不再是纯色条带，而是显示 Vic3 来源的 Diffuse / Normal 贴图效果。
* [ ] 至少可以在土路和铺砌路两套道路材质之间切换，并正确影响渲染结果。
* [ ] 道路重做后不再依赖修改高度图来形成最终视觉效果。
* [ ] 重做后道路在视口中不出现明显 z-fighting 回归。
* [ ] 引入的新贴图资产在项目内有明确归档位置与可重复导入方式。

## Definition of Done (team quality bar)

* Tests added/updated (unit/integration where appropriate)
* Lint / typecheck / CI green
* Docs/notes updated if behavior changes
* Rollout/rollback considered if risky

## Out of Scope (explicit)

* Devastation / Pollution 覆盖系统
* 自动路口拓扑或自动交点拆分
* 铁路与多套道路类型一起落地
* 完整复刻 Vic3 的 stacked textures / 多变体随机混合
* 程序化路网生成工具

## Research References

* `../archive/2026-05/05-06-editor-road-river-drawing/research/vic3-road-river-rendering.md` — Vic3 道路/河流渲染结构、贴图类型、深度偏移与河流双层方案总结。

## Technical Notes

* 当前核心实现文件：
  * `Terrain.Editor/Services/PathFeatures/PathFeatureService.cs` — 现有路径编辑、地形塑形、ribbon mesh 构建与材质创建；其中高度图塑形逻辑需要退出主路径。
  * `Terrain.Editor/Rendering/PathDepthBiasPipelineProcessor.cs` — 当前路径渲染组深度状态处理。
  * `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` — 路径编辑输入与视口模式接入。
  * `Terrain.Editor/Services/TerrainManager.cs` — 路径数据保存/加载与地形状态集成。
  * `Terrain.Editor/Services/TomlProjectConfig.cs` — `path_nodes` / `path_features` TOML 序列化模型。
  * `Terrain.Editor/Services/MaterialSlotManager.cs` — 材质贴图数组和导入管理。
* 当前实现与目标实现的关键差异：
  * 已有：控制点图、样条采样、地形塑形、mesh 生成、基本 UV。
  * 缺失：道路贴图化材质、法线/属性贴图、河流底部/表面分层、河岸 alpha 淡出、流动法线、按目标风格整理的项目内资产。
* 最可能需要修改的文件：
  * `Terrain.Editor/Services/PathFeatures/PathFeatureService.cs`
  * `Terrain.Editor/Rendering/PathDepthBiasPipelineProcessor.cs`
  * `Terrain.Editor/Services/MaterialSlotManager.cs`
  * `Terrain.Editor/Services/TerrainManager.cs`
  * `Terrain.Editor/Services/TomlProjectConfig.cs`
  * `Terrain/Effects/` 或 `Terrain.Editor/Effects/` 下新增/调整的 shader 文件
  * 项目内新增的道路/河流纹理资产路径
