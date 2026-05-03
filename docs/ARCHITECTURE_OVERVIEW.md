# 地形编辑器架构概览
**从这里开始了解整个系统**

---

## TL;DR - 30 秒核心架构

**三层系统：**
- **Core（核心）**: 地形数据、高度图、流式加载
- **Rendering（渲染）**: GPU 实例化、LOD、虚拟纹理
- **Editor（编辑器）**: 笔刷系统、ImGui UI、编辑操作

**关键原则：** 分离数据层、渲染层、编辑层

**当前状态：** Core ✅ | Rendering ✅ | Editor ✅ | Vegetation 🚧 | Path ❌

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
| **虚拟纹理** | 🚧 进行中 | - |

### 编辑器层

| 系统 | 状态 | 文档 |
|------|------|------|
| **高度编辑** | ✅ 已实现 | [terrain-editor-design-phase-2](design/terrain-editor-design-phase-2.md) |
| **笔刷系统** | ✅ 已实现 | [terrain-editor-design-phase-2](design/terrain-editor-design-phase-2.md) |
| **ImGui UI** | ✅ 已实现 | [terrain-editor-design-phase-2](design/terrain-editor-design-phase-2.md) |
| **气候蒙版（ClimateMask）** | ✅ 已实现 | R8 格式，1/4 高度图分辨率，规则驱动材质索引 |
| **季节过滤（Season）** | ✅ 已实现 | EditorState.ActiveSeason 驱动规则求值 |
| **纹理刷** | ✅ 已实现 | [2026-04-06-3](log/2026/04/06/2026-04-06-3-terrain-texture-brush-implementation.md) |
| **纹理导入增强** | ✅ 已实现 | [texture-auto-normal-import-and-inspector](design/texture-auto-normal-import-and-inspector.md) |
| **数据同步机制** | ✅ 已实现 | [2026-04-07-1](log/2026/04/07/2026-04-07-1-unified-terrain-data-sync.md) |
| **材质索引图增强** | ✅ 已实现 | [2026-04-07-2](log/2026/04/07/2026-04-07-2-index-map-enhancement.md) |
| **Undo/Redo（Chunk事务）** | ✅ 已实现 | [2026-04-07-5](log/2026/04/07/2026-04-07-5-chunk-based-undo-redo-implementation.md) |
| **项目持久化（TOML）** | ✅ 已实现 | [2026-04-08-2](log/2026/04/08/2026-04-08-2-toml-project-persistence.md) |
| **植被编辑** | 🚧 进行中 | [terrain-editor-design-phase-3](design/terrain-editor-design-phase-3.md) |
| **导出系统（IExporter）** | ✅ 已实现 | 包含 Terrain 和 Biome Config 导出器 |

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

### 6. TOML 项目持久化
**问题：** 编辑器没有真正的 Open/Save 流程，用户无法保存和恢复工作状态
**方案：** 使用 .toml 文件作为项目配置（Tommy 库），存储 heightmap/climate_mask 路径和 material slot 纹理路径
**权衡：** TOML 比 JSON 更易手写编辑，但需要额外 NuGet 依赖
**关键：** 所有路径使用相对路径（相对于 .toml 所在目录），确保项目可移植

### 7. 导出系统（IExporter 模式）
**问题：** 编辑器中的修改无法直接导出为运行时 .terrain 文件，需依赖独立的 TerrainPreProcessor
**方案：** IExporter 接口 + ExportManager 单例，每种导出类型实现接口并注册；TerrainExporter 从内存状态直接导出
**权衡：** 在 Editor 内重写导出逻辑 vs 引用 TerrainPreProcessor 库；选择重写以避免跨项目依赖
**关键：** 流式 + 分层并行（逐层 mipmap → 并行计算 tiles → 顺序写入），HeightMap padding=2, SplatMap padding=1

### 8. Biome Config 导出
**问题：** Runtime 依赖编辑器项目 TOML 文件加载材质，运行时不应依赖编辑器项目文件
**方案：** BiomeConfigExporter 导出独立的 biome_config.toml，Runtime 的 BiomeConfigPath 指向该文件
**权衡：** 独立 TOML 文件 vs 嵌入 .terrain 文件；选择独立文件以保持关注点分离
**关键：** 路径转换使用 TomlProjectConfig.MakeRelative（绝对→相对），Tommy TomlArray+TomlTable 自动生成 [[material_slots]] 格式

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
| `Terrain.Editor/Services/ProjectManager.cs` | 项目管理（TOML 配置、dirty tracking） |
| `Terrain.Editor/Services/TomlProjectConfig.cs` | TOML 配置数据模型和读写器 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | 地形实体（含统一数据同步接口） |
| `Terrain.Editor/Brushes/` | 笔刷系统 |
| `Terrain.Editor/UI/Dialogs/NewProjectWizard.cs` | 新建项目模态弹窗 |
| `Terrain.Editor/Services/Export/IExporter.cs` | 导出器接口（可扩展） |
| `Terrain.Editor/Services/Export/ExportManager.cs` | 导出管理器（注册、执行、错误回滚） |
| `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` | .terrain 文件导出实现 |
| `Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs` | Biome 配置 TOML 导出实现 |
| `Terrain.Editor/UI/Dialogs/ExportProgressDialog.cs` | 导出进度模态弹窗 |

### 着色器
| 文件 | 职责 |
|------|------|
| `Terrain/Effects/Build/` | LOD 构建 |
| `Terrain/Effects/Material/` | 材质着色器 |

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

*最后更新: 2026-04-20*
*状态: 反映当前实现状态*
