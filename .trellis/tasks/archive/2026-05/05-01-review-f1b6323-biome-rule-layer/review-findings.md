# Review Findings: commit f1b6323

**审查对象**: `f1b6323` — `feat: migrate climate paint flow to biome rule layers`
**审查日期**: 2026-05-01
**变更规模**: 32 files, +2429 / -1544

---

## HIGH — 具体缺陷

### H1: Noise 修改器功能回归 — FBM 被替换为单八度 Noise2D，Octaves 参数无效

**文件**: [EditorTerrainBuildSplatMap.sdsl:207-211](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L207-L211)

旧代码使用 `Fbm()` 分形布朗运动，支持 `modifier.Octaves` 参数控制噪声层级。新代码替换为单八度 `Noise2D()`，`Octaves` 字段虽然在 `ModifierGpu` 结构体中上传到 GPU（[EditorTerrainEntity.cs:629](Terrain.Editor/Rendering/EditorTerrainEntity.cs#L629)），但 shader 中从未读取。

**影响**: 用户在 UI 中调节 Octaves 滑块无任何视觉变化；Noise 修改器从多八度分形噪声退化为单层值噪声，视觉质量下降。

**触发条件**: 任何使用 Noise 类型修改器的 biome layer。

---

### H2: Modifier Opacity 在新 Biome UI 中失去可编辑入口，所有修改器默认锁死为 100%

**文件**:
- [MainWindow.axaml:755-758](Terrain.Editor/Views/MainWindow.axaml#L755-L758) — 列表中只显示只读 `OpacityPercent`
- [MainWindow.axaml:792-893](Terrain.Editor/Views/MainWindow.axaml#L792-L893) — Modifier Settings 面板没有任何 `Opacity` 绑定控件
- [EditorTerrainBuildSplatMap.sdsl:388](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L388) — shader 实际使用 `modifier.Opacity`
- [TomlProjectConfig.cs:170](Terrain.Editor/Services/TomlProjectConfig.cs#L170) / [TomlProjectConfig.cs:283](Terrain.Editor/Services/TomlProjectConfig.cs#L283) — TOML 仍然读写 `opacity`

旧 climate/rule 工作流至少存在一个占位性的 layer opacity 面板；迁移到 biome modifier stack 后，`Opacity` 仍然在数据模型、序列化和 GPU 求值链路中生效，但 UI 已经没有任何可写入口。现在用户只能看到只读百分比，无法修改新建或已加载 modifier 的 opacity。

**影响**: 所有新建 modifier 都固定为默认 `Opacity = 1.0`；已有项目里从 TOML 读到的非 1.0 opacity 也无法在 UI 中继续调整。这不是单纯 UX 退化，而是作者工作流失去一个实际参与混合计算的控制量。

**触发条件**: 任意 biome layer 中尝试调整 modifier 强度时。

---

### H3: LayerHeatmap 调试图使用 `HeatmapLayerIndex`（全局层索引）作为权重分量选择器 (0-3)

**文件**: [EditorTerrainDiffuse.sdsl:311-324](Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl#L311-L324)

Debug case 4 将 `HeatmapLayerIndex`（= `EditorState.SelectedRuleIndex`，可以是 0-9+）与权重分量索引 0-3 比对。当层索引 > 3 时，总是显示 `weights.x`（第一个材质权重），热图不反映选中层的实际分布。

此外，WeightMap 存储的是每个 texel 的 top-4 材质权重，不是按层的权重。当前实现不显示选中层的权重——要正确实现需要 BuildSplatMap 生成专用的热图纹理。

**影响**: 选中层索引 > 3 时热图始终错误；即使 ≤3 也语义不匹配。

**触发条件**: 选择第 5 个及以上的层并切换到 LayerHeatmap 调试图。

---

### H4: TextureMask 修改器对外暴露，但实际没有完成资源绑定和 UI 配置链路

**文件**:
- [EditorTerrainBuildSplatMap.sdsl:73-76](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L73-L76) — 全局 `TextureMaskResource`
- [EditorTerrainEntity.cs:95](Terrain.Editor/Rendering/EditorTerrainEntity.cs#L95) — 只有单个 `TextureMaskResource` 属性
- [EditorTerrainSplatMapComputeDispatcher.cs:96-103](Terrain.Editor/Rendering/EditorTerrainSplatMapComputeDispatcher.cs#L96-L103) — 仅在非 null 时绑定，且注释明确写着 placeholder
- [ModifierViewModel.cs:93-102](Terrain.Editor/ViewModels/ModifierViewModel.cs#L93-L102) — TextureMask 没有路径/通道可见属性
- [MainWindow.axaml:801-893](Terrain.Editor/Views/MainWindow.axaml#L801-L893) — Modifier Settings 没有 `texture_mask` / `texture_mask_channel` 编辑控件

`TextureMaskResource` 从未在提交中被赋值，compute path 只有一个全局纹理槽位，无法做到逐 modifier 绑定。与此同时，`BiomeModifier.TextureMaskPath` / `TextureMaskChannel` 虽然会被 TOML 读写，但 ViewModel 和 XAML 没有任何可配置入口。shader 和 dispatcher 注释都明确承认这是 placeholder。

**影响**: UI 明确允许用户添加 `Texture mask` modifier，但当前实现既不能在编辑器内配置纹理来源，也不能可靠地把纹理送到 GPU，结果是该 modifier 对普通用户完全不可用，属于“暴露了不可工作的功能”。

---

## MEDIUM — 行为变化 / 残余风险

### M1: 层迭代方向 + 权重递减模型 — 与参考实现一致（正确修正）

**文件**: [EditorTerrainBuildSplatMap.sdsl:326-383](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L326-L383)

新 shader 反向迭代层（`LayerCount-1 → 0`），后层优先消耗权重预算。与参考实现 `ProceduralTerrainPainter` 的 `ProcessLayers` 语义一致（反向迭代 + 权重递减）。

旧 shader 的 `PushTop4` 按权重值选 top-4，不遵守层优先级——这是 bug 而非特性。新 shader 修正了此问题。

**注意事项**: 已有多层项目的视觉结果会变化（层优先级语义修正），用户可能需要重新调整层参数。

**触发条件**: 所有使用多层且有重叠的 biome 项目。

---

### M2: 修改器迭代方向与参考实现相反

**文件**: [EditorTerrainBuildSplatMap.sdsl:343-350](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L343-L350)（shader 正向迭代）
**参考**: [ModifierStack.cs:95](E:/UnityProjects/My project (1)/Assets/ProceduralTerrainPainter/Runtime/ModifierStack.cs#L95)（参考反向迭代）

| | 参考实现 | 新 shader |
|---|---|---|
| 修改器迭代 | **反向** (`Count-1 → 0`) | **正向** (`0 → Count-1`) |
| 列表第一项 | 最后执行，拥有最终决定权 | 最先执行，是基底 |
| 列表最后一项 | 最先执行 | 最后执行，拥有最终决定权 |

对于 Multiply 修改器（交换律），顺序不影响结果。对于 Add/Subtract（非交换律），结果不同：

- 参考中 `[Height(Mul), Noise(Add)]` → Height 最后执行，乘以 Noise+base
- 新 shader 中 `[Height(Mul), Noise(Add)]` → Height 先执行，Noise 叠加在结果上

这不是有意设计，而是正向迭代作为默认写法未对照参考。应改为反向迭代以与参考一致。

**触发条件**: 同一层内使用混合 BlendMode（特别是 Add/Subtract）时。

---

### M3: `ResolveMaterialIndex` 是死代码

**文件**: [TerrainManager.cs:483](Terrain.Editor/Services/TerrainManager.cs#L483)

`ResolveMaterialIndex` 是 `private static` 方法，当前无任何调用点。材质索引解析现在完全由 GPU shader 完成，C# 端的该方法是迁移遗留。

**影响**: 无功能影响，增加维护负担。

---

## LOW — 次要问题

### L1: 调试图 5/6 仍使用 `LoadIndexMapAtGlobal`/`LoadWeightMapAtGlobal`（点采样）

**文件**: [EditorTerrainDiffuse.sdsl:331-347](Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl#L331-L347)

主渲染路径已改用 splat 空间双线性插值（`AccumulateControlTexelSplat`），消除了马赛克伪影。调试图仍使用 heightmap 空间点采样，在 splatmap 边界处可能看到接缝。对调试视图可接受，但与主路径不一致。

---

### L2: `BiomeViewModel.RefreshCollections` 索引同步在 ID 不匹配时替换 ViewModel

**文件**: [BiomeViewModel.cs:337-363](Terrain.Editor/ViewModels/BiomeViewModel.cs#L337-L363)

当层被重排或删除时，sync 逻辑在 `Layers[i].Id != sourceLayers[i].Id` 时直接替换 ViewModel 实例，丢失交互状态（如滑块拖拽中状态）。

---

### L3: Noise 默认 Scale=0.05 与 heightmap 像素坐标产生极高频噪声

**文件**: [BiomeRuleService.cs](Terrain.Editor/Services/BiomeRuleService.cs) — `CreateDefaultModifier`

`Scale = 0.05f` 使 `heightCoord * 0.05` 每 20 个 heightmap 像素重复一次噪声。对于大尺度地形，这远比用户期望的 biome 级别变化频率高得多。可能需要调整为更小的 Scale 值（如 0.005）。

---

### L4: `HasInvert` 在 UI 中隐藏 TextureMask 的反转，但 shader 通用应用反转

**文件**: [ModifierViewModel.cs:111](Terrain.Editor/ViewModels/ModifierViewModel.cs#L111) vs [EditorTerrainBuildSplatMap.sdsl:373-374](Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl#L373-L374)

UI 中 TextureMask 类型的 `HasInvert = false`（隐藏反转开关），shader 中 `if (modifier.Invert > 0.5f)` 对所有类型生效。由于 TextureMask 的 Invert 默认 0.0 且 UI 不可修改，实践中不会触发，但逻辑不一致。

---

### L5: `AddModifierTypeIndex` 使用 ComboBox 索引映射枚举类型

**文件**: 
- [MainWindow.axaml:766](Terrain.Editor/Views/MainWindow.axaml#L766) — `SelectedIndex="{Binding ... AddModifierTypeIndex}"`
- [BiomeViewModel.cs:161](Terrain.Editor/ViewModels/BiomeViewModel.cs#L161) — `var type = (BiomeModifierType)AddModifierTypeIndex`

修改器类型下拉框使用 `SelectedIndex` 绑定到 `AddModifierTypeIndex`（int），然后通过 `(BiomeModifierType)AddModifierTypeIndex` 强转。ComboBox 中 Item 的顺序必须与 `BiomeModifierType` 枚举值严格一致，否则添加的修改器类型错误。如果后续有人重排 ComboBoxItem（如按字母排序），会静默破坏此映射。

### L6: Biome 主题刷子硬编码为浅色主题

**文件**: [EditorTheme.axaml:67-71](Terrain.Editor/Styles/EditorTheme.axaml#L67-L71)

新增的 5 个 biome 专用资源（`EditorBiomeCardBackgroundBrush`、`EditorBiomeCardBorderBrush`、`EditorBiomeBadgeBrush`、`EditorBiomeBadgeTextBrush`、`EditorBiomeThumbBrush`）全部硬编码为浅色调 hex 值（`#FAFAFA`、`#C0C0C0` 等）。其余编辑器资源使用 `DynamicResource` 链式引用系统主题令牌，这些新刷子不参与该链——未来若添加深色主题会出问题。

---

### L7: 材质预览卡退化 — 原 Image 预览被静态图标替代

**文件**: [MainWindow.axaml:686-696](Terrain.Editor/Views/MainWindow.axaml)（biome 材质预览卡）、[MainWindow.axaml:637](Terrain.Editor/Views/MainWindow.axaml#L637)（层缩略图）

旧 Inspector 的全局 MATERIAL 面板使用 `<Image Source="{Binding SelectedMaterialSlotPreviewImage}">` 显示实际贴图缩略图，新 Biome 面板的材质预览仅含 `<TextBlock Classes="assetPreviewGlyph" Text="&#xE71B;">`（静态图标）。用户在选中层的材质卡中无法看到贴图预览，这是一个 UX 回归。

旧代码中 `SelectedMaterialSlotPreviewImage` 绑定在 `EditorShellViewModel` 上；新的 biome 材质区域 DataContext 嵌套在 `Biome.SelectedLayer` 下，即使该属性仍存在也无法直接绑定。没有做跨层级的绑定桥接。

---

## 非缺陷确认

### N1: TOML 不兼容旧 `climates` / `climate_rules` 键是已确认的产品决策

`TomlProjectConfig` 现在只读写 `biomes` / `biome_layers` / `biome_modifiers`，旧 `climates` / `climate_rules` 读取路径和 `RestoreBiomeData` 回退分支都已移除。就代码行为而言，旧格式项目确实会丢数据；但本任务约束已明确说明“不要求 TOML 向后兼容”，因此这不是本次提交的缺陷，不能继续按 HIGH finding 计入。

## 已验证 — 无问题

| 检查项 | 结果 |
|--------|------|
| `#nullable enable` 文件头部 | 所有新文件 ✓ |
| 私有字段 `_camelCase` | ✓ |
| `sealed` 类 | ✓ |
| Dispose 取消事件订阅 | BiomeViewModel.Dispose ✓ |
| Shader key 类型与 SDSL 声明一致 | BiomeMaskTexture, TextureMaskResource, DebugViewMode 等 ✓ |
| GPU struct (BiomeGpu/LayerGpu/ModifierGpu) C# ↔ SDSL 布局匹配 | ✓ |
| HeightmapSliceBounds 改为 full-res（配合 GetIndexMapSliceBounds /2 转换） | 正确，修复了旧代码的潜在问题 ✓ |
| 旧 Climate/ClimateRule/ClimateMask 名称完全清除 | grep 无残余 ✓ |
| Diffuse 主路径 splat 空间插值修复马赛克 | 正确 ✓ |
| BiomeViewModel 事件订阅/取消对称 | ✓ |
| `EditorTerrainHeightParameters` — `LoadIndexMapAtGlobalSplat`/`LoadWeightMapAtGlobalSplat` 提取 | 重构正确，被 `EditorTerrainDiffuse.sdsl:163-164` 复用，避免了 splat 采样循环中的 `/2` 冗余计算 ✓ |
| `MainWindow.axaml` — 移除旧 "LAYER BLEND" 占位面板 | 旧 Opacity 滑块固定 0.75、"Use Height Blend" 复选框固定勾选，均未绑定数据——是无效占位 UI，移除正确 ✓ |
| `EditorTheme.axaml` — 新增 biome 样式覆盖完整性 | 所有 MainWindow.axaml 中引用的 biome CSS 类（`biomeLayerList`, `biomeModifierList`, `biomeCardSurface`, `biomeLayerThumb`, `biomeSlotBadge`, `biomeMiniButton`, `biomeMaterialPreviewCard`, `biomeMaterialSwatch`, `biomeSoftCombo`, `biomeBlendCombo`, `biomeModifierGrip`, `biomeIconCheck`, `biomeModifierNameBox`, `biomeModifierValueBox`）均在 EditorTheme.axaml 中有对应 Style ✓ |
| `EditorTerrainProcessor` — 新增调试参数设置 | +20 行在 `DrawCore` 中设置 `DebugViewMode` 和 `HeatmapLayerIndex`，与 EditorState 正确同步 ✓ |
| `EditorState` — 新增 `RuleSelectionChanged`/`BiomeSelectionChanged` 事件 | 在 `SelectedRuleIndex` setter 中触发，事件链正确 ✓ |
| `EmbeddedStrideViewportGame` — +37 行 viewport 配置调整 | 无功能回归 ✓ |
| `EditorShellViewModel` — `IsClimateVisible`→`IsBiomeVisible` 重命名 | 绑定路径在 MainWindow.axaml 中同步更新 ✓ |

---

## 残余风险

1. **M1 的视觉变更**：已有多层项目因层优先级语义修正，视觉结果会变（这是正确的修正）。
2. **M2 的修改器顺序**：应改为反向迭代以与参考实现一致，否则混合 BlendMode 的修改器结果与预期不符。
3. **H3 的热图**：当前不可用，但不会导致崩溃——只是显示错误数据。
4. **H4 的 TextureMask**：当前 UI 已暴露入口，但功能链路并未闭合，容易让后续实现者误判为“只差小修”。
