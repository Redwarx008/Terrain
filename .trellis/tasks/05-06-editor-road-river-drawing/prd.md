# brainstorm: editor road and river drawing

## Goal

在 Terrain 编辑器中新增可直接画线的道路网/河流编辑能力，其中河流被视为一种基于 mesh 的道路类型，以便用户能够在地形上交互式勾画路径、塑形地表并保存到项目中。

## What I already know

* 用户目标是“加入道路网和河流（河流也是一种道路，基于 mesh），要在编辑器中可直接画线”。
* 现有编辑器已经有模式切换 `Terrain.Editor/Models/EditorMode.cs`、全局编辑状态 `Terrain.Editor/Services/EditorState.cs`、地形拾取 `Terrain.Editor/Services/TerrainRaycast.cs`、撤销重做 `Terrain.Editor/Services/Commands/HistoryManager.cs`。
* 视口运行时承载在 `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`，现有交互已经区分左键编辑、右键相机导航。
* 项目保存当前通过 `Terrain.Editor/Services/TerrainManager.cs` + `Terrain.Editor/Services/TomlProjectConfig.cs` 写入 TOML，已有高度图、BiomeMask、材质槽、Biome 层与 modifier，但还没有道路/河流数据结构。
* `Terrain.Editor/Services/ProjectManager.cs` 已有项目目录和 dirty 状态管理，可作为新增道路数据持久化入口。

## Requirements

* 编辑器内可创建道路/河流路径。
* 河流作为道路的一种类型，但视觉与材质参数可区分。
* 路径数据可保存到项目，并在打开项目时恢复。
* 路径编辑需能与地形拾取、视口输入、项目 dirty 状态集成。
* MVP 采用混合交互：以控制点/样条编辑为主，同时支持拖拽草绘后自动简化为控制点。
* MVP 需要支持真实路网语义：路径之间可共享节点或明确连接关系，而不只是彼此独立的样条。
* MVP 中道路/河流编辑需要联动高度图，支持按路径压平路床或挖出河道，并与现有地形编辑/撤销重做体系一致。
* 首版宽度、深度、边坡与材质参数按整条路径统一配置。
* 首版连接规则为显式连接：只有节点吸附到已有节点时才建立连接，线段相交不自动拆分。
* MVP 包含已有路径编辑完整闭环：移动节点、插入节点、删除节点、断开节点、重连节点。
* 节点编辑时需要在视口中显示 GUI 提示（gizmos），让控制点、选中点和连接点可辨识。
* 路线与河流几何表现使用 centripetal Catmull-Rom 样条采样；拓扑、保存和连接关系仍以控制点节点图为准。

## Acceptance Criteria

* [ ] 用户可以在编辑器视口中创建至少一条道路或河流路径。
* [ ] 用户可以通过控制点编辑已有路径，包括移动节点、插入节点、删除节点、断开节点、重连节点。
* [ ] 路径编辑模式下控制点有可见 gizmo 提示，选中节点和共享连接节点有明显区分。
* [ ] 道路/河流 mesh、拾取和地形塑形沿 Catmull-Rom 采样曲线工作，而不是只沿控制点折线工作。
* [ ] 用户可以将节点显式吸附到已有节点以建立连接，普通线段相交不会自动拆分成连接。
* [ ] 道路/河流会生成可见 mesh，并根据路径参数修改地形高度图。
* [ ] 保存项目后重新打开，路径、连接关系和参数能够恢复。
* [ ] 道路/河流相关创建与编辑操作可纳入撤销/重做。

## Definition of Done (team quality bar)

* Tests added/updated (unit/integration where appropriate)
* Lint / typecheck / CI green
* Docs/notes updated if behavior changes
* Rollout/rollback considered if risky

## Technical Approach

建议将道路与河流统一建模为一类 path feature：底层包含节点、边/样条段、整条路径样式参数，以及道路/河流类型标记。编辑器侧新增一个独立模式或工具组，接入现有 `EditorState`、`EmbeddedStrideViewportGame` 与 `TerrainRaycast`，实现创建、草绘简化、节点编辑和显式吸附连接。运行时侧在 TerrainManager 附近新增一层 path feature 管理与 mesh/高度塑形生成，保存时扩展 `TomlProjectConfig`，将路径节点、连接关系和统一参数写入项目文件，并纳入现有 dirty、保存/加载与 HistoryManager 命令体系。

## Decision (ADR-lite)

**Context**: 道路/河流功能既要能在编辑器中直接画线，又要支持真实路网、河道/路床塑形、保存加载和后续可扩展性。

**Decision**: MVP 采用“混合交互 + 真实路网 + 显式连接 + 整条路径统一参数 + 完整路径编辑闭环”的方案。河流与道路共享 path feature 基础抽象；路径编辑既生成 mesh，也联动高度图。几何表现层使用 centripetal Catmull-Rom 样条从控制点生成采样曲线，但底层拓扑仍保存控制点节点图。

**Consequences**: 首版范围已经不小，但能形成完整可用闭环，避免做出只能创建不能维护的半成品。为了控制复杂度，MVP 明确不做自动交点拆分、自动路口拓扑、按节点参数变化和更高级的程序化能力。

## Out of Scope

* 自动路口拓扑生成
* 线段相交自动拆分/自动建连接
* 按段或按节点变化的宽度/深度/边坡参数
* 交通/水流模拟
* 与外部 GIS/道路数据源的导入导出
* 完整程序化路网生成工具

## Technical Notes

* 关键集成点：`Terrain.Editor/Services/EditorState.cs`、`Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`、`Terrain.Editor/Services/TerrainRaycast.cs`、`Terrain.Editor/Services/Commands/HistoryManager.cs`、`Terrain.Editor/Services/TerrainManager.cs`、`Terrain.Editor/Services/TomlProjectConfig.cs`。
* 现有保存格式基于 TOML，适合新增 `path_features` / `nodes` / `edges` 等节；也可以按 feature 类型拆成 `roads` 与 `rivers`，但底层仍建议共用一个抽象。
* 现有 TerrainManager 已负责高度图缓存与保存，因此道路/河流塑形最好直接复用其高度数据更新与 dirty 标记机制。
* 视口当前已有左键编辑、右键导航模式，适合在 `EmbeddedStrideViewportGame` 中扩展出路径编辑状态机。
