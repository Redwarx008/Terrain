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
| **流式加载** | ✅ 已实现 | [terrain-streaming-design](../plans/terrain-streaming-design.md) |
| **LOD 系统** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md) |

### 渲染层

| 系统 | 状态 | 文档 |
|------|------|------|
| **地形渲染** | ✅ 已实现 | [terrain-editor-design-phase-1](design/terrain-editor-design-phase-1.md) |
| **实例化渲染** | ✅ 已实现 | [instance-buffer-refactor](../plans/instance-buffer-refactor.md) |
| **材质系统** | ✅ 已实现 | - |
| **map_data TOML 规格** | ✅ 已记录 | [map-data-toml-formats](design/map-data-toml-formats.md) |
| **虚拟纹理** | 🚧 进行中 | - |

### 路径与河流层

| 系统 | 状态 | 文档 |
|------|------|------|
| **路径编辑** | ✅ 已实现 | [Phase 6](design/terrain-editor-design-phase-6.md) |
| **道路渲染** | ✅ 已实现 | [adr-013-vic3-path-rendering](log/decisions/adr-013-vic3-path-rendering.md) |
| **河流网格生成** | ✅ 已实现 | [2026-06-05-1](log/2026/06/05/2026-06-05-1-river-mesh-generation-fix.md) |
| **河流多 pass 渲染** | ✅ 已实现 | [adr-014-river-rendering-architecture](log/decisions/adr-014-river-rendering-architecture.md) |

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
| **Editor 作者态启动** | ✅ 已实现 | 自动补齐 `default.toml` / `descriptor.toml` / `biome_settings.toml`，缺失 `heightmap.png` 时以待补资源模式进入 |
| **旧项目持久化（TOML）** | ❌ 已移除 | Editor 固定 Terrain 工作区；旧 ProjectManager/TomlProjectConfig 已删除 |
| **植被编辑** | 🚧 进行中 | [terrain-editor-design-phase-3](design/terrain-editor-design-phase-3.md) |
| **导出系统（IExporter）** | ✅ 已实现 | 当前保留 Terrain `.terrain` 导出；旧 Biome Config 导出已移除 |

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
**方案：** `GameResourceRootLocator` 继续从二进制位置向上扫描工作区同级 `game/`；如果起点本身已经位于目录名为 `game` 且包含 `map_data/` 的合法根，也会直接接受该根。`LaunchSetting.json` 固定放在 `AppContext.BaseDirectory`，缺失时自动生成默认文件。Editor 与 Runtime 都先通过共享的 `GameResourceResolverBootstrap` 构建 `base(gameRoot) + enabled absolute-path mods`，再进入各自 bootstrap。
**关键：** `mods[*].Root` 保持绝对路径语义；Editor 仍允许缺失 `.terrain` / `biome_mask.png`，Runtime 仍严格要求它们。

### 7. 导出系统（IExporter 模式）
**问题：** 编辑器中的修改无法直接导出为运行时 .terrain 文件，需依赖独立的 TerrainPreProcessor
**方案：** IExporter 接口 + ExportManager 单例，每种导出类型实现接口并注册；TerrainExporter 从内存状态直接导出
**权衡：** 在 Editor 内重写导出逻辑 vs 引用 TerrainPreProcessor 库；选择重写以避免跨项目依赖
**关键：** 流式 + 分层并行（逐层 mipmap → 并行计算 tiles → 顺序写入），HeightMap padding=2, SplatMap padding=1

### 8. 虚拟资源系统驱动 Runtime 地形加载
**问题：** Runtime 依赖组件上的显式文件路径和旧 BiomeConfig TOML，无法表达 base + mod 覆盖顺序
**方案：** Runtime 固定从当前二进制位置向上定位工作区 `game/` 资源根；如果起点本身已在目录名为 `game` 且包含 `map_data/` 的合法根，也会直接接受该根。随后从 exe 目录旁的 `LaunchSetting.json` 读取或自动生成本地 mod 配置；base 作为隐式根，按启用 mod 顺序构建 `GameResourceResolver`，再通过 `GameRuntimeResourceBootstrap` 解析 `map_data/default.toml` 与固定 companion 资源
**权衡：** 不保留旧路径兼容，迁移更直接但资源入口更统一
**关键：** `TerrainComponent` 不再保存资源路径；`.terrain` 仍由 `bundle.TerrainDataPath` 直接读取；Runtime 会忽略 `default.toml` 中的 `heightmap` 声明，并使用 `.terrain` 内的高度数据配合 `biome_settings.toml` + `materials/descriptor.toml` / `biome_mask.png` 构建 detail map；若 `terrain.terrain` 或 `biome_mask.png` 缺失，`TerrainProcessor` 记录错误日志并保持 terrain 未初始化；同配置失败后不会逐帧重复重试

### 9. 河流渲染采用 RiverComponent → RiverProcessor → RiverRenderObject → RiverRenderFeature
**问题：** 仅靠 editor service 或临时 `ModelComponent` 预览无法承载河流的独立 mesh 生命周期、双 pass 渲染和视口调试模式。
**方案：** 将河流作为独立渲染子系统接入 Stride 渲染管线：
- `RiverRenderingService` 仅负责 editor façade（接收 `RiverSegment`、驱动 mesh 生成、同步可见性）
- `RiverComponent` 持有快照化 `RiverMeshData`
- `RiverProcessor` 负责版本同步与 `RiverRenderObject` 生命周期
- `RiverRenderFeature` 负责河底/水面双 pass、底部折射缓冲与调试光栅状态
- 河流 shader 统一使用 `TransformationWAndVP` 生成 `PositionWS / PositionH / DepthVS`
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
| `Terrain.Editor/Services/TerrainManager.cs` | 地形管理服务 |
| `Terrain.Editor/Services/HeightEditor.cs` | 高度编辑服务 |
| `Terrain.Editor/Services/PaintEditor.cs` | 材质绘制服务 |
| `Terrain.Editor/Services/ClimateEditor.cs` | 气候蒙版笔刷服务 |
| `Terrain.Editor/Services/ClimateMask.cs` | 气候蒙版数据（R8, 1/4 高度图） |
| `Terrain.Editor/Services/ClimateRuleService.cs` | 气候定义和规则栈管理 |
| `Terrain.Editor/Services/Commands/HistoryManager.cs` | Undo/Redo 历史事务管理 |
| `Terrain.Editor/Services/Commands/StrokeChunkTracker.cs` | 笔触 Chunk 跟踪与去重 |
| `Terrain.Editor/Services/MaterialSlotManager.cs` | 材质槽位管理 |
| `Terrain.Editor/Services/RiverRenderingService.cs` | 河流渲染 façade（mesh 同步、显隐控制、桥接编辑器与渲染组件） |
| `Terrain.Editor/Services/EditorDirtyState.cs` | 编辑器 dirty 状态跟踪（不携带项目路径） |
| `Terrain.Editor/Services/Resources/EditorBootstrapService.cs` | 启动时按 exe 目录旁 `LaunchSetting.json` 构建 Editor 资源会话 |
| `Terrain.Editor/Services/Resources/EditorResourceSession.cs` | 当前命中的虚拟资源实体路径与写回目标 |
| `Terrain.Editor/Services/Resources/*Writer.cs` | 作者态资源写回到当前命中的实体文件 |
| `Terrain/Resources/GameResourceRootLocator.cs` | 从二进制位置向上定位工作区 `game/` 资源根 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | 地形实体（含统一数据同步接口） |
| `Terrain.Editor/Rendering/River/RiverComponent.cs` | 河流 mesh 快照组件 |
| `Terrain.Editor/Rendering/River/RiverProcessor.cs` | 河流组件到渲染对象的同步处理器 |
| `Terrain.Editor/Rendering/River/RiverRenderObject.cs` | 河流 GPU 顶点/索引缓冲与 bounds |
| `Terrain.Editor/Rendering/River/RiverRenderFeature.cs` | 河流河底/水面双 pass 渲染特性 |
| `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs` | 注册河流 RenderFeature 并创建编辑器侧 RiverSystem |
| `Terrain.Editor/Brushes/` | 笔刷系统 |
| `Terrain.Editor/Services/Export/IExporter.cs` | 导出器接口（可扩展） |
| `Terrain.Editor/Services/Export/ExportManager.cs` | 导出管理器（注册、执行、错误回滚） |
| `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` | .terrain 文件导出实现 |
| `Terrain.Editor/UI/Dialogs/ExportProgressDialog.cs` | 导出进度模态弹窗 |

### 着色器
| 文件 | 职责 |
|------|------|
| `Terrain/Effects/Build/` | LOD 构建 |
| `Terrain/Effects/Material/` | 材质着色器 |
| `Terrain.Editor/Effects/RiverBottom.sdsl` | 河底 pass（折射缓冲/底色） |
| `Terrain.Editor/Effects/RiverSurface.sdsl` | 水面 pass（流动、泡沫、折射采样） |
| `Terrain.Editor/Effects/RiverVertexStreams.sdsl` | 河流自定义顶点语义 |
| `Terrain.Editor/Effects/RiverWaterCommon.sdsl` | 河流水体共用函数 |

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

*最后更新: 2026-06-15*
*状态: 反映当前实现状态*
