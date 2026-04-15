# 运行时质量规范

> Terrain 运行时的构建验证、代码质量和资源管理标准

---

## 构建验证

```bash
dotnet build -c Debug
```

- 提交前必须通过 Debug 构建
- Terrain.Editor 输出到 `Bin/Editor/$(Configuration)/`
- Terrain.Windows 输出到 `Bin/Windows/$(Configuration)/`

---

## 目标框架

| 项目 | 框架 | 原因 |
|------|------|------|
| Terrain | net10.0-windows | Stride 引擎要求 |
| Terrain.Editor | net10.0-windows (win-x64) | 独立编辑器 |
| Terrain.Windows | net10.0-windows (win-x64) | 运行时启动器 |

---

## 中央包管理

所有 NuGet 包版本在 `Directory.Packages.props` 中集中管理：

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
```

**规则**：不要在 `.csproj` 中硬编码包版本号。新增包时，先在 `Directory.Packages.props` 添加版本，再在 `.csproj` 中引用。

关键依赖版本：
- Stride 4.3.0.1
- Hexa.NET.ImGui 2.2.9
- SixLabors.ImageSharp 3.1.12
- Tommy 3.1.2

---

## Nullable 引用类型

所有文件使用 `#nullable enable`：

```csharp
#nullable enable
```

- `null!` 仅用于 Stride 框架要求的非 null 属性初始化
- GPU 资源等可选字段使用 `?` 声明
- 不要用 `!` 操作符抑制编译器警告（框架互操作除外）

---

## Struct Layout

GPU 交互的结构体必须使用 `[StructLayout(LayoutKind.Sequential)]`：

```csharp
// Terrain/Streaming/TerrainStreaming.cs
[StructLayout(LayoutKind.Sequential)]
internal struct TerrainChunkNode
{
    public Int4 NodeInfo;
    public Int4 StreamInfo;
    public Vector4 SplatInfo;
}
```

- GPU 缓冲区结构体：必须 `Sequential` 布局
- 文件格式结构体：`Sequential` + `Pack = 4`（如 `TerrainFileHeader`）
- 纯 CPU 逻辑结构体：无需显式 Layout

---

## IDisposable 模式

拥有 GPU 资源或文件句柄的类必须实现 `IDisposable`：

```csharp
// Terrain/Streaming/TerrainStreaming.cs — TerrainStreamingManager.Dispose
public void Dispose()
{
    cancellation.Cancel();
    pendingRequests.CompleteAdding();
    ioThread.Join();
    DrainRequests(pendingRequests);
    DrainRequests(completedRequests);
    pendingRequests.Dispose();
    cancellation.Dispose();
    heightmapBufferPool.Dispose();
    splatMapBufferPool?.Dispose();       // 可选资源用 ?.
    gpuHeightArray.Dispose();
    gpuSplatMapArray?.Dispose();
    fileReader.Dispose();
}
```

**规则**：
- 按创建的反序释放资源
- 可选资源用 `?.Dispose()`
- 在 Dispose 中取消 CancellationToken 并等待后台线程退出

---

## 反模式

- **不要泄漏 GPU 资源** — Texture、Buffer、CommandList 等必须 Dispose
- **不要在渲染热路径中分配** — Draw() / Update() 中避免 `new`、LINQ、闭包分配
- **不要在 Draw() 中使用 async** — 渲染循环必须是同步的
- **不要忽略 `Debug.Assert` 失败** — 渲染断言标记真正的 bug
