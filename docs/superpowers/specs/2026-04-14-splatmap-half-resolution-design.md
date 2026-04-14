# Splatmap 半分辨率解耦设计

**日期**: 2026-04-14
**状态**: 待实施

## Context

当前 splatmap（MaterialIndexMap）与 heightmap 强制 1:1 分辨率。材质混合的精度需求低于高度细节，2048×2048 地形的 splatmap 占用 16MB（RGBA8）。将其降为 1/2 分辨率可降至 4MB，且视觉差异极小。

## 决策

- **固定 1/2 比例**: splatmap 始终为 heightmap 的一半尺寸（如 2048→1024）
- **全链路改造**: 编辑器、预处理、文件格式、运行时流式加载、Shader
- **Shader 内缩放坐标**: displacement shader 计算 `splatPageLocalPos`，diffuse shader 直接使用

## 核心设计：独立 Page 网格 + Key 转换

Splatmap 半尺寸导致 VT page 数量减半，需要独立的 page key 映射：

```
Heightmap page (mip=0, px=4, py=6) → Splatmap page (mip=0, px=2, py=3)
```

每个 splatmap page 覆盖 2 倍的世界范围（以 heightmap texel 为单位）。

## 变更层次

### 1. 文件格式（低复杂度）

**文件**: `TerrainPreProcessor/Models/TerrainFileHeader.cs`

- `CURRENT_VERSION` 2 → 3
- `Reserved1` → `SplatMapResolutionRatio`（值 2 = 半分辨率，1 = 原始）
- VTHeader 已独立存储 Width/Height，无需改动

**向后兼容**: v2 文件默认 ratio=1，行为不变。

### 2. 预处理（低复杂度）

**文件**: `TerrainPreProcessor/Services/TerrainProcessor.cs`

流程变更：
1. 加载 splatmap 图片后，如果尺寸等于 heightmap，用 `CoordinateConsistentMipmap.GenerateNextMip()` 降采样到 1/2
2. 如果已经是半尺寸，跳过降采样
3. 从降采样后的尺寸计算 `splatMapMipLevels`
4. 写入 header 时设置 `SplatMapResolutionRatio = 2`

复用现有的 `CoordinateConsistentMipmap` 保证坐标一致性：splatmap texel (x,y) 精确对应 heightmap texel (2x, 2y)。

### 3. 运行时流式加载（高复杂度）

**文件**: `Terrain/Streaming/TerrainStreaming.cs`

#### 3a. 文件读取

`TerrainFileReader` 已独立读取两个 VT header，无需结构性改动。新增：

```csharp
public int SplatMapResolutionRatio =>
    Header.Version >= 3 ? Header.SplatMapResolutionRatio : 1;
```

#### 3b. TerrainChunkNode 扩展

从 2 个 Int4 扩展到 3 个：

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainChunkNode
{
    public Int4 NodeInfo;    // chunkX, chunkY, lodLevel, state
    public Int4 StreamInfo;  // heightSliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
    public Int4 SplatInfo;   // splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride
}
```

`splatPageOffsetX/Y = heightmapPageOffsetX/Y / ratio`
`splatPageTexelStride = heightmapPageTexelStride / ratio`

#### 3c. Page Key 转换

```csharp
private TerrainPageKey TranslateToSplatMapPageKey(TerrainPageKey heightmapKey)
{
    int ratio = fileReader.SplatMapResolutionRatio;
    if (ratio <= 1) return heightmapKey;

    int splatMip = Math.Min(heightmapKey.MipLevel, splatMapMipLayouts.Length - 1);
    int splatPageX = heightmapKey.PageX / ratio;
    int splatPageY = heightmapKey.PageY / ratio;
    return new TerrainPageKey(splatMip, splatPageX, splatPageY);
}
```

#### 3d. TerrainStreamingManager 变更

- `TryGetResidentPageForChunk`: 返回两个 slice index（height + splat）
- `RequestPage`: splatmap 请求使用转换后的 key
- `PreloadTopLevelChunks`: splatmap preloading 使用转换后的 key
- `IsChunkResident`: splatmap 驻留检查使用转换后的 key
- `queuedKeys`: 需要分别跟踪 heightmap 和 splatmap 的请求，避免重复

### 4. 运行时 Shader（中复杂度）

#### 4a. TerrainHeightStream.sdsl

新增 splatmap streams：

```hlsl
stage stream float TerrainSplatSliceIndex;
stage stream float2 TerrainSplatPageLocalPos;
```

#### 4b. MaterialTerrainDisplacement.sdsl

从 `SplatInfo` 读取 splatmap 参数，计算 `splatPageLocalPos`：

```hlsl
int4 splatInfo = instance.SplatInfo;
float splatSliceIndex = float(splatInfo.x);
float splatPageTexelStride = max(1.0f, float(splatInfo.w));
float2 splatPageLocalPos = float2(splatInfo.y, splatInfo.z) + localPos * splatPageTexelStride;
streams.TerrainSplatSliceIndex = splatSliceIndex;
streams.TerrainSplatPageLocalPos = splatPageLocalPos;
```

Chunk node struct 定义更新为 3 个 int4。

#### 4c. MaterialTerrainDiffuse.sdsl

`Compute()` 使用新的 splatmap streams：

```hlsl
int splatSliceIndex = (int)(streams.TerrainSplatSliceIndex + 0.5f);
float2 splatPageLocalPos = streams.TerrainSplatPageLocalPos;
```

`LoadMaterialPixel` 参数改为接收 `splatPageLocalPos` 和 `splatSliceIndex`。

### 5. 编辑器数据层（中复杂度）

**文件**: `Terrain.Editor/Services/TerrainManager.cs`

`MaterialIndexMap` 创建为半尺寸：

```csharp
MaterialIndices = new MaterialIndexMap(
    (heightDataWidth + 1) / 2, (heightDataHeight + 1) / 2);
```

### 6. 编辑器画笔（低复杂度）

**文件**: `Terrain.Editor/Services/PaintBrushCore.cs`, `PaintTool.cs`, `EraseTool.cs`

画笔坐标从 heightmap 空间转换到 splatmap 空间：

```csharp
int splatCenterX = context.CenterX / 2;
int splatCenterZ = context.CenterZ / 2;
int splatRadius = (int)MathF.Ceiling(context.BrushRadius / 2.0f);
```

### 7. 编辑器 Shader（低复杂度）

**文件**: `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl`

`LoadIndexMapAtGlobal` 中坐标 ÷2：

```hlsl
int2 splatPixel = globalPixel / 2;
```

## 关键文件清单

| 文件 | 变更类型 |
|------|---------|
| `TerrainPreProcessor/Models/TerrainFileHeader.cs` | 字段重命名 + 版本号 |
| `TerrainPreProcessor/Services/TerrainProcessor.cs` | 降采样逻辑 |
| `Terrain/Streaming/TerrainStreaming.cs` | Page key 转换、ChunkNode 扩展、流式加载分离 |
| `Terrain/Rendering/TerrainQuadTree.cs` | 填充 SplatInfo 字段 |
| `Terrain/Effects/Stream/TerrainHeightStream.sdsl` | 新增 splat streams |
| `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` | 计算 splatPageLocalPos |
| `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | 使用 splat streams |
| `Terrain.Editor/Services/TerrainManager.cs` | 半尺寸 MaterialIndexMap |
| `Terrain.Editor/Services/PaintBrushCore.cs` | 坐标缩放 |
| `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl` | 坐标 ÷2 |

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Page 边界对齐：heightmap 尺寸不被 `2×(tileSize-1)` 整除 | `ReadTileToPixels` 已有 clamp-to-edge 处理 |
| Mip 层级不匹配：splatmap mip 链更短 | `TranslateToSplatMapPageKey` 中 clamp 到最粗 mip |
| Editor undo/redo 区域捕获 | `MaterialIndexMap` 的 `CopyRegionToBytes` 已在 splatmap 空间操作 |
| TerrainChunkNode 增大（+16 bytes） | 增幅 ~50%（32→48 bytes），对 GPU buffer 影响有限 |

## 验证方案

1. 用现有 heightmap + splatmap 运行 PreProcessor，生成 v3 .terrain 文件
2. 运行时加载 v3 文件，确认渲染结果与 v2 视觉一致
3. 确认 splatmap GPU 内存占用减半
4. Editor 画笔在半分辨率 splatmap 上操作正常
5. 加载 v2 文件确认向后兼容
6. `dotnet build` 零错误
