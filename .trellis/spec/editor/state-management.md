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

当前使用的 7 个单例服务：

| 服务 | 职责 |
|------|------|
| `EditorState` | 当前工具选择状态 |
| `BrushParameters` | 画笔参数（大小、强度、衰减） |
| `HeightEditor` | 高度编辑逻辑（BeginStroke/ApplyStroke/EndStroke） |
| `PaintEditor` | 绘制编辑逻辑 |
| `HistoryManager` | 撤销/重做栈管理 |
| `MaterialSlotManager` | 256 材质槽 + GPU 数组 |
| `ProjectManager` | TOML 项目文件 I/O |

`ExportManager` 使用稍有不同——直接用 `static` 属性而非 `Lazy<T>`：

```csharp
// Terrain.Editor/Services/Export/ExportManager.cs
public static ExportManager Instance { get; } = new();
```

---

## 事件通信

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
