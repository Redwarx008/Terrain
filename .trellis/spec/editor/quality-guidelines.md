# Quality Guidelines

> Code quality standards for UI development.

---

## Overview

UI 代码遵循 C#、Avalonia、MVVM 和 Simple 主题约定。旧 ImGui 约定仅适用于迁移前遗留代码，不得用于新增 UI。

---

## Code Style

### 文件结构

```csharp
#nullable enable

using System;
using System.Collections.Generic;
using Terrain.Editor.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Terrain.Editor.ViewModels;

public sealed partial class MyPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _selectedTool;

    [RelayCommand]
    private void SelectTool(string tool)
    {
        SelectedTool = tool;
    }
}
```

### 字段命名

- 私有字段: `_camelCase`
- 属性: `PascalCase`

### 访问修饰符

- 公共 API: `public`
- 内部使用: `internal`
- 私有: `private`

---

## Avalonia Layout Rules

### 使用布局表达结构

优先使用 `Grid`、`DockPanel`、`StackPanel`、`TabControl`、`ItemsControl` 等布局容器表达界面结构。

```xml
<Grid RowDefinitions="Auto,*" ColumnDefinitions="Auto,*,Auto">
  <ToolBar Grid.Row="0" Grid.ColumnSpan="3" />
  <ContentControl Grid.Row="1" Grid.Column="1" />
</Grid>
```

### 使用 Margin/Padding/Alignment 定位

使用 `Margin`、`Padding`、`HorizontalAlignment`、`VerticalAlignment`、`DockPanel.Dock`、`Grid.Row`、`Grid.Column`、`ColumnSpan`、`RowSpan` 完成布局内定位。

```xml
<Button
  Padding="10,4"
  HorizontalAlignment="Right"
  VerticalAlignment="Center"
  Content="Export" />
```

### 禁止手算像素位置

不要通过窗口宽高、控件宽高或鼠标坐标手算普通 UI 控件位置。只有共享纹理视口尺寸同步、命中测试、图形资源大小等技术边界可以进行必要的像素计算。

```csharp
// Bad: 普通面板布局不应手算位置
button.Margin = new Thickness(windowWidth - 140, 0, 0, 0);

// Good: 交给布局系统
button.HorizontalAlignment = HorizontalAlignment.Right;
```

### 使用 Auto/*/Span

优先使用 `Auto`、`*`、`MinWidth`、`MaxWidth`、`MinHeight`、`MaxHeight`、`ColumnSpan`、`RowSpan` 和对齐属性表达自适应布局。

## Dispose Pattern

ViewModel 或控件订阅服务事件时必须释放订阅：

```csharp
public void Dispose()
{
    EditorState.Instance.ToolChanged -= OnEditorToolChanged;
}
```

---

## Event Subscription

在构造函数中订阅事件，在 Dispose 中取消订阅：

```csharp
public MyPanelViewModel()
{
    EditorState.Instance.SomeEvent += OnHandler;
}

public void Dispose()
{
    EditorState.Instance.SomeEvent -= OnHandler;
}
```

---

## Code Review Checklist

- [ ] `#nullable enable` 在文件顶部
- [ ] 所有公共 API 有可空性标注
- [ ] 字段使用 `_camelCase` 前缀
- [ ] 有 Dispose 方法用于事件取消订阅
- [ ] 新 UI 使用 Avalonia Simple 主题
- [ ] XAML 优先使用布局容器、Margin、Padding、Alignment、Auto、Star、Span
- [ ] 普通 UI 无手算像素定位
- [ ] 颜色值使用 Avalonia 资源

---

## Anti-patterns

1. **不要**新增 ImGui 代码
2. **不要**忽略事件订阅的取消
3. **不要**使用 Canvas/绝对坐标实现普通应用布局
4. **不要**在 code-behind 中堆业务逻辑，使用 ViewModel 命令和绑定
5. **不要**使用硬编码颜色
