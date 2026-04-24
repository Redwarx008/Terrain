# State Management

> How state is managed in this project.

---

## Overview

编辑器状态使用分层管理：

- **EditorState** - 全局编辑器状态（工具选择、模式切换）
- **PanelBase** - 面板内部状态
- **Service** - 功能性服务状态

---

## EditorState (全局状态)

单例服务管理全局状态：

```csharp
public sealed class EditorState
{
    public static EditorState Instance => InstanceFactory.Value;

    // 编辑器模式
    public EditorMode CurrentMode { get; set; }
    public EditorMode CurrentTool { get; set; }

    // 工具状态
    public bool HasSelectedTool { get; set; }
    public HeightTool CurrentHeightTool { get; set; }
    public PaintTool CurrentPaintTool { get; set; }

    // 事件
    public event EventHandler? ToolChanged;
}
```

---

## Panel State (面板状态)

每个面板管理自己的内部状态：

```csharp
public class ToolsPanel : PanelBase
{
    public EditorMode CurrentMode { get; set; }
    public string? SelectedTool { get; set; }
    public List<ToolItem> Tools { get; } = new();
}
```

---

## Service State (服务状态)

服务管理功能相关状态：

```csharp
public sealed class BrushParameters
{
    private static readonly Lazy<BrushParameters> InstanceFactory = new(() => new());

    public static BrushParameters Instance => InstanceFactory.Value;

    public float Size { get; set; } = 10.0f;
    public float Strength { get; set; } = 1.0f;
    public float EffectiveFalloff { get; private set; }
}
```

---

## State Categories

| 类别 | 管理方式 | 示例 |
|------|----------|------|
| 全局状态 | 单例 EditorState | 当前工具、编辑器模式 |
| 面板状态 | 面板实例字段 | 选中项、展开状态 |
| 服务状态 | 功能单例 | 笔刷参数、撤销栈 |
| 场景状态 | SceneSystem | 场景、实体 |

---

## When to Use Global State

仅在以下情况使用全局状态：
- 多个面板/服务需要访问
- 生命周期与应用相同
- 不需要多个实例

---

## Anti-patterns

1. **不要**为每个面板创建全局状态
2. **不要**在面板中存储场景级状态
3. **不要**使用静态字段替代单例服务