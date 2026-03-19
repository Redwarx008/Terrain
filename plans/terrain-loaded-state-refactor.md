# TerrainLoadedState 重构方案

## 当前问题

### 分散的 Loaded* 字段

[`TerrainComponent`](../Terrain/TerrainComponent.cs) 中存在多个分散的加载状态字段：

```csharp
[DataMemberIgnore]
internal Texture? LoadedHeightmapTexture;

[DataMemberIgnore]
internal int LoadedBaseChunkSize;

[DataMemberIgnore]
internal int LoadedMaxVisibleChunkInstances;

[DataMemberIgnore]
internal Texture? LoadedDiffuseTexture;
```

### 问题

1. **状态分散**：相关联的状态字段散落在各处，语义不清晰
2. **比较繁琐**：有效性检查需要逐个比较每个字段
3. **更新冗长**：状态更新需要逐个赋值，容易遗漏
4. **扩展困难**：添加新配置字段时需要修改多处代码

## 重构方案

### 新增 TerrainConfig 结构体

创建一个轻量级的配置结构体，用于捕获和比较触发重构建的配置参数：

```csharp
/// <summary>
/// 地形配置，用于检测需要触发重构建的配置变化。
/// </summary>
internal struct TerrainConfig : IEquatable<TerrainConfig>
{
    public Texture? HeightmapTexture;
    public int BaseChunkSize;
    public int MaxVisibleChunkInstances;

    public static TerrainConfig Capture(TerrainComponent component)
    {
        return new TerrainConfig
        {
            HeightmapTexture = component.HeightmapTexture,
            BaseChunkSize = component.BaseChunkSize,
            MaxVisibleChunkInstances = component.MaxVisibleChunkInstances
        };
    }

    public bool Equals(TerrainConfig other)
    {
        return ReferenceEquals(HeightmapTexture, other.HeightmapTexture)
            && BaseChunkSize == other.BaseChunkSize
            && MaxVisibleChunkInstances == other.MaxVisibleChunkInstances;
    }

    public override bool Equals(object? obj)
        => obj is TerrainConfig other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(HeightmapTexture, BaseChunkSize, MaxVisibleChunkInstances);

    public static bool operator ==(TerrainConfig left, TerrainConfig right)
        => left.Equals(right);

    public static bool operator !=(TerrainConfig left, TerrainConfig right)
        => !left.Equals(right);
}
```

### 重构后的 TerrainComponent

```csharp
public sealed class TerrainComponent : ActivableEntityComponent
{
    // ... 公共配置属性保持不变 ...

    // 运行时数据
    [DataMemberIgnore]
    internal Int4[] InstanceData = Array.Empty<Int4>();

    [DataMemberIgnore]
    internal int MaxLeafChunkCount;

    [DataMemberIgnore]
    internal int InstanceCapacity;

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

    // 加载状态快照
    [DataMemberIgnore]
    internal TerrainConfig LoadedConfig;

    [DataMemberIgnore]
    internal Texture? LoadedDiffuseTexture;

    [DataMemberIgnore]
    internal bool IsInitialized;

    [DataMemberIgnore]
    internal bool IsRegisteredWithVisibilityGroup;
}
```

### 重构后的 TerrainProcessor

#### 简化的有效性检查

```csharp
private static bool IsCurrentInitializationValid(TerrainComponent component, TerrainRenderObject renderObject)
{
    return component.IsInitialized
        && component.LoadedConfig == TerrainConfig.Capture(component)
        && IsGpuDataValid(renderObject);
}
```

#### 简化的状态更新

```csharp
private void ApplyLoadedTerrainData(GraphicsDevice graphicsDevice, TerrainComponent component, TerrainRenderObject renderObject, LoadedTerrainData loadedData)
{
    // ... 其他初始化代码 ...

    // 单行更新配置快照
    component.LoadedConfig = TerrainConfig.Capture(component);
    component.IsInitialized = true;
}
```

## 重构对比

| 方面 | 重构前 | 重构后 |
|------|--------|--------|
| 状态字段数量 | 3个分散字段 | 1个 TerrainConfig 结构体 |
| 有效性检查 | 4个条件比较 | 1个相等比较 |
| 状态更新 | 3个赋值语句 | 1个赋值语句 |
| 扩展新字段 | 修改3处代码 | 修改结构体定义 |

## 文件变更清单

1. **TerrainComponent.cs**
   - 移除 `LoadedHeightmapTexture`、`LoadedBaseChunkSize`、`LoadedMaxVisibleChunkInstances`
   - 新增 `TerrainConfig` 结构体
   - 新增 `LoadedConfig` 字段

2. **TerrainProcessor.cs**
   - 简化 `IsCurrentInitializationValid()` 方法
   - 简化 `ApplyLoadedTerrainData()` 方法中的状态更新

## 注意事项

- `LoadedDiffuseTexture` 保持独立，因为它用于材质状态追踪，不属于触发重构建的配置
- 结构体是值类型，赋值时会复制全部字段，但这里只有3个字段，开销可忽略
- `IEquatable<T>` 实现确保了高效的相等比较
