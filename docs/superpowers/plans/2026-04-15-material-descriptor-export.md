# Material Descriptor Export 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Editor 中添加材质描述符导出功能，生成独立的 `material_descriptor.toml` 文件，使 Runtime 不再依赖编辑器项目文件。

**Architecture:** 新增 `MaterialDescriptorExporter`（实现 `IExporter` 接口），从 `MaterialSlotManager` 获取活跃材质槽，将绝对路径转为相对路径，使用 Tommy 写入 TOML 文件。Runtime 侧零修改。

**Tech Stack:** C# / Tommy TOML / Stride Engine / ImGui

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs` | IExporter 实现，导出材质描述符 TOML |
| Modify | `Terrain.Editor/UI/MainWindow.cs` | 注册导出器 + 菜单项 + 处理方法 |

---

### Task 1: 创建 MaterialDescriptorExporter

**Files:**
- Create: `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs`

- [ ] **Step 1: 创建 MaterialDescriptorExporter.cs**

创建文件 `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs`，内容如下：

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tommy;

namespace Terrain.Editor.Services.Export.Exporters;

/// <summary>
/// Exports material slot configuration to a standalone material_descriptor.toml file.
/// The exported file can be used by the runtime RuntimeMaterialManager without
/// depending on the editor project TOML.
/// </summary>
public class MaterialDescriptorExporter : IExporter
{
    public string Name => "Material Descriptor";
    public string FileFilter => "Material Descriptor Files (*.toml)|*.toml";
    public string DefaultExtension => "toml";

    public async Task ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)
    {
        var slotManager = MaterialSlotManager.Instance;
        var activeSlots = slotManager.GetActiveSlots().ToList();

        if (activeSlots.Count == 0)
        {
            throw new InvalidOperationException("No material slots configured for export.");
        }

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            progress.Report(ExportProgress.Running(0, 2, "Converting material paths..."));

            string outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? "";

            var root = new TomlTable();
            var slotsArray = new TomlArray();

            foreach (var slot in activeSlots)
            {
                ct.ThrowIfCancellationRequested();

                var slotTable = new TomlTable();
                slotTable["index"] = slot.Index;

                if (!string.IsNullOrEmpty(slot.AlbedoTexturePath))
                    slotTable["albedo"] = TomlProjectConfig.MakeRelative(slot.AlbedoTexturePath, outputDir);
                else
                    slotTable["albedo"] = "";

                if (!string.IsNullOrEmpty(slot.NormalTexturePath))
                    slotTable["normal"] = TomlProjectConfig.MakeRelative(slot.NormalTexturePath, outputDir);
                else
                    slotTable["normal"] = "";

                slotsArray.Add(slotTable);
            }

            root["material_slots"] = slotsArray;

            progress.Report(ExportProgress.Running(1, 2, "Writing material descriptor file..."));

            // Ensure output directory exists
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var writer = File.CreateText(outputPath);
            root.WriteTo(writer);

            progress.Report(ExportProgress.Completed());
        }, ct);
    }
}
```

关键设计点：
- 使用 `MaterialSlotManager.Instance` 单例获取活跃槽位（与 `TerrainExporter` 使用 `TerrainManager` 属性的模式不同，因为 `MaterialSlotManager` 是单例）
- 使用 `TomlProjectConfig.MakeRelative()` 转换路径（复用现有方法）
- Tommy 的 `TomlArray` + `TomlTable` 组合自动输出 `[[material_slots]]` 多行格式
- 空槽位时抛异常（而非静默返回），由 `ExportManager` 的错误回滚机制处理

- [ ] **Step 2: 验证编译**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 编译成功，零错误

- [ ] **Step 3: 提交**

```bash
git add Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs
git commit -m "feat: add MaterialDescriptorExporter for standalone TOML export"
```

---

### Task 2: 集成到 MainWindow

**Files:**
- Modify: `Terrain.Editor/UI/MainWindow.cs`

- [ ] **Step 1: 注册导出器**

在 `MainWindow.cs` 构造函数中（约 line 79），在 `ExportManager.Instance.Register(new TerrainExporter());` 之后添加：

```csharp
ExportManager.Instance.Register(new MaterialDescriptorExporter());
```

- [ ] **Step 2: 添加菜单项**

在 `RenderMenuBar` 方法中（约 line 514-520），在 `ImGui.MenuItem("Terrain...")` 之后添加：

```csharp
if (ImGui.MenuItem("Material Descriptor..."))
{
    HandleExportMaterialDescriptor();
}
```

完整的 Export 菜单区域变为：

```csharp
if (ImGui.BeginMenu("Export"))
{
    if (ImGui.MenuItem("Terrain..."))
    {
        HandleExportTerrain();
    }
    if (ImGui.MenuItem("Material Descriptor..."))
    {
        HandleExportMaterialDescriptor();
    }
    ImGui.EndMenu();
}
```

- [ ] **Step 3: 添加处理方法**

在 `HandleExportTerrain()` 方法之后添加 `HandleExportMaterialDescriptor()` 方法：

```csharp
private void HandleExportMaterialDescriptor()
{
    var slotManager = MaterialSlotManager.Instance;
    if (slotManager.ActiveSlotCount == 0)
    {
        Console.LogInfo("No material slots configured to export");
        return;
    }

    nint hwnd = GetNativeWindowHandle();
    string defaultName = ProjectManager.Instance.IsProjectOpen
        ? $"{ProjectManager.Instance.ProjectName}_material_descriptor.toml"
        : "terrain_material_descriptor.toml";

    if (FileDialog.ShowSaveDialog(hwnd, "Material Descriptor Files (*.toml)|*.toml", "Export Material Descriptor", defaultName, out string? filePath))
    {
        exportProgressDialog.Open();
        var progress = new Progress<ExportProgress>(exportProgressDialog.UpdateProgress);

        _ = ExportManager.Instance.ExecuteAsync(
            "Material Descriptor", filePath, progress, exportProgressDialog.CancellationToken)
            .ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    exportProgressDialog.SetResult(false, t.Exception.InnerException?.Message ?? "Export failed");
            });
    }
}
```

关键设计点：
- 复用现有 `exportProgressDialog` 显示导出进度
- 空槽位时提前返回并显示日志消息
- 默认文件名基于项目名称
- 使用 `ExportManager.ExecuteAsync` 统一错误回滚

- [ ] **Step 4: 验证编译**

Run: `dotnet build Terrain.Editor/Terrain.Editor.csproj`
Expected: 编译成功，零错误

- [ ] **Step 5: 提交**

```bash
git add Terrain.Editor/UI/MainWindow.cs
git commit -m "feat: add Material Descriptor export menu item and handler"
```

---

### Task 3: 端到端验证

- [ ] **Step 1: 完整构建**

Run: `dotnet build`
Expected: 整体解决方案编译成功，零错误

- [ ] **Step 2: 功能验证清单**

启动 Editor 后手动验证：
1. 打开项目 → 加载地形和材质槽
2. File → Export → 出现 "Material Descriptor..." 菜单项
3. 点击后出现文件保存对话框，默认文件名为 `{projectName}_material_descriptor.toml`
4. 保存后检查 .toml 文件格式：
   - 使用 `[[material_slots]]` 多行格式
   - 每个槽位有 `index`、`albedo`、`normal` 键
   - 路径为相对路径（相对于 .toml 文件所在目录）
5. 将 `TerrainComponent.MaterialConfigPath` 指向导出的 .toml 文件，Runtime 加载正常

- [ ] **Step 3: 更新架构文档**

更新 `docs/ARCHITECTURE_OVERVIEW.md` 中编辑器层导出系统行：
```markdown
| **导出系统（IExporter）** | ✅ 已实现 | - |
```
→ 改为：
```markdown
| **导出系统（IExporter）** | ✅ 已实现 | 包含 Terrain 和 Material Descriptor 导出器 |
```

在关键文件表中添加：
```markdown
| `Terrain.Editor/Services/Export/Exporters/MaterialDescriptorExporter.cs` | 材质描述符导出器 |
```

- [ ] **Step 4: 创建会话日志**

创建 `docs/log/2026/04/15/2026-04-15-2-material-descriptor-export.md`，使用模板格式。

- [ ] **Step 5: 最终提交**

```bash
git add docs/
git commit -m "docs: update architecture overview and session log for material descriptor export"
```