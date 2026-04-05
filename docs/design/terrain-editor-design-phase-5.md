# 阶段五：植被系统扩展

## 概述

**目标：** 实现大规模树木渲染系统，支持GPU实例化和三级LOD

**范围：**
- 植被实例数据结构扩展
- GPU实例化渲染
- 三级LOD系统（完整模型→简化模型→Billboard）
- GPU剔除优化

**参考：**
- Godot MTerrain: MGrass草地系统
- Unity GPU Indirect: GPU剔除、Indirect绘制

## 架构设计

### 数据层次

```
FoliageTypeManager (管理器)
    |
    +-- FoliageType[] (类型定义)
    |    +-- Lod0Model (0-50m)
    |    +-- Lod1Model (50-150m)
    |    +-- Lod2Model/Billboard (150-500m)
    |
    +-- FoliageChunk[] (空间划分)
         +-- FoliageInstance[] (实例数据)
         +-- GPU Buffer
         +-- BoundingBox
```

### 渲染流程

```
FoliageRenderFeature
    |
    +-- FoliageCuller (GPU Compute)
    |    +-- 视锥剔除
    |    +-- 距离LOD选择
    |    +-- 写入可见列表
    |
    +-- FoliageRenderer
         +-- DrawMeshInstancedIndirect
         +-- 三级LOD切换
```

## 核心数据结构

### FoliageInstance (扩展版)

```csharp
// GPU对齐, 32 bytes
[StructLayout(LayoutKind.Sequential)]
public struct FoliageInstance
{
    public Vector3 Position;      // 12 bytes
    public float RotationY;       // 4 bytes - Y轴旋转
    public float Scale;           // 4 bytes
    public uint TypeIndex;        // 4 bytes - 植被类型索引
    public uint RandomSeed;       // 4 bytes - 随机变化种子
    public float MinDistance;     // 4 bytes - 到相机距离(GPU剔除用)
}
```

### FoliageType (支持三级LOD)

```csharp
public class FoliageType
{
    public int Id { get; set; }
    public string Name { get; set; } = "Unnamed";

    // 三级LOD模型
    public Model? Lod0Model { get; set; }       // 完整模型 (0-50m)
    public Model? Lod1Model { get; set; }       // 简化模型 (50-150m)
    public Model? Lod2Model { get; set; }       // Billboard (150-500m)

    // LOD距离配置
    public float Lod0Distance { get; set; } = 50f;
    public float Lod1Distance { get; set; } = 150f;
    public float Lod2Distance { get; set; } = 500f;

    // 放置参数
    public float MinScale { get; set; } = 0.8f;
    public float MaxScale { get; set; } = 1.2f;
    public float MinDistance { get; set; } = 2.0f;
    public float BrushDensity { get; set; } = 1.0f;

    // 渲染属性
    public RenderGroup RenderGroup { get; set; } = RenderGroup.Group1;
    public bool CastShadows { get; set; } = true;
}
```

### FoliageChunk (空间划分单元)

```csharp
public class FoliageChunk : IDisposable
{
    public int ChunkX, ChunkZ;
    public BoundingBox Bounds;

    private readonly List<FoliageInstance> instances = new();
    private Buffer? instanceBuffer;
    private Buffer? indirectArgsBuffer;
    private bool isDirty = true;

    public const float ChunkSize = 64.0f; // 世界单位

    public IReadOnlyList<FoliageInstance> Instances => instances;
    public int InstanceCount => instances.Count;

    public void AddInstance(FoliageInstance instance);
    public int RemoveInstancesInRadius(Vector3 center, float radius);
    public void UpdateGpuBuffer(GraphicsDevice device, CommandList commandList);
}
```

## GPU剔除实现

### TerrainFoliageCulling.sdsl

```hlsl
shader TerrainFoliageCulling : ComputeShaderBase
{
    struct FoliageInstance
    {
        float3 Position;
        float RotationY;
        float Scale;
        uint TypeIndex;
        uint RandomSeed;
        float MinDistance;
    };

    struct IndirectArgs
    {
        uint IndexCountPerInstance;
        uint InstanceCount;
        uint StartIndexLocation;
        int BaseVertexLocation;
        uint StartInstanceLocation;
    };

    stage StructuredBuffer<FoliageInstance> InstanceBuffer;
    stage RWStructuredBuffer<FoliageInstance> VisibleInstanceBuffer;
    stage RWStructuredBuffer<IndirectArgs> IndirectArgsBuffer;
    stage RWBuffer<uint> VisibleCounter;

    stage float4 FrustumPlanes[6];
    stage float3 CameraPosition;
    stage float LodDistances[3];
    stage uint InstanceCount;

    bool IsInsideFrustum(float3 position, float radius)
    {
        for (int i = 0; i < 6; i++)
        {
            if (dot(FrustumPlanes[i].xyz, position) + FrustumPlanes[i].w < -radius)
                return false;
        }
        return true;
    }

    uint ComputeLodLevel(float3 position)
    {
        float dist = distance(position, CameraPosition);
        if (dist < LodDistances[0]) return 0;
        if (dist < LodDistances[1]) return 1;
        if (dist < LodDistances[2]) return 2;
        return 3; // Culled
    }

    override void Compute()
    {
        uint instanceIndex = streams.DispatchThreadId.x;
        if (instanceIndex >= InstanceCount)
            return;

        FoliageInstance instance = InstanceBuffer[instanceIndex];

        // 1. 视锥剔除
        if (!IsInsideFrustum(instance.Position, instance.Scale * 2.0))
            return;

        // 2. LOD选择
        uint lodLevel = ComputeLodLevel(instance.Position);
        if (lodLevel > 2)
            return;

        // 3. 写入可见列表
        uint visibleIndex;
        InterlockedAdd(VisibleCounter[0], 1, visibleIndex);

        instance.MinDistance = distance(instance.Position, CameraPosition);
        VisibleInstanceBuffer[visibleIndex] = instance;

        // 4. 更新Indirect Args
        InterlockedAdd(IndirectArgsBuffer[lodLevel].InstanceCount, 1);
    }
};
```

## 渲染集成

### FoliageRenderFeature

```csharp
public sealed class FoliageRenderFeature : RootEffectRenderFeature
{
    public override Type SupportedRenderObjectType => typeof(FoliageRenderObject);

    private ComputeEffectShader? cullingEffect;

    protected override void InitializeCore()
    {
        base.InitializeCore();
        cullingEffect = new ComputeEffectShader(Context);
        // ... 初始化
    }

    public override void Prepare(RenderDrawContext context)
    {
        // 1. 收集可见块
        // 2. 更新实例缓冲
        // 3. 调度GPU剔除
        DispatchCulling(context);
    }

    public override void Draw(RenderDrawContext context, RenderView renderView, ...)
    {
        // 按LOD级别绘制
        for (int lod = 0; lod < 3; lod++)
        {
            DrawLodLevel(context, lod);
        }
    }
}
```

### MaterialFoliageInstancing.sdsl

```hlsl
shader MaterialFoliageInstancing : IMaterialSurface, Transformation
{
    struct FoliageInstance
    {
        float3 Position;
        float RotationY;
        float Scale;
        uint TypeIndex;
        uint RandomSeed;
        float MinDistance;
    };

    rgroup PerMaterial
    {
        stage StructuredBuffer<FoliageInstance> InstanceBuffer;
        stage uint CurrentLodLevel;
    }

    float3x3 RotationMatrixY(float angle)
    {
        float c = cos(angle);
        float s = sin(angle);
        return float3x3(c, 0, -s, 0, 1, 0, s, 0, c);
    }

    override void Compute()
    {
        FoliageInstance instance = InstanceBuffer[streams.InstanceID];

        float3x3 rotation = RotationMatrixY(instance.RotationY);
        float3 scaledPos = streams.Position * instance.Scale;
        float3 rotatedPos = mul(rotation, scaledPos);

        streams.Position = float4(rotatedPos + instance.Position, 1.0);
        streams.Normal = mul(rotation, streams.Normal);

        // 根据LOD级别调整细节
        // LOD0: 完整细节
        // LOD1: 简化细节
        // LOD2: Billboard（特殊处理）
    }
}
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Terrain/Core/Foliage/FoliageInstance.cs` | 扩展的实例数据结构 |
| `Terrain/Core/Foliage/FoliageType.cs` | 三级LOD类型定义 |
| `Terrain/Core/Foliage/FoliageChunk.cs` | 空间划分块 |
| `Terrain/Rendering/FoliageRenderFeature.cs` | 渲染特性 |
| `Terrain/Rendering/FoliageRenderObject.cs` | 渲染对象 |
| `Terrain/Effects/Foliage/TerrainFoliageCulling.sdsl` | GPU剔除着色器 |
| `Terrain/Effects/Foliage/MaterialFoliageInstancing.sdsl` | 实例化材质 |

### 修改文件

| 文件路径 | 修改内容 |
|---------|---------|
| `Terrain.Editor/Services/Foliage/FoliageTypeManager.cs` | 支持三级LOD |
| `Terrain.Editor/Services/Foliage/FoliageLayer.cs` | 扩展实例结构 |
| `Terrain.Editor/UI/Panels/FoliagePanel.cs` | LOD配置UI |

## 实现步骤

### Phase 5.1: 核心数据结构扩展
1. 创建扩展的 `FoliageInstance` 结构
2. 创建 `FoliageType` 三级LOD定义
3. 实现 `FoliageChunk` 空间划分

### Phase 5.2: GPU剔除实现
1. 创建 `TerrainFoliageCulling.sdsl`
2. 实现视锥剔除
3. 实现距离LOD选择
4. 实现Indirect Args写入

### Phase 5.3: 渲染集成
1. 创建 `FoliageRenderFeature`
2. 创建 `FoliageRenderObject`
3. 创建 `MaterialFoliageInstancing.sdsl`
4. 集成到渲染管线

### Phase 5.4: 编辑器更新
1. 更新 `FoliageTypeManager` 支持三级LOD
2. 更新UI面板
3. 添加LOD预览功能

## 验证方案

1. **GPU剔除验证**：100,000实例剔除后可见数<10,000
2. **LOD切换验证**：相机移动时LOD正确切换，无闪烁
3. **性能验证**：10,000棵树帧率>30FPS
4. **Billboard验证**：远处实例正确使用Billboard渲染
