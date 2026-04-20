---
doc_type: architecture
slug: streaming
scope: 运行时虚拟纹理流式加载系统
summary: TerrainStreamingManager 协调后台 IO 线程和 GPU 纹理数组，按需从 .terrain 文件加载 heightmap/splatmap 页
status: current
last_reviewed: 2026-04-20
tags: [streaming, virtual-texture, gpu, lru]
depends_on: [core-component, render-pipeline]
---

## 0. 术语

| 术语 | 定义 |
|---|---|
| TerrainPageKey | 虚拟纹理页标识（MipLevel, PageX, PageY） |
| TerrainChunkKey | LOD chunk 标识（LodLevel, ChunkX, ChunkY） |
| GpuVirtualTextureArray | GPU 纹理数组管理器，带 LRU 淘汰和 slice 分配 |
| PageBufferAllocator | Native 内存池分配器，减少每帧 GC |

## 1. 定位与受众

本文档描述运行时流式加载系统。读者是调试加载卡顿、LOD 切换闪烁、或 GPU 内存溢出的人。

## 2. 结构与交互

```
.terrain 文件
    ↓ TerrainFileReader (随机访问页读取)
TerrainStreamingManager
    ├─ Background IO Thread (BlockingCollection 队列)
    ├─ PageBufferAllocator (Native 内存池)
    ├─ GpuVirtualTextureArray × 2
    │   ├─ HeightmapArray (R16_UNorm)
    │   └─ SplatMapArray (RGBA8_UNorm)
    └─ TerrainQuadTree (页驻留状态)
```

### 关键交互

1. **QuadTree.Select()** 发现页缺失 → `RequestChunk(chunkKey)` → 入队
2. **IO Thread** 读取页数据 → `PageBufferAllocator.Rent()` 分配内存 → 入上传队列
3. **ProcessPendingUploads()** 每帧最多上传 `MaxStreamingUploadsPerFrame` 页到 GPU
4. **GpuVirtualTextureArray** 分配 slice → LRU 淘汰最旧未使用页

## 3. 数据与状态

| 数据 | 类型 | 归属 | 持久化 |
|---|---|---|---|
| TerrainFileHeader | struct | TerrainFileReader | .terrain 文件头部 |
| TerrainPageKey | struct | GpuVirtualTextureArray | 索引键 |
| Slice allocation map | Dictionary | GpuVirtualTextureArray | 内存（GPU→page 映射） |
| Pending uploads | BlockingCollection | TerrainStreamingManager | 内存（IO→GPU 队列） |

## 4. 关键决策

- **SplatMap 固定 1/2 高度图分辨率** → `2026-04-20-decision-splatmap-half-resolution.md`
- **HeightMap 与 IndexMap 页同步驻留**：同一 PageKey 的两种纹理页一起加载

## 5. 代码锚点

| 锚点 | 文件 | 说明 |
|---|---|---|
| TerrainFileReader | `Terrain/Streaming/TerrainStreaming.cs:136` | .terrain 文件读取器 |
| ReadHeightPage | `Terrain/Streaming/TerrainStreaming.cs:220` | 随机访问 heightmap 页 |
| ReadSplatMapPage | `Terrain/Streaming/TerrainStreaming.cs:242` | 随机访问 splatmap 页 |
| GpuVirtualTextureArray | `Terrain/Streaming/TerrainStreaming.cs:402` | GPU 纹理数组管理器 |
| IsPageResident | `Terrain/Streaming/TerrainStreaming.cs:432` | 页驻留检查 |
| UploadPage | `Terrain/Streaming/TerrainStreaming.cs:447` | GPU 页上传 |
| TryAllocateSlice | `Terrain/Streaming/TerrainStreaming.cs:483` | slice 分配 |
| TryEvictLeastRecentlyUsed | `Terrain/Streaming/TerrainStreaming.cs:506` | LRU 淘汰 |
| TerrainStreamingManager | `Terrain/Streaming/TerrainStreaming.cs:588` | 流式加载主管理器 |
| RequestChunk | `Terrain/Streaming/TerrainStreaming.cs:739` | chunk 请求入口 |
| IoThreadMain | `Terrain/Streaming/TerrainStreaming.cs:829` | 后台 IO 线程 |
| PageBufferAllocator | `Terrain/Streaming/PageBufferAllocator.cs:62` | Native 内存池 |
| TerrainPageKey | `Terrain/Streaming/TerrainStreaming.cs:77` | 页标识结构体 |
| TerrainChunkNode | `Terrain/Streaming/TerrainStreaming.cs:25` | GPU 实例数据 |

## 6. 已知约束 / 边界情况

- MaxStreamingUploadsPerFrame 默认 8，超出则排队到下帧
- LRU 淘汰时 pinned 页（顶层 LOD）不参与淘汰
- .terrain 文件版本 v3，SplatMap 分辨率比率存储在 header.Reserved1 字段

## 7. 相关文档

- [core-component.md](core-component.md) — TerrainProcessor 初始化流式系统
- [render-pipeline.md](render-pipeline.md) — QuadTree 驱动页请求