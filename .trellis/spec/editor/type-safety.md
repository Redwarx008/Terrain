# 类型安全与命名约定

> C# 类型系统、命名约定和序列化模式

---

## 命名约定

| 类别 | 约定 | 示例 |
|------|------|------|
| 类 | PascalCase | `TerrainComponent`, `EditorTerrainEntity` |
| 方法 | PascalCase | `LoadTerrainAsync`, `MarkDataDirty` |
| 属性 | PascalCase | `HeightScale`, `TerrainDataPath` |
| 布尔属性 | `Is` 前缀 | `IsInitialized`, `IsVisible`, `IsCollapsed` |
| 私有字段 | camelCase（无前缀） | `heightDataCache`, `terrainEntities` |
| static readonly | PascalCase | `Log`, `ExtractKey`, `MaxCommandCount` |
| 异步方法 | `Async` 后缀 | `ExportAsync`, `LoadTerrainAsync`, `ProcessAsync` |
| Try-模式 | `Try` 前缀 | `TryLoadTerrainData`, `TryGetResidentSlice`, `TryAllocateSlot` |
| 枚举 | PascalCase，单数类型名 | `HeightTool`, `PaintTool`, `ControlState` |
| 接口 | `I` 前缀 | `ICommand`, `IHeightTool`, `IPaintTool`, `IExporter` |

---

## 文件命名

- **一个类一个文件**，文件名与类名完全匹配
- 着色器 `.sdsl` 文件名与着色器类名匹配：`MaterialTerrainDiffuse.sdsl`
- 生成的 `.sdsl.cs` Key 文件与 `.sdsl` 并列
- 嵌套类型放在外部类的文件中（如 `TerrainRenderFeature.cs` 内的 `TerrainSharedShadowMapRendererProxy`）

---

## Nullable 引用类型

所有文件顶部使用 `#nullable enable`：

```csharp
#nullable enable
```

- 可为 null 的引用类型用 `?`：`string? TerrainDataPath`
- Stride 框架互操作中使用 `null!` 或 `null!!`：`public VisibilityGroup VisibilityGroup { get; set; } = null!;`
- 运行时内部字段使用 `?` 表示可选资源：`internal TerrainQuadTree? QuadTree;`

---

## 序列化模式

### Stride DataContract（实体组件）

用于场景序列化，使用 `[DataMember(N)]` 显式排序，序号间留有间隔以便未来插入：

```csharp
// Terrain/Core/TerrainComponent.cs
[DataContract("TerrainComponent")]
public sealed class TerrainComponent : ActivableEntityComponent
{
    [DataMember(10)]
    public string? TerrainDataPath { get; set; }

    [DataMember(15)]
    public string? MaterialConfigPath { get; set; }

    [DataMember(20)]
    public float HeightScale { get; set; } = 24.0f;

    [DataMember(40)]
    public float MaxScreenSpaceErrorPixels { get; set; } = 8.0f;

    [DataMemberIgnore]
    internal TerrainChunkNode[] ChunkNodeData = Array.Empty<TerrainChunkNode>();
}
```

- `[DataMemberIgnore]` 标记运行时内部字段，不参与序列化
- 序号间隔：10, 15, 20, 40, 45...（方便在 20 和 40 之间插入 25, 30 等）

### TOML 配置（项目文件）

使用 Tommy 库进行读写，路径存储为相对路径（正斜杠）：

```csharp
// Terrain.Editor/Services/TomlProjectConfig.cs
// 路径在写入时转为相对路径，读取时转回绝对路径
```

### 二进制 .terrain 格式

使用 `[StructLayout(LayoutKind.Sequential)]` 的结构体定义文件头：

```csharp
// Terrain/Streaming/TerrainStreaming.cs
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainFileHeader
{
    public int Magic;
    public int Version;
    // ...
}
```

通过 `BinaryReader` / `BinaryWriter` 进行读写。

---

## Record 类型

轻量不可变数据使用 `readonly record struct`：

```csharp
// Terrain/Core/TerrainProcessor.cs
readonly record struct LoadedTerrainData(
    string TerrainDataPath,
    TerrainFileReader FileReader,
    int Width, int Height,
    float MinHeight, float MaxHeight,
    int MaxLod, int BaseChunkSize,
    // ...
);
```

---

## 泛型约束

图像处理使用 SixLabors.ImageSharp 的像素约束：

```csharp
where TPixel : unmanaged, IPixel<TPixel>
```

GPU 结构体读取使用非托管约束：

```csharp
where T : unmanaged
```

---

## 反模式

- **不要使用 `dynamic`** — 使用泛型或接口
- **不要使用 `object` 作为通用容器** — 使用具体类型或泛型
- **不要在 `#nullable enable` 文件中用 `!` 抑制警告** — 除非是 Stride 框架互操作必须
- **不要用 `new()` 创建泛型实例** — 使用工厂方法或 `Activator.CreateInstance`
