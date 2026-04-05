# 阶段一：核心编辑体验完善

## 概述

**目标：** 完善高度编辑功能，达到可用状态

**范围：**
- 撤销/重做系统
- 项目保存/加载
- 高度图导入/导出

## 架构设计

### 命令模式（撤销/重做）

```
ICommand (interface)
    |
    v
HeightEditCommand (concrete command)
    |
    v
HistoryManager (invoker/manager)
    |
    v
HeightEditor (receiver - existing)
```

### 项目管理

```
TerrainProject (data model)
    |
    v
ProjectService (save/load)
    |
    v
MainWindow (UI integration)
```

## 核心接口设计

### ICommand 接口

```csharp
namespace Terrain.Editor.Services.Commands;

public interface ICommand
{
    string Description { get; }
    void Execute();
    void Undo();
    long EstimatedSizeBytes { get; }
}
```

### HeightEditCommand

```csharp
public sealed class HeightEditCommand : ICommand
{
    private readonly TerrainManager terrainManager;
    private readonly int centerX, centerZ;
    private readonly float radius;
    private readonly string toolName;

    // Before/After 状态：仅存储受影响区域
    private ushort[]? beforeData;
    private ushort[]? afterData;
    private Rectangle affectedRegion;

    public void CaptureBeforeState();  // 笔刷开始时调用
    public void CaptureAfterState();   // 笔刷结束时调用

    public void Execute() => ApplyState(afterData);
    public void Undo() => ApplyState(beforeData);
}
```

### HistoryManager

```csharp
public sealed class HistoryManager
{
    public static HistoryManager Instance { get; }

    // 配置
    private const int MaxCommandCount = 100;
    private const long MaxMemoryBytes = 500 * 1024 * 1024; // 500 MB

    // 状态
    private readonly List<ICommand> undoStack = new();
    private readonly List<ICommand> redoStack = new();

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public void BeginCommand(ICommand command);
    public void CommitCommand();
    public void CancelCommand();
    public bool Undo();
    public bool Redo();
    public void Clear();

    public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;
}
```

## 项目文件格式

### TerrainProject 数据模型

```csharp
public class TerrainProject
{
    public int Version { get; init; } = 1;
    public string? Name { get; set; }
    public required HeightmapConfig Heightmap { get; set; }
    public List<MaterialSlotConfig> MaterialSlots { get; set; } = new();
    public List<VegetationInstance> VegetationInstances { get; set; } = new();
    public EditorSettings? EditorSettings { get; set; }
}

public class HeightmapConfig
{
    public required string RelativePath { get; set; }
    public string? SourcePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float HeightScale { get; set; } = 100.0f;
}
```

### JSON 示例

```json
{
  "version": 1,
  "name": "My Terrain",
  "heightmap": {
    "relativePath": "terrain_heightmap.png",
    "width": 2048,
    "height": 2048,
    "heightScale": 100.0
  },
  "materialSlots": [],
  "vegetationInstances": [],
  "editorSettings": {
    "camera": {
      "positionX": 0, "positionY": 100, "positionZ": 0,
      "yaw": 0, "pitch": -45
    }
  }
}
```

### ProjectService

```csharp
public sealed class ProjectService
{
    public static ProjectService Instance { get; }

    public TerrainProject? CurrentProject { get; }
    public string? CurrentProjectPath { get; }
    public bool HasUnsavedChanges { get; }

    public TerrainProject CreateProject(string heightmapPath);
    public TerrainProject? LoadProject(string projectPath);
    public bool SaveProject();
    public bool SaveProjectAs(string projectPath);
    public void MarkDirty();

    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;
}
```

## 高度图导出

```csharp
public static class HeightmapExporter
{
    public static bool ExportToPng(
        ushort[] heightData,
        int width,
        int height,
        string outputPath);

    public static bool ExportCurrentTerrain(
        TerrainManager terrainManager,
        string outputPath);
}
```

## 文件清单

### 新建文件

| 文件路径 | 说明 |
|---------|------|
| `Services/Commands/ICommand.cs` | 命令接口 |
| `Services/Commands/HeightEditCommand.cs` | 高度编辑命令 |
| `Services/Commands/HistoryManager.cs` | 撤销/重做管理器 |
| `Models/TerrainProject.cs` | 项目数据模型 |
| `Services/ProjectService.cs` | 项目保存/加载服务 |
| `Services/HeightmapExporter.cs` | PNG 导出功能 |

### 修改文件

| 文件路径 | 修改内容 |
|---------|---------|
| `Services/HeightEditor.cs` | 集成命令捕获 |
| `UI/MainWindow.cs` | 连接 Undo/Redo/Save 处理器 |
| `UI/Panels/ToolbarPanel.cs` | 启用/禁用撤销重做按钮 |
| `Platform/FileDialog.cs` | 添加 ShowSaveDialog |

## 实现步骤

### Step 1: 命令模式基础
1. 创建 `ICommand.cs` 接口
2. 创建 `HeightEditCommand.cs`
3. 创建 `HistoryManager.cs`
4. 编写单元测试

### Step 2: HeightEditor 集成
1. 修改 `HeightEditor.cs` 在笔刷开始/结束时创建命令
2. 连接 HistoryManager 与笔刷生命周期
3. 测试撤销/重做功能

### Step 3: UI 集成
1. 更新 `MainWindow.cs` 添加 Undo/Redo 处理器
2. 添加键盘快捷键（Ctrl+Z, Ctrl+Y）
3. 更新 `ToolbarPanel.cs` 按钮状态
4. 添加历史状态显示

### Step 4: 项目格式
1. 创建 `TerrainProject.cs` 模型
2. 创建 `ProjectService.cs` 保存/加载
3. 添加项目脏状态跟踪

### Step 5: 高度图导出
1. 创建 `HeightmapExporter.cs`
2. 添加 `ShowSaveDialog` 到 `FileDialog.cs`
3. 连接保存菜单项
4. 测试导出/导入循环

## 关键设计决策

### 内存管理
- **策略：** 仅复制受影响区域（Copy-on-write）
- **限制：** 100 条命令或 500MB，以先到者为准
- **原因：** 完整高度图复制对大地形来说内存消耗过大

### 文件格式选择
- **决策：** 项目文件使用 JSON
- **原因：** 人类可读、版本控制友好、元数据足够
- **注意：** 高度数据保留 PNG L16 格式以保证效率

### 笔刷粒度
- **决策：** 每次拖拽操作为一个命令
- **原因：** 更细的粒度会产生过多撤销步骤，影响性能

## 验证方案

1. **撤销/重做：** 执行笔刷操作后，Ctrl+Z 撤销，Ctrl+Y 重做
2. **项目保存：** 保存项目，关闭编辑器，重新打开验证数据完整
3. **高度图导出：** 导出 PNG，用图像查看器验证
