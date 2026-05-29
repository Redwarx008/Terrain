## Context

当前地形编辑器缺少河流系统。之前有完整的实现（RiverMaskMeshService, 2556 行），但已在 da45569 中删除。需要基于颜色索引 PNG（river.png）重新构建，参考 CK3 的河流渲染方式。

**现有状态**：
- 高程图 + Biome 蒙版系统已就绪
- 之前的路径/河流着色器已被清理
- 已研究 Vic3/CK3 的河流渲染实现（双 Pass：底部视差映射 + 水面流动动画）
- CK3 的 `rivers.png` 使用颜色索引格式，每段河流独立 draw call

**约束**：
- 项目使用 Stride Engine + C#，自定义着色器通过 SDSL
- river.png 分辨率 = 高度图的一半
- 每像素世界坐标映射：`worldX = pixelX * (terrainWorldSize / riverMapWidth)`

## Goals / Non-Goals

**Goals:**
- 新增 `EditorMode.River`，在模式栏显示 River 按钮
- 在右侧 Inspector 面板提供：导入 PNG → 预览 → Generate 按钮的完整 UI
- 从颜色索引 PNG 解析出 RiverCell[,] 数据
- 像素追踪提取河流段 → Catmull-Rom 样条插值 → Ribbon 网格
- 每段河流生成独立的 Entity（参考 CK3 的每段独立 draw call）
- 支持 Source（绿）/ Confluence（红）/ Bifurcation（黄）三类交汇语义
- 双 Pass 渲染：底部（简化的视差映射）+ 水面（流动法线动画）
- 宽度映射：13 种颜色从 1.25（浅蓝）到 2.75（深绿）
- 河流灰度图路径持久化到 TOML 项目配置

**Non-Goals:**
- 不实现完整的折射/反射链路（JominiRefraction/CompressWorldSpace）
- 不做河流交汇的动态编辑（只从 PNG 一次性生成）
- 不对河流地形做高度修改（使用 DepthBias 替代）
- 不与植被、道路等系统交互
- 不实现河流泡沫/雾气等高级效果

## Decisions

### D1：每段河流独立 Entity
- **选择**：每段 `RiverSegment` 生成独立的 `Entity`，包含自己的 `MeshDraw` + `Material`
- **理由**：参考 CK3 的 Draw 460（580 indices）所示，每段独立 draw call。便于独立控制显隐、调试、后续增量更新
- **已考虑 alternative**：用单个 Entity 合并所有段 → 拒绝，因为交汇 taper 需要单独的 vertex buffer

### D2：像素追踪而非骨架提取
- **选择**：直接从颜色索引数组做连通性分析，追踪 River 像素路径
- **理由**：颜色索引格式天然编码了路径语义（每种颜色就是河流的一个段），不需要额外的二值化/骨架细化步骤
- **已考虑 alternative**：旧版的 BuildSkeleton/ExtractSegments 流程 → 拒绝，过于复杂（2556 行中约一半是骨架代码）

### D3：Catmull-Rom 样条平滑
- **选择**：以像素中心为控制点，Catmull-Rom 插值生成密集中心线
- **理由**：Catmull-Rom 保证通过所有控制点（像素路径），自然平滑，适合像素级路径
- **已考虑 alternative**：线性插值 → 锯齿明显；Bezier → 需要拟合不在控制点上的曲线

### D4：宽度取自像素颜色平均值
- **选择**：取段内所有 River 像素的宽度值平均值作为 `AvgHalfWidth`，交汇端再 taper
- **理由**：避免单像素颜色突变导致的宽度抖动，整段河流宽度一致更自然

### D5：交汇处理用 DistanceToMain + Taper
- **选择**：参考 CK3/Vic3 的 `DistanceToMain` 参数控制 alpha 渐隐，配合几何端的 taper
- **理由**：不需要专门的 junction shader，纯靠顶点参数 + blend state 实现
- **已考虑 alternative**：像素 shader 中判断交汇 → 需要额外纹理采样，复杂度高

### D6：双 Pass 渲染简化版
- **选择**：底部 Pass（简化视差映射）+ 水面 Pass（流动法线 + 水色）
- **理由**：CK3 的双 Pass 带来了视觉深度感，即使简化也能达到不错的效果
- **已考虑 alternative**：单 Pass 水面 → 缺少河底深度感；完整 CK3 双 Pass → 需要 dual-source blending 和折射缓冲，实现成本高

### D7：独立 River Mode
- **选择**：新增 `EditorMode.River`，与 Sculpt/Biome/Foliage/Settings 并列
- **理由**：河流是一个独立的编辑功能，有自己的工具面板和交互模式
- **已考虑 alternative**：作为 Foliage 子模式或复用 Path Mode → 语义不够清晰，功能耦合

## Risks / Trade-offs

- **[Risk] 像素追踪效率**：如果 river.png 分辨率很高（如 4096×4096），连通性分析可能变慢 → **Mitigation**：在中低分辨率（高度图/2）下工作，扫描复杂度 O(N) 可控
- **[Risk] 交汇处几何不自然**：简单 taper 可能导致视觉突兀 → **Mitigation**：参考 CK3 的 `DistanceToMain * EdgeFade` 公式做双重渐隐
- **[Risk] DepthBias 数值不匹配**：不同 GPU/驱动下 -50000 的行为可能不同 → **Mitigation**：在 Stride 中测试确认，提供可配置参数
- **[Trade-off] 简化的双 Pass vs 完整折射链路**：不做折射意味着水底颜色是固定的（不随水面波纹扭曲） → **接受**：第一阶段简化实现，后续可迭代增强
- **[Trade-off] 每段独立 Entity 的开销**：100 段河流就有 200 个 Entity（底部+水面） → **接受**：CK3 在类似规模下运行良好

## Migration Plan

1. 创建数据模型（RiverPixelType, RiverCell, RiverSegment）
2. 实现颜色索引解析（PNG → RiverCell[,]）
3. 实现像素追踪与段提取
4. 实现 Catmull-Rom 样条插值与 Ribbon 网格生成
5. 实现 SDSL 着色器（底部 Pass + 水面 Pass）
6. 实现 River Mode 的 UI（Inspector 面板、导入、Generate）
7. 集成到 Editor（EditorMode, ViewModel, MainWindow.axaml）
8. 集成到 TOML 项目持久化
9. 测试并调整宽度/DepthBias 参数

## Open Questions

- 底部着色器的纹理资源：使用简单程序化颜色还是导入外部 DDS？
- UV 平铺比例（TextureUvScale）：默认值需要在地形编辑器内测试确定
