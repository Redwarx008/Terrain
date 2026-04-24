# Hook Guidelines

> How stateful logic patterns are used in this project.

---

## Overview

这是一个 C# 项目，没有 React Hooks。状态逻辑通过以下方式组织：

- **单例模式** - 全局状态服务
- **事件模式** - 组件间通信
- **命令模式** - 可撤销操作

---

## State Service (单例状态服务)

全局编辑器状态使用单例模式：

```csharp
public sealed class EditorState
{
    private static readonly Lazy<EditorState> InstanceFactory = new(() => new());

    public static EditorState Instance => InstanceFactory.Value;

    private EditorState() { }

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;
    public bool HasSelectedTool { get; set; }
    public HeightTool CurrentHeightTool { get; set; }
    public PaintTool CurrentPaintTool { get; set; }

    public event EventHandler? ToolChanged;
}
```

使用示例：

```csharp
// 订阅事件
EditorState.Instance.ToolChanged += OnToolChanged;

// 访问状态
if (EditorState.Instance.HasSelectedTool)
{
    var tool = EditorState.Instance.CurrentHeightTool;
}

// 触发更新
EditorState.Instance.ToolChanged?.Invoke(this, EventArgs.Empty);
```

---

## Service Patterns (服务模式)

功能性服务使用单例：

```csharp
public sealed class ClimateEditor
{
    private static readonly Lazy<ClimateEditor> InstanceFactory = new(() => new());

    public static ClimateEditor Instance => InstanceFactory.Value;

    private ClimateEditor() { }

    public void ApplyStroke(...) { /* ... */ }
}
```

---

## Command Pattern (命令模式)

可撤销操作使用命令模式：

```csharp
public interface ICommand
{
    void Execute();
    void Undo();
    void Redo();
}

public sealed class TerrainEditCommand : ICommand
{
    public void Execute() { /* ... */ }
    public void Undo() { /* ... */ }
    public void Redo() => Execute();
}
```

---

## Event Patterns (事件模式)

组件间通信使用事件：

```csharp
public event EventHandler<ToolSelectedEventArgs>? ToolSelected;
public event EventHandler? ToolDeselected;
public event EventHandler<string>? HeightmapLoaded;

// 触发
HeightmapLoaded?.Invoke(this, terrainPath);
```

---

## Naming Conventions

| 类型 | 命名规则 | 示例 |
|------|----------|------|
| 状态类 | `EditorState`, `BrushParameters` | 单例访问 |
| 事件参数 | `*EventArgs` | `ToolSelectedEventArgs` |
| 命令接口 | `ICommand` | `TerrainEditCommand` |
| 服务类 | `*Editor`, `*Service`, `*Manager` | `ClimateEditor`, `ProjectManager` |

---

## Examples

[ClimateEditor.cs](Terrain.Editor/Services/ClimateEditor.cs) - 服务单例模式

[EditorState (隐式)](Terrain.Editor/UI/Panels/ToolsPanel.cs) - 在 ToolsPanel 中使用状态服务

---

## Anti-patterns

1. **不要**创建大量静态字段替代单例
2. **不要**使用跨线程不安全的静态状态
3. **不要**在构造函数外初始化单例