# 地形裂缝修复执行计划 - 边缘顶点 Snap 到粗 LOD 网格

## 目标

- 消除不同 LOD 块相邻时的几何裂缝。
- 保持现有 patch 网格拓扑不变，不引入 skirts。
- 保持主视图、阴影视图使用同一套缝合规则。
- 改动范围尽量收敛在 `TerrainRenderFeature`、地形位移 shader 和法线初始化 shader。

## 当前实现事实

- 块选择发生在 `TerrainRenderFeature.SelectChunks` / `TraverseChunk` 中，当前实例数据写入的是 `Int4(chunkX, chunkY, lodLevel, 0)`。
- patch 网格顶点在 `TerrainRenderObject.CreatePatchGeometry` 中按整数格点生成，局部坐标范围是 `0..BaseChunkSize`。
- 位移 shader `MaterialTerrainDisplacement.sdsl` 直接按 `sampleCoord = worldOrigin + streams.Position.xz * lodScale` 采样高度图。
- 法线在 `TerrainMaterialStreamInitializer.sdsl` 中单独从高度图邻域采样，没有 seam 逻辑。
- 当前块选择只按 SSE 停止递归，没有显式的邻居平衡逻辑。

## 非目标

- 这次不改为 skirts 方案。
- 这次不重写 LOD 选择准则。
- 这次不引入新的 mesh 格式或额外顶点流。

## 关键设计决策

### 1. 只让“更细的块”去贴齐“更粗的块”

- 裂缝来自细块边缘多出来的中间顶点。
- 粗块不需要变形，细块边缘顶点直接 snap 到粗块共享采样点即可。
- 如果当前块旁边是同级或更细块，该侧 `neighborDelta = 0`。

### 2. 邻居关系不在 CPU 上逐块查，改为 GPU 后处理

- CPU 只负责按当前 `RenderView` 选出最终叶子块。
- GPU 基于这份叶子块列表生成一张低分辨率 `LODMap`。
- 再由 GPU 基于 `LODMap` 为每个块计算四边 `neighborDelta`。
- 这样邻居关系由“覆盖表”驱动，避免 CPU 侧逐块向上查祖先。

### 3. `Int4.w` 继续复用，但编码改为每边 8 bit

当前计划不再使用每边 2 bit，而改为：

```text
bits  0-7   : leftDelta
bits  8-15  : rightDelta
bits 16-23  : topDelta
bits 24-31  : bottomDelta
```

- `0` 表示该侧不需要缝合。
- `n > 0` 表示该侧相邻的已选块比当前块粗 `n` 个 LOD 层级。
- 8 bit 的好处是不用先假设相邻 LOD 差值一定小于等于 3。

### 4. `LODMap` 按 LOD0 patch 覆盖分辨率构建

- `LODMap` 不是按 heightmap sample 分辨率建。
- `LODMap` 的每个 texel 对应一个 LOD0 patch 单元。
- 这本质上是 GPU 版 `sectorToChunkMap`。
- 这样分辨率可控，compute 成本基本随 `MaxLeafChunkCount` 规模变化，而不是随高度图像素数暴涨。

### 5. 角点不做额外修正

- 角点本身已经落在共享采样点上。
- 真正需要修正的是边上的中间顶点。
- shader 中如果同时命中两条边，按角点处理，直接保留原始坐标即可，避免双向 snap。

### 6. 优先做“规则清晰、位移简单”，不追求无分支

- patch 边缘顶点占比很小。
- 与其做边缘高度插值，不如直接改写边缘顶点的 `sampleCoord`，让细块使用粗块会命中的采样点。
- 首版以正确性和可调试性为先，确认无缝后再考虑压缩分支。

## 交付标准

满足以下条件后再视为完成：

- 主视图中看不到 LOD 交界处的几何裂缝。
- 阴影视图下没有新增的明显裂缝或断裂阴影。
- 法线/高光在接缝处没有肉眼明显的亮暗断层。
- `MaxVisibleChunkInstances` 截断时，不会因为 neighbor mask 未写入而产生未初始化数据。
- 不需要手工维护 `.sdsl.cs`，由 Stride 正常自动生成。

## 实施步骤

### 阶段 1：CPU 只输出当前视图的最终叶子块列表

**文件**

- `Terrain/TerrainRenderFeature.cs`

**目标**

- `SelectChunks(...)` / `TraverseChunk(...)` 仍然负责 frustum + SSE 选块。
- 但 CPU 不再计算 `neighborMask`。
- CPU 只输出 `Int4(chunkX, chunkY, lodLevel, 0)` 形式的最终叶子块列表。
- 这份列表既用于绘制，也作为 GPU 后处理的输入。

**改造方式**

1. 保持 `TraverseChunk(...)` 的递归选块逻辑不变。
2. 叶子块命中停止条件后，继续写：
   - `Int4(chunkX, chunkY, lodLevel, 0)`
3. `PrepareTerrainDraw(...)` 在上传实例数据后，额外调度 GPU 计算：
   - 根据 `InstanceBuffer` 填充 `LODMap`
   - 根据 `LODMap` 回写 `InstanceBuffer.w`

**伪代码**

```csharp
int instanceCount = SelectChunks(..., instanceData);
renderObject.UpdateInstanceData(commandList, instanceData, instanceCount);

DispatchBuildLodMap(commandList, instanceCount);
DispatchBuildNeighborMask(commandList, instanceCount);
```

### 阶段 2：GPU 构建 `LODMap`

**文件**

- `Terrain/TerrainRenderFeature.cs`
- 新增 GPU compute shader 文件

**目标**

- 把当前视图选中的最终叶子块投影到一张 `LOD0 patch` 分辨率的 `LODMap` 上。
- `LODMap` 的每个 texel 存该 patch 当前由哪个 `lodLevel` 覆盖。

**核心规则**

- 对于一个已选块：
  - `lodScale = 1 << lodLevel`
  - 它会覆盖 `lodScale x lodScale` 个 LOD0 patch texel
- `LODMap` 的尺寸：
  - `lodMapWidth = ceil((heightmapWidth - 1) / BaseChunkSize)`
  - `lodMapHeight = ceil((heightmapHeight - 1) / BaseChunkSize)`
- 每个块把自己覆盖到这张图上。

**推荐资源**

- `R8_UInt` 或 `R16_UInt` 的 `LODMap` 纹理
- 输入：`StructuredBuffer<int4> InstanceBuffer`
- 输出：`RWTexture2D<uint> LODMap`

**推荐实现方式**

优先使用“每个实例一个 compute 线程组”的方式：

- 一个线程组处理一个已选块。
- 线程组内部遍历该块覆盖的 `lodScale x lodScale` patch 区域。
- 向 `LODMap` 写入当前块的 `lodLevel`。

这样可以避免“遍历整张图，再遍历所有块”的 O(图大小 × chunk 数) 结构。

**写入正确性**

- 最终叶子块集合理论上不会重叠。
- 当前视图的最终叶子块集合应完整覆盖 terrain 的 LOD0 patch 域。
- 因此正常情况下不会出现两个块写同一个 texel 的冲突，也不需要在每帧前单独清空 `LODMap`。
- 如果后续调试发现覆盖不完整，再考虑引入 debug-only 的清屏和无效值检测。

### 阶段 3：GPU 基于 `LODMap` 回写四边 `neighborDelta`

**文件**

- `Terrain/TerrainRenderFeature.cs`
- 新增 GPU compute shader 文件

**目标**

- 针对 `InstanceBuffer` 中的每个已选块，读取 `LODMap` 计算四边 `neighborDelta`。
- 把编码后的结果直接回写到 `InstanceBuffer.w`。

**核心规则**

- 当前块 `lodLevel = chunk.z`
- 当前块覆盖范围：
  - `lodScale = 1 << lodLevel`
  - `origin = (chunkX * lodScale, chunkY * lodScale)`
- 四边邻居查询点：
  - `Left`:   `(origin.x - 1, origin.y)`
  - `Right`:  `(origin.x + lodScale, origin.y)`
  - `Top`:    `(origin.x, origin.y - 1)`
  - `Bottom`: `(origin.x, origin.y + lodScale)`
- 查询结果是邻侧 patch texel 对应的 `neighborLod`
- `neighborDelta = max(0, neighborLod - currentLod)`

**为什么查一个 texel 就够**

- `LODMap` 按 LOD0 patch 覆盖构建后，同一条共享边外侧应由单一最终块覆盖。
- 因此在边起点读取一个代表 texel 即可判断该侧是否贴着更粗块。
- 如果后续验证发现不稳，再升级为“沿整条边扫描并取最大值”。

**编码**

```text
packedMask =
    (leftDelta) |
    (rightDelta << 8) |
    (topDelta << 16) |
    (bottomDelta << 24)
```

### 阶段 4：在位移 shader 中将边缘中点 snap 到粗 LOD 网格

**文件**

- `Terrain/Effects/MaterialTerrainDisplacement.sdsl`

**要点**

- 读取 `chunk.w`，按 8 bit 解码四边的 `neighborDelta`。
- 只在当前顶点位于边缘且不是角点时尝试 snap。
- 左右边只改 `sampleCoord.y`。
- 上下边只改 `sampleCoord.x`。
- 先改 `sampleCoord`，再用改后的坐标统一采样高度和输出 `TexCoord`。

**必须避免的错误**

- 左右边去改 `sampleCoord.x`。
- 上下边去改 `sampleCoord.y`。
- 角点同时命中两边时重复 snap。
- 用 `0-3` 的 2 bit 解码方式截断真实 LOD 差值。

**推荐辅助函数**

```hlsl
int DecodeNeighborDelta(int packedMask, int shift)
{
    return (packedMask >> shift) & 0xFF;
}
```

```hlsl
float SnapCoordToCoarserGrid(float coord, float coarseStrideInSamples)
{
    float phase = fmod(coord, coarseStrideInSamples);
    if (phase < 1e-4 || coarseStrideInSamples - phase < 1e-4)
    {
        return coord;
    }

    return coord + (coarseStrideInSamples - phase);
}
```

**边方向规则**

- `Left` / `Right`
  - 共享边是竖边。
  - 只允许 snap `sampleCoord.y`。
- `Top` / `Bottom`
  - 共享边是横边。
  - 只允许 snap `sampleCoord.x`。

**粗采样间隔**

```text
currentStrideInSamples = exp2(lodLevel)
coarseStrideInSamples = exp2(lodLevel + neighborDelta)
```

**核心思路**

- 细块边缘顶点原本可能命中 `2, 6, 10 ...` 这样的中间采样点。
- 若相邻块比它粗 1 级，则粗块边界只命中 `0, 4, 8 ...`。
- 此时直接把细块边缘顶点沿共享边方向 snap 到下一个粗网格点即可。
- snap 完成后，位置高度、`TexCoord`、后续法线中心点都以新坐标为准。

### 阶段 5：把法线计算同步到同一套 snap 规则

**文件**

- `Terrain/Effects/TerrainMaterialStreamInitializer.sdsl`
- `Terrain/TerrainProcessor.cs`

**原因**

- 如果位移 shader 已经把边缘顶点的中心采样点 snap 到粗网格，而法线初始化仍按未 snap 的逻辑推导，就可能留下接缝高光。

**修改方向**

1. 给 `TerrainMaterialStreamInitializer` 增加与位移 shader 同样需要的参数：
   - `StructuredBuffer<int4> InstanceBuffer`
   - `float2 HeightmapDimensionsInSamples`
   - `int BaseChunkSize`
2. 在 `TerrainProcessor.UpdateMaterialParameters(...)` 中同步为 stream initializer 绑定这些参数。
3. 在 stream initializer 中复用同样的 neighbor 解码和中心点 snap 规则。
4. 法线采样仍可基于高度图邻域差分，但中心点必须与位移后的 `sampleCoord` 一致。

**实现建议**

- 首版可以把 snap helper 在两个 shader 中各复制一份，先求可用。
- 如果验证后接缝高光已经足够自然，可以不额外对法线邻域采样点继续做 seam 处理。
- 等验证正确后，再考虑抽取公共 `.sdsl` 片段。

### 阶段 6：收尾与保护逻辑

**文件**

- `Terrain/TerrainRenderFeature.cs`

**需要补的保护**

- 如果 `selectedCount >= instanceCapacity`，要确保：
  - 被保留下来的块才会参与 GPU 后处理。
  - compute dispatch 只处理 `instanceCount` 范围。
- 如果 `instanceCount == 0`，直接跳过 `LODMap` / `neighborMask` compute。
- 日志仍然保留当前的截断 warning，但内容可以补充：
  - 当前 render view
  - 选择后的块数量
  - `LODMap` 尺寸

## 具体改动清单

### 必改文件

1. `Terrain/TerrainRenderFeature.cs`
   - 保持 CPU 选块。
   - 在上传 `InstanceBuffer` 后新增 compute 调度。
   - 管理 `LODMap` 和邻居回写所需 GPU 资源。

2. `Terrain/Effects/MaterialTerrainDisplacement.sdsl`
   - 解码 neighbor delta。
   - 只对边缘非角点执行坐标 snap。
   - 修正边方向与 snap 轴的对应关系。

3. `Terrain/Effects/TerrainMaterialStreamInitializer.sdsl`
   - 读取同样的实例信息。
   - 让法线中心点跟位移使用一致的 snap 后坐标。

4. `Terrain/TerrainProcessor.cs`
   - 为 stream initializer 额外绑定 `InstanceBuffer`、`HeightmapDimensionsInSamples`、`BaseChunkSize`。

5. 新增 compute shader
   - 由 `InstanceBuffer` 填充 `LODMap`
   - 由 `LODMap` 回写 `InstanceBuffer.w`

### 自动生成文件

- `Terrain/Effects/MaterialTerrainDisplacement.sdsl.cs`
- `Terrain/Effects/TerrainMaterialStreamInitializer.sdsl.cs`

说明：

- 这两个文件不手改。
- 改动 `.sdsl` 后由 Stride 生成新的 keys。

## 建议提交顺序

### 提交 1：GPU `LODMap` 与邻居表

- 改 `TerrainRenderFeature.cs`
- 新增 compute shader
- 验证 `InstanceBuffer.w` 是否被正确回写

### 提交 2：位移 snap

- 改 `MaterialTerrainDisplacement.sdsl`
- 验证几何裂缝是否消失

### 提交 3：法线一致性

- 改 `TerrainMaterialStreamInitializer.sdsl`
- 改 `TerrainProcessor.cs`
- 验证接缝高光和阴影一致性

## 验收清单

### 基础功能

- 相同 LOD 相邻时，地形外观与当前版本一致。
- LOD 差为 1 时，裂缝消失。
- LOD 差大于 1 时，仍然不会出现明显开口。

### 边界情况

- 地形外边缘不会错误触发缝合。
- 角点不会出现被双向 snap 导致的塌陷或尖峰。
- 非正方形 heightmap 边缘区域不会越界采样。
- `MaxVisibleChunkInstances` 被打满时，不会出现随机裂缝或闪烁。
- 连续绘制多个 `RenderView` 时，不会读到上一个视图遗留的 `LODMap` 数据。

### 渲染一致性

- 主摄像机视图下无缝。
- 阴影视图下无新增裂缝。
- 高光/法线在接缝处无明显跳变。

### 性能

- 记录修复前后的帧率。
- 记录相机贴地飞行时的帧时间波动。
- 确认 shader 额外采样只发生在边缘顶点，不会对整块顶点都触发。
- 记录三段 compute pass 的 GPU 时间：
  - 填充 `LODMap`
  - 回写邻居表

## 风险与应对

### 风险 1：`LODMap` 坐标映射算错

- 现象：某一侧 seam 有效，另一侧仍开裂，或块整体被错误拉扯。
- 应对：先把 `LODMap` 可视化，确认每个 texel 真正对应 LOD0 patch 覆盖，而不是 sample 覆盖。

### 风险 2：位移无缝但法线有缝

- 现象：几何接上了，但高光/阴影有一条线。
- 应对：让法线初始化至少复用同一套中心点 snap 规则，再决定是否继续细化法线邻域采样。

### 风险 3：角点处理不稳定

- 现象：四块交汇处出现尖刺或折角。
- 应对：角点直接跳过额外 snap，只保留原始共享采样点。

### 风险 4：性能回退

- 现象：新增 compute pass 后 GPU 时间上升明显。
- 应对：确保 `LODMap` 分辨率按 LOD0 patch，而不是按 height sample；优先优化填图和邻居回写的线程布局。

### 风险 5：跨视图状态串扰

- 现象：主视图正常，但阴影视图或其他 RenderView 出现错误 neighbor mask。
- 应对：确保每次 `PrepareTerrainDraw(...)` 都用当前视图的完整叶子块集合重新写满 `LODMap`，不要依赖上一视图残留内容。

## 结论

这次修复的关键不是“在 shader 里插值一下”本身，而是把当前视图的最终叶子块稳定转换成一张 GPU 可查询的覆盖表。只要保证：

- `LODMap` 正确表达当前视图下每个 LOD0 patch 被哪个 LOD 覆盖。
- `Int4.w` 准确表达“当前块哪一侧贴着更粗的块”。
- 位移和法线都使用同一套 snap 规则。
- 角点和边界情况有明确保护。

这套方案就可以在不改现有 patch 拓扑的前提下，用更简单直接的边缘顶点对齐方式稳定消掉 LOD 裂缝。
