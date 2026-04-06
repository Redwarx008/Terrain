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
| **纹理刷** | ✅ 已实现 | [2026-04-06-3](log/2026/04/06/2026-04-06-3-terrain-texture-brush-implementation.md) |
| **纹理导入增强** | ✅ 已实现 | [texture-auto-normal-import-and-inspector](design/texture-auto-normal-import-and-inspector.md) |
| **数据同步机制** | ✅ 已实现 | [2026-04-07-1](log/2026/04/07/2026-04-07-1-unified-terrain-data-sync.md) |
| **植被编辑** | 🚧 进行中 | [terrain-editor-design-phase-3](design/terrain-editor-design-phase-3.md) |

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

### 1. 四叉树 LOD
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
| `Terrain.Editor/Services/MaterialSlotManager.cs` | 材质槽位管理 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | 地形实体（含统一数据同步接口） |
| `Terrain.Editor/Brushes/` | 笔刷系统 |

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

*最后更新: 2026-04-07*
*状态: 反映当前实现状态*
