<!--
Raw capture of superpowers:brainstorming output.

本檔原樣捕捉 brainstorming skill 的產出，不強制結構。
Skill 的自然產出通常是 decision log 格式（背景 → 決議鏈 Q1-Qn → 設計取捨），
但依對話內容可能有不同組織方式。

design.md 從本檔萃取並重新整理為結構化設計文件。

不要將本檔的內容複製到 design.md — design.md 是獨立的重組產物，
兩者互補但不重疊。
-->

# Brainstorm: 基于颜色索引地图构建河流系统

## 背景调研

- 地形编辑器架构：Core/Rendering/Editor 三层
- 之前有完整的河流系统（RiverMaskMeshService，2556 行），已被删除（da45569）
- 道路/路径系统也已被删除（981339d）
- 已有的调研：Vic3 道路与河流渲染（docs/log/learnings/）
- CK3 河流实现可供参考（E:\SteamLibrary\steamapps\common\Crusader Kings III）

## 澄清问题与决策

### Q1: 输入文件 river.png
- river.png 在 Editor River Mode 中通过"导入"按钮选择
- 尺寸 = 高度图分辨率的一半
- 点击 Generate 按钮才构建 Mesh（非自动）

### Q2: River Mode 定位
- **决策**: 独立 EditorMode.River
- 在模式栏新增 "River" 按钮

### Q3: Mesh 生成策略
- **决策**: 方案 A — 像素追踪 + Catmull-Rom 样条插值 + 带状网格
- 不是旧的骨架提取方式（太复杂）
- 不是直接像素转顶点（太锯齿）

### Q4: 每段独立 Entity
- **决策**: 每段河流一个独立 Entity（参考 CK3 而非 Vic3）
- CK3 Draw 460 (580 indices) 所示：每段独立 draw

### Q5: 渲染参考
- **决策**: CK3 风格双 Pass（底部 + 水面）
- 简化的 steep parallax mapping
- 不追求完整的折射反射链路

## 宽度映射

13 种颜色，从浅蓝（最窄 1.25）到深绿（最宽 2.75）渐变：
最小半宽 0.625，最大半宽 1.375，步长 0.0625

## 数据模型

RiverCell[,] → 像素追踪 → RiverSegment[] → Catmull-Rom 样条 → Ribbon Mesh → Entity

## 交汇处理

- Confluence（红色）: 支流 taper，主河保持
- Bifurcation（黄色）: 分叉支流 taper start，入流保持
- Source（绿色）: 源头 taper start
- DistanceToMain 参数控制渐隐

## 验证规则

- 每系统 1 Source
- 红/黄正交相邻
- 每像素 ≤ 2 正交邻居
- 每段一端特殊颜色，另一端无
