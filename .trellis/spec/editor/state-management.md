# 编辑器状态管理

> Terrain Editor 的状态流、单例服务和撤销/重做系统

---

## 单例服务模式

编辑器服务使用 `Lazy<T>` 线程安全单例模式，通过 `Instance` 属性访问：

```csharp
// Terrain.Editor/Services/EditorState.cs
private static readonly Lazy<EditorState> _instance = new(() => new());
public static EditorState Instance => _instance.Value;
```

当前使用的单例服务：

| 服务 | 职责 |
|------|------|
| `EditorState` | 当前工具状态、气候选择、活跃季节（ActiveSeason） |
| `BrushParameters` | 画笔参数（大小、强度、衰减） |
| `HeightEditor` | 高度编辑逻辑（BeginStroke/ApplyStroke/EndStroke） |
| `PaintEditor` | 绘制编辑逻辑 |
| `ClimateEditor` | 气候蒙版笔刷（ApplyStroke 写入 ClimateMask） |
| `ClimateRuleService` | 气候定义和规则栈管理 |
| `HistoryManager` | 撤销/重做栈管理 |
| `MaterialSlotManager` | 256 材质槽 + GPU 数组 |
| `ProjectManager` | TOML 项目文件 I/O |

`ExportManager` 使用稍有不同——直接用 `static` 属性而非 `Lazy<T>`：

```csharp
// Terrain.Editor/Services/Export/ExportManager.cs
public static ExportManager Instance { get; } = new();
```

`EditorPreferences` 使用 `??=` 懒加载模式（因为需要从文件加载，无法使用无参 `Lazy<T>`）：

```csharp
// Terrain.Editor/Services/EditorPreferences.cs
private static EditorPreferences? instance;
public static EditorPreferences Instance => instance ??= Load();
```

`TerrainManager` **不是单例**——它由 `SceneViewPanel` 创建并拥有，因为需要 `GraphicsDevice` 和 `Scene` 参数，无法使用无参构造函数。

---

## 静态工具类

部分服务是纯静态工具类，不使用单例模式：

| 类 | 位置 | 说明 |
|----|------|------|
| `HeightmapLoader` | Services/ | 高度图加载/校验的静态方法 |
| `TextureImporter` | Services/ | 纹理导入的静态方法 |
| `TerrainRaycast` | Services/ | 地形射线检测的静态方法 |
| `TerrainSplitter` | Services/ | 地形分割的静态方法 |
| `PaintBrushCore` | Services/ | 画笔数学计算的静态方法 |

---

## 事件通信

服务使用 `EventHandler` / `EventHandler<T>` 通知状态变化，**不使用 `INotifyPropertyChanged`**。

### 向后兼容别名

`EditorState` 使用 add/remove 访问器将旧事件名转发到新事件：

```csharp
// Terrain.Editor/Services/EditorState.cs
public event EventHandler? ToolChanged
{
    add => HeightToolChanged += value;
    remove => HeightToolChanged -= value;
}
```

面板中通常订阅 `ToolChanged` 而非直接订阅 `HeightToolChanged`。

---

## 策略模式：编辑工具

画笔工具使用策略模式，通过接口多态分发编辑行为：

```csharp
// 工具接口
public interface IHeightTool { ... }
public interface IPaintTool { ... }

// 具体实现
public class PaintTool : IPaintTool { ... }
public class EraseTool : IPaintTool { ... }

// 上下文传递（ref readonly struct 避免分配）
public readonly ref struct PaintEditContext { ... }
```

---

## 导出注册表模式

`ExportManager` 维护 `Dictionary<string, IExporter>` 注册表，支持动态注册导出器：

```csharp
// Terrain.Editor/Services/Export/ExportManager.cs
private readonly Dictionary<string, IExporter> exporters = new();

public void RegisterExporter(string name, IExporter exporter)
    => exporters[name] = exporter;
```

当前注册的导出器：`TerrainExporter`（.terrain 运行时文件）、`MaterialDescriptorExporter`（材质描述 TOML）。

服务使用 `EventHandler` / `EventHandler<T>` 通知状态变化，**不使用 `INotifyPropertyChanged`**。

### 定义事件

```csharp
// Terrain.Editor/Services/EditorState.cs
public event EventHandler? HeightToolChanged;
public event EventHandler? PaintToolChanged;

// Terrain.Editor/Services/Commands/HistoryManager.cs
public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
```

### 订阅/取消订阅

UI 面板在构造函数中订阅，在 `Dispose` 中取消订阅：

```csharp
// Terrain.Editor/UI/Panels/ToolsPanel.cs
public ToolsPanel()
{
    // 订阅
    EditorState.Instance.HeightToolChanged += OnHeightToolChanged;
    EditorState.Instance.PaintToolChanged += OnPaintToolChanged;
}

public override void Dispose()
{
    // 取消订阅
    EditorState.Instance.HeightToolChanged -= OnHeightToolChanged;
    EditorState.Instance.PaintToolChanged -= OnPaintToolChanged;
    base.Dispose();
}
```

---

## 命令模式：撤销/重做系统

### 接口层次

```
ICommand                         -- 基础接口（Execute, Undo, EstimatedSizeBytes, AffectedChannel）
  └─ TerrainEditCommand          -- 抽象基类（分块状态捕获）
       ├─ HeightEditCommand      -- 高度编辑命令（HeightChunkDelta）
       └─ PaintEditCommand       -- 绘制编辑命令（PaintChunkDelta）
```

### 笔画生命周期

```
BeginCommand → MarkCommandChunks (每次 ApplyStroke) → CommitCommand / CancelCommand
```

**关键设计**：分块状态捕获——只在首次触碰某个 chunk 时捕获其 before-state，而非在笔画开始时快照整个地图：

```csharp
// Terrain.Editor/Services/Commands/TerrainEditCommand.cs
public void MarkAffectedArea(int x, int z, float radius)
{
    chunkTracker.MarkCircle(x, z, radius, GetDataWidth(), GetDataHeight(), CaptureBeforeChunk);
}
```

### 内存限制

```csharp
// Terrain.Editor/Services/Commands/HistoryManager.cs
private const int MaxCommandCount = 100;
private const long MaxMemoryBytes = 500 * 1024 * 1024; // 500 MB
```

超出限制时，从栈底移除最旧的命令。

---

## 数据脏标记系统

编辑器使用 `TerrainDataChannel` 枚举驱动统一的脏标记和 GPU 同步机制：

```csharp
// 数据通道枚举
enum TerrainDataChannel { Height, MaterialIndex }

// EditorTerrainEntity 上的标记/检查/同步
entity.MarkDataDirty(channel);
entity.IsDataDirty(channel);
entity.SyncDataToGpu(channel, commandList);
```

---

## 反模式

- **不要在 Update 循环中轮询服务状态** — 使用事件订阅，只在状态变化时响应
- **不要在服务外部直接修改服务内部状态** — 通过服务提供的公共方法操作
- **不要创建循环事件订阅** — A 监听 B，B 监听 A 会导致无限递归
- **不要忘记在 Dispose 中取消事件订阅** — 会导致内存泄漏和悬挂回调
