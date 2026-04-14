# Splatmap 半分辨率解耦 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 splatmap（MaterialIndexMap）从 heightmap 的 1:1 分辨率解耦为固定 1/2 分辨率，全链路改造：文件格式、预处理、运行时流式加载、Shader、编辑器。

**Architecture:** Splatmap VT 拥有独立的 page 网格（page 数量是 heightmap 的一半）。运行时通过 `TranslateToSplatMapPageKey()` 将 heightmap page key 转换为 splatmap page key。`TerrainChunkNode` 扩展为 3 个 Int4，新增 `SplatInfo` 字段。Displacement shader 计算独立的 `splatPageLocalPos`，diffuse shader 使用它采样 splatmap。

**Tech Stack:** C# (.NET), Stride Engine, SDSL (Stride Shader Language), ImageSharp

**Design Spec:** `docs/superpowers/specs/2026-04-14-splatmap-half-resolution-design.md`

---

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `TerrainPreProcessor/Models/TerrainFileHeader.cs` | Modify | 文件格式 v3，`Reserved1` → `SplatMapResolutionRatio` |
| `TerrainPreProcessor/Services/TerrainProcessor.cs` | Modify | 加载 splatmap 后降采样到 1/2 |
| `Terrain/Streaming/TerrainStreaming.cs` | Modify | `TerrainChunkNode` 扩展 + page key 转换 + 流式加载分离 |
| `Terrain/Rendering/TerrainRenderObject.cs` | No change | GPU 数组格式不变 |
| `Terrain/Rendering/TerrainQuadTree.cs` | Modify | 填充 `SplatInfo` 字段 |
| `Terrain/Core/TerrainProcessor.cs` | Modify | 传递 splatmap resolution ratio |
| `Terrain/Effects/Stream/TerrainHeightStream.sdsl` | Modify | 新增 splatmap streams |
| `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` | Modify | 读取 `SplatInfo`，计算 `splatPageLocalPos` |
| `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | Modify | 使用 splatmap streams |
| `Terrain.Editor/Services/TerrainManager.cs` | Modify | `MaterialIndexMap` 半尺寸创建 |
| `Terrain.Editor/Services/PaintEditor.cs` | Modify | 画笔坐标缩放 |
| `Terrain.Editor/Services/PaintBrushCore.cs` | No change | 已在 splatmap 空间操作 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | Modify | 纹理半尺寸 + 脏区域坐标缩放 |
| `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl` | Modify | `LoadIndexMapAtGlobal` 坐标 ÷2 |
| `Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl` | Modify | `ClampIndexPixel` 使用半尺寸边界 |

---

## Task 1: 文件格式升级 (TerrainFileHeader v3)

**Files:**
- Modify: `TerrainPreProcessor/Models/TerrainFileHeader.cs`

- [ ] **Step 1: 修改 TerrainFileHeader — 重命名 Reserved1 并升级版本号**

```csharp
// Line 33: Reserved1 → SplatMapResolutionRatio
public int SplatMapResolutionRatio;  // 1 = same resolution (legacy), 2 = half resolution

// Line 39: Bump version
public const int CURRENT_VERSION = 3;

// Line 55: Update default
SplatMapResolutionRatio = 1,  // 默认与 heightmap 同分辨率（向后兼容）
```

完整变更（`TerrainPreProcessor/Models/TerrainFileHeader.cs`）：

将第 33 行 `public int Reserved1;` 改为 `public int SplatMapResolutionRatio;`

将第 39 行 `public const int CURRENT_VERSION = 2;` 改为 `public const int CURRENT_VERSION = 3;`

将 `CreateDefault()` 中 `Reserved1 = 0` 改为 `SplatMapResolutionRatio = 1`

- [ ] **Step 2: 验证构建**

Run: `dotnet build TerrainPreProcessor/TerrainPreProcessor.csproj`
Expected: 成功

- [ ] **Step 3: Commit**

```bash
git add TerrainPreProcessor/Models/TerrainFileHeader.cs
git commit -m "feat: bump terrain file format to v3 with SplatMapResolutionRatio field"
```

---

## Task 2: 预处理器降采样

**Files:**
- Modify: `TerrainPreProcessor/Services/TerrainProcessor.cs`

- [ ] **Step 1: 在 `ProcessInternal` 中添加 splatmap 降采样逻辑**

在 `TerrainProcessor.cs` 的 `ProcessInternal` 方法中，第 111 行 `var splatMapInfo = LoadSplatMap(...)` 之后、第 114 行 `int splatMapMipLevels = ...` 之前，插入降采样逻辑：

```csharp
// 加载 SplatMap（必需）
if (string.IsNullOrWhiteSpace(config.SplatMapPath))
{
    return Result.Failure("SplatMap (IndexMap) path is required for terrain processing.");
}

progress?.Report((50, 100, "加载 SplatMap..."));
var splatMapInfo = LoadSplatMap(config.SplatMapPath!);
Image splatMap = splatMapInfo.image;
VTFormat splatMapFormat = splatMapInfo.format;

// 降采样 SplatMap 到 heightmap 的 1/2 分辨率
if (splatMap.Width == heightMap.Width && splatMap.Height == heightMap.Height)
{
    progress?.Report((52, 100, "降采样 SplatMap 到 1/2 分辨率..."));
    var downsampled = DownsampleSplatMap(splatMap, splatMapFormat);
    splatMap.Dispose();
    splatMap = downsampled;
}

int splatMapMipLevels = CoordinateConsistentMipmap.CalculateMipLevels(
    splatMap.Width, splatMap.Height, config.TileSize);
```

同时将 `splatMapFormat` 从 `LoadSplatMap` 调用中保留到后面（当前代码已在前面声明了 `splatMapFormat`，不需要额外改动）。

- [ ] **Step 2: 在 `WriteTerrainFile` 调用中传入降采样标志**

将 `WriteTerrainFile` 调用前，设置 header 的 `SplatMapResolutionRatio`。找到 `WriteTerrainFile` 内部的 header 构建代码（约第 196-208 行），修改：

```csharp
var header = new TerrainFileHeader
{
    Magic = TerrainFileHeader.MAGIC_VALUE,
    Version = TerrainFileHeader.CURRENT_VERSION,
    Width = heightMap.Width,
    Height = heightMap.Height,
    LeafNodeSize = config.LeafNodeSize,
    TileSize = config.TileSize,
    Padding = HeightMapPadding,
    HeightMapMipLevels = heightMapMipLevels,
    SplatMapFormat = (int)splatMapFormat,
    SplatMapMipLevels = splatMapMipLevels,
    SplatMapResolutionRatio = splatMap.Width == heightMap.Width ? 1 : 2
};
```

- [ ] **Step 3: 添加 `DownsampleSplatMap` 方法**

在 `TerrainProcessor` 类中添加新方法：

```csharp
/// <summary>
/// 将 SplatMap 降采样到 1/2 分辨率，使用坐标一致性算法。
/// </summary>
private static Image DownsampleSplatMap(Image splatMap, VTFormat format)
{
    return format switch
    {
        VTFormat.R8 => CoordinateConsistentMipmap.GenerateNextMip((Image<L8>)splatMap),
        VTFormat.L16 => CoordinateConsistentMipmap.GenerateNextMip((Image<L16>)splatMap),
        VTFormat.Rg32 => CoordinateConsistentMipmap.GenerateNextMip((Image<Rg32>)splatMap),
        _ => CoordinateConsistentMipmap.GenerateNextMip((Image<Rgba32>)splatMap),
    };
}
```

- [ ] **Step 4: 验证构建**

Run: `dotnet build TerrainPreProcessor/TerrainPreProcessor.csproj`
Expected: 成功

- [ ] **Step 5: Commit**

```bash
git add TerrainPreProcessor/Services/TerrainProcessor.cs
git commit -m "feat: downsample splatmap to 1/2 resolution in terrain preprocessor"
```

---

## Task 3: 运行时 TerrainChunkNode 扩展 + 文件读取兼容

**Files:**
- Modify: `Terrain/Streaming/TerrainStreaming.cs`

- [ ] **Step 1: 扩展 `TerrainChunkNode` 结构体**

在 `TerrainStreaming.cs` 中找到 `TerrainChunkNode` 定义（约第 26-30 行），添加 `SplatInfo` 字段：

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainChunkNode
{
    public Int4 NodeInfo;    // chunkX, chunkY, lodLevel, state (Stop=0, Subdivided=1)
    public Int4 StreamInfo;  // heightSliceIndex, pageOffsetX, pageOffsetY, pageTexelStride
    public Int4 SplatInfo;   // splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride
}
```

- [ ] **Step 2: 扩展运行时 `TerrainFileHeader` 结构体**

在同一文件中找到运行时的 `TerrainFileHeader`（约第 102-122 行），将 `Reserved1` 重命名为 `SplatMapResolutionRatio`：

```csharp
public int SplatMapResolutionRatio;  // 替换 public int Reserved1;
```

- [ ] **Step 3: 在 `TerrainFileReader` 中添加 `SplatMapResolutionRatio` 属性**

在 `TerrainFileReader` 类中添加（约第 202 行 `public TerrainFileHeader Header { get; }` 之后）：

```csharp
/// <summary>
/// Splatmap 与 heightmap 的分辨率比。1 = 同分辨率（legacy v2），2 = 半分辨率（v3）。
/// </summary>
public int SplatMapResolutionRatio =>
    Header.Version >= 3 ? Header.SplatMapResolutionRatio : 1;
```

- [ ] **Step 4: 添加 page key 转换方法**

在 `TerrainStreamingManager` 类中添加（约第 597 行字段声明区域之后）：

```csharp
/// <summary>
/// 将 heightmap page key 转换为对应的 splatmap page key。
/// </summary>
private TerrainPageKey TranslateToSplatMapPageKey(TerrainPageKey heightmapKey)
{
    int ratio = fileReader.SplatMapResolutionRatio;
    if (ratio <= 1)
        return heightmapKey;

    int splatMip = Math.Min(heightmapKey.MipLevel, splatMapMipLayouts.Length - 1);
    int splatPageX = heightmapKey.PageX / ratio;
    int splatPageY = heightmapKey.PageY / ratio;
    return new TerrainPageKey(splatMip, splatPageX, splatPageY);
}
```

注意：`splatMapMipLayouts` 是 `TerrainFileReader` 的私有字段。需要通过 `fileReader` 暴露。最简单的方式是在 `TerrainFileReader` 中添加一个属性：

```csharp
public int SplatMapMipCount => splatMapMipLayouts.Length;
```

然后将 `TranslateToSplatMapPageKey` 中的 `splatMapMipLayouts.Length` 改为 `fileReader.SplatMapMipCount`。

- [ ] **Step 5: 添加 `splatPageOffset` 计算字段**

`TerrainStreamingManager` 需要存储 splatmap 的 `effectivePageSpanInSamples` 用于 offset 计算。添加字段：

```csharp
private readonly int splatMapEffectivePageSpanInSamples;
```

在构造函数中初始化（在 `effectivePageSpanInSamples` 赋值之后）：

```csharp
splatMapEffectivePageSpanInSamples = Math.Max(1, (fileReader.SplatMapHeader.TileSize - 1));
```

- [ ] **Step 6: 修改 `TryGetResidentPageForChunk` — 返回两个 slice index**

将方法签名（约第 641 行）改为：

```csharp
public bool TryGetResidentPageForChunk(TerrainChunkKey chunkKey,
    out int heightSliceIndex, out int splatSliceIndex,
    out int pageOffsetX, out int pageOffsetY, out int pageTexelStride)
{
    TerrainPageKey heightPageKey = GetPageKey(chunkKey, out pageOffsetX, out pageOffsetY, out pageTexelStride);
    if (!gpuHeightArray.TryGetResidentSlice(heightPageKey, out heightSliceIndex))
    {
        splatSliceIndex = -1;
        return false;
    }

    if (gpuSplatMapArray != null)
    {
        TerrainPageKey splatPageKey = TranslateToSplatMapPageKey(heightPageKey);
        if (!gpuSplatMapArray.IsPageResident(splatPageKey))
        {
            splatSliceIndex = -1;
            return false;
        }
        gpuSplatMapArray.TryGetResidentSlice(splatPageKey, out splatSliceIndex);
    }
    else
    {
        splatSliceIndex = -1;
    }

    return true;
}
```

- [ ] **Step 7: 添加 `GetSplatMapPageInfo` 方法**

在 `TerrainStreamingManager` 中添加，计算 splatmap 页内偏移和步幅：

```csharp
/// <summary>
/// 计算 chunk 在 splatmap page 内的偏移和步幅。
/// </summary>
public (int splatPageOffsetX, int splatPageOffsetY, int splatPageTexelStride) GetSplatMapPageInfo(TerrainChunkKey chunkKey)
{
    int ratio = fileReader.SplatMapResolutionRatio;
    if (ratio <= 1)
    {
        // Legacy: 与 heightmap 相同
        GetPageKey(chunkKey, out int _, out int _, out int heightStride);
        return (0, 0, heightStride);
    }

    // 计算 chunk 在 heightmap page 内的 offset
    GetPageKey(chunkKey, out int heightPageOffsetX, out int heightPageOffsetY, out int heightPageTexelStride);

    // Splatmap 坐标 = heightmap 坐标 / ratio
    int splatPageOffsetX = heightPageOffsetX / ratio;
    int splatPageOffsetY = heightPageOffsetY / ratio;
    int splatPageTexelStride = Math.Max(1, heightPageTexelStride / ratio);

    return (splatPageOffsetX, splatPageOffsetY, splatPageTexelStride);
}
```

- [ ] **Step 8: 修改 `RequestPage` — splatmap 使用转换后的 key**

找到 `RequestPage` 方法（约第 793 行），修改 splatmap 请求部分：

```csharp
// Enqueue matching splat map page request
if (gpuSplatMapArray != null)
{
    TerrainPageKey splatPageKey = TranslateToSplatMapPageKey(pageKey);
    if (!gpuSplatMapArray.IsPageResident(splatPageKey))
    {
        if (splatMapBufferPool != null && splatMapBufferPool.TryRent(out IMemoryOwner<byte>? splatMapBuffer) && splatMapBuffer != null)
        {
            try
            {
                pendingRequests.Add(new StreamingRequest(splatPageKey, splatMapBuffer, pinned, isSplatMap: true));
            }
            catch
            {
                splatMapBuffer.Dispose();
            }
        }
    }
    else if (pinned)
    {
        gpuSplatMapArray.TrySetPinned(splatPageKey, pinned: true);
    }
}
```

- [ ] **Step 9: 修改 `PreloadTopLevelChunks` — splatmap 使用转换后的 key**

找到 `PreloadTopLevelChunks` 方法（约第 663 行），修改 splatmap 预加载：

```csharp
if (splatMapPageData != null && gpuSplatMapArray != null)
{
    TerrainPageKey splatPageKey = TranslateToSplatMapPageKey(pageKey);
    if (!gpuSplatMapArray.IsPageResident(splatPageKey))
    {
        fileReader.ReadSplatMapPage(splatPageKey, splatMapPageData.Memory.Span);
        gpuSplatMapArray.UploadPage(commandList, splatPageKey, splatMapPageData.Memory.Span, pinned: false);
    }
}
```

- [ ] **Step 10: 修改 `IsChunkResident` — 使用转换后的 key**

```csharp
public bool IsChunkResident(TerrainChunkKey chunkKey)
{
    TerrainPageKey pageKey = GetPageKey(chunkKey, out _, out _, out _);
    if (!gpuHeightArray.IsPageResident(pageKey))
        return false;

    if (gpuSplatMapArray != null)
    {
        TerrainPageKey splatPageKey = TranslateToSplatMapPageKey(pageKey);
        if (!gpuSplatMapArray.IsPageResident(splatPageKey))
            return false;
    }

    return true;
}
```

- [ ] **Step 11: 验证构建**

Run: `dotnet build Terrain/Terrain.csproj`
Expected: 可能因调用方签名变更报错（Task 4 修复）

- [ ] **Step 12: Commit**

```bash
git add Terrain/Streaming/TerrainStreaming.cs
git commit -m "feat: extend TerrainChunkNode with SplatInfo, add page key translation for splatmap"
```

---

## Task 4: 运行时 QuadTree — 填充 SplatInfo

**Files:**
- Modify: `Terrain/Rendering/TerrainQuadTree.cs`

- [ ] **Step 1: 修改 `SelectRenderNode` — 获取两个 slice index 并填充 SplatInfo**

找到 `SelectRenderNode` 方法（约第 225 行），替换为：

```csharp
private void SelectRenderNode(ref SelectionState state, TerrainChunkKey key, int chunkX, int chunkY, int lodLevel)
{
    bool isResident = streamingManager.TryGetResidentPageForChunk(key,
        out int heightSliceIndex, out int splatSliceIndex,
        out int pageOffsetX, out int pageOffsetY, out int pageTexelStride);
    if (!isResident)
    {
        streamingManager.RequestChunk(key);
        WriteSubdividedNode(ref state, chunkX, chunkY, lodLevel, TerrainLodLookupNodeState.Stop);
        return;
    }

    var (splatPageOffsetX, splatPageOffsetY, splatPageTexelStride) = streamingManager.GetSplatMapPageInfo(key);

    state.Data[state.RenderCount++] = new TerrainChunkNode
    {
        NodeInfo = new Int4(chunkX, chunkY, lodLevel, (int)TerrainLodLookupNodeState.Stop),
        StreamInfo = new Int4(heightSliceIndex, pageOffsetX, pageOffsetY, pageTexelStride),
        SplatInfo = new Int4(splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride),
    };
}
```

- [ ] **Step 2: 验证构建**

Run: `dotnet build Terrain/Terrain.csproj`
Expected: 成功（如果 Runtime TerrainProcessor 也需要更新，见下一步）

- [ ] **Step 3: Commit**

```bash
git add Terrain/Rendering/TerrainQuadTree.cs
git commit -m "feat: populate SplatInfo in TerrainChunkNode during LOD selection"
```

---

## Task 5: 运行时 TerrainProcessor — 传递 resolution ratio

**Files:**
- Modify: `Terrain/Core/TerrainProcessor.cs`

- [ ] **Step 1: 更新 `GpuVirtualTextureArray` 创建（如需要）**

查看当前代码（约第 194-199 行）：

```csharp
var gpuHeightArray = new GpuVirtualTextureArray(renderObject.HeightmapArray!, component.HeightmapTileSize, component.HeightmapTilePadding, loadedData.MaxResidentChunks);
GpuVirtualTextureArray? gpuSplatMapArray = null;
if (renderObject.SplatMapArray != null)
{
    gpuSplatMapArray = new GpuVirtualTextureArray(renderObject.SplatMapArray, component.HeightmapTileSize, component.HeightmapTilePadding, loadedData.MaxResidentChunks);
}
```

这里 `GpuVirtualTextureArray` 使用 `component.HeightmapTileSize` 和 `component.HeightmapTilePadding`。对于 splatmap，如果它使用不同的 TileSize/Padding，需要从 `fileReader.SplatMapHeader` 获取。但实际上两者的 TileSize 和 Padding 相同（预处理时使用的相同值），只是 page 数量不同。所以这里**不需要修改**。

- [ ] **Step 2: 验证无需改动后 commit**

如果 `dotnet build Terrain/Terrain.csproj` 已成功，跳过此 Task。

---

## Task 6: 运行时 Shader — Stream + Displacement + Diffuse

**Files:**
- Modify: `Terrain/Effects/Stream/TerrainHeightStream.sdsl`
- Modify: `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl`
- Modify: `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl`

- [ ] **Step 1: 更新 `TerrainHeightStream.sdsl` — 添加 splatmap streams**

```sdsl
namespace Terrain
{
    shader TerrainHeightStream
    {
        stage stream float TerrainSliceIndex;
        stage stream float2 TerrainPageLocalPos;
        stage stream float TerrainPageTexelStride;
        stage stream float TerrainSampleSpacing;
        stage stream float TerrainSplatSliceIndex;
        stage stream float2 TerrainSplatPageLocalPos;
    };
}
```

- [ ] **Step 2: 更新 `MaterialTerrainDisplacement.sdsl` — 添加 SplatInfo 到 chunk node 并计算 splatPageLocalPos**

在 `MaterialTerrainDisplacement.sdsl` 中，更新 struct 和 `Compute` 方法：

Struct 定义（第 5-9 行）改为：

```sdsl
struct MaterialTerrainChunkNode
{
    int4 NodeInfo;
    int4 StreamInfo;
    int4 SplatInfo;  // splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride
};
```

`Compute` 方法（第 66-93 行）改为：

```sdsl
override void Compute()
{
    MaterialTerrainChunkNode instance = InstanceBuffer[streams.InstanceID];
    int4 nodeInfo = instance.NodeInfo;
    int4 streamInfo = instance.StreamInfo;
    int4 splatInfo = instance.SplatInfo;
    int sliceIndex = streamInfo.x;
    float pageTexelStride = max(1.0f, (float)streamInfo.w);
    float lodLevel = nodeInfo.z;
    int neighborMask = nodeInfo.w;
    float lodScale = exp2(lodLevel);
    float chunkWorldSize = BaseChunkSize * lodScale;
    float2 worldOrigin = float2(nodeInfo.x, nodeInfo.y) * chunkWorldSize;
    float2 localPos = streams.Position.xz;
    localPos = ApplyCrackSnap(localPos, neighborMask);

    float2 sampleCoord = worldOrigin + localPos * lodScale;
    sampleCoord = clamp(sampleCoord, 0.0f, HeightmapDimensionsInSamples);

    float2 pageLocalPos = float2(streamInfo.y, streamInfo.z) + localPos * pageTexelStride;
    float height = SampleHeightAtLocalPos(pageLocalPos, sliceIndex);

    // Splatmap page-local coordinates
    int splatSliceIndex = splatInfo.x;
    float splatPageTexelStride = max(1.0f, (float)splatInfo.w);
    float2 splatPageLocalPos = float2(splatInfo.y, splatInfo.z) + localPos * splatPageTexelStride;

    streams.TexCoord = ComputeHeightmapUv(pageLocalPos);
    streams.TerrainSliceIndex = (float)sliceIndex;
    streams.TerrainPageLocalPos = pageLocalPos;
    streams.TerrainPageTexelStride = pageTexelStride;
    streams.TerrainSampleSpacing = max(1.0f, lodScale);
    streams.TerrainSplatSliceIndex = (float)splatSliceIndex;
    streams.TerrainSplatPageLocalPos = splatPageLocalPos;
    streams.Position = float4(sampleCoord.x, height, sampleCoord.y, 1.0f);
}
```

- [ ] **Step 3: 更新 `MaterialTerrainDiffuse.sdsl` — 使用 splatmap streams**

修改 `LoadMaterialPixel`（第 134-144 行）参数名（逻辑不变，只是改参数名以反映实际用途）：

```sdsl
MaterialPixel LoadMaterialPixel(float2 splatPageLocalPos, int splatSliceIndex)
{
    float padding = float(HeightmapTilePadding);
    float2 paddedMin = float2(-padding, -padding);
    float2 paddedMax = float2((float)(HeightmapTileSize - 1) + padding, (float)(HeightmapTileSize - 1) + padding);
    float2 clampedPos = clamp(splatPageLocalPos, paddedMin, paddedMax);
    int2 texelCoord = int2(clampedPos + float2(padding, padding) + 0.5f);

    float4 raw = IndexMapArray.Load(int4(texelCoord, splatSliceIndex, 0));
    return DecodeMaterialPixel(raw);
}
```

修改 `Compute` 方法（第 195-259 行），替换 slice/page 引用为 splat 版本：

```sdsl
override void Compute()
{
    int sliceIndex = (int)(streams.TerrainSliceIndex + 0.5f);
    int splatSliceIndex = (int)(streams.TerrainSplatSliceIndex + 0.5f);
    float2 pageLocalPos = streams.TerrainPageLocalPos;
    float2 splatPageLocalPos = streams.TerrainSplatPageLocalPos;
    float pageTexelStride = max(1.0f, streams.TerrainPageTexelStride);
    float sampleSpacing = max(1.0f, streams.TerrainSampleSpacing);
    float3 localNormal = CaculateNormal(pageLocalPos, sliceIndex, pageTexelStride, sampleSpacing);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)WorldInverseTranspose));
    streams.meshNormal = localNormal;
    streams.meshNormalWS = worldNormal;
    streams.normalWS = worldNormal;

    // Mirror-neighbor sampling for splatmap
    float2 indexPixels = splatPageLocalPos;
    float2 index00Pixel = floor(indexPixels);

    float2 mirror00 = frac(index00Pixel * 0.5f) * 2.0f;
    float2 mirror11 = 1.0f - mirror00;

    float2 weights1 = saturate(indexPixels - index00Pixel);
    weights1 = lerp(weights1, 1.0f - weights1, mirror00);
    float2 weights0 = 1.0f - weights1;

    int2 p00 = int2(index00Pixel + mirror00);
    int2 p01 = int2(index00Pixel + float2(mirror00.x, mirror11.y));
    int2 p10 = int2(index00Pixel + float2(mirror11.x, mirror00.y));
    int2 p11 = int2(index00Pixel + mirror11);

    MaterialPixel m00 = LoadMaterialPixel(float2(p00), splatSliceIndex);
    MaterialPixel m01 = LoadMaterialPixel(float2(p01), splatSliceIndex);
    MaterialPixel m10 = LoadMaterialPixel(float2(p10), splatSliceIndex);
    MaterialPixel m11 = LoadMaterialPixel(float2(p11), splatSliceIndex);

    float w00 = weights0.x * weights0.y;
    float w01 = weights0.x * weights1.y;
    float w10 = weights1.x * weights0.y;
    float w11 = weights1.x * weights1.y;

    float3 colorSum = float3(0.0f, 0.0f, 0.0f);
    float3 normalSum = float3(0.0f, 0.0f, 0.0f);
    float totalWeight = 0.0f;
    AccumulateMaterialSample(m00, w00, streams.PositionWS.xyz, worldNormal, colorSum, normalSum, totalWeight);
    AccumulateMaterialSample(m01, w01, streams.PositionWS.xyz, worldNormal, colorSum, normalSum, totalWeight);
    AccumulateMaterialSample(m10, w10, streams.PositionWS.xyz, worldNormal, colorSum, normalSum, totalWeight);
    AccumulateMaterialSample(m11, w11, streams.PositionWS.xyz, worldNormal, colorSum, normalSum, totalWeight);

    float invWeight = 1.0f / max(totalWeight, 0.0001f);
    float3 finalColor = colorSum * invWeight;
    float3 normalAverage = normalSum * invWeight;

    float normalLen2 = dot(normalAverage, normalAverage);
    bool invalidNormal =
        normalLen2 < 0.000001f ||
        !(normalAverage.x == normalAverage.x) ||
        !(normalAverage.y == normalAverage.y) ||
        !(normalAverage.z == normalAverage.z);
    float3 blendedNormal = invalidNormal ? worldNormal : normalize(normalAverage);

    streams.matDiffuse = float4(finalColor, 1.0f);
    streams.matColorBase = float4(finalColor, 1.0f);
    float3 mappedNormalWS = normalize(blendedNormal);
    if (dot(mappedNormalWS, worldNormal) < 0.0f)
        mappedNormalWS = -mappedNormalWS;
    streams.normalWS = mappedNormalWS;
}
```

- [ ] **Step 4: 更新 .sdsl.cs key 文件（如 Stride 需要手动更新的）**

检查并更新 Shader key 文件以反映新增的 stream 字段。如果 Stride 自动生成则跳过。

- [ ] **Step 5: 验证构建**

Run: `dotnet build Terrain/Terrain.csproj`
Expected: 成功

- [ ] **Step 6: Commit**

```bash
git add Terrain/Effects/Stream/TerrainHeightStream.sdsl Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl
git commit -m "feat: add splatmap streams and SplatInfo to runtime shaders for half-res splatmap"
```

---

## Task 7: 编辑器 TerrainChunkNode 扩展

**Files:**
- Modify: `Terrain.Editor/Rendering/EditorTerrainEntity.cs`

- [ ] **Step 1: 扩展编辑器 `TerrainChunkNode` 结构体**

找到 `TerrainChunkNode` 定义（第 866-871 行），添加 `SplatInfo` 字段：

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct TerrainChunkNode
{
    public Int4 NodeInfo;
    public Int4 StreamInfo;
    public Int4 SplatInfo;  // 新增：与 heightmap StreamInfo 相同（编辑器不使用 VT streaming）
}
```

- [ ] **Step 2: 更新 `EditorTerrainQuadTree.SelectRenderNode`**

找到 `EditorTerrainQuadTree.cs` 的 `SelectRenderNode`（第 205 行），填充 `SplatInfo`：

```csharp
private void SelectRenderNode(ref SelectionState state, int chunkX, int chunkY, int lodLevel, int originSampleX, int originSampleY)
{
    int sliceIndex = terrainEntity.TryResolveSampleSlice(originSampleX, originSampleY, out var slice)
        ? slice.Index
        : 0;

    state.Data[state.RenderCount++] = new TerrainChunkNode
    {
        NodeInfo = new Int4(chunkX, chunkY, lodLevel, (int)TerrainLodLookupNodeState.Stop),
        StreamInfo = new Int4(sliceIndex, 0, 0, 0),
        SplatInfo = new Int4(sliceIndex, 0, 0, 0),  // 编辑器中与 heightmap 相同
    };
}
```

同时更新 `WriteSubdividedNode`（第 218 行）：

```csharp
state.Data[subdividedIndex] = new TerrainChunkNode
{
    NodeInfo = new Int4(chunkX, chunkY, lodLevel, (int)nodeState),
    StreamInfo = default,
    SplatInfo = default,
};
```

- [ ] **Step 3: 验证构建**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 成功

- [ ] **Step 4: Commit**

```bash
git add Terrain.Editor/Rendering/EditorTerrainEntity.cs Terrain.Editor/Rendering/EditorTerrainQuadTree.cs
git commit -m "feat: extend editor TerrainChunkNode with SplatInfo field"
```

---

## Task 8: 编辑器 Displacement Shader — 匹配新 struct

**Files:**
- Modify: `Terrain.Editor/Effects/EditorTerrainDisplacement.sdsl`

- [ ] **Step 1: 更新 `EditorTerrainChunkNode` struct 和 `Compute`**

更新 struct：

```sdsl
struct EditorTerrainChunkNode
{
    int4 NodeInfo;
    int4 StreamInfo;  // sliceIndex, sliceLocalOriginX, sliceLocalOriginY, reserved
    int4 SplatInfo;   // 与 StreamInfo 相同（编辑器不使用 VT streaming）
};
```

`Compute` 方法中，读取 `SplatInfo` 并写入 stream（编辑器暂时不使用 splatPageLocalPos，但需要读取 struct 以保持布局一致）：

```sdsl
override void Compute()
{
    EditorTerrainChunkNode instance = InstanceBuffer[streams.InstanceID];
    int4 nodeInfo = instance.NodeInfo;
    int4 streamInfo = instance.StreamInfo;
    // SplatInfo 读取（保持 struct 布局一致，编辑器暂不使用独立 splat 坐标）
    int splatSliceIndex = instance.SplatInfo.x;
    float lodLevel = nodeInfo.z;
    int neighborMask = nodeInfo.w;
    int sliceIndex = streamInfo.x;
    float lodScale = exp2(lodLevel);
    float chunkWorldSize = BaseChunkSize * lodScale;
    float2 worldOrigin = float2(nodeInfo.x, nodeInfo.y) * chunkWorldSize;
    float2 localPos = ApplyCrackSnap(streams.Position.xz, neighborMask);

    float2 sampleCoord = worldOrigin + localPos * lodScale;
    sampleCoord = clamp(sampleCoord, 0.0f, HeightmapDimensionsInSamples);
    float heightValue = SampleHeight(sampleCoord, sliceIndex);

    streams.TerrainLocalPos = sampleCoord;
    streams.TerrainSampleSpacing = max(1.0f, lodScale);
    streams.TerrainSliceIndex = sliceIndex;
    streams.Position = float4(sampleCoord.x, heightValue, sampleCoord.y, 1.0f);
}
```

- [ ] **Step 2: 验证构建**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 成功

- [ ] **Step 3: Commit**

```bash
git add Terrain.Editor/Effects/EditorTerrainDisplacement.sdsl
git commit -m "feat: update editor displacement shader for extended chunk node struct"
```

---

## Task 9: 编辑器 MaterialIndexMap 半尺寸 + 画笔坐标缩放

**Files:**
- Modify: `Terrain.Editor/Services/TerrainManager.cs`
- Modify: `Terrain.Editor/Services/PaintEditor.cs`
- Modify: `Terrain.Editor/Rendering/EditorTerrainEntity.cs`

这是编辑器端最核心的改动。

- [ ] **Step 1: 修改 `TerrainManager.LoadTerrainAsync` — 创建半尺寸 MaterialIndexMap**

找到第 155 行：

```csharp
MaterialIndices = new MaterialIndexMap(heightDataWidth, heightDataHeight);
```

改为：

```csharp
// 材质索引图使用 heightmap 的 1/2 分辨率
int splatMapWidth = (heightDataWidth + 1) / 2;
int splatMapHeight = (heightDataHeight + 1) / 2;
MaterialIndices = new MaterialIndexMap(splatMapWidth, splatMapHeight);
```

- [ ] **Step 2: 修改 `PaintEditor.ApplyStroke` — 画笔坐标缩放到 splatmap 空间**

找到 `ApplyStroke` 方法（第 57 行），将 heightmap 坐标转换为 splatmap 坐标：

```csharp
public void ApplyStroke(Vector3 worldPosition, MaterialIndexMap indexMap, int mapWidth, int mapHeight, TerrainManager terrainManager)
{
    if (!isStrokeActive || currentTool == null)
        return;

    // 转换世界坐标到 heightmap 像素坐标
    int pixelX = (int)MathF.Round(worldPosition.X);
    int pixelZ = (int)MathF.Round(worldPosition.Z);

    // 缩放到 splatmap 坐标空间（splatmap 是 heightmap 的 1/2）
    int splatPixelX = pixelX / 2;
    int splatPixelZ = pixelZ / 2;

    // 获取笔刷参数
    var brushParams = BrushParameters.Instance;
    float brushRadius = brushParams.Size * 0.5f;
    float brushInnerRadius = brushRadius * brushParams.EffectiveFalloff;

    // 缩放笔刷半径到 splatmap 空间
    float splatBrushRadius = MathF.Ceiling(brushRadius) / 2.0f;
    float splatBrushInnerRadius = MathF.Ceiling(brushInnerRadius) / 2.0f;

    // 获取目标材质索引
    byte targetIndex = ResolveTargetMaterialIndex();
    if (targetIndex == byte.MaxValue)
        return;

    // 构建编辑上下文（使用 splatmap 坐标空间）
    var context = new PaintEditContext
    {
        IndexMap = indexMap,
        DataWidth = mapWidth,
        DataHeight = mapHeight,
        CenterX = splatPixelX,
        CenterZ = splatPixelZ,
        BrushRadius = splatBrushRadius,
        BrushInnerRadius = splatBrushInnerRadius,
        Strength = brushParams.Strength,
        TargetMaterialIndex = targetIndex,

        Weight = brushParams.Weight,
        RandomRotation = brushParams.RandomRotation,
        FixedRotationDegrees = brushParams.FixedRotationDegrees,
        Use3DProjection = brushParams.Use3DProjection,
        RandomSeed = strokeSeed,
        HeightData = terrainManager.HeightDataCache,
        HeightDataWidth = terrainManager.HeightCacheWidth,
        HeightDataHeight = terrainManager.HeightCacheHeight
    };

    // Mark chunks（使用 splatmap 坐标）
    HistoryManager.Instance.MarkCommandChunks(splatPixelX, splatPixelZ, splatBrushRadius);

    // 应用工具
    currentTool.Apply(ref context);

    // 标记脏区域（使用 heightmap 坐标，因为 MarkDataDirty 按 heightmap 切片工作）
    terrainManager.MarkDataDirty(TerrainDataChannel.MaterialIndex, pixelX, pixelZ, brushRadius);
}
```

- [ ] **Step 3: 修改 `EditorTerrainEntity.InitializeMaterialResources` — 半尺寸纹理**

找到第 210-223 行，将 IndexMap 纹理创建为半尺寸：

```csharp
public void InitializeMaterialResources(GraphicsDevice graphicsDevice)
{
    MaterialIndexMapTextures = new Texture[SplitConfig.TotalSliceCount];
    for (int i = 0; i < SplitConfig.TotalSliceCount; i++)
    {
        var sliceInfo = SplitConfig.Slices[i];
        // IndexMap 纹理使用 heightmap 切片的 1/2 分辨率
        int indexMapWidth = (sliceInfo.Width + 1) / 2;
        int indexMapHeight = (sliceInfo.Height + 1) / 2;
        MaterialIndexMapTextures[i] = Texture.New2D(
            graphicsDevice,
            indexMapWidth,
            indexMapHeight,
            PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.ShaderResource);
    }
}
```

- [ ] **Step 4: 修改 `EditorTerrainEntity.SyncMaterialIndexMapToGpu` — 适配半尺寸**

找到第 229 行，移除尺寸检查中的硬编码匹配，并更新区域上传逻辑：

```csharp
public void SyncMaterialIndexMapToGpu(CommandList commandList, Services.MaterialIndexMap indexMap)
{
    // IndexMap 是 heightmap 的 1/2 分辨率，不再强制要求尺寸匹配
    var rawData = indexMap.GetRawData(); // uint[]

    for (int i = 0; i < MaterialIndexMapTextures.Length; i++)
    {
        var texture = MaterialIndexMapTextures[i];
        if (texture == null)
            continue;

        var slice = slices[i];
        if (!slice.Dirty.IsChannelDirty(TerrainDataChannel.MaterialIndex))
            continue;

        // heightmap 切片区域 → splatmap 区域（坐标 /2）
        int splatSliceWidth = (slice.Width + 1) / 2;
        int splatSliceHeight = (slice.Height + 1) / 2;

        // splatmap 中该切片的起始位置（相对于 splatmap 原点）
        int splatStartX = slice.StartSampleX / 2;
        int splatStartZ = slice.StartSampleZ / 2;

        if (slice.Dirty.HasRegion)
        {
            // 将 heightmap 脏区域转换为 splatmap 区域
            int regionLeft = Math.Max(0, slice.Dirty.MinX / 2);
            int regionTop = Math.Max(0, slice.Dirty.MinZ / 2);
            int regionRight = Math.Min(splatSliceWidth - 1, (slice.Dirty.MaxX + 1) / 2);
            int regionBottom = Math.Min(splatSliceHeight - 1, (slice.Dirty.MaxZ + 1) / 2);
            int regionWidth = regionRight - regionLeft + 1;
            int regionHeight = regionBottom - regionTop + 1;

            if (regionWidth <= 0 || regionHeight <= 0)
            {
                slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
                continue;
            }

            int regionByteSize = regionWidth * regionHeight * Services.MaterialIndexMap.BytesPerPixel;
            byte[] uploadBuffer = ArrayPool<byte>.Shared.Rent(regionByteSize);
            try
            {
                // 从 indexMap 全局 splatmap 空间复制
                int globalSplatX = splatStartX + regionLeft;
                int globalSplatZ = splatStartZ + regionTop;

                CopyMatIndexRegionDataAt(
                    indexMap, globalSplatX, globalSplatZ,
                    regionWidth, regionHeight, uploadBuffer);

                var region = new ResourceRegion(
                    left: regionLeft,
                    top: regionTop,
                    front: 0,
                    right: regionLeft + regionWidth,
                    bottom: regionTop + regionHeight,
                    back: 1);

                texture.SetData(commandList, uploadBuffer.AsSpan(0, regionByteSize), arrayIndex: 0, mipLevel: 0, region);
                slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(uploadBuffer);
            }
        }
        else
        {
            // Full slice upload
            int byteSize = splatSliceWidth * splatSliceHeight * Services.MaterialIndexMap.BytesPerPixel;
            byte[] uploadBuffer = ArrayPool<byte>.Shared.Rent(byteSize);
            try
            {
                CopyMatIndexRegionDataAt(
                    indexMap, splatStartX, splatStartZ,
                    splatSliceWidth, splatSliceHeight, uploadBuffer);

                texture.SetData(commandList, uploadBuffer.AsSpan(0, byteSize));
                slice.Dirty.ClearChannel(TerrainDataChannel.MaterialIndex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(uploadBuffer);
            }
        }
    }
}
```

添加新的辅助方法 `CopyMatIndexRegionDataAt`（在现有的 `CopyMatIndexRegionData` 方法附近）：

```csharp
/// <summary>
/// 从 MaterialIndexMap 的指定全局位置复制区域数据到 byte 数组。
/// 直接在 splatmap 坐标空间操作。
/// </summary>
private static void CopyMatIndexRegionDataAt(
    Services.MaterialIndexMap indexMap,
    int startX, int startZ,
    int regionWidth, int regionHeight,
    byte[] destination)
{
    for (int row = 0; row < regionHeight; row++)
    {
        var srcSpan = indexMap.GetSliceBytesPerRow(startX, startZ + row, 0, regionWidth);
        int dstOffset = row * regionWidth * Services.MaterialIndexMap.BytesPerPixel;
        srcSpan.CopyTo(destination.AsSpan(dstOffset));
    }
}
```

- [ ] **Step 5: 验证构建**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 成功

- [ ] **Step 6: Commit**

```bash
git add Terrain.Editor/Services/TerrainManager.cs Terrain.Editor/Services/PaintEditor.cs Terrain.Editor/Rendering/EditorTerrainEntity.cs
git commit -m "feat: editor uses half-resolution MaterialIndexMap with coordinate scaling"
```

---

## Task 10: 编辑器 Shader — 坐标缩放

**Files:**
- Modify: `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl`
- Modify: `Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl`

- [ ] **Step 1: 更新 `LoadIndexMapAtGlobal` — 坐标 ÷2**

在 `EditorTerrainHeightParameters.sdsl` 中找到 `LoadIndexMapAtGlobal`（约第 196 行），修改：

```sdsl
float4 LoadIndexMapAtGlobal(int2 globalPixel)
{
    // 从 heightmap 坐标转换到 splatmap 坐标（1/2 分辨率）
    int2 splatPixel = globalPixel / 2;
    float2 sampleCoord = float2(splatPixel);
    int sliceIndex = ResolveSliceIndex(sampleCoord, 0);
    int4 bounds = GetSliceBounds(sliceIndex);
    int2 localCoord = ComputeSliceTexelCoord(sampleCoord, bounds);
    return LoadIndexMapSliceAt(sliceIndex, localCoord);
}
```

- [ ] **Step 2: 更新 `EditorTerrainDiffuse.sdsl` 的 `ClampIndexPixel` — 使用半尺寸边界**

找到 `ClampIndexPixel`（约第 114 行），修改边界为半尺寸：

```sdsl
int2 ClampIndexPixel(int2 pixel)
{
    // IndexMap 使用 heightmap 的 1/2 分辨率
    int maxX = (int)(HeightmapDimensionsInSamples.x + 0.5f) / 2;
    int maxY = (int)(HeightmapDimensionsInSamples.y + 0.5f) / 2;
    return int2(
        clamp(pixel.x, 0, maxX),
        clamp(pixel.y, 0, maxY));
}
```

同时更新 `Compute` 方法中的 `indexPixels` 计算（约第 208 行），从 heightmap 坐标转换：

```sdsl
// 在 Compute() 中，替换 float2 indexPixels = sampleCoord; 为：
float2 indexPixels = sampleCoord * 0.5;
```

- [ ] **Step 3: 验证构建**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 成功

- [ ] **Step 4: Commit**

```bash
git add Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl
git commit -m "feat: editor shaders scale coordinates for half-resolution splatmap"
```

---

## Task 11: 全量构建验证 + 修复

- [ ] **Step 1: 完整构建**

Run: `dotnet build`
Expected: 零错误

- [ ] **Step 2: 修复任何编译错误**

如果出现签名不匹配、缺少引用等问题，逐一修复。

- [ ] **Step 3: 更新 .sdsl.cs key 文件（如果 Stride 要求手动更新）**

检查 shader key 文件是否需要手动更新以反映新增的 stream 和 struct 字段。

- [ ] **Step 4: 最终构建验证**

Run: `dotnet build`
Expected: 零错误

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "fix: resolve build errors for splatmap half-resolution feature"
```

---

## Verification

1. **构建验证**: `dotnet build` 零错误
2. **预处理验证**: 用现有 heightmap + splatmap 运行 PreProcessor，确认生成 v3 .terrain 文件
3. **运行时验证**: 加载 v3 文件，确认渲染结果视觉正确
4. **内存验证**: 确认 splatmap GPU 内存占用减半
5. **编辑器验证**: 画笔操作正常，材质混合效果正确
6. **向后兼容**: 加载 v2 文件确认正常工作
