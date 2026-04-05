# 阶段三：植被系统

## 概述

**目标：** 实现基础植被放置功能

**范围：**
- 植被实例数据结构
- 植被放置/移除工具
- 植被管理 UI
- 植被模型导入
- 实例化渲染

## 架构设计

### 数据层次

```
FoliageTypeManager (管理器单例)
    |
    +-- FoliageType[] (类型定义)
    |    +-- Model 引用
    |    +-- 放置参数（密度、最小距离、缩放范围）
    |
    +-- FoliageLayer[] (实例层)
         +-- FoliageInstance[] (实例列表)
         +-- SpatialGrid (空间索引)
         +-- GPU Buffer (实例缓冲区)
```

### 编辑流程

```
FoliageEditor (编辑器)
    |
    +-- IFoliageTool (工具接口)
    |    +-- FoliagePlaceTool (放置)
    |    +-- FoliageRemoveTool (移除)
    |
    +-- FoliageTypeManager (类型管理)
    |
    +-- TerrainManager (地形高度查询)
```

### 渲染流程

```
FoliageRenderFeature (渲染特性)
    |
    +-- FoliageRenderObject (渲染对象)
    |    +-- Model (模型引用)
    |    +-- InstanceBuffer (GPU 实例数据)
    |
    +-- MaterialFoliageInstancing.sdsl (着色器)
```

## 核心数据结构

### FoliageInstance

> **注意：** 此结构在 Phase 5 中扩展以支持 GPU 剔除和 LOD。此处定义基础版本，Phase 5 扩展版本包含 `RandomSeed` 和 `MinDistance` 字段。

```csharp
// Phase 3 基础版本 (32 bytes, GPU对齐)
[StructLayout(LayoutKind.Sequential)]
public struct FoliageInstance
{
    public Vector3 Position;      // 12 bytes - 世界坐标
    public float RotationY;       // 4 bytes - Y 轴旋转（弧度）
    public float Scale;           // 4 bytes - 统一缩放
    public uint TypeIndex;        // 4 bytes - 植被类型索引（原 PrefabIndex）
    public uint RandomSeed;       // 4 bytes - 随机变化种子（Phase 5 新增）
    public float MinDistance;     // 4 bytes - 到相机距离（Phase 5 GPU剔除用）
}
```

> **迁移说明：** Phase 3 实现时可暂时忽略 `RandomSeed` 和 `MinDistance` 字段（设为默认值 0），Phase 5 实现时启用这些字段。字段名已统一为 `RotationY` 和 `TypeIndex`。

### FoliageType

> **命名说明：** Phase 3 原使用 `FoliagePrefab`，现统一为 `FoliageType` 以与 Phase 5 保持一致。`FoliagePrefabManager` 对应更名为 `FoliageTypeManager`。

```csharp
public class FoliageType
{
    public int Id { get; set; }
    public string Name { get; set; } = "Unnamed";
    public string Icon { get; set; } = Icons.Tree;

    // 模型引用
    public Model? Model { get; set; }
    public string? ModelPath { get; set; }

    // 放置参数
    public float MinScale { get; set; } = 0.8f;
    public float MaxScale { get; set; } = 1.2f;
    public bool RandomRotation { get; set; } = true;
    public float BrushDensity { get; set; } = 1.0f;
    public float MinDistance { get; set; } = 2.0f;

    // 渲染属性
    public RenderGroup RenderGroup { get; set; } = RenderGroup.Group1;
    public bool CastShadows { get; set; } = true;

    // Phase 5 扩展：三级 LOD 支持
    // public Model? Lod0Model { get; set; }  // 完整模型 (0-50m)
    // public Model? Lod1Model { get; set; }  // 简化模型 (50-150m)
    // public Model? Lod2Model { get; set; }  // Billboard (150-500m)
}
```

### FoliageLayer（实例集合）

```csharp
public class FoliageLayer : IDisposable
{
    private readonly FoliageType foliageType;  // 原 prefab 字段
    private readonly List<FoliageInstance> instances = new();
    private readonly Dictionary<(int, int), List<int>> spatialGrid = new();

    private Buffer? instanceBuffer;
    private bool isDirty = true;

    private const float CellSize = 64.0f; // 空间网格单元大小

    public FoliageType FoliageType => foliageType;  // 原 Prefab 属性
    public IReadOnlyList<FoliageInstance> Instances => instances;
    public int InstanceCount => instances.Count;

    // 添加实例（检查最小距离）
    public bool TryAddInstance(Vector3 worldPosition, float scale, float rotation);

    // 移除半径内的实例
    public int RemoveInstancesInRadius(Vector3 center, float radius);

    // 更新 GPU 缓冲区
    public void UpdateGpuBuffer(GraphicsDevice device, CommandList commandList);
}
```

### FoliageTypeManager

> **命名说明：** 原 `FoliagePrefabManager` 已重命名为 `FoliageTypeManager`，与 `FoliageType` 命名保持一致。Phase 5 扩展时将在此基类上添加 LOD 管理。

```csharp
public sealed class FoliageTypeManager : IDisposable
{
    public static FoliageTypeManager Instance { get; }

    private readonly List<FoliageType> types = new();  // 原 prefabs
    private readonly List<FoliageLayer> layers = new();
    private readonly Dictionary<int, FoliageLayer> layerByTypeId = new();  // 原 layerByPrefabId

    public IReadOnlyList<FoliageType> Types => types;  // 原 Prefabs
    public IReadOnlyList<FoliageLayer> Layers => layers;
    public int SelectedTypeIndex { get; set; } = 0;    // 原 SelectedPrefabIndex
    public FoliageType? SelectedType => types.Count > SelectedTypeIndex ? types[SelectedTypeIndex] : null;

    // 类型管理
    public FoliageType AddType(string name, Model model, string? modelPath = null);  // 原 AddPrefab
    public void RemoveType(int index);  // 原 RemovePrefab

    // 实例操作
    public int PlaceInstances(Vector3 center, float radius, FoliageType? type = null);
    public int RemoveInstances(Vector3 center, float radius);

    // 地形同步
    public void UpdateInstanceHeights(TerrainManager terrainManager);
    public void SyncToGpu(CommandList commandList);
}
```

## 绘制工具

### IFoliageTool 接口

```csharp
public interface IFoliageTool
{
    string Name { get; }
    void Apply(ref FoliageEditContext context);
}

public readonly struct FoliageEditContext
{
    public FoliageTypeManager TypeManager;    // 原 PrefabManager
    public TerrainManager TerrainManager;
    public Vector3 BrushCenter;
    public float BrushRadius;
    public float BrushInnerRadius;
    public float FrameTime;
}
```

### FoliagePlaceTool

```csharp
internal sealed class FoliagePlaceTool : IFoliageTool
{
    public string Name => "Place";

    public void Apply(ref FoliageEditContext context)
    {
        var selectedType = context.TypeManager.SelectedType;  // 原 PrefabManager.SelectedPrefab
        if (selectedType == null) return;

        // 获取地形高度
        float? height = context.TerrainManager.GetHeightAtPosition(
            context.BrushCenter.X, context.BrushCenter.Z);
        if (height == null) return;

        var placeCenter = new Vector3(context.BrushCenter.X, height.Value, context.BrushCenter.Z);

        // 放置实例
        context.TypeManager.PlaceInstances(placeCenter, context.BrushRadius, selectedType);
    }
}
```

### FoliageRemoveTool

```csharp
internal sealed class FoliageRemoveTool : IFoliageTool
{
    public string Name => "Remove";

    public void Apply(ref FoliageEditContext context)
    {
        float? height = context.TerrainManager.GetHeightAtPosition(
            context.BrushCenter.X, context.BrushCenter.Z);
        if (height == null) return;

        var removeCenter = new Vector3(context.BrushCenter.X, height.Value, context.BrushCenter.Z);

        context.TypeManager.RemoveInstances(removeCenter, context.BrushRadius);
    }
}
```

### FoliageEditor

```csharp
public sealed class FoliageEditor
{
    public static FoliageEditor Instance { get; }

    private IFoliageTool? currentTool;
    private bool isStrokeActive;

    public void BeginStroke(string toolName);
    public void ApplyStroke(Vector3 worldPosition,
        FoliageTypeManager typeManager,
        TerrainManager terrainManager,
        float frameTime);
    public void EndStroke();
}
```

## 渲染集成

### FoliageRenderObject

```csharp
public sealed class FoliageRenderObject : RenderMesh
{
    public FoliageLayer? Layer { get; private set; }
    public Model? Model { get; private set; }
    public Buffer? InstanceBuffer { get; private set; }
    public int InstanceCount { get; set; }

    public void Initialize(FoliageLayer layer, Model model);
    public void UpdateInstanceBuffer(CommandList commandList, Buffer buffer, int instanceCount);
    public void UpdateBounds();
}
```

### FoliageRenderFeature

```csharp
public sealed class FoliageRenderFeature : RootEffectRenderFeature
{
    public override Type SupportedRenderObjectType => typeof(FoliageRenderObject);

    protected override void InitializeCore();
    public override void Prepare(RenderDrawContext context);
    protected override void ProcessPipelineState(...);
    public override void Draw(RenderDrawContext context, RenderView renderView, ...);
}
```

### MaterialFoliageInstancing.sdsl

```hlsl
shader MaterialFoliageInstancing : IMaterialSurface, Transformation
{
    struct FoliageInstance
    {
        float3 Position;
        float RotationY;      // 与 C# 结构统一
        float Scale;
        uint TypeIndex;       // 原 PrefabIndex
        uint RandomSeed;      // Phase 5 启用
        float MinDistance;    // Phase 5 启用
    };

    rgroup PerMaterial
    {
        stage StructuredBuffer<FoliageInstance> InstanceBuffer;
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

        float3x3 rotation = RotationMatrixY(instance.Rotation);
        float3 scaledPos = streams.Position * instance.Scale;
        float3 rotatedPos = mul(rotation, scaledPos);

        streams.Position = float4(rotatedPos + instance.Position, 1.0);
        streams.Normal = mul(rotation, streams.Normal);
    }
}
```

## 空间索引

使用网格划分实现高效的空间查询：

```
CellSize = 64.0 世界单位

spatialGrid: Dictionary<(int cellX, int cellZ), List<int instanceIndices>>

GetCellCoord(position):
    return (floor(position.X / CellSize), floor(position.Z / CellSize))

CheckMinDistance(position, cell):
    检查 cell 及其 8 个邻居
    对于每个邻居中的实例：
        if distance(instance.position, position) < MinDistance:
            return false
    return true
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Services/FoliageInstance.cs` | 实例数据结构（与 Phase 5 统一） |
| `Services/FoliageType.cs` | 植被类型定义（原 FoliagePrefab） |
| `Services/FoliageLayer.cs` | 实例集合与空间索引 |
| `Services/FoliageTypeManager.cs` | 类型/层管理单例（原 FoliagePrefabManager） |
| `Services/IFoliageTool.cs` | 工具接口 |
| `Services/FoliagePlaceTool.cs` | 放置工具 |
| `Services/FoliageRemoveTool.cs` | 移除工具 |
| `Services/FoliageEditor.cs` | 编辑编排器 |
| `Rendering/FoliageRenderObject.cs` | GPU 渲染对象 |
| `Rendering/FoliageRenderFeature.cs` | 自定义渲染特性 |
| `Effects/MaterialFoliageInstancing.sdsl` | 实例化着色器 |

### 修改文件

| 文件路径 | 修改内容 |
|---------|---------|
| `UI/Panels/SceneViewPanel.cs` | 添加植被编辑循环 |
| `UI/Panels/AssetsPanel.cs` | 连接 FoliageItem 到管理器 |
| `UI/Panels/ToolsPanel.cs` | 添加植被工具选择 |
| `Services/EditorState.cs` | 添加植被工具状态 |
| `EditorGame.cs` | 初始化植被系统 |

## 实现步骤

### Phase 3.1: 核心数据结构
1. 创建 `FoliageInstance.cs`（与 Phase 5 统一的结构）
2. 创建 `FoliageType.cs`（原 FoliagePrefab）
3. 创建 `FoliageLayer.cs` 及空间网格
4. 创建 `FoliageTypeManager.cs`（原 FoliagePrefabManager）

### Phase 3.2: 工具实现
1. 创建 `IFoliageTool.cs` 接口
2. 创建 `FoliagePlaceTool.cs`
3. 创建 `FoliageRemoveTool.cs`
4. 创建 `FoliageEditor.cs`
5. 扩展 `EditorState.cs`

### Phase 3.3: UI 集成
1. 修改 `AssetsPanel.cs` 连接管理器
2. 修改 `ToolsPanel.cs` 添加工具选择
3. 修改 `SceneViewPanel.cs` 添加编辑循环

### Phase 3.4: 渲染
1. 创建 `FoliageRenderObject.cs`
2. 创建 `FoliageRenderFeature.cs`
3. 创建 `MaterialFoliageInstancing.sdsl`
4. 在 `EditorGame.cs` 初始化

### Phase 3.5: 完善
1. 添加笔刷预览
2. 添加撤销/重做支持
3. 添加序列化
4. 性能优化

## 性能考虑

- **空间索引：** 网格划分实现 O(1) 最小距离检查
- **GPU Instancing：** 单次 draw call 渲染所有同类型实例
- **延迟同步：** 仅在笔刷结束时更新 GPU 缓冲区
- **剔除：** 使用边界盒进行视锥剔除

## 验证方案

1. **植被放置：** 选择预制体，使用 Place 工具放置，验证实例渲染
2. **植被移除：** 使用 Remove 工具移除实例
3. **地形对齐：** 修改地形高度后，验证植被实例跟随地形
4. **性能测试：** 放置 10000+ 实例，验证帧率稳定
