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

## Common Mistake: HasSelectedTool 与后端实现脱节

**Symptom**: 工具栏显示某工具已激活（`HasSelectedTool = true`），但选择该工具后视口无任何笔刷响应。

**Cause**: `EditorMode` 枚举新增了模式值（如 Foliage），ViewModel 在模式切换时无条件设置 `HasSelectedTool = true`，但 `EmbeddedStrideViewportGame` 中尚无对应笔刷后端。

**Fix**: 在模式切换逻辑中加守卫，仅对有后端实现的模式设置 `HasSelectedTool = true`：

```csharp
HasSelectedTool = mode != EditorMode.Foliage; // Foliage 无笔刷后端
```

**Prevention**: 新增 `EditorMode` 枚举值时，同步确认 `EmbeddedStrideViewportGame.GetBrushForMode` 是否返回有效笔刷；若没有，工具可用性必须返回 `false`。

---

## Common Mistake: 模式专属 Overlay 状态跨模式泄漏

**Symptom**: 某个面板只在特定模式下可见，例如 `Biome` 模式下的 biome mask overlay，但切到别的模式后渲染上的 tinted overlay 仍然存在，而且 UI 已经没有控制项可关闭。

**Cause**: 只改了 ViewModel / XAML 的 `IsVisible`，却没有让渲染参数或服务逻辑同时受 `CurrentEditorMode` 约束，导致隐藏的全局状态继续生效。

**Fix**: 模式专属状态如果会影响 viewport 渲染，提交到 shader / processor / service 前必须和 `CurrentEditorMode` 一起判定，例如：

```csharp
bool showMaskOverlay = EditorState.Instance.CurrentEditorMode == EditorMode.Paint
    && EditorState.Instance.ShowMaskOverlay;
```

**Prevention**: 任何“只在某模式显示”的面板开关，都要检查其后端效果是否也在离开该模式时自动失效；不要只做 UI 隐藏。

---

## Anti-patterns

1. **不要**为每个面板创建全局状态
2. **不要**在面板中存储场景级状态
3. **不要**使用静态字段替代单例服务
4. **不要**在无后端实现的工具模式上设置 `HasSelectedTool = true`
