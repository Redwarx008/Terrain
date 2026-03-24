# InstanceBuffer 重构计划

## 问题分析

### 当前问题

1. **每帧创建Buffer**：[`TerrainProcessor.UploadChunkBuffer()`](Terrain/TerrainProcessor.cs:242) 每帧销毁并重新创建Buffer，导致GPU内存频繁分配/释放
2. **命名不准确**：`ChunkBuffer` 应该叫 `InstanceBuffer`，因为它存储实例数据
3. **架构冗余**：`TerrainRuntimeData` 作为中间层没有必要，CPU端数据可以直接由 `TerrainComponent` 持有

## 解决方案

### 新架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      TerrainComponent                           │
│  - 配置属性（HeightmapPath, HeightScale, BaseChunkSize等）       │
│  - CPU端运行时数据（SelectedChunks, InstanceData, MinMaxErrorMaps）│
│  - 不持有对RenderObject的引用（由Processor管理映射）              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (Processor通过ComponentDatas管理映射)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     TerrainRenderObject                         │
│  - GPU资源（InstanceBuffer, HeightTexture, PatchMesh等）         │
│  - 继承自RenderMesh                                              │
└─────────────────────────────────────────────────────────────────┘
```

### 职责划分

| 类 | 职责 |
|----|------|
| `TerrainComponent` | 配置属性 + CPU端运行时数据（SelectedChunks、InstanceData数组、MinMaxErrorMaps等） |
| `TerrainRenderObject` | GPU资源持有者（InstanceBuffer、HeightTexture、PatchMesh等） |
| `TerrainProcessor` | 处理逻辑，通过 `EntityProcessor<TerrainComponent, TerrainRenderObject>` 管理映射 |
| ~~`TerrainRuntimeData`~~ | **删除** |

### 最大实例数量

假定最大被选择的Chunk数量为 **200**。

## 需要修改的文件

### 1. TerrainComponent.cs

**修改内容：**
- 添加CPU端运行时数据属性
- 不持有对RenderObject的引用（由Processor管理）

```csharp
public sealed class TerrainComponent : ActivableEntityComponent
{
    // === 配置属性（已有）===
    public string HeightmapPath { get; set; } = "Resources/terrain_heightmap.png";
    public float HeightScale { get; set; } = 24.0f;
    public int BaseChunkSize { get; set; } = 32;
    // ...

    // === 运行时数据（新增，不序列化）===
    [DataMemberIgnore]
    internal readonly List<TerrainSelectedChunk> SelectedChunks = new();
    
    [DataMemberIgnore]
    internal readonly Int4[] InstanceData = new Int4[MaxInstanceCount];
    
    [DataMemberIgnore]
    internal TerrainMinMaxErrorMap[]? MinMaxErrorMaps;
    
    [DataMemberIgnore]
    internal int HeightmapWidth;
    
    [DataMemberIgnore]
    internal int HeightmapHeight;
    
    [DataMemberIgnore]
    internal int MaxLod;
    
    [DataMemberIgnore]
    internal float MinHeight;
    
    [DataMemberIgnore]
    internal float MaxHeight;
    
    public const int MaxInstanceCount = 200;
}
```

### 2. TerrainRenderObject.cs

**修改内容：**
- 重命名 `ChunkBufferAsset` → `InstanceBuffer`
- 添加 `HeightTexture` 属性
- 添加初始化方法，在RenderObject内部创建所有GPU资源

```csharp
public sealed class TerrainRenderObject : RenderMesh
{
    public Texture? HeightTexture;
    public Buffer? InstanceBuffer;  // 预创建的固定大小InstanceBuffer
    public Buffer? PatchVertexBuffer;
    public Buffer? PatchIndexBuffer;
    
    private const int MaxInstanceCount = 200;
    
    /// <summary>
    /// 初始化GPU资源（在Processor创建RenderObject时调用）
    /// </summary>
    public void Initialize(GraphicsDevice graphicsDevice, int baseChunkSize, int heightmapWidth, int heightmapHeight, float[] heights)
    {
        // 创建HeightTexture
        HeightTexture = Texture.New2D(
            graphicsDevice,
            heightmapWidth,
            heightmapHeight,
            PixelFormat.R32_Float,
            heights,
            TextureFlags.ShaderResource);
        
        // 创建预分配的InstanceBuffer
        InstanceBuffer = Buffer.Structured.New<Int4>(graphicsDevice, MaxInstanceCount);
        
        // 创建Patch顶点和索引缓冲
        CreatePatchGeometry(graphicsDevice, baseChunkSize);
    }
    
    private void CreatePatchGeometry(GraphicsDevice graphicsDevice, int baseChunkSize)
    {
        int vertexCountPerAxis = baseChunkSize + 1;
        var vertices = new TerrainPatchVertex[vertexCountPerAxis * vertexCountPerAxis];
        // ... 创建顶点数据
        
        PatchVertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices);
        PatchIndexBuffer = Buffer.Index.New(graphicsDevice, indices);
        
        Mesh = new Mesh(new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            VertexBuffers = [new VertexBufferBinding(PatchVertexBuffer, TerrainPatchVertex.Layout, vertices.Length)],
            IndexBuffer = new IndexBufferBinding(PatchIndexBuffer, true, indices.Length),
        }, new ParameterCollection());
        
        ActiveMeshDraw = Mesh.Draw;
    }
    
    /// <summary>
    /// 更新实例数据
    /// </summary>
    public void UpdateInstanceData(Int4[] data, int count)
    {
        InstanceBuffer?.SetData(data, 0, count);
        InstanceCount = count;
    }
}
```

### 3. TerrainProcessor.cs

**修改内容：**
- 修改泛型参数：`EntityProcessor<TerrainComponent, TerrainRenderObject>`
- 移除对 `TerrainRuntimeData` 的依赖
- 在创建RenderObject时初始化GPU资源
- 每帧只更新数据

```csharp
public sealed class TerrainProcessor : EntityProcessor<TerrainComponent, TerrainRenderObject>, IEntityComponentRenderProcessor
{
    // GenerateComponentData 返回 TerrainRenderObject 而不是 TerrainRuntimeData
    protected override TerrainRenderObject GenerateComponentData(Entity entity, TerrainComponent component) 
        => new();

    // 每帧更新
    private void UploadInstanceData(TerrainComponent component, TerrainRenderObject renderObject)
    {
        int count = Math.Min(component.SelectedChunks.Count, TerrainComponent.MaxInstanceCount);
        
        // 填充CPU端数据
        for (int i = 0; i < count; i++)
        {
            var chunk = component.SelectedChunks[i];
            component.InstanceData[i] = new Int4(chunk.ChunkX, chunk.ChunkY, chunk.LodLevel, 0);
        }
        
        // 上传到GPU
        renderObject.InstanceBuffer.SetData(component.InstanceData, 0, count);
        renderObject.InstanceCount = count;
    }
}
```

### 4. MaterialTerrainDisplacement.sdsl

**修改内容：**
- 重命名 `ChunkBuffer` → `InstanceBuffer`

```shader
rgroup PerMaterial
{
    stage Texture2D<float> HeightTexture;
    stage StructuredBuffer<int4> InstanceBuffer;  // 重命名
}

override void Compute()
{
    int4 chunk = InstanceBuffer[streams.InstanceID];  // 重命名
    // ...
}
```

### 5. MaterialTerrainDisplacement.sdsl.cs

**修改内容：**
- 重命名参数Key `ChunkBuffer` → `InstanceBuffer`

```csharp
public static readonly ObjectParameterKey<Buffer> InstanceBuffer = ParameterKeys.NewObject<Buffer>();
```

### 6. TerrainRuntimeData.cs

**修改内容：**
- **删除此文件**
- 将必要的数据结构（如 `TerrainSelectedChunk`、`TerrainMinMaxErrorMap`、`TerrainPatchVertex`）移动到单独文件

## 重命名映射表

| 旧名称 | 新名称 | 文件 |
|--------|--------|------|
| `ChunkBuffer` | 移除 | TerrainRuntimeData.cs |
| `ChunkBufferAsset` | `InstanceBuffer` | TerrainRenderObject.cs |
| `ChunkBuffer` (key) | `InstanceBuffer` (key) | MaterialTerrainDisplacement.sdsl.cs |
| `ChunkBuffer` (shader) | `InstanceBuffer` (shader) | MaterialTerrainDisplacement.sdsl |
| `UploadChunkBuffer()` | `UploadInstanceData()` | TerrainProcessor.cs |
| `TerrainRuntimeData` | 删除，数据移到TerrainComponent | - |

## 执行顺序

1. 创建 `TerrainDataStructures.cs` - 提取 `TerrainSelectedChunk`、`TerrainMinMaxErrorMap`、`TerrainPatchVertex` 等数据结构
2. 修改 `TerrainComponent.cs` - 添加运行时数据属性（标记 `[DataMemberIgnore]`）
3. 修改 `TerrainRenderObject.cs` - 添加InstanceBuffer属性
4. 修改 `MaterialTerrainDisplacement.sdsl` - 重命名shader变量
5. 修改 `MaterialTerrainDisplacement.sdsl.cs` - 重命名参数Key
6. 修改 `TerrainProcessor.cs` - 重构为直接使用Component和RenderObject
7. 删除 `TerrainRuntimeData.cs`
8. 测试验证

## 注意事项

1. **Buffer创建时机**：在创建RenderObject时创建
2. **Buffer销毁时机**：在RenderObject被移除时销毁
3. **数据更新**：使用 `Buffer.SetData()` 方法
4. **边界检查**：当 `SelectedChunks.Count > MaxInstanceCount` 时截断并警告
5. **序列化**：`TerrainComponent` 的新增运行时数据字段必须标记 `[DataMemberIgnore]` 避免序列化
6. **映射管理**：Processor通过 `ComponentDatas[component]` 获取对应的RenderObject

## 性能优化效果

- 消除每帧的GPU内存分配/释放
- 减少GC压力
- 提高渲染性能
- 更简洁的架构（移除中间层，Component不持有RenderObject引用）
