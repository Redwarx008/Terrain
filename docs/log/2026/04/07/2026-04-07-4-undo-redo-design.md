# Undo/Redo 系统实现

**日期：** 2026-04-07
**状态：** ✅ 已完成

---

## Session Goal

设计并实现 Terrain Editor 的 Undo/Redo 系统，使用命令模式实现可撤销的编辑操作。

---

## What We Did

1. 使用 Explore subagent 读取架构概览和最新日志
2. 分析现有的编辑器代码结构
3. 使用 Plan agent 设计 Undo/Redo 系统架构
4. 实现完整的命令系统：

**新建文件：**
- [ICommand.cs](Terrain.Editor/Services/Commands/ICommand.cs) - 命令接口
- [TerrainEditCommand.cs](Terrain.Editor/Services/Commands/TerrainEditCommand.cs) - 基类（区域追踪）
- [HeightEditCommand.cs](Terrain.Editor/Services/Commands/HeightEditCommand.cs) - 高度编辑命令
- [PaintEditCommand.cs](Terrain.Editor/Services/Commands/PaintEditCommand.cs) - 材质绘制命令
- [HistoryManager.cs](Terrain.Editor/Services/Commands/HistoryManager.cs) - 历史管理器

**修改文件：**
- [HeightEditor.cs](Terrain.Editor/Services/HeightEditor.cs) - 集成命令生命周期
- [PaintEditor.cs](Terrain.Editor/Services/PaintEditor.cs) - 集成命令生命周期，修改 BeginStroke 签名
- [SceneViewPanel.cs](Terrain.Editor/UI/Panels/SceneViewPanel.cs) - 更新 PaintEditor 调用
- [MainWindow.cs](Terrain.Editor/UI/MainWindow.cs) - 连接 Undo/Redo 菜单

---

## Decisions Made

### D-01: 命令模式架构
使用命令模式封装编辑操作：
- `ICommand` 接口定义 Execute/Undo
- `TerrainEditCommand` 基类提供区域追踪
- `HeightEditCommand` / `PaintEditCommand` 具体实现

### D-02: Copy-on-Write 内存优化
仅存储受影响区域的数据快照，而非整个高度图/材质图。

**Why:** 全图复制（2048×2048 = 8MB）会快速耗尽内存。笔刷操作通常只影响 ~60×60 区域（~15KB）。

**How:** 在 `ApplyStroke` 期间累积 `AffectedRegion`，仅在 `EndStroke` 时复制数据。

### D-03: 集成点选择
在 `BeginStroke/EndStroke` 处拦截，而非在每个工具内部。

**Why:** 保持工具代码简洁，集中管理命令生命周期。

### D-04: PaintEditor.BeginStroke 签名变更
添加 `TerrainManager` 参数以获取 `MaterialIndices` 引用。

**Why:** 命令需要在 `BeginStroke` 时捕获 before 状态，需要访问数据源。

### D-05: 内存限制策略
- 最大命令数：100 条
- 最大内存：500 MB
- 超出时移除最旧命令

---

## What Worked

- 现有的三阶段生命周期与命令模式完美契合
- `TerrainDataChannel` 枚举已存在，可直接复用
- `MarkDataDirty()` 机制可用于 Undo/Redo 后的 GPU 同步
- 构建一次通过，无编译错误

---

## What Didn't Work

N/A

---

## Next Session

1. 测试 Undo/Redo 功能：
   - 启动编辑器，加载地形
   - 使用 Raise 工具绘制，按 Ctrl+Z 撤销
   - 按 Ctrl+Y 重做
   - 验证多次撤销/重做

2. 可选增强：
   - 添加 Escape 键取消当前笔触
   - UI 按钮禁用状态（无历史时禁用 Undo/Redo）
   - 显示历史数量

---

## Quick Reference for Future Claude

### 命令系统架构
```
HistoryManager (Singleton)
├── BeginCommand(command)    // 开始笔触，捕获 before 状态
├── UpdateCommandRegion()    // 累积受影响区域
├── CommitCommand()          // 结束笔触，捕获 after 状态
├── Undo() / Redo()          // 执行撤销/重做
└── CancelCommand()          // 取消当前命令

命令类：
ICommand → TerrainEditCommand → HeightEditCommand
                             → PaintEditCommand
```

### 关键文件
- `Terrain.Editor/Services/Commands/HistoryManager.cs` - 核心管理器
- `Terrain.Editor/Services/HeightEditor.cs:50-70` - 命令集成点
- `Terrain.Editor/UI/MainWindow.cs:560-580` - UI 连接

### 使用示例
```csharp
// 开始笔触（自动捕获 before 状态）
HeightEditor.Instance.BeginStroke("Raise", position, terrainManager);

// 应用笔触（累积受影响区域）
HeightEditor.Instance.ApplyStroke(position, terrainManager, frameTime);

// 结束笔触（自动捕获 after 状态并添加到历史）
HeightEditor.Instance.EndStroke();

// 撤销/重做
HistoryManager.Instance.Undo();
HistoryManager.Instance.Redo();
```
