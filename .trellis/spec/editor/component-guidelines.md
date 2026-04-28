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

## 导出命令接线模式

导出命令通过 `ExportManager` 单例调用历史 exporter，不直接构造 exporter 实例：

```csharp
[RelayCommand]
private async Task ExportTerrainAsync()
{
    var path = await _dialogService.SaveFileDialog(...);
    if (path == null) return;
    await ExportManager.Instance.ExecuteAsync("Terrain", path);
}
```

关键约束：
- 使用 `ExportManager.Instance.ExecuteAsync(exporterName, path)` 而非直接 `new TerrainExporter().Export(...)`。
- 导出器名称字符串（`"Terrain"`、`"Material Descriptor"`）对应 `ExportManager` 注册时使用的键。
- 异步导出必须用 `async Task` 命令方法，不要用 `async void`。

---

## Anti-patterns

1. **不要**新增 ImGui 调用
2. **不要**创建每帧新的 GC 对象
3. **不要**在 Dispose 中忘记取消事件订阅
4. **不要**用 Canvas/绝对坐标实现普通布局
5. **不要**在 code-behind 中写业务逻辑

---

## Common Mistake: Avalonia Classes 属性不能绑定

**Symptom**: XAML 编译报错 `AVLN3000: Unable to find suitable setter for property Classes`

**Cause**: `Classes` 是 `IList<string>` 集合属性，Avalonia XAML 不支持将 Binding 表达式直接赋值给集合属性（即使是转换为字符串的 Converter 也不行）。

**Fix**: 用以下方法之一切换 CSS 类：
- Code-behind 中用 `button.Classes.Remove("assetTab"); button.Classes.Add("assetTabActive");`（纯 UI 逻辑，不违反 MVVM）
- 用 `RadioButton` 互斥组实现单选标签
- 用 `DataTrigger` 样式（Avalonia 11+ 支持）

**Prevention**: 任何需要根据 ViewModel 状态切换 CSS 类的场景，优先考虑 code-behind UI 处理或 DataTrigger。

---

## Common Mistake: DataTemplate 内的 ContextMenu 编译错误

**Symptom**: XAML 编译报错 `AVLN2000: Unable to resolve suitable regular or attached property ContextMenu on type Border`

**Cause**: 在 DataTemplate 内部对 `Border` 等非 Control 元素使用 `ContextMenu` 附加属性时，XAML 编译器可能无法解析。

**Fix**: 将 ContextMenu 移到 ListBox 的 `ContextMenu` 属性上，或改用 Button 等支持 ContextMenu 的 Control 作为容器。

---

## 资源类型图标映射

项目使用 Segoe MDL2 Assets 图标字体显示资源类型图标。映射表：

| Kind | Glyph | Unicode | 含义 |
|------|-------|---------|------|
| Texture | Photo | `\xE71B` | 纹理/图片 |
| Mesh | Cube | `\xE80A` | 3D 模型 |
| Tree/Shrub/Grass | MapleLeaf | `\xEC7A` | 植被 |
| Prefab | Package | `\xE7B8` | 预制体 |
| Create | Add | `\xE710` | 添加资源 |

参考: https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-icons-family

---

## ViewModel 包装后端单例服务的同步模式

当 ViewModel 包装后端单例服务（如 `ClimateRuleService`）时，需要处理三层状态的双向同步：

```
后端服务 (单例) ←→ EditorState (桥梁) ←→ ViewModel (Avalonia 绑定)
```

### 循环更新防护

ViewModel 属性变更 → 写入后端服务 → 触发 `StateChanged` → `RefreshCollections` → 再次写入 ViewModel 属性。必须用 `_syncing` 标志打断循环：

```csharp
private bool _syncing;

partial void OnMinAltitudeChanged(float value)
{
    if (_syncing) return;
    ClimateRuleService.Instance.SetRuleAltitude(_globalIndex, value, null);
}

public void SyncFromSource(int globalIndex)
{
    _syncing = true;
    MinAltitude = _source.MinAltitude;
    _syncing = false;
}
```

### 增量集合同步

后端集合变更时，使用索引对比进行增量同步，避免 Clear+AddAll 导致 UI 闪烁和选中状态丢失：

```csharp
private void RefreshCollections()
{
    var sourceClimates = _service.Climates;
    for (int i = 0; i < sourceClimates.Count; i++)
    {
        if (i < Climates.Count && Climates[i].Id == sourceClimates[i].Id)
            Climates[i].SyncFromSource();       // 原地更新
        else if (i < Climates.Count)
            Climates[i] = new(...);             // 替换
        else
            Climates.Add(new(...));              // 追加
    }
    while (Climates.Count > sourceClimates.Count)
        Climates.RemoveAt(Climates.Count - 1);  // 裁剪尾部
}
```

### EditorState 桥梁模式

当多个 ViewModel 需要共享同一状态时，`EditorState` 作为桥梁：
- ViewModel 写入 `EditorState` → 触发事件 → 其他 ViewModel 响应
- 后端服务变更 → `EditorState` 事件 → ViewModel 同步

```csharp
// ViewModel A → EditorState → ViewModel B
partial void OnSelectedClimateChanged(ClimateDefinitionViewModel? value)
{
    if (value != null)
        _editorState.CurrentClimateId = value.Id;
}
```

---

## 资产面板数据源模式

资产面板的分类标签保持固定结构（Textures / Meshes / Foliage / Prefabs），各分类的数据来源独立：

| 分类 | 数据源 | 状态 |
|------|--------|------|
| Textures | `MaterialSlotManager.GetActiveSlots()` | 真实数据 |
| Meshes | 无（仅显示 "+" 瓷贴） | 待实现 |
| Foliage | 无（仅显示 "+" 瓷贴） | 待实现 |
| Prefabs | 无（仅显示 "+" 瓷贴） | 待实现 |

**关键约束**：
- 当 `MaterialSlotManager.SlotsChanged` 事件触发时，如果当前选中 Textures 分类，必须刷新 `AssetItems`
- 每个分类末尾必须有 "+" 瓷贴（`IsCreateItem = true`），点击触发对应的导入命令
- 右键菜单的 Delete 命令必须用 `CanExecute` 控制可用性，不可删除的项应禁用菜单项

### ViewModel 中颜色值的处理

ViewModel 中需要传入预览颜色的场景（如 `AssetBrowserItemViewModel.PreviewBackground`），颜色值必须从常量类读取，禁止在方法体中硬编码十六进制字符串：

```csharp
// 正确：从常量类读取
new AssetBrowserItemViewModel(name, category, "Texture",
    AssetColors.TexturePreviewBackground,
    AssetColors.TexturePreviewForeground,
    "\xE71B", materialSlotIndex: slot.Index)

// 错误：硬编码颜色
new AssetBrowserItemViewModel(name, category, "Texture",
    "#3A3A3A", "#E0E0E0", "\xE71B")
```

常量类 `AssetColors` 集中管理所有预览颜色值，便于统一修改和与主题同步。

---

## Code-behind 中处理特殊 UI 选择逻辑

当 UI 需要在选中项上执行"选中后立即取消选中+派发命令"的逻辑时（如点击 "+" 瓷贴），纯 XAML 绑定无法实现（ViewModel 无法取消 ListBox 的选中状态）。此时在 code-behind 中处理是可接受的 MVVM 妥协：

```csharp
// MVVM note: selection deselection is UI logic (ViewModel can't deselect a ListBoxItem).
// The command dispatch is acceptable here because it's a UI-triggered action
// with no clean pure-binding alternative.
private void OnAssetSelectionChanged(object? sender, SelectionChangedEventArgs e)
{
    if (sender is not ListBox listBox || listBox.SelectedItem is not AssetBrowserItemViewModel item)
        return;

    if (item.IsCreateItem && DataContext is EditorShellViewModel vm)
    {
        listBox.SelectedItem = null;          // UI 逻辑：取消选中
        vm.AddAssetForCategoryCommand.Execute(item.Category);  // 命令派发
    }
}
```

**约束**：code-behind 中只能做 UI 状态操作（取消选中、CSS 类切换）和命令派发，不得包含业务逻辑。
