# 运行时日志规范

> Terrain 运行时和编辑器的日志初始化与使用规范

---

## Logger 初始化

每个类使用 Stride 的 `GlobalLogger` 静态初始化 Logger：

```csharp
private static readonly Logger Log = GlobalLogger.GetLogger("category");
```

---

## Logger 分类命名

| 分类 | 使用位置 | 说明 |
|------|----------|------|
| `"Quantum"` | TerrainProcessor, TerrainStreamingManager, TerrainRenderFeature | 运行时核心 |
| `"Terrain.Materials"` | RuntimeMaterialManager | 运行时材质系统 |
| `"Terrain.Editor"` | EditorTerrainProcessor, HeightmapLoader, TerrainManager | 编辑器主模块 |
| `"Terrain.Editor.MaterialSlots"` | MaterialSlotManager | 编辑器子模块 |

**命名规则**：
- 运行时核心使用历史分类名 `"Quantum"`
- 编辑器使用 `"Terrain.Editor"` 前缀
- 子模块用点号分隔：`"Terrain.Editor.{SubModule}"`

---

## 日志级别与使用场景

### `Log.Warning` — 非致命问题，功能降级

当某个功能无法完成但系统可以继续运行时使用：

```csharp
// Terrain/Core/TerrainProcessor.cs — 地形加载失败，跳过渲染
Log.Warning($"Terrain data could not be read: {exception.Message}");

// Terrain/Streaming/TerrainStreaming.cs — 单个页面读取失败，继续处理其他页面
Log.Warning($"Failed to read terrain page {request.Key}: {ex.Message}");

// Terrain/Streaming/TerrainStreaming.cs — 缺少配置路径
Log.Warning($"MaterialConfigPath not set, using default material.");
```

### `Log.Error` — 操作失败，阻止用户操作完成

当用户触发的操作完全失败时使用，通常伴随 error state 存储供 UI 显示：

```csharp
// Terrain.Editor/Services/TerrainManager.cs — 地形加载失败
Log.Error($"{lastLoadError}\n{ex.StackTrace}");
```

### `Log.Info` — 重要操作的确认信息

记录显著的状态变化，不在热路径中使用：

```csharp
// Terrain/Streaming/TerrainStreaming.cs — 线程正常退出
Log.Info("Terrain streaming thread exited.");

// Terrain.Editor/Services/TerrainManager.cs — 地形加载成功
Log.Info($"Loaded terrain: {info.Width}x{info.Height} as 1 logical terrain with {terrainEntity.Slices.Count} slice(s)");
```

---

## 反模式

- **不要在 Draw/Update 热路径中打日志** — 每帧调用会产生大量日志，严重影响性能
- **不要使用 `Console.WriteLine`** — 运行时中统一使用 Stride Logger
- **不要在运行时日志中包含完整异常堆栈** — 只记录 `ex.Message`，完整堆栈仅用于编辑器 `Log.Error`
- **不要使用 `System.Diagnostics.Debug.WriteLine`** — 仅作为临时调试手段，不应提交到版本控制
