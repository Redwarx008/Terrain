# Type Safety

> Type safety patterns in this project.

---

## Overview

本项目使用 C# 的强类型系统和 nullable 引用类型注解确保类型安全。

---

## Nullable Reference Types

每个文件顶部启用 `#nullable enable`：

```csharp
#nullable enable

public class MyClass
{
    public string? OptionalString { get; set; }
    public string RequiredString { get; set; } = "";
}
```

---

## Event Args Types

定义明确的事件参数类型：

```csharp
public class ToolSelectedEventArgs : EventArgs
{
    public ToolItem Tool { get; set; } = null!;
}
```

注意：`= null!` 用于构造函数字段初始化，避免 nullable 警告。

---

## Enum Types

使用强类型枚举表示有限选项：

```csharp
public enum EditorMode
{
    Sculpt,
    Paint,
    Foliage,
    Climate
}

public enum HeightTool
{
    Raise,
    Lower,
    Smooth,
    Flatten
}

public enum PaintTool
{
    Paint,
    Erase
}
```

---

## Interface Patterns

定义服务接口：

```csharp
public interface ICommand
{
    void Execute();
    void Undo();
}

public interface IHeightTool
{
    void ApplyStroke(Vector3 worldPosition, TerrainManager manager);
    ToolProperties GetProperties();
}
```

---

## Struct Types

用于值语义的小型数据结构：

```csharp
public struct TerrainConfig : IEquatable<TerrainConfig>
{
    public string? TerrainDataPath;
    public int MaxVisibleChunkInstances;
    public int MaxResidentChunks;
}
```

---

## Type Organization

| 类型 | 位置 | 示例 |
|------|------|------|
| 枚举 | 功能文件附近 | `ToolsPanel.cs` 内 `EditorMode` |
| 事件参数 | 紧跟使用类 | `ToolsPanel.cs` 内 `ToolSelectedEventArgs` |
| 接口 | `Services/` | `ICommand.cs` |
| 配置结构 | 核心组件 | `TerrainComponent.cs` 内 `TerrainConfig` |

---

## Anti-patterns

1. **不要**使用 `object` 传递类型化数据
2. **不要**忽略 nullable 警告
3. **不要**使用 `dynamic`
4. **不要**使用裸数组替代强类型集合