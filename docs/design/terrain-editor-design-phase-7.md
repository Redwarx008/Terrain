# 阶段七：渲染优化

## 概述

**目标：** 优化现有地形渲染性能，将LOD计算和剔除移至GPU

**范围：**
- GPU LOD计算迁移
- GPU视锥剔除
- Hi-Z遮挡剔除（可选）
- 流式加载优化

**参考：**
- Unity GPU Indirect: GPU LOD、Hi-Z剔除
- 当前实现: `TerrainQuadTree.cs` (CPU LOD)

## 当前实现分析

### 现有架构

```
当前渲染流程:
CPU QuadTree.Select() → CPU LOD选择 → GPU BuildLod* → GPU Draw
                         ↑ 瓶颈
```

### 识别的问题

1. **CPU LOD选择瓶颈**: `TerrainQuadTree.Select()` 在CPU端遍历四叉树，大量节点时成为瓶颈
2. **缺乏GPU剔除**: 所有渲染节点都提交GPU，没有视锥/遮挡剔除
3. **流式加载延迟**: 单线程IO可能导致帧卡顿

## GPU LOD迁移

### 目标架构

```
优化后渲染流程:
CPU Submit All Nodes → GPU LOD Select → GPU Cull → GPU Draw
                       ↑ 并行化
```

### TerrainGpuQuadTree.sdsl

```hlsl
shader TerrainGpuQuadTree : ComputeShaderBase
{
    struct TerrainNode
    {
        int4 NodeInfo;      // chunkX, chunkY, lodLevel, parentIndex
        int4 ChildIndices;  // tl, tr, bl, br or -1
        float4 Bounds;      // minX, minY, maxX, maxY
    };

    stage StructuredBuffer<TerrainNode> NodeTree;
    stage RWStructuredBuffer<uint> SelectedNodes;
    stage RWBuffer<uint> SelectedCount;

    stage float4 FrustumPlanes[6];
    stage float3 CameraPosition;
    stage float ScreenSpaceScale;
    stage float MaxSSE;
    stage float GeometricError[8]; // 每个LOD的几何误差

    float ComputeSSE(float3 boundsMin, float3 boundsMax, int lodLevel)
    {
        float3 center = (boundsMin + boundsMax) * 0.5;
        float dist = distance(center, CameraPosition);
        float error = GeometricError[lodLevel];
        return ScreenSpaceScale * error / max(dist, 0.001);
    }

    bool IsVisible(float3 boundsMin, float3 boundsMax)
    {
        for (int i = 0; i < 6; i++)
        {
            float4 plane = FrustumPlanes[i];
            if (dot(plane.xyz, boundsMin) + plane.w < 0 &&
                dot(plane.xyz, boundsMax) + plane.w < 0)
                return false;
        }
        return true;
    }

    override void Compute()
    {
        uint nodeIndex = streams.DispatchThreadId.x;
        TerrainNode node = NodeTree[nodeIndex];

        // 1. 视锥剔除
        if (!IsVisible(node.Bounds.xyz, node.Bounds.wzw))
            return;

        // 2. SSE计算
        float sse = ComputeSSE(node.Bounds.xyz, node.Bounds.wzw, node.NodeInfo.z);

        if (sse <= MaxSSE || node.ChildIndices.x < 0)
        {
            // 渲染此节点
            uint idx;
            InterlockedAdd(SelectedCount[0], 1, idx);
            SelectedNodes[idx] = nodeIndex;
        }
        // 否则：需要细分 - 子节点会被其他线程处理
    }
};
```

### CPU端集成

```csharp
// TerrainGpuQuadTreeDispatcher.cs
public class TerrainGpuQuadTreeDispatcher : IDisposable
{
    private ComputeEffectShader? gpuQuadTreeEffect;
    private Buffer? nodeTreeBuffer;
    private Buffer? selectedNodesBuffer;
    private Buffer? selectedCountBuffer;

    public void Initialize(RenderContext renderContext)
    {
        gpuQuadTreeEffect = new ComputeEffectShader(renderContext)
        {
            ShaderSourceName = "TerrainGpuQuadTree",
            ThreadNumbers = new Int3(64, 1, 1),
        };
    }

    public void Dispatch(RenderDrawContext context, TerrainRenderObject renderObject)
    {
        // 上传节点树数据
        UpdateNodeTreeBuffer(context.CommandList, renderObject);

        // 重置计数器
        context.CommandList.Clear(selectedCountBuffer, new Vector4(0));

        // 设置参数
        gpuQuadTreeEffect!.Parameters.Set(TerrainGpuQuadTreeKeys.NodeTree, nodeTreeBuffer);
        gpuQuadTreeEffect.Parameters.Set(TerrainGpuQuadTreeKeys.SelectedNodes, selectedNodesBuffer);
        gpuQuadTreeEffect.Parameters.Set(TerrainGpuQuadTreeKeys.SelectedCount, selectedCountBuffer);
        gpuQuadTreeEffect.Parameters.Set(TerrainGpuQuadTreeKeys.FrustumPlanes, frustumPlanes);
        gpuQuadTreeEffect.Parameters.Set(TerrainGpuQuadTreeKeys.CameraPosition, cameraPosition);
        gpuQuadTreeEffect.Parameters.Set(TerrainGpuQuadTreeKeys.MaxSSE, maxSSE);

        // 调度
        int nodeCount = renderObject.TotalNodeCount;
        int threadGroups = (nodeCount + 63) / 64;
        gpuQuadTreeEffect.ThreadGroupCounts = new Int3(threadGroups, 1, 1);
        gpuQuadTreeEffect.Draw(context);
    }

    public int[] ReadSelectedNodes(CommandList commandList)
    {
        // 读取可见节点计数
        var countData = new int[4];
        selectedCountBuffer!.GetData(commandList, countData);
        int count = countData[0];

        // 读取可见节点列表
        var nodes = new int[count];
        selectedNodesBuffer!.GetData(commandList, nodes, 0, count);
        return nodes;
    }
}
```

## GPU视锥剔除

### TerrainHiZCulling.sdsl (可选高级优化)

```hlsl
shader TerrainHiZCulling : ComputeShaderBase
{
    stage Texture2D<float> DepthPyramid;  // 深度图Mip链
    stage StructuredBuffer<float4> NodeBounds;
    stage RWStructuredBuffer<uint> VisibleNodes;
    stage RWBuffer<uint> VisibleCount;
    stage float4x4 ViewProjection;

    float SampleDepthMip(float2 uv, int mipLevel)
    {
        return DepthPyramid.SampleLevel(samplerPoint, uv, mipLevel);
    }

    bool IsOccluded(float3 boundsMin, float3 boundsMax)
    {
        // 计算AABB在屏幕空间的包围盒
        float4 corners[8];
        corners[0] = mul(ViewProjection, float4(boundsMin, 1));
        corners[1] = mul(ViewProjection, float4(boundsMax.x, boundsMin.yzw, 1));
        corners[2] = mul(ViewProjection, float4(boundsMin.x, boundsMax.y, boundsMin.z, 1));
        corners[3] = mul(ViewProjection, float4(boundsMax.xy, boundsMin.z, 1));
        corners[4] = mul(ViewProjection, float4(boundsMin.xy, boundsMax.z, 1));
        corners[5] = mul(ViewProjection, float4(boundsMax.x, boundsMin.y, boundsMax.z, 1));
        corners[6] = mul(ViewProjection, float4(boundsMin.x, boundsMax.yz, 1));
        corners[7] = mul(ViewProjection, float4(boundsMax, 1));

        float2 minScreen = float2(1, 1);
        float2 maxScreen = float2(0, 0);
        float minDepth = 1;

        [unroll]
        for (int i = 0; i < 8; i++)
        {
            float3 clip = corners[i].xyz / corners[i].w;
            float2 screen = clip.xy * 0.5 + 0.5;
            minScreen = min(minScreen, screen);
            maxScreen = max(maxScreen, screen);
            minDepth = min(minDepth, clip.z);
        }

        // 选择合适的mip级别
        float2 size = maxScreen - minScreen;
        int mipLevel = (int)ceil(log2(max(size.x, size.y) * 1024));
        mipLevel = clamp(mipLevel, 0, 10);

        // 采样深度金字塔中心
        float2 centerUv = (minScreen + maxScreen) * 0.5;
        float hiZDepth = SampleDepthMip(centerUv, mipLevel);

        // 如果节点在Hi-Z后面，则被遮挡
        // 注意：Reverse Z需要反向比较
        return minDepth > hiZDepth;
    }

    override void Compute()
    {
        uint nodeIndex = streams.DispatchThreadId.x;
        float4 bounds = NodeBounds[nodeIndex];
        float3 minB = bounds.xyz;
        float3 maxB = bounds.xyz + bounds.w;

        if (IsOccluded(minB, maxB))
            return;

        uint idx;
        InterlockedAdd(VisibleCount[0], 1, idx);
        VisibleNodes[idx] = nodeIndex;
    }
};
```

### Hi-Z深度金字塔生成

```hlsl
// TerrainBuildDepthPyramid.sdsl
shader TerrainBuildDepthPyramid : ComputeShaderBase
{
    stage Texture2D<float> InputDepth;
    stage RWTexture2D<float> OutputDepth;
    stage uint MipLevel;
    stage uint2 OutputSize;

    override void Compute()
    {
        uint2 pixel = streams.DispatchThreadId.xy;
        if (any(pixel >= OutputSize))
            return;

        // 采样上一级mip的4个像素
        float2 uv = (float2(pixel * 2) + 1.0) / (OutputSize * 2.0);
        float d0 = InputDepth.SampleLevel(samplerPoint, uv, MipLevel - 1, int2(0, 0));
        float d1 = InputDepth.SampleLevel(samplerPoint, uv, MipLevel - 1, int2(1, 0));
        float d2 = InputDepth.SampleLevel(samplerPoint, uv, MipLevel - 1, int2(0, 1));
        float d3 = InputDepth.SampleLevel(samplerPoint, uv, MipLevel - 1, int2(1, 1));

        // 取最远深度（对于Reverse Z是最小值）
        OutputDepth[pixel] = min(min(d0, d1), min(d2, d3));
    }
};
```

## 流式加载优化

### 多线程IO

```csharp
// FoliageStreamingManager.cs
public class FoliageStreamingManager : IDisposable
{
    private readonly BlockingCollection<StreamingRequest> requestQueue = new();
    private readonly Thread[] ioThreads;
    private readonly int maxIoThreads = 2;

    public FoliageStreamingManager()
    {
        ioThreads = new Thread[maxIoThreads];
        for (int i = 0; i < maxIoThreads; i++)
        {
            ioThreads[i] = new Thread(IoThreadProc)
            {
                IsBackground = true,
                Name = $"FoliageIO_{i}"
            };
            ioThreads[i].Start();
        }
    }

    private void IoThreadProc()
    {
        foreach (var request in requestQueue.GetConsumingEnumerable())
        {
            ProcessRequest(request);
        }
    }

    public void RequestChunkLoad(int chunkX, int chunkZ, Priority priority)
    {
        requestQueue.Add(new StreamingRequest
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Priority = priority,
            Type = RequestType.Load
        });
    }

    private void ProcessRequest(StreamingRequest request)
    {
        // 从磁盘加载植被数据
        var data = LoadChunkData(request.ChunkX, request.ChunkZ);

        // 上传到GPU（在渲染线程）
        lock (pendingUploads)
        {
            pendingUploads.Enqueue(data);
        }
    }
}
```

### LRU缓存

```csharp
public class FoliageChunkCache
{
    private readonly int maxResidentChunks = 256;
    private readonly Dictionary<(int, int), FoliageChunk> chunks = new();
    private readonly LinkedList<(int, int)> lruList = new();

    public FoliageChunk? GetChunk(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (chunks.TryGetValue(key, out var chunk))
        {
            // 移动到LRU前端
            lruList.Remove(key);
            lruList.AddFirst(key);
            return chunk;
        }
        return null;
    }

    public void AddChunk(FoliageChunk chunk)
    {
        // 超过容量时驱逐
        while (chunks.Count >= maxResidentChunks)
        {
            var evictKey = lruList.Last.Value;
            lruList.RemoveLast();
            var evictChunk = chunks[evictKey];
            chunks.Remove(evictKey);
            evictChunk.Dispose();
        }

        var key = (chunk.ChunkX, chunk.ChunkZ);
        chunks[key] = chunk;
        lruList.AddFirst(key);
    }
}
```

## 性能对比验证

### 基准测试

```csharp
public class TerrainPerformanceBenchmark
{
    public struct BenchmarkResult
    {
        public string Name;
        public float CpuTimeMs;
        public float GpuTimeMs;
        public int VisibleNodeCount;
        public int TotalNodeCount;
    }

    public BenchmarkResult RunBenchmark(TerrainRenderObject terrain)
    {
        var result = new BenchmarkResult
        {
            Name = "GPU LOD + Culling",
            TotalNodeCount = terrain.TotalNodeCount
        };

        // CPU时间测量
        var cpuSw = Stopwatch.StartNew();

        // ... 执行LOD选择和剔除

        cpuSw.Stop();
        result.CpuTimeMs = cpuSw.ElapsedMilliseconds;

        // GPU时间测量（使用时间戳查询）
        result.GpuTimeMs = QueryGpuTime();

        return result;
    }

    public void CompareResults()
    {
        var cpuLodResult = RunCpuLodBenchmark();
        var gpuLodResult = RunGpuLodBenchmark();

        Console.WriteLine($"CPU LOD: {cpuLodResult.CpuTimeMs}ms, {cpuLodResult.VisibleNodeCount} visible");
        Console.WriteLine($"GPU LOD: {gpuLodResult.CpuTimeMs}ms, {gpuLodResult.VisibleNodeCount} visible");
        Console.WriteLine($"Improvement: {(1 - gpuLodResult.CpuTimeMs / cpuLodResult.CpuTimeMs) * 100:F1}%");
    }
}
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Terrain/Effects/Build/TerrainGpuQuadTree.sdsl` | GPU LOD选择 |
| `Terrain/Effects/Build/TerrainHiZCulling.sdsl` | Hi-Z遮挡剔除 |
| `Terrain/Effects/Build/TerrainBuildDepthPyramid.sdsl` | 深度金字塔生成 |
| `Terrain/Rendering/TerrainGpuQuadTreeDispatcher.cs` | GPU LOD调度器 |
| `Terrain/Streaming/FoliageStreamingManager.cs` | 植被流式加载 |

### 修改文件

| 文件路径 | 修改内容 |
|---------|---------|
| `Terrain/Rendering/TerrainRenderFeature.cs` | 集成GPU LOD |
| `Terrain/Rendering/TerrainQuadTree.cs` | 保留作为fallback |

## 实现步骤

### Phase 7.1: GPU LOD迁移
1. 创建 `TerrainGpuQuadTree.sdsl`
2. 创建 `TerrainGpuQuadTreeDispatcher.cs`
3. 修改 `TerrainRenderFeature` 集成
4. 添加CPU fallback路径

### Phase 7.2: GPU视锥剔除
1. 视锥平面参数传递
2. GPU剔除逻辑验证
3. Indirect绘制集成

### Phase 7.3: Hi-Z遮挡剔除（可选）
1. 创建深度金字塔生成Shader
2. 创建Hi-Z剔除Shader
3. 渲染管线集成

### Phase 7.4: 流式加载优化
1. 实现多线程IO
2. 实现LRU缓存
3. 性能测试验证

## 验证方案

1. **GPU LOD验证**: 帧时间对比，CPU占用降低
2. **剔除正确性**: 可见节点数与预期一致
3. **性能提升**: 目标CPU时间降低50%以上
4. **大场景测试**: 100万节点场景稳定运行
