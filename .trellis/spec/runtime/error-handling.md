# 运行时错误处理

> Terrain 运行时和编辑器的错误处理模式

---

## 核心原则

**运行时绝不崩溃游戏，编辑器绝不丢失用户数据。**

---

## 模式 1：Catch-and-Log + 优雅降级（运行时）

运行时中，所有 I/O 和初始化操作都包裹在 try-catch 中。失败时记录日志并禁用功能，绝不抛出异常到游戏循环。

```csharp
// Terrain/Core/TerrainProcessor.cs — TryLoadTerrainData
private bool TryLoadTerrainData(TerrainComponent component, out LoadedTerrainData loadedData)
{
    loadedData = default;
    try
    {
        string terrainDataPath = ResolveTerrainDataPath(component.TerrainDataPath!);
        ValidateTerrainDataPath(terrainDataPath);
        var fileReader = new TerrainFileReader(terrainDataPath);
        // ... 初始化逻辑 ...
        return true;
    }
    catch (Exception exception)
    {
        Log.Warning($"Terrain data could not be read: {exception.Message}");
        return false;
    }
}
```

失败后果：地形不渲染（`renderObject.Enabled = false`），游戏继续运行。

### 流式加载线程的错误处理

后台 I/O 线程同样采用 catch-and-continue 模式，单个页面加载失败不影响其他页面：

```csharp
// Terrain/Streaming/TerrainStreaming.cs — IoThreadMain
try
{
    foreach (var request in pendingRequests.GetConsumingEnumerable(cancellation.Token))
    {
        try
        {
            // 读取页面数据
            completedRequests.Enqueue(request);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to read terrain page {request.Key}: {ex.Message}");
            request.Data.Dispose();
            queuedKeys.TryRemove(request.Key, out _);
        }
    }
}
catch (OperationCanceledException)
{
    Log.Info("Terrain streaming thread exited.");
}
```

---

## 模式 2：Try-Catch + Error State（编辑器）

编辑器捕获异常后，将错误信息存储在 `lastLoadError` 字段中供 UI 显示，同时返回空/默认值：

```csharp
// Terrain.Editor/Services/TerrainManager.cs — LoadTerrainAsync
catch (Exception ex)
{
    lastLoadError = $"Failed to load terrain: {ex.Message}";
    Log.Error($"{lastLoadError}\n{ex.StackTrace}");
    return new List<EditorTerrainEntity>();
}
```

编辑器中错误信息需要同时满足两个目的：
1. **日志记录** — `Log.Error` 包含完整堆栈
2. **UI 展示** — `lastLoadError` 存储用户可读的错误描述

---

## 验证：BCL 异常

项目不定义自定义异常类型，统一使用 BCL 异常：

| 异常类型 | 使用场景 |
|----------|----------|
| `InvalidDataException` | 地形文件格式校验失败（最常见） |
| `FileNotFoundException` | 地形数据文件不存在 |
| `ArgumentOutOfRangeException` | 无效的 mip/page 坐标 |
| `EndOfStreamException` | 文件读取不完整 |
| `InvalidOperationException` | 无效的状态转换 |

---

## Debug.Assert：渲染不变量

在渲染热路径中，使用 `Debug.Assert` 而非异常来验证 GPU 资源状态。这些断言标记"在正确代码中绝不会为 false"的不变量：

```csharp
// Terrain/Core/TerrainProcessor.cs — ApplyLoadedTerrainData 之后
Debug.Assert(renderObject.HeightmapArray != null);
Debug.Assert(renderObject.ChunkNodeBuffer != null);

// Terrain/Streaming/TerrainStreaming.cs — GPU 资源验证
Debug.Assert(renderObject.HeightmapArray != null);
```

**规则**：仅在初始化完成后的渲染代码中使用 `Debug.Assert`，不在用户输入或文件 I/O 路径中使用。

---

## 反模式

- **不要在 Draw() 循环中抛异常** — 渲染是每帧调用，异常会彻底卡死
- **不要创建自定义异常层次** — BCL 异常已足够，避免过度设计
- **不要在运行时中让异常逃逸到游戏循环** — 总是 catch 并降级
- **不要忽略后台线程的异常** — 至少记录 Warning 日志
