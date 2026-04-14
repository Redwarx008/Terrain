# Runtime IndexMap 流式渲染设计

**日期**: 2026-04-10
**状态**: Draft
**目标**: 将 Editor 的 IndexMap 材质系统完整移植到 Runtime，通过流式系统加载

---

## Context

编辑器已完成 IndexMap RGBA 材质系统（材质索引、权重、3D投影、旋转），但运行时仅有简单的单一纹理平铺着色器。用户编辑的地形材质在构建后完全丢失。需要将 IndexMap 数据和材质混合着色器完整移植到运行时，通过 `.terrain` 文件的 SplatMap VT 层流式加载。

---

## 决策记录

| 决策 | 选择 | 理由 |
|------|------|------|
| 数据来源 | 流式加载 | 适配超大地形 |
| 材质纹理加载 | 从编辑器项目路径 | 开发阶段最简单 |
| 着色器范围 | 完整移植 | 与编辑器 WYSIWYG 一致 |
| 文件集成 | 复用 SplatMap VT 层 | 零文件格式变更 |
| GpuHeightArray | 抽象为 GpuVirtualTextureArray | Heightmap/IndexMap 共用 LRU 管理结构 |
| 页面加载策略 | 同步加载 | Heightmap/IndexMap 页面同时驻留 |
| 运行时 fallback | 不需要 | 直接替换现有着色器 |
| 数组大小参数 | 不需要 | 用 `GetDimensions()` 获取 |

---

## 1. GpuVirtualTextureArray 抽象

### 现状

`GpuHeightArray`（`Terrain/Streaming/TerrainStreaming.cs:352-536`）管理 Heightmap 的 GPU Texture2DArray 分页：
- `Dictionary<TerrainPageKey, int>` 页面→切片映射
- `Queue<int>` 空闲切片
- `LinkedList<int>` LRU 驱逐链表
- `SlotState[]` 每槽状态（占用/固定/键）
- `UploadPage()` 硬编码 `MemoryMarshal.Cast<byte, ushort>`

### 改造

```
GpuHeightArray → GpuVirtualTextureArray
```

**变更点**：
- `Texture HeightmapArray` → `Texture TextureArray`
- `UploadPage()` 移除 `MemoryMarshal.Cast<byte, ushort>`，直接传 `Span<byte>` 给 `Texture.SetData(commandList, data, sliceIndex, 0, null)`
- 构造函数接收外部创建的 `Texture` 实例，不绑定格式
- `TileSize`/`Padding` 保留为信息属性（供外部查询）

**GpuHeightArray** 变为 `GpuVirtualTextureArray` 的类型别名，或直接重命名。两者共享完全相同的 LRU 逻辑。

### 涉及文件

| 文件 | 变更 |
|------|------|
| `Terrain/Streaming/TerrainStreaming.cs` | 重构 GpuHeightArray → GpuVirtualTextureArray |
| `Terrain/Streaming/TerrainStreaming.cs` | TerrainStreamingManager 使用两个 GpuVirtualTextureArray 实例 |
| `Terrain/Rendering/TerrainRenderObject.cs` | 新增 IndexMapArray Texture 属性 |

---

## 2. 流式集成 — IndexMap VT 层

### 文件格式

复用现有 Header 字段：
- `HasSplatMap = 1`
- `SplatMapFormat = R8G8B8A8_UNorm`（4 字节/像素）
- `SplatMapMipLevels` = IndexMap 的 mip 级数

IndexMap 使用与 Heightmap 相同的 TileSize 和 Padding。文件中 Heightmap VT 数据之后写入 SplatMap VT 数据。

### TerrainFileReader 扩展

```
新增属性:
  VTHeader SplatMapHeader    // IndexMap 的 VT 元数据
  long SplatMapDataOffset    // IndexMap 数据起始偏移

新增方法:
  ReadSplatMapPage(TerrainPageKey key, Span<byte> destination)
    → 计算 mip layout 偏移
    → 读取 RGBA 页面数据
```

### TerrainStreamingManager 扩展

```
新增字段:
  GpuVirtualTextureArray gpuSplatMapArray
  PageBufferAllocator splatMapBufferPool

修改 RequestChunk():
  → 请求 Heightmap 页面时，同步请求 IndexMap 页面
  → 两个 I/O 请求合并或串行执行
  → 两者的 pageData 放入同一个 completed queue

修改 ProcessPendingUploads():
  → 上传 Heightmap 页面后，上传对应 IndexMap 页面
  → 同步驻留：两者都驻留后才标记 chunk 可渲染

修改 TryGetResidentPageForChunk():
  → 检查 Heightmap 和 IndexMap 都驻留
  → 返回两个 sliceIndex（height + splatmap）

修改 TerrainChunkNode.StreamInfo:
  → 新增字段或扩展结构，包含 IndexMap sliceIndex
```

### TerrainChunkNode 结构 — 无需变更

Heightmap 和 IndexMap 共享相同的 TerrainPageKey，同步加载到相同的 slice 索引。着色器中用同一个 `sliceIndex` 采样 `HeightmapArray` 和 `IndexMapArray`。

`TerrainChunkNode` 结构体保持不变。

### 同步加载策略

1. I/O 线程：读取 Heightmap 页面 → 立即读取对应 IndexMap 页面
2. 主线程：两个页面都上传后，chunk 标记为可渲染
3. 如果文件不包含 IndexMap VT 数据（HasSplatMap=0），TerrainStreamingManager 跳过 IndexMap 加载，着色器使用默认材质采样

### 涉及文件

| 文件 | 变更 |
|------|------|
| `Terrain/Streaming/TerrainStreaming.cs` | 新增 IndexMap 加载逻辑 |
| `Terrain/Streaming/PageBufferAllocator.cs` | 新增 RGBA 缓冲池 |
| `Terrain/Core/TerrainComponent.cs` | 新增 IndexMap 相关属性 |
| `Terrain/Rendering/TerrainQuadTree.cs` | 驻留判断包含 IndexMap |
| `Terrain/Streaming/TerrainStreaming.cs` | TerrainFileReader 读取 SplatMap 页面 |

---

## 3. 运行时着色器 — 完整移植

### 替换 MaterialTerrainDiffuse.sdsl

将 `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` 替换为编辑器着色器的完整移植版。

**新着色器参数**：

```hlsl
rgroup PerMaterial
{
    stage Texture2DArray<float> HeightmapArray;      // 已有
    stage Texture2DArray IndexMapArray;              // 新增：IndexMap VT 数组
    stage Texture2DArray MaterialAlbedoArray;        // 新增：材质 Albedo 数组
    stage Texture2DArray MaterialNormalArray;        // 新增：材质法线数组
    stage SamplerState MaterialIndexSampler;         // 新增：PointClamp
    stage SamplerState MaterialAlbedoSampler;        // 新增：LinearWrap
}

cbuffer PerMaterial
{
    stage float HeightScale;                         // 已有
    stage int BaseChunkSize;                         // 已有
    stage int HeightmapTileSize;                     // 已有
    stage int HeightmapTilePadding;                  // 已有
    stage float MaterialTilingScale;                 // 新增
    stage float DetailContrast;                      // 新增
    stage float DiffuseWorldRepeatSize;              // 已有
}
```

**从 EditorTerrainDiffuse.sdsl 移植的函数**：
1. `MaterialPixel` 结构体（index, weight, projDir, rotation）
2. `DecodeMaterialPixel(float4 raw)` — RGBA 解码
3. `DecodeProjectionDirection(float encoded)` — 4:4 方向解码
4. `BuildProjectionBasis(float3 dir, float rot, out float3 u, out float3 v)` — 投影基
5. `GetProjectedUV(float3 worldPos, float3 projDir, float rotation)` — 投影 UV
6. `BuildProjectedNormalWS(float3 normalTS, float3 projDir, float rotation)` — 投影法线
7. `ApplyDetailContrast(float weight, float detail)` — 细节对比度
8. `AccumulateMaterialSample(...)` — 材质采样累积
9. 4 邻居镜像采样主循环

**关键差异**：编辑器用 `Texture2D MaterialIndexMap`，运行时用 `Texture2DArray IndexMapArray`。采样时：
```hlsl
// 编辑器:
float4 raw = MaterialIndexMap.Load(int3(texelCoord, 0));

// 运行时: 复用同一个 sliceIndex（Heightmap 和 IndexMap 共享分页）
float4 raw = IndexMapArray.Load(int4(texelCoord, sliceIndex, 0));
```

`sliceIndex` 从现有 `TerrainChunkNode.StreamInfo.x` 获取，与 Heightmap 采样完全一致，无需新增 stream。

### Stream 数据 — 无需变更

`TerrainHeightStream.sdsl` 保持不变，`sliceIndex` 已通过现有 stream 传递。

### Shader Keys

需要新增的 Stride ShaderKey 参数类：
- `IndexMapArray` (Texture2DArray)
- `MaterialAlbedoArray` (Texture2DArray)
- `MaterialNormalArray` (Texture2DArray)
- `MaterialTilingScale` (float)
- `DetailContrast` (float)
- `MaterialIndexSampler` (SamplerState)
- `MaterialAlbedoSampler` (SamplerState)

### 涉及文件

| 文件 | 变更 |
|------|------|
| `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | 完全重写 |
| `Terrain/Effects/Stream/TerrainHeightStream.sdsl` | 新增 SplatMapSliceIndex stream |
| `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` | 传递 SplatMapSliceIndex |
| `Terrain/Effects/Material/TerrainHeightParameters.sdsl` | 新增 IndexMap 参数 |
| `Terrain/Rendering/TerrainRenderFeature.cs` | 绑定新 shader 参数 |
| `Terrain/Rendering/TerrainRenderObject.cs` | 新增 IndexMapArray 资源 |

---

## 4. 材质纹理数组管理

### RuntimeMaterialManager

新建 `RuntimeMaterialManager`（在 `Terrain/` 项目中）：

**职责**：
- 从编辑器项目配置读取材质纹理路径列表
- 启动时加载所有材质纹理（Albedo + Normal）
- 构建 `Texture2DArray`（Albedo 数组 + Normal 数组）
- 提供给 TerrainRenderFeature 绑定到着色器

**接口**：
```csharp
class RuntimeMaterialManager : IDisposable
{
    Texture? AlbedoArray { get; }
    Texture? NormalArray { get; }

    void Initialize(GraphicsDevice device, string projectPath);
    void Dispose();
}
```

**加载流程**：
1. 读取项目目录下的材质配置（从 TOML 或导出文件）
2. 按索引顺序加载每张纹理
3. 确保所有纹理尺寸和格式一致（不匹配则缩放/转换）
4. 构建 Texture2DArray

### 涉及文件

| 文件 | 变更 |
|------|------|
| 新建 `Terrain/Materials/RuntimeMaterialManager.cs` | 材质纹理加载和管理 |

---

## 5. TerrainPreProcessor 导出

### 导出 IndexMap 为 SplatMap VT

修改 `TerrainPreProcessor/Services/TerrainProcessor.cs` 的写入流程：

```
现有: WriteTerrainFile(heightmap, minMaxErrors, config)
新增: WriteTerrainFile(heightmap, minMaxErrors, indexMap, config)
```

**写入步骤**（在 Heightmap VT 之后）：
1. 写 `VTHeader`（IndexMap 的 Width/Height/TileSize/Padding/4bpp/mipLevels）
2. 逐 mip 逐 tile 写入 RGBA 数据
3. Tile 读取逻辑复用 Heightmap 的分页函数（通用化）
4. Header 设置 `HasSplatMap=1`, `SplatMapFormat=R8G8B8A8_UNorm`

### Mip 策略

IndexMap 的 mip 需要特殊处理 — 不能简单平均 RGBA 值（会混合材质索引）。策略：
- **主导材质**：取 4 像素中出现最多的材质索引
- **权重/投影/旋转**：取主导材质像素对应的值
- 或者简化：直接取左上角像素的值（近似，远 LOD 不影响视觉）— **推荐此方案**，实现简单，远 LOD 视觉差异极小

### 涉及文件

| 文件 | 变更 |
|------|------|
| `TerrainPreProcessor/Services/TerrainProcessor.cs` | 新增 IndexMap VT 写入 |
| `TerrainPreProcessor/Models/TerrainFileHeader.cs` | 无变更（字段已存在） |
| `Terrain.Editor/Services/TerrainManager.cs` | 导出时传递 IndexMap 数据 |

---

## 6. TerrainChunkNode — 无需变更

Heightmap 和 IndexMap 共享相同的 sliceIndex（同步加载、相同 TerrainPageKey）。`TerrainChunkNode` 结构体保持原样，着色器中直接用 `StreamInfo.x`（sliceIndex）同时采样两个纹理数组。

---

## 验证计划

1. **单元验证**：
   - `GpuVirtualTextureArray` 的 LRU 驱逐行为与原 `GpuHeightArray` 一致
   - IndexMap 页面读写正确（Round-trip 测试）

2. **集成验证**：
   - 编辑器导出 `.terrain` 文件包含 SplatMap VT 数据
   - 运行时加载 `.terrain` 文件，正确读取 IndexMap 页面
   - 运行时渲染与编辑器视觉效果一致

3. **构建验证**：
   - `dotnet build` 零错误
   - 着色器编译通过（Stride SDSL → HLSL）

4. **性能验证**：
   - 流式加载不引入明显卡顿
   - GPU 内存增量合理（IndexMap 页面约 Heightmap 的 2 倍）
