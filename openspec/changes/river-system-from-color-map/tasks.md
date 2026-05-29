## 1. 数据模型

- [x] 1.1 创建 `RiverPixelType` 枚举（Land=0, River=1, Source=2, Confluence=3, Bifurcation=4, Ocean=5）
- [x] 1.2 创建 `RiverCell` record struct（Type + Width）
- [x] 1.3 创建 `RiverSegment` 数据结构（Cells, StartKind, EndKind, NodeKeys, AvgHalfWidth, Centerline, WorldLength, TaperFlags）
- [x] 1.4 定义 13 色宽度映射表（0.625~1.375 half-width）
- [x] 1.5 实现 RiverCell.ToRgba32() / FromRgba32() 颜色转换

## 2. 颜色索引导入

- [x] 2.1 实现 `RiverColorMapService`：PNG → RiverCell[,] 加载
- [x] 2.2 实现精确颜色匹配（±2 RGB tolerance）
- [x] 2.3 实现正交连通性验证（≤2 邻居、禁止对角）
- [x] 2.4 实现 Source/Confluence/Bifurcation 语义验证（单 Source、红/黄正交相邻）

## 3. 网格生成

- [x] 3.1 实现像素追踪算法（4-方向连通性，提取 RiverSegment）
- [x] 3.2 实现 Catmull-Rom 样条插值生成中心线
- [x] 3.3 实现地形高度采样（从 TerrainManager.HeightDataCache 读取）
- [x] 3.4 实现 Ribbon 网格构建（左右顶点、UV、Tangent/垂线计算）
- [x] 3.5 实现交汇 taper（TaperStart/TaperEnd 渐隐）
- [x] 3.6 实现 VertexBuffer + IndexBuffer 生成
- [x] 3.7 实现网格清理（Re-generate 时移除旧 Entity）

## 4. 渲染着色器

- [x] 4.1 创建 `RiverSurface.sdsl`：水面 Pass（基础水色 + 流动法线 + 边缘淡出）
- [x] 4.2 创建 `RiverSurface.sdsl.cs`：着色器 key 和参数
- [x] 4.3 创建 `RiverBottom.sdsl`：底部 Pass（简化视差映射 + 底部纹理）
- [x] 4.4 创建 `RiverBottom.sdsl.cs`：着色器 key 和参数
- [x] 4.5 创建 `RiverEffect.sdfx`：组合双 Pass 效果文件
- [x] 4.6 实现 Material 创建逻辑（关联着色器、设置 BlendState/DepthBias）
- [x] 4.7 注册着色器到 `.csproj`（sdsgl 编译流程）

## 5. River Mode UI

- [x] 5.1 添加 `EditorMode.River` 枚举值
- [x] 5.2 在 `EditorShellViewModel` 添加 River Mode 属性逻辑
- [x] 5.3 在 `MainWindow.axaml` 添加 River Mode Inspector 面板
- [x] 5.4 实现导入 PNG 按钮和文件对话框
- [x] 5.5 实现 Generate 按钮和状态显示
- [x] 5.6 实现 Width Scale 滑块和 Show/Hide 复选框
- [x] 5.7 在 `EmbeddedStrideViewportGame` 添加 River Mode 支持

## 6. 项目持久化

- [x] 6.1 在 TOML 配置添加 `RiverMapImagePath` 字段
- [x] 6.2 实现保存/加载 river.png 路径
- [x] 6.3 项目加载时自动恢复 RiverCell[,] 数据

## 7. 集成与测试

- [ ] 7.1 验证 13 色宽度映射正确性
- [ ] 7.2 测试 Source → River → Confluence 完整链路
- [ ] 7.3 测试 Bifurcation（分叉）场景
- [ ] 7.4 测试无效地图的错误报告
- [ ] 7.5 测试 TOML 保存/加载往返
- [ ] 7.6 测试 Re-generate 清理
- [ ] 7.7 视觉调整：DepthBias、宽度缩放、水色参数
