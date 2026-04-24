# Component Guidelines

> How UI components are built in this project.

---

## Overview

本项目迁移目标使用 Avalonia 构建界面。组件称为 View、Panel View、Control 或 ViewModel，通过 XAML 布局、数据绑定和命令连接现有编辑器服务。

---

## Base Classes

### ViewModel

```csharp
#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Terrain.Editor.ViewModels;

public sealed partial class ToolsPanelViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private bool _isSculptMode;

    [RelayCommand]
    private void SelectSculptMode()
    {
        IsSculptMode = true;
    }

    public void Dispose()
    {
    }
}
```

---

## Panel Structure

```xml
<UserControl xmlns="https://github.com/avaloniaui">
  <Grid RowDefinitions="Auto,*" Margin="8">
    <TextBlock Grid.Row="0" Text="Tools" />
    <StackPanel Grid.Row="1" Spacing="6">
      <Button Content="Sculpt" Command="{Binding SelectSculptModeCommand}" />
    </StackPanel>
  </Grid>
</UserControl>
```

---

## Layout Conventions

- 使用 Simple 主题作为基础主题。
- 使用布局容器组织结构，禁止普通 UI 使用绝对坐标。
- 使用 `Margin` / `Padding` 控制间距。
- 使用 `HorizontalAlignment` / `VerticalAlignment` / `DockPanel.Dock` / Grid attached properties 控制定位。
- 使用 `Auto`、`*`、`ColumnSpan`、`RowSpan` 表达自适应布局。
- 只有视口共享纹理尺寸、渲染资源、命中测试等技术边界允许必要的像素计算。

## Event and Command Patterns

优先使用命令和绑定。只有跨服务通知或现有服务事件适配时使用事件，并在 `Dispose` 中取消订阅。

```csharp
[RelayCommand]
private void OpenHeightmap()
{
    // 调用对话框服务和 TerrainManager
}
```

---

## Styling

使用 Avalonia 资源和样式进行样式控制。不要在控件中散落硬编码颜色。

```xml
<SolidColorBrush x:Key="EditorPanelBackgroundBrush" Color="#202020" />
```

---

## Examples

`Views/Panels/*PanelView.axaml` - 面板视图实现

---

## Anti-patterns

1. **不要**新增 ImGui 调用
2. **不要**创建每帧新的 GC 对象
3. **不要**在 Dispose 中忘记取消事件订阅
4. **不要**用 Canvas/绝对坐标实现普通布局
5. **不要**在 code-behind 中写业务逻辑
