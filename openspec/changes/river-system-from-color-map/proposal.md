## Why

地形编辑器缺少河流系统。之前的研究（Vic3/CK3）确定了颜色索引 PNG + 双 Pass 渲染是最佳路径，且已被验证可行。旧的 RiverMaskMeshService（2556行）过于复杂且已被删除，需要基于 CK3 风格的简化方案重新构建——每段河流独立 Entity、Catmull-Rom 样条插值、双 Pass 渲染。这是地形编辑器从高度/材质编辑迈向完整环境编辑的关键一步。

## What Changes

**新增 EditorMode.River**
- From: 无河流编辑模式
- To: 模式栏新增 River 按钮，右侧 Inspector 面板提供导入 PNG → 预览 → Generate 完整交互
- Reason: 河流是独立的地形特征，需要独立的编辑入口

**从颜色索引 PNG 构建河流 Mesh**
- From: 无河流渲染
- To: 从 river.png（分辨率=高度图/2）解析颜色 → 像素追踪提取段 → Catmull-Rom 样条 → Ribbon 网格 → 每段独立 Entity 渲染
- Reason: 颜色索引格式天然编码路径+宽度，比旧版骨架提取更简洁

**双 Pass 河流渲染**
- From: 无河流着色器（PathRiverSurface.sdsl 已删除）
- To: 底部 Pass（简化视差映射）+ 水面 Pass（流动法线动画），DepthBias = -50000
- Reason: 参考 CK3 实现，提供视觉深度感和流动效果

**交汇语义**
- From: 不支持河流交汇
- To: 支持 Source（绿）/ Confluence（红）/ Bifurcation（黄），通过 DistanceToMain + taper 渐隐
- Reason: 真实河流系统需要支流汇入和分叉

**项目持久化**
- From: TOML 项目配置无河流相关字段
- To: 保存 river.png 路径到 .toml 项目文件

## Capabilities

### New Capabilities
- `river-data-model`: RiverCell[,] 数据、RiverPixelType 枚举、RiverSegment 结构体、宽度映射表
- `river-color-map-import`: 从 PNG 读取颜色索引图像并解析为 RiverCell[,]，包括精确颜色匹配与容差
- `river-mesh-generation`: 像素追踪提取段 → Catmull-Rom 样条插值 → Ribbon 带状网格 → 顶点/索引 Buffer
- `river-rendering`: SDSL 双 Pass 着色器（底部视差映射 + 水面流动法线），Material 创建，Entity 管理
- `river-editor-mode`: EditorMode.River、Inspector UI（导入按钮、预览、Generate、状态显示）
- `river-persistence`: river.png 路径的 TOML 保存/加载

### Modified Capabilities
- （无现有 spec 被修改）

## Impact

- **新文件**：RiverDataModels.cs, RiverColorMapService.cs, RiverMeshGenerator.cs, RiverSurface.sdsl, RiverBottom.sdsl, RiverSurface.sdsl.cs, RiverBottom.sdsl.cs
- **修改文件**：EditorMode.cs, EditorShellViewModel.cs, MainWindow.axaml, EmbeddedStrideViewportGame.cs, TerrainManager.cs, TomlProjectConfig.cs, Terrain.Editor.csproj
- **依赖**：SixLabors.ImageSharp（已有），Stride 引擎 SDK（已有）
