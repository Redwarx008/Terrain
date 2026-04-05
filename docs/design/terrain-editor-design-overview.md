# 地形编辑器设计文档总览

## 状态图例

| 符号 | 设计状态 | 实现状态 | 说明 |
|------|---------|---------|------|
| ✅ | 完成 | 完成 | 设计和实现均已完成 |
| 🚧 | 完成 | 待实现 | 设计完成，等待实现 |
| 📋 | 规划中 | 未开始 | 初步规划，详细设计待补充 |
| 🆕 | 新增 | 未开始 | 新增功能，设计待细化 |

> **注意：** 本文档状态反映"设计完成度"与"实现完成度"两个维度。设计阶段文档完成不代表代码实现完成。

## 文档索引

| 阶段 | 文档 | 状态 | 说明 |
|------|------|------|------|
| Phase 1 | [terrain-editor-design-phase-1.md](terrain-editor-design-phase-1.md) | ✅ 完成 | 基础地形渲染 |
| Phase 2 | [terrain-editor-design-phase-2.md](terrain-editor-design-phase-2.md) | ✅ 完成 | 高度编辑工具 |
| Phase 3 | [terrain-editor-design-phase-3.md](terrain-editor-design-phase-3.md) | ✅ 完成 | 植被系统基础 |
| Phase 4 | [terrain-editor-design-phase-4.md](terrain-editor-design-phase-4.md) | 📋 规划中 | 高级功能（笔刷扩展、侵蚀模拟） |
| Phase 5 | [terrain-editor-design-phase-5.md](terrain-editor-design-phase-5.md) | 🆕 新增 | 植被系统扩展（三级LOD、GPU剔除） |
| Phase 6 | [terrain-editor-design-phase-6.md](terrain-editor-design-phase-6.md) | 🆕 新增 | 路径系统（贝塞尔曲线、道路、河流） |
| Phase 7 | [terrain-editor-design-phase-7.md](terrain-editor-design-phase-7.md) | 🆕 新增 | 渲染优化（GPU LOD、Hi-Z剔除） |

## 实现路线图

```
Phase 1: 基础地形渲染 ────────────────────── ✅ 已完成
    │
    ├─ 四叉树LOD
    ├─ SVT虚拟纹理流式加载
    └─ GPU实例化渲染
    │
Phase 2: 高度编辑工具 ────────────────────── ✅ 已完成
    │
    ├─ 笔刷系统
    ├─ 高度编辑操作
    └─ ImGui编辑器UI
    │
Phase 3: 植被系统基础 ────────────────────── ✅ 已完成（设计）
    │
    ├─ 实例数据结构
    ├─ 放置/移除工具
    └─ 基础渲染
    │
Phase 5: 植被系统扩展 ────────────────────── 🚧 待实现
    │
    ├─ 三级LOD（完整→简化→Billboard）
    ├─ GPU剔除
    └─ Indirect绘制
    │
Phase 6: 路径系统 ────────────────────────── 🚧 待实现
    │
    ├─ 贝塞尔曲线网络
    ├─ 道路网格生成
    └─ 地形变形适配
    │
Phase 7: 渲染优化 ────────────────────────── 🚧 待实现
    │
    ├─ GPU LOD迁移
    ├─ GPU视锥剔除
    └─ Hi-Z遮挡剔除（可选）
    │
Phase 4: 高级功能 ────────────────────────── 📋 未来规划
    │
    ├─ 笔刷形状扩展
    ├─ 侵蚀模拟
    └─ 程序化生成
```

## 预估时间

| 阶段 | 内容 | 预估时间 | 优化点 |
|------|------|---------|--------|
| **Phase 5** | 植被扩展 + GPU剔除 | 2.5周 | GPU剔除验证 |
| **Phase 6** | 路径系统 + 地形变形 | 3周 | 曲线采样优化 |
| **Phase 7** | 渲染优化 | 2周 | GPU LOD迁移 |

**总计**: 约7.5周

## 技术决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| 植被LOD方案 | 三级LOD | 平衡性能和质量 |
| 路径变形时机 | CPU预计算 | 运行时零GPU开销 |
| 优化策略 | 边开发边优化 | 确保每步达标 |

## 参考项目

- **Godot MTerrain Plugin** (`E:\reference\Godot-MTerrain-plugin`)
  - 贝塞尔曲线网络 (MCurve)
  - 地形变形适配
  - 草地系统 (MGrass)

- **Unity GPU Indirect** (`https://github.com/EricHu33/UnityGrassIndirectRenderingExample`)
  - GPU实例化渲染
  - Compute Shader剔除

- **Terrain3D** (`https://github.com/TokisanGames/Terrain3D`)
  - LOD系统参考
  - 流式加载架构

## 关键文件

### 核心运行时
- `Terrain/Core/TerrainComponent.cs` - 地形组件
- `Terrain/Rendering/TerrainRenderFeature.cs` - 渲染特性
- `Terrain/Streaming/TerrainStreaming.cs` - 流式加载

### 编辑器
- `Terrain.Editor/Services/TerrainManager.cs` - 地形管理
- `Terrain.Editor/Services/HeightEditor.cs` - 高度编辑

### 着色器
- `Terrain/Effects/Build/TerrainBuildLodLookup.sdsl` - LOD构建
- `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` - 位移着色器
