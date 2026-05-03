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

### Scenario: Rule Service 变更必须显式触发 GPU Splat 重算

#### 1. Scope / Trigger
- Trigger: Inspector / ViewModel 直接修改 `BiomeRuleService`、`ClimateRuleService` 这类规则服务，且最终结果由 GPU splat / heatmap / render texture 消费。

#### 2. Signatures
- 服务层变更事件：`BiomeRuleService.StateChanged`
- 渲染重算入口：`TerrainManager.RegenerateMaterialIndices()`
- Terrain 侧脏标记：`EditorTerrainEntity.MarkBiomeRulesDirty()` + `MarkAllBiomeSplatDirty()`

#### 3. Contracts
- UI 写入规则服务后，只触发 `ViewModel.RefreshCollections()` 不足以让视口更新。
- 至少要有一条服务层到渲染层的显式链路：
  `RuleViewModel/ModifierViewModel -> BiomeRuleService.NotifyMutated() -> TerrainManager.RegenerateMaterialIndices() -> EditorTerrainEntity dirty flags -> BuildSplatMap compute`
- `RegenerateMaterialIndices()` 必须同时标记：
  - 规则 buffer 需要重传
  - splat / weight map 需要重算
  - 不能触发 CPU `MaterialIndexMap` 全图重建；Editor 预览真源是 `BiomeMask + GPU rule buffers`

#### 4. Validation & Error Matrix
- 仅订阅到 ViewModel，未订阅 TerrainManager -> Inspector 数值变化，但 viewport 完全不变
- 只标记 rules dirty，未标记 splat dirty -> GPU buffer 更新，但 compute shader 不重跑
- 只标记 splat dirty，未标记 rules dirty -> compute 继续使用旧的 layer/modifier buffer

#### 5. Good/Base/Bad Cases
- Good: 修改 layer2 的 modifier 后，不切换工具/不重载地形，材质混合立即变化
- Base: 项目刚加载、尚未有 terrain entity 时触发 `StateChanged`，允许无效果但不得抛异常
- Bad: UI 面板显示 layer2 已新增/已修改，但地形结果仍像只有 layer1 一样

#### 6. Tests Required
- 手工回归：新增第二个 biome layer，给它不同 material slot 和更窄的 height/slope 范围，确认 viewport 立即出现叠加效果
- 手工回归：只改 modifier 的 `BlendMode/Opacity/Min/Max`，确认不需要重新加载 terrain
- 断点/日志验证：`BiomeRuleService.StateChanged` 后必须命中 `TerrainManager.RegenerateMaterialIndices()`，并继续落到 `MarkBiomeRulesDirty()` / `MarkAllBiomeSplatDirty()`

#### 7. Wrong vs Correct
#### Wrong
```csharp
// 只有 UI 刷新，没有通知 terrain 重算
_service.StateChanged += (_, _) => RefreshCollections();
```

#### Correct
```csharp
// UI 和渲染侧都要消费同一个服务层事件
biomeRuleService.StateChanged += OnBiomeRuleStateChanged;

private void OnBiomeRuleStateChanged(object? sender, EventArgs e)
{
    RegenerateMaterialIndices();
}
```

### Scenario: HeightmapSliceBounds 在 compute / render 间必须保持全分辨率语义

#### 1. Scope / Trigger
- Trigger: 修改 `EditorTerrainHeightParameters` 的 slice bounds 绑定、`EditorTerrainSplatMapComputeDispatcher`、`EditorTerrainProcessor`，或任何依赖 `GetSliceBounds()` / `GetIndexMapSliceBounds()` 的 shader。

#### 2. Signatures
- CPU 侧参数键：`EditorTerrainHeightParametersKeys.HeightmapSliceBounds0..7`
- Render 侧绑定：`EditorTerrainProcessor.SetSliceBounds(...)`
- Compute 侧绑定：`EditorTerrainSplatMapComputeDispatcher.SetSliceBounds(...)`
- Shader 转换入口：`EditorTerrainHeightParameters.GetIndexMapSliceBounds(int sliceIndex)`

#### 3. Contracts
- `HeightmapSliceBounds*` 永远表示全分辨率 heightmap 空间：
  `startSampleX`, `startSampleZ`, `width`, `height`
- 任何 splatmap / biome mask 的半分辨率换算都必须在 shader 内通过 `GetIndexMapSliceBounds()` 完成。
- CPU 侧 dispatcher 不能把 `/2` 后的 splat bounds 塞进 `HeightmapSliceBounds*`，否则 shader 再调用 `GetIndexMapSliceBounds()` 时会发生二次缩放。

#### 4. Validation & Error Matrix
- CPU 传全分辨率 bounds，shader 内 `/2` 一次 -> 正常
- CPU 预先 `/2`，shader 再 `/2` -> slice 起点和尺寸缩成 1/4，出现 biome mask / splatmap 错位、条纹、跨区块拉伸
- Diffuse 路径和 Compute 路径使用不同 bounds 语义 -> 调试图与最终材质表现不一致

#### 5. Good/Base/Bad Cases
- Good: 同一张 biome mask 在单 slice 和多 slice 地形上都覆盖到正确世界位置
- Base: 地形宽高为奇数时，`GetIndexMapSliceBounds()` 负责处理 `(width + 1) / 2`，CPU 不额外补偿
- Bad: biome 分区在最终材质上表现为大面积斜切、重复条带，或边界明显按 slice 错位

#### 6. Tests Required
- 手工回归：加载多 slice 大地形，导入带清晰边界的 biome mask，确认最终材质分区与 mask 世界位置一致
- 手工回归：切换 `BiomeMaskMap` / 最终材质输出，确认两者边界位置一致
- 代码检查：`EditorTerrainProcessor` 与 `EditorTerrainSplatMapComputeDispatcher` 传入 `HeightmapSliceBounds*` 的语义必须一致

#### 7. Wrong vs Correct
#### Wrong
```csharp
// CPU 先转成 splat 空间，shader 又会再 /2 一次
return new Int4(slice.StartSampleX / 2, slice.StartSampleZ / 2, (slice.Width + 1) / 2, (slice.Height + 1) / 2);
```

#### Correct
```csharp
// HeightmapSliceBounds* 一律传全分辨率，half-res 换算留给 shader
return new Int4(slice.StartSampleX, slice.StartSampleZ, slice.Width, slice.Height);
```

### Scenario: 单 ID BiomeMask 不能用随机覆盖伪装软边缘

#### 1. Scope / Trigger
- Trigger: 编辑 `BiomeEditor`、导入 biome mask、或尝试在当前单通道 biome authoring 模型里实现“软刷子/羽化边缘”。

#### 2. Signatures
- 画笔写入入口：`BiomeEditor.ApplyStroke(...)`
- 蒙版存储：`BiomeMask.SetValue(int x, int y, byte biomeId)`
- 导入入口：`TerrainManager.LoadBiomeMask(...)`

#### 3. Contracts
- 当前 `BiomeMask` 每个 texel 只能存一个离散 biome ID，不支持多个 biome 同时占比。
- 在这个模型下，不能用 `Random`/概率覆盖去“模拟”软边缘；那会把分类边界变成椒盐噪声，最终在 splat 输出里表现为马赛克块。
- 真正的软边缘需要未来切到连续 biome weight/alpha 图；在此之前，画笔应使用确定性阈值写入，导入 full-res mask 时应做稳定降采样而不是随手抽样。

#### 4. Validation & Error Matrix
- 概率覆盖离散 ID -> 画笔边缘出现随机碎块，重复笔触结果不稳定
- full-res 导入直接取 `2x2` 左上角样本 -> 半分辨率 mask 出现锯齿和抖动
- 确定性阈值写入 + 多数投票降采样 -> 边界仍是硬边，但稳定、不闪、不噪

#### 5. Good/Base/Bad Cases
- Good: 同一笔刷参数重复涂抹，biome 边界稳定一致，不出现随机岛状碎片
- Base: 当前版本允许 biome 边界是硬过渡，只要最终输出不马赛克
- Bad: 软刷边缘看似过渡，实际由随机小块拼出来，缩放或重绘后形状还会变化

#### 6. Tests Required
- 手工回归：用带 falloff 的 biome 画笔连续涂抹，确认边缘不会出现随机椒盐块
- 手工回归：导入全分辨率 biome mask，确认半分辨率结果没有大量孤立单像素碎片
- 稳定性检查：同一位置重复笔触，输出应可重复，而不是每次随机不同

#### 7. Wrong vs Correct
#### Wrong
```csharp
if (Random.Shared.NextSingle() < strength)
    mask.SetValue(x, y, biomeId);
```

#### Correct
```csharp
if (strength >= 0.5f)
    mask.SetValue(x, y, biomeId);
```

### Scenario: 最终材质必须在 splat 空间插值控制图

#### 1. Scope / Trigger
- Trigger: 修改 `EditorTerrainDiffuse.sdsl`、`EditorTerrainHeightParameters.sdsl`，或任何读取 `IndexMap/WeightMap` 控制图并参与最终地表混合的 shader。

#### 2. Signatures
- 控制图读取：`LoadIndexMapAtGlobal(...)`、`LoadWeightMapAtGlobal(...)`
- splat 读取：`LoadIndexMapAtGlobalSplat(...)`、`LoadWeightMapAtGlobalSplat(...)`
- 最终混合入口：`EditorTerrainDiffuse.Compute()`

#### 3. Contracts
- `IndexMap/WeightMap` 存储在 splat 空间，即高度图的半分辨率。
- 最终材质如果要做平滑混合，必须先把 `sampleCoord` 转成 splat 坐标，再对相邻 splat texel 做插值。
- 不能先在全分辨率 heightmap 像素上做 4 邻域插值，再在读取 helper 里用 `/2` 折到 splat 像素；那样相邻 `2x2` 高度像素会落到同一控制 texel，直接产生块状马赛克。

#### 4. Validation & Error Matrix
- 先转 splat 空间再插值 -> 权重过渡平滑，和参考 alphamap 行为一致
- 先按 height 像素插值，再在 helper 里 `/2` -> 每个 `2x2` block 共享同一权重，最终材质出现大块离散边界
- 用 point load 直接读单个 splat texel -> 结果稳定但仍然是硬块，不符合最终地表混合预期

#### 5. Good/Base/Bad Cases
- Good: 单 biome 下仅由 layer/modifier 生成的材质边界也能连续过渡，不出现规则马赛克块
- Base: 即使 `BiomeMask` 全图都是同一个 biome，最终材质混合也应保持平滑
- Bad: 用户关闭/忽略 biome 逻辑后，场景仍然出现明显 `2x2` 或更大块状边界

#### 6. Tests Required
- 手工回归：全局单 biome、两个不同 layer/material，确认边界不是规则马赛克块
- 手工回归：关闭所有 biome 复杂性，只保留单 biome + modifier，确认仍然平滑
- 代码检查：最终混合路径中，控制图插值权重必须来自 splat 坐标 `sampleCoord * 0.5`

#### 7. Wrong vs Correct
#### Wrong
```csharp
float2 indexPixels = sampleCoord;
float2 index00Pixel = floor(indexPixels);
// helper 内部再 /2，导致 2x2 height 像素量化到同一 splat texel
```

#### Correct
```csharp
float2 splatPixels = sampleCoord * 0.5f;
float2 splat00Pixel = floor(splatPixels);
// 直接对相邻 splat texel 做插值
```

### Scenario: 噪声修改器必须使用多八度分形噪声

#### 1. Scope / Trigger
- Trigger: 修改 `EditorTerrainBuildSplatMap.sdsl` 中 `BiomeModifierType.Noise` 的实现，或新增任何类型的噪声修改器。

#### 2. Signatures
- Shader 声明：`Noise2D()` → 单八度值噪声（禁用）
- Shader 声明：`Fbm()` → 多八度分形布朗运动（正确选择）
- C# 上传：`ModifierGpu.Octaves`（`EditorTerrainEntity.cs` ~L629）→ 控制噪声八度数

#### 3. Contracts
- 噪声修改器必须使用 `Fbm()` 而非 `Noise2D()`。
- `Octaves` 参数必须被 shader 消费，与 UI 中的 `HasOctaves` 滑块联动。
- UI 中展示 Octaves 滑块（`ModifierViewModel.HasOctaves => _source.Type is BiomeModifierType.Noise`），shader 不使用该参数会造成静默的功能退化。

#### 4. Validation & Error Matrix
- 使用 `Fbm()` + 消费 `Octaves` → 调节滑块有视觉变化，视觉质量正常
- 使用 `Noise2D()` + 不消费 `Octaves` → 滑块无效，单八度质量下降

#### 5. Good/Base/Bad Cases
- Good: `Fbm(coord, modifier.Octaves)` — 多八度噪声，Octaves 滑块 1-8 均可调
- Bad: `Noise2D(coord)` — 单八度噪声，Octaves 字段上传但从未读取

#### 7. Wrong vs Correct
#### Wrong
```hlsl
// 单八度值噪声，不使用 Octaves
float noise = Noise2D(coord * modifier.Scale + modifier.Offset);
return noise * modifier.Opacity;
```

#### Correct
```hlsl
// 多八度分形噪声，消费 Octaves 参数
float noise = Fbm(coord * modifier.Scale + modifier.Offset, modifier.Octaves);
return noise * modifier.Opacity;
```

### Scenario: Shader 中修改器栈必须反向迭代

#### 1. Scope / Trigger
- Trigger: 修改 `EditorTerrainBuildSplatMap.sdsl` 中 modifier stack 的迭代循环。

#### 2. Signatures
- 参考实现：`ModifierStack.ProcessLayers`（倒数迭代 `Count-1 → 0`）
- Shader 迭代：`for (int idx = LayerCount - 1; idx >= 0; idx--)` → 正确
- Shader 迭代：`for (int idx = 0; idx < LayerCount; idx++)` → 错误

#### 3. Contracts
- 修改器栈必须反向迭代（列表最后一项最先执行，第一项拥有最终决定权）。
- 对于 Multiply（交换律），顺序不影响结果；对于 Add/Subtract（非交换律），顺序决定最终输出。

#### 4. Validation & Error Matrix
- 反向迭代 → 与参考输出一致
- 正向迭代 + 仅有 Multiply 修改器 → 碰巧一致（交换律），不易察觉
- 正向迭代 + 混合 Add/Subtract 修改器 → 结果与参考预期不符

#### 5. Good/Base/Bad Cases
- Good: `[Height(Mul), Noise(Add)]` 反向迭代 → Height 最后执行（最终决定权），效果为 NoisedBase × Height
- Bad: `[Height(Mul), Noise(Add)]` 正向迭代 → Noise 最后执行，效果为 (Base × Height) + Noise

#### 7. Wrong vs Correct
#### Wrong
```hlsl
for (int m = 0; m < ModifierCount; m++)  // 正向迭代
{
    float weight = ApplyModifier(modifiers[m], ...);
    baseWeight = BlendModifier(baseWeight, weight, modifiers[m].BlendMode);
}
```

#### Correct
```hlsl
for (int m = ModifierCount - 1; m >= 0; m--)  // 反向迭代，与参考一致
{
    float weight = ApplyModifier(modifiers[m], ...);
    baseWeight = BlendModifier(baseWeight, weight, modifiers[m].BlendMode);
}
```

### Scenario: Modifier 参数只要参与 GPU 求值，就必须保留可写 UI 链路

#### 1. Scope / Trigger
- Trigger: 修改 biome / climate modifier 的 Inspector UI、`ModifierViewModel`，或 shader/TOML 中新增、保留任何 modifier 参数字段。

#### 2. Signatures
- UI 绑定：`MainWindow.axaml` 中 `Modifier Settings` 面板
- ViewModel 属性：`ModifierViewModel.Opacity`
- GPU 求值：`EditorTerrainBuildSplatMap.sdsl` 中 `modifier.Opacity`
- 持久化：`TomlProjectConfig` 的 `opacity`

#### 3. Contracts
- 只要参数仍会被 shader 消费，且 TOML 仍会读写，该参数就不能只剩只读展示，必须在 Inspector 中保留可写入口。
- biome modifier 的 `Opacity` 必须保持完整链路：
  `Slider/TextBox -> ModifierViewModel.OnOpacityChanged -> BiomeRuleService.NotifyMutated() -> TerrainManager.RegenerateMaterialIndices() -> shader lerp(weight, blended, modifier.Opacity)`
- “列表里显示百分比、详情面板里没有输入控件” 视为功能回归，不是单纯 UX 退化。

#### 4. Validation & Error Matrix
- UI 可写 + ViewModel 通知服务层 -> 修改后 viewport 立即重算
- 只有只读 `OpacityPercent`，没有输入控件 -> 参数被锁死在默认值或旧存档值
- UI 改值但未触发 `NotifyMutated()` -> 面板变了，地形结果不变

#### 5. Good/Base/Bad Cases
- Good: 选中 Noise modifier，把 Opacity 从 `1.0` 拉到 `0.35`，地形混合强度立即变弱
- Base: 旧 TOML 中存了 `opacity = 0.5`，打开项目后仍能继续调，而不是只能看不能改
- Bad: shader 仍在用 `modifier.Opacity`，但新 UI 里完全没有可编辑入口

#### 6. Tests Required
- 手工回归：新建 modifier 后直接调 `Opacity`，确认 viewport 材质混合立即变化
- 手工回归：加载带非 1.0 `opacity` 的项目，确认 Inspector 读值正确且能继续编辑
- 代码检查：`ModifierViewModel.OnOpacityChanged()` 必须继续调用 `BiomeRuleService.NotifyMutated()`

#### 7. Wrong vs Correct
#### Wrong
```xml
<!-- 只显示只读百分比，没有可写控件 -->
<TextBox IsReadOnly="True" Text="{Binding OpacityPercent}" />
```

#### Correct
```xml
<Slider Minimum="0" Maximum="1" Value="{Binding Opacity}" />
<TextBox IsReadOnly="True"
         Text="{Binding OpacityPercent, StringFormat='{}{0}%'}" />
```

### Scenario: LayerHeatmap 复用 compute 输出时，必须在 debug 状态变化时重算

#### 1. Scope / Trigger
- Trigger: 修改 `LayerHeatmap` 调试模式、`TerrainManager` 对 `EditorState` 的订阅、或让 `BuildSplatMap` 在调试模式下重写 `WeightMap` / 其它 compute 输出。

#### 2. Signatures
- Debug 状态：`EditorState.CurrentDebugViewMode`
- Rule 选择：`EditorState.SelectedRuleIndex`
- Terrain 订阅：`TerrainManager.OnDebugViewModeChanged(...)`、`OnRuleSelectionChanged(...)`
- 重算入口：`EditorTerrainEntity.MarkAllBiomeSplatDirty()`

#### 3. Contracts
- 如果 `LayerHeatmap` 不是独立纹理，而是临时复用 splat compute 输出，那么“进入热图模式”“离开热图模式”“热图模式下切换选中 rule”都必须重新标脏并重跑 compute。
- 只在普通规则变更时重算是不够的，因为 debug 模式切换本身会改变 compute 输出语义。
- 退出 `LayerHeatmap` 时也要重算一次，把 `WeightMap` 恢复为正常 top-4 material 权重，而不是继续保留热图值。

#### 4. Validation & Error Matrix
- 进入热图模式时重算 -> 显示当前选中 layer 的最新热图
- 热图模式下切换选中 rule 时重算 -> 热图跟随选中项变化
- 退出热图模式时重算 -> 最终材质重新读取正常权重，不残留热图污染
- 不订阅 `DebugViewModeChanged` / `RuleSelectionChanged` -> 继续显示旧 layer 或错误的 weight 语义

#### 5. Good/Base/Bad Cases
- Good: 打开 `LayerHeatmap` 后切换第 2 层和第 5 层，热图立即切换到各自分布
- Base: 当前 UI 还没把 debug mode 暴露出来也没关系，但只要有人通过状态入口切换到 `LayerHeatmap`，结果必须正确
- Bad: 先看 layer A 的热图，再切到 layer B，屏幕仍显示 A；或者退出热图后最终材质明显错乱

#### 6. Tests Required
- 手工回归：进入 `LayerHeatmap`，切换不同 rule，确认每次都能看到不同热图
- 手工回归：从 `LayerHeatmap` 切回最终材质输出，确认材质结果恢复正常
- 代码检查：`TerrainManager` 必须订阅并在 `Dispose()` 中取消订阅 `DebugViewModeChanged` / `RuleSelectionChanged`

#### 7. Wrong vs Correct
#### Wrong
```csharp
// 只有规则数据变化才重算，debug 状态变化不会触发 compute
biomeRuleService.StateChanged += OnBiomeRuleStateChanged;
```

#### Correct
```csharp
EditorState.Instance.DebugViewModeChanged += OnDebugViewModeChanged;
EditorState.Instance.RuleSelectionChanged += OnRuleSelectionChanged;

private void OnRuleSelectionChanged(object? sender, EventArgs e)
{
    if (EditorState.Instance.CurrentDebugViewMode == SceneDebugViewMode.LayerHeatmap)
        RegenerateLayerHeatmapPreview();
}
```

### 过滤子集列表的选择桥接

当 Inspector 展示的是后端全量集合的一个过滤子集（例如“当前 Biome 下的 RuleLayer 列表”），而渲染/调试状态仍然使用全局索引（例如 `EditorState.SelectedRuleIndex`）时，ViewModel 必须同时维护：

- 全量集合的稳定引用（用于和服务层同步）
- 过滤后的可见集合（用于 UI 展示）
- 从 UI 选中项到全局索引的映射逻辑

推荐模式：

```csharp
public ObservableCollection<RuleViewModel> Layers { get; } = new();
public ObservableCollection<RuleViewModel> VisibleLayers { get; } = new();

partial void OnSelectedLayerChanged(RuleViewModel? value)
{
    int globalIndex = value != null ? Layers.IndexOf(value) : -1;
    if (_editorState.SelectedRuleIndex != globalIndex)
        _editorState.SelectedRuleIndex = globalIndex;
}

private void RefreshVisibleLayers()
{
    var source = Layers.Where(layer => layer.BiomeId == SelectedBiome?.Id).ToList();
    // 对 VisibleLayers 做增量同步，不要 Clear()+AddAll()
}
```

为什么要这样做：
- 直接把过滤列表的局部索引写回 `EditorState.SelectedRuleIndex` 会选错 heatmap/rule
- `Clear()+AddAll()` 会打断当前选中项和绑定状态，Inspector 会闪烁
- 选中项如果还会驱动别的单例服务（如 `MaterialSlotManager`），必须在 `SelectedLayer` 变化时一起同步

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
