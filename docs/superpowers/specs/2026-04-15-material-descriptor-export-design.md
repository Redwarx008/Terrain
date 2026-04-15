# Material Descriptor Export 设计文档

**Date**: 2026-04-15
**Status**: Draft
**Author**: Claude

---

## Context

当前 Runtime 加载材质纹理依赖编辑器的 TOML 项目文件（`TerrainComponent.MaterialConfigPath` 指向编辑器项目 .toml），这是不对的 — 运行时不应该依赖编辑器项目文件。

需要在 Editor 中新增导出功能，将材质槽配置导出为独立的 `material_descriptor.toml` 文件，Runtime 直接使用该文件加载材质。

---

## 目标

1. Editor 新增 `File → Export → Material Descriptor...` 菜单
2. 导出独立的 `material_descriptor.toml` 文件，包含材质槽配置和纹理路径
3. Runtime 侧零修改，复用现有 `RuntimeMaterialManager.InitializeFromToml()`
4. 导出的 TOML 使用多行展开的 `[[material_slots]]` 格式

---

## 导出文件格式

```toml
[[material_slots]]
index = 0
albedo = "textures/grass_albedo.png"
normal = "textures/grass_normal.png"

[[material_slots]]
index = 1
albedo = "textures/rock_albedo.png"
normal = "textures/rock_normal.png"
```

- 纹理路径为**相对路径**（相对于 material_descriptor.toml 文件所在目录）
- 使用正斜杠 `/` 作为路径分隔符
- 不包含 `name` 字段（Runtime 不使用）

---

## 架构

```
Editor 导出链路:
  MaterialSlotManager.GetActiveSlots()
    → 路径转换 (绝对→相对, 复用 TomlProjectConfig.MakeRelative)
    → Tommy TomlTable 写入
    → material_descriptor.toml

Runtime 加载链路 (不变):
  TerrainComponent.MaterialConfigPath → material_descriptor.toml
    → RuntimeMaterialManager.InitializeFromToml()
    → ReadMaterialSlots() 解析 [[material_slots]]
    → 加载纹理
```

---

## 实现细节

### 1. 新建 `MaterialDescriptorExporter.cs`

**路径**: `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs`

**IExporter 属性**:
- `Name` => `"Material Descriptor"`
- `FileFilter` => `"Material Descriptor Files (*.toml)|*.toml"`
- `DefaultExtension` => `"toml"`

**ExportAsync 实现**:
1. 从 `MaterialSlotManager.Instance.GetActiveSlots()` 获取活跃槽位
2. 如果没有活跃槽位，报告失败并返回
3. 从 `outputPath` 计算输出目录
4. 对每个槽位：
   - `slot.AlbedoTexturePath` / `slot.NormalTexturePath` 是绝对路径
   - 用 `TomlProjectConfig.MakeRelative(absPath, outputDir)` 转为相对路径
5. 构建 `TomlTable` root + `TomlArray` material_slots
6. 每个槽位创建 `TomlTable`，设置 `index`、`albedo`、`normal` 键
7. `root.WriteTo(writer)` 写入文件

**路径转换**:
- `TomlProjectConfig.MakeRelative` 是 `internal static` 方法，已在 Editor 内部可访问
- 该方法使用 `Path.GetRelativePath` + 正斜杠替换，与 Runtime 的 `ResolvePath` 兼容
- 跨盘符路径会回退为绝对路径（`MakeRelative` 内部 try-catch）

### 2. 修改 `MainWindow.cs`

**变更 1** — 注册导出器 (构造函数, ~line 79):
```csharp
ExportManager.Instance.Register(new TerrainExporter());
ExportManager.Instance.Register(new MaterialDescriptorExporter());  // 新增
```

**变更 2** — 菜单项 (RenderMenuBar, ~line 514-520):
```csharp
if (ImGui.BeginMenu("Export"))
{
    if (ImGui.MenuItem("Terrain..."))
        HandleExportTerrain();
    if (ImGui.MenuItem("Material Descriptor..."))  // 新增
        HandleExportMaterialDescriptor();           // 新增
    ImGui.EndMenu();
}
```

**变更 3** — 处理方法 `HandleExportMaterialDescriptor()`:
- 前置检查：`MaterialSlotManager.Instance.ActiveSlotCount == 0` 时跳过
- 默认文件名：`{projectName}_material_descriptor.toml`
- 复用 `ExportProgressDialog` 显示进度
- 调用 `ExportManager.Instance.ExecuteAsync("Material Descriptor", ...)`

---

## 不修改的文件

| 文件 | 原因 |
|------|------|
| `RuntimeMaterialManager.cs` | 已能解析 `material_slots` TOML 格式 |
| `TerrainComponent.cs` | `MaterialConfigPath` 已支持任意 TOML 路径 |
| `TerrainProcessor.cs` | 已正确调用 `InitializeFromToml` |
| `ExportManager.cs` | 已支持注册任意 IExporter |
| `IExporter.cs` | 接口已足够 |
| `TomlProjectConfig.cs` | `MakeRelative` 方法已可复用 |

---

## Tommy 写入格式验证

Tommy 库中 `TomlArray` 包含 `TomlTable` 条目时，`WriteTo` 输出格式为：

```toml
[[material_slots]]
index = 0
albedo = "textures/grass_albedo.png"
normal = "textures/grass_normal.png"

[[material_slots]]
index = 1
...
```

这是 Tommy 的标准行为 — 数组中的 Table 条目自动使用 `[[section]]` 语法，每个键值对独占一行。符合需求。

---

## 验证方案

1. **构建验证**: `dotnet build` 零错误
2. **菜单检查**: Editor 中 File → Export 出现 "Material Descriptor..." 项
3. **空槽位**: 无材质槽位时点击导出，控制台显示提示，无文件对话框
4. **正常导出**: 配置 2+ 材质槽位后导出，检查输出的 .toml 文件格式正确、路径为相对路径
5. **Runtime 兼容**: 将 `TerrainComponent.MaterialConfigPath` 指向导出的 .toml，运行游戏验证材质加载
6. **路径转换**: 导出到不同目录，验证路径正确重新计算