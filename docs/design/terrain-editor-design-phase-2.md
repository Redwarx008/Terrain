# 阶段二：材质绘制系统

## 概述

**目标：** 实现材质槽位绘制功能

**范围：**
- SplatMap 数据结构
- 材质绘制工具（Paint / Erase）
- 材质槽位管理（256 个槽位）
- 材质导入功能

## 架构设计

### 数据层次

```
MaterialSlotManager (槽位管理)
    |
    +-- MaterialSlot[256] (槽位配置)
    |
    +-- SplatMapData (权重存储)
         |
         +-- byte[] layers[64] (RGBA8 纹理层数组)
```

### 绘制流程

```
PaintEditor (编辑器)
    |
    +-- IPaintTool (工具接口)
    |    +-- PaintTool (增加权重)
    |    +-- EraseTool (减少权重)
    |
    +-- SplatMapData (数据操作)
    |
    +-- TerrainManager (地形同步)
```

## 核心数据结构

### SplatMap 数据格式

**层计算：**
- 256 材质槽 / 4 通道每纹理 = 64 SplatMap 层
- 每层是 RGBA8 纹理
- 每像素内存：64 层 * 4 字节 = 256 字节

**优化策略：** 稀疏层分配
- 默认分配 4 层（16 材质）
- 动态扩展层

### SplatMapData

```csharp
public sealed class SplatMapData
{
    public int Width { get; }
    public int Height { get; }

    // 层存储：每个 byte[] 是一个 RGBA8 纹理
    private readonly List<byte[]> layers;
    public int ActiveLayerCount { get; private set; }

    // 槽位到层/通道映射
    // Slot 0-3   -> Layer 0 (R, G, B, A)
    // Slot 4-7   -> Layer 1 (R, G, B, A)
    // ...

    public byte GetWeight(int x, int z, int materialSlot);
    public void SetWeight(int x, int z, int materialSlot, byte weight);
    public (byte r, byte g, byte b, byte a) GetLayerWeights(int x, int z, int layerIndex);

    public static (int layerIndex, int channelIndex) SlotToLayerChannel(int slot)
        => (slot / 4, slot % 4);
}
```

### MaterialSlot

```csharp
public sealed class MaterialSlot
{
    public int Index { get; init; }
    public string Name { get; set; } = "";
    public string? AlbedoTexturePath { get; set; }
    public string? NormalTexturePath { get; set; }
    public float TilingScale { get; set; } = 1.0f;
    public bool IsEmpty => string.IsNullOrEmpty(AlbedoTexturePath);

    // 运行时纹理引用
    internal Texture? AlbedoTexture;
    internal Texture? NormalTexture;
}
```

### MaterialSlotManager

```csharp
public sealed class MaterialSlotManager
{
    public static MaterialSlotManager Instance { get; }

    private readonly MaterialSlot[] slots = new MaterialSlot[256];
    public MaterialSlot this[int index] => slots[index];
    public int SelectedSlotIndex { get; set; } = 0;

    public bool ImportTexture(int slotIndex, string texturePath, GraphicsDevice device);
    public IEnumerable<MaterialSlot> GetActiveSlots();
    public void LoadTextures(GraphicsDevice device);
}
```

## 绘制工具

### IPaintTool 接口

```csharp
public interface IPaintTool
{
    string Name { get; }
    void Apply(ref PaintEditContext context);
}

public readonly struct PaintEditContext
{
    public SplatMapData SplatMap;
    public int DataWidth;
    public int DataHeight;
    public int CenterX;
    public int CenterZ;
    public float BrushRadius;
    public float BrushInnerRadius;
    public float Strength;
    public float FrameTime;
    public int SelectedMaterialSlot;
}
```

### PaintTool 算法

```
对于笔刷半径内的每个像素：
    falloff = 计算衰减(距离, 外半径, 内半径)
    delta = Strength * FrameTime * falloff * 255

    currentWeight = GetWeight(x, z, selectedSlot)
    newWeight = min(255, currentWeight + delta)

    // 归一化其他权重
    totalOtherWeights = 其他材质权重之和
    if totalOtherWeights > 0:
        reductionFactor = (1.0 - newWeight / 255) / (totalOtherWeights / 255)
        对于每个其他槽位：
            SetWeight(x, z, otherSlot, GetWeight(x, z, otherSlot) * reductionFactor)

    SetWeight(x, z, selectedSlot, newWeight)
```

### EraseTool 算法

```
对于笔刷半径内的每个像素：
    falloff = 计算衰减(距离, 外半径, 内半径)
    delta = Strength * FrameTime * falloff * 255

    currentWeight = GetWeight(x, z, selectedSlot)
    newWeight = max(0, currentWeight - delta)

    // 重新分配给其他材质
    weightDiff = currentWeight - newWeight
    totalOtherWeights = 其他非零权重之和
    if totalOtherWeights > 0:
        对于每个有权重的其他槽位：
            SetWeight(x, z, otherSlot, GetWeight(x, z, otherSlot) +
                      weightDiff * GetWeight(x, z, otherSlot) / totalOtherWeights)

    SetWeight(x, z, selectedSlot, newWeight)
```

### PaintEditor

```csharp
public sealed class PaintEditor
{
    public static PaintEditor Instance { get; }

    private IPaintTool? currentTool;
    private bool isStrokeActive;

    public void BeginStroke(string toolName, Vector3 worldPosition, TerrainManager terrainManager);
    public void ApplyStroke(Vector3 worldPosition, TerrainManager terrainManager, float frameTime);
    public void EndStroke();
}
```

## GPU 集成

### 着色器资源

```hlsl
// EditorTerrainSplatMap.sdsl
rgroup PerMaterial
{
    stage Texture2DArray<float4> SplatMapLayers;
    stage Texture2D MaterialAlbedoTextures[16];
    stage SamplerState TerrainMaterialSampler;
}

cbuffer PerMaterial
{
    stage int SplatMapLayerCount;
    stage float4 MaterialTilingScales[16];
}

float4 GetSplatWeights(float2 worldPos, int sliceIndex)
{
    float2 uv = worldPos / HeightmapDimensionsInSamples;
    return SplatMapLayers.Sample(TerrainMaterialSampler, float3(uv, 0));
}
```

### GPU 资源管理

扩展 `EditorTerrainEntity`：

```csharp
public Texture[]? SplatMapTextures { get; private set; }

public void InitializeSplatMaps(GraphicsDevice device, int layerCount = 4)
{
    SplatMapTextures = new Texture[layerCount];
    for (int i = 0; i < layerCount; i++)
    {
        SplatMapTextures[i] = Texture.New2D(
            device,
            HeightmapWidth,
            HeightmapHeight,
            PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.ShaderResource | TextureFlags.UnorderedAccess);
    }
}
```

## 项目文件集成

### MaterialSlotConfig

```csharp
public class MaterialSlotConfig
{
    public int SlotIndex { get; set; }
    public string? Name { get; set; }
    public string? AlbedoTexturePath { get; set; }
    public string? NormalTexturePath { get; set; }
    public float TilingScale { get; set; } = 1.0f;
}
```

### 项目 JSON 示例

```json
{
  "version": 1,
  "heightmap": { ... },
  "materialSlots": [
    { "slotIndex": 0, "name": "Grass", "albedoTexturePath": "textures/grass.png", "tilingScale": 4.0 },
    { "slotIndex": 1, "name": "Dirt", "albedoTexturePath": "textures/dirt.png", "tilingScale": 2.0 },
    { "slotIndex": 2, "name": "Rock", "albedoTexturePath": "textures/rock.png", "tilingScale": 8.0 }
  ],
  "splatMapPaths": [
    "splatmap_layer0.png",
    "splatmap_layer1.png"
  ]
}
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Services/SplatMapData.cs` | CPU 端权重存储 |
| `Services/MaterialSlot.cs` | 材质槽数据类 |
| `Services/MaterialSlotManager.cs` | 槽位管理单例 |
| `Services/PaintEditContext.cs` | 绘制上下文结构 |
| `Services/IPaintTool.cs` | 绘制工具接口 |
| `Services/PaintTool.cs` | 绘制工具实现 |
| `Services/EraseTool.cs` | 擦除工具实现 |
| `Services/PaintEditor.cs` | 绘制编辑器 |
| `Effects/EditorTerrainSplatMap.sdsl` | SplatMap 着色器 |

### 修改文件

| 文件路径 | 修改内容 |
|---------|---------|
| `Services/EditorState.cs` | 添加 PaintTool 枚举、材质选择 |
| `Services/TerrainManager.cs` | 添加 SplatMapData 引用、同步方法 |
| `UI/Panels/AssetsPanel.cs` | 连接 MaterialSlotManager |
| `UI/Panels/ToolsPanel.cs` | 添加 Paint/Erase 工具处理 |
| `UI/Panels/SceneViewPanel.cs` | 添加绘制编辑模式 |
| `Rendering/EditorTerrainEntity.cs` | 添加 SplatMap GPU 资源 |

## 实现步骤

### Phase 2.1: 核心数据结构
1. 创建 `SplatMapData.cs`
2. 创建 `MaterialSlot.cs` 和 `MaterialSlotManager.cs`
3. 创建 `PaintEditContext.cs` 和 `IPaintTool.cs`
4. 编写权重操作单元测试

### Phase 2.2: 绘制工具
1. 实现 `PaintTool.cs`
2. 实现 `EraseTool.cs`
3. 创建 `PaintEditor.cs`
4. 集成现有 `BrushParameters`

### Phase 2.3: UI 集成
1. 更新 `AssetsPanel.cs` 连接 MaterialSlotManager
2. 实现纹理导入
3. 添加材质槽配置 UI
4. 在 `ToolsPanel` 添加 Paint 模式工具选择

### Phase 2.4: GPU 集成
1. 创建 `EditorTerrainSplatMap.sdsl` 着色器
2. 扩展 `EditorTerrainEntity` 添加 SplatMap 纹理
3. 更新 `EditorTerrainProcessor` 绑定 SplatMap 资源

### Phase 2.5: 项目集成
1. 扩展 `TerrainProject.cs` 添加材质配置
2. 实现 SplatMap PNG 导出/导入
3. 测试完整工作流

## 性能考虑

- **CPU 绘制：** 实时编辑使用 CPU 计算，2048x2048 地形 16 材质仅需 16MB RAM
- **GPU 同步：** 仅在笔刷结束时同步到 GPU，而非每帧
- **稀疏层：** 仅在材质实际使用时分配层

## 验证方案

1. **材质绘制：** 选择材质槽，使用 Paint 工具绘制，验证视觉效果
2. **多材质混合：** 绘制多个材质，验证混合效果正确
3. **保存/加载：** 保存项目后重新加载，验证材质数据持久化
