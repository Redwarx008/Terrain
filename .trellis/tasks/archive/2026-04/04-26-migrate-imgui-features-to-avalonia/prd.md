# IMGUI → Avalonia 功能对接

## Goal

将 IMGUI 时代已有的编辑器功能逐项对接到 Avalonia UI 层，确保后端服务（ClimateEditor、PaintEditor、HeightEditor、ExportManager 等）能够被用户通过 Avalonia 界面实际操作，而非仅作为占位静态数据存在。

本次范围：**P0 全部** — Paint 模式对接 + Climate 三个面板 + Console 面板。

## Requirements

### 1. Paint 模式对接

PaintEditor/HeightEditor 笔刷生命周期（BeginStroke → ApplyStroke → EndStroke）接入 ViewModel，由视口鼠标事件驱动。Inspector Paint 区的材质槽位选择绑定 MaterialSlotManager，贴图导入/清除实际调用 TextureImporter。

**涉及服务：**
- `PaintEditor.Instance` — 笔刷生命周期
- `HeightEditor.Instance` — 笔刷生命周期
- `MaterialSlotManager.Instance` — 材质槽位查询
- `TextureImporter` — 贴图导入

### 2. Climate 面板（3 个子面板）

嵌入 Inspector 区域，在 Paint/Landscape 模式下可见。

**2a. ClimateManagerPanel** — 气候调色板管理
- 绑定 `ClimateRuleService.Instance.Climates`
- 操作：AddClimate / RemoveClimate / 编辑名称和颜色
- 切换遮罩可见性（绑定 EditorState.ShowMaskOverlay）

**2b. RuleManagerPanel** — 规则栈管理
- 绑定 `ClimateRuleService.Instance.Rules`
- 操作：AddRule / RemoveRuleAt / MoveRule（上移下移）
- 选中规则时显示 RuleInspectorPanel

**2c. RuleInspectorPanel** — 规则属性编辑
- 绑定选中 `ClimateRuleLayer` 的属性
- 编辑：MinAltitude/MaxAltitude、MinSlopeDegrees/MaxSlopeDegrees、BlendRange、MaterialSlotIndex
- 通过 ClimateRuleService.SetRuleAltitude / SetRuleSlope / NotifyMutated 提交变更

**涉及服务：**
- `ClimateRuleService.Instance` — 气候和规则的 CRUD + 事件
- `ClimateEditor.Instance` — 气候笔刷（ApplyStroke）

### 3. Console 面板增强

~延后到后续迭代，本期不接入。~

## Acceptance Criteria

- [ ] Paint 模式下选择材质槽位后，笔刷操作实际调用 PaintEditor.ApplyStroke
- [ ] Sculpt 模式下笔刷操作实际调用 HeightEditor.ApplyStroke
- [ ] Climate 面板可增删气候定义、编辑名称和颜色
- [ ] Rule 面板可增删/重排规则
- [ ] Rule Inspector 可编辑规则的高度/坡度/混合权重/材质槽位
- [ ] Console 面板支持日志级别过滤和搜索
- [ ] Console 面板支持命令输入（至少支持基本命令分发）

> 注：Console 面板延后，本期不实现。

## Definition of Done

- 每个对接功能有对应的 ViewModel 属性/命令绑定到 Avalonia View
- 后端服务事件（ClimateRuleService.StateChanged）正确传播到 UI 层
- 编译通过、无类型错误
- 关键路径手动测试通过

## Technical Approach

### ViewModel 设计

新增以下 ViewModel：

1. **ClimateViewModel** — 包装 ClimateRuleService，暴露 Climates/Rules 集合、CRUD 命令
2. **ClimateDefinitionViewModel** — 包装单个 ClimateDefinition（名称、颜色、ID）
3. **RuleViewModel** — 包装单个 ClimateRuleLayer（高度/坡度/混合/材质槽位等属性）

### View 设计

在 MainWindow.axaml Inspector 区域增加 Climate 相关 StackPanel（Paint/Landscape 模式可见），包含：
- Climate 列表 + 添加/删除按钮
- Rule 列表 + 添加/删除/上移/下移按钮
- 选中 Rule 的属性编辑区

Console 可作为底部面板或 Inspector 底部区域。

### 笔刷驱动

视口鼠标事件 → EditorShellViewModel 命令 → 调用 HeightEditor/PaintEditor 笔刷生命周期。

## Out of Scope

- P1/P2 功能（Export、Texture Inspector、Inputs Data、Settings、Asset Browser 真实数据等）
- 新增后端服务或修改后端 API
- IMGUI 代码恢复
- 着色器开发

## Technical Notes

### 后端服务 API 摘要

**ClimateRuleService.Instance** (单例, 有 StateChanged 事件):
- `Climates` / `Rules` — 只读集合
- `AddClimate()` / `RemoveClimate(id)` / `AddRule(climateId)` / `RemoveRuleAt(index)` / `MoveRule(from, to)`
- `SetRuleAltitude(index, min?, max?)` / `SetRuleSlope(index, min?, max?)`
- `NotifyMutated()` — 重算优先级并触发 StateChanged

**ClimateEditor.Instance** (单例, 无事件):
- `ApplyStroke(worldPos, mask, terrainMgr, climateId)` — 气候笔刷

**PaintEditor.Instance** (单例, 无事件):
- `BeginStroke(toolName, terrainMgr)` / `ApplyStroke(worldPos, ...)` / `EndStroke()`

**HeightEditor.Instance** (单例, 无事件):
- `BeginStroke(toolName, worldPos, terrainMgr)` / `ApplyStroke(worldPos, terrainMgr, frameTime)` / `EndStroke()`
