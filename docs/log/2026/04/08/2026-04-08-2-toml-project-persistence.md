# TOML 项目持久化实现
**Date**: 2026-04-08
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 实现编辑器的 New/Open/Save 项目持久化功能，使用 .toml 格式作为项目配置文件

**Secondary Objectives:**
- ImGui 模态弹窗向导用于新建项目
- 动态标题栏显示项目名和 dirty 标记
- 快捷键支持 (Ctrl+N/O/S/Shift+S)

**Success Criteria:**
- dotnet build 零错误
- New → 模态弹窗向导 → 创建 .toml 项目文件
- Open → 选择 .toml → 恢复所有资源
- Save → 写入 .toml + indexmap PNG
- 标题栏动态更新

---

## What We Did

### 1. 添加 Tommy TOML 库
**Files Changed:** `Terrain.Editor/Terrain.Editor.csproj`, `Directory.Packages.props`

- 添加 Tommy 3.1.2 NuGet 包
- Tommy API: `TomlNode.IsString`/`.IsInteger` 判断类型，`.HasKey()` 检查 key，`.AsInteger`/`.AsString.Value` 取值

### 2. 创建 TomlProjectConfig 数据模型
**Files Changed:** `Terrain.Editor/Services/TomlProjectConfig.cs` (新建)

- `TomlProjectConfig` 和 `TomlMaterialSlotConfig` 数据类
- `ReadFrom()` — 解析 TOML，相对路径自动解析为绝对路径
- `WriteTo()` — 绝对路径自动转相对，写入 TOML
- 防御性检查：所有字段用 `HasKey` + `IsString`/`IsInteger` 检查，缺失用默认值

### 3. 重写 ProjectManager
**Files Changed:** `Terrain.Editor/Services/ProjectManager.cs`

- JSON → TOML 迁移，移除旧 `ProjectConfig`/`MaterialSlotConfig` 类型
- 新增 `IsDirty`/`MarkDirty()`/`MarkClean()`/`DirtyChanged` 事件
- `ProjectFilePath` 替代旧的 `ProjectPath`（目录），从文件路径派生目录
- 修复：CreateProject 先设置 `ProjectFilePath` 再创建子目录
- 优化：`LoadConfig()` 优先返回缓存，避免重复解析

### 4. FileDialog 添加 Save 对话框
**Files Changed:** `Terrain.Editor/Platform/FileDialog.cs`

- `ShowSaveDialog()` — Win32 `GetSaveFileName` P/Invoke
- 修复：`PadRight(260, '\0')` 用 null 字符填充（不用空格）

### 5. TerrainManager 适配 TOML
**Files Changed:** `Terrain.Editor/Services/TerrainManager.cs:352-510`

- `SaveProject()` — 构建 `TomlProjectConfig`，路径转相对，保存 indexmap PNG
- `LoadProject()` — 读取 TOML，恢复材质槽位，加载 heightmap + indexmap
- 新增：`LoadProject` 开头调用 `RemoveCurrentTerrain()` + `ClearAll()` 清理旧状态

### 6. NewProjectWizard 模态弹窗
**Files Changed:** `Terrain.Editor/UI/Dialogs/NewProjectWizard.cs` (新建)

- ImGui `BeginPopupModal` 模态弹窗
- 字段：Project Name、Project File (.toml)、Heightmap (.png)、Index Map (可选)
- `ImGui.IsPopupOpen` 检查避免每帧重复 `OpenPopup`

### 7. MainWindow UI 连接
**Files Changed:** `Terrain.Editor/UI/MainWindow.cs`

- `HandleToolbarAction` — New/Open/Save/SaveAs 完整流程
- 动态标题栏：`"Terrain Editor - {ProjectName} *"` (dirty 标记)
- 快捷键：Ctrl+S/N/O + Ctrl+Shift+S (SaveAs)
- Dirty tracking：纹理导入/清除、Undo/Redo 自动 MarkDirty

---

## Decisions Made

### Decision 1: TOML 格式而非 JSON
**Context:** 需要选择项目配置格式
**Options:**
1. JSON — 已有 `System.Text.Json`，无需额外依赖
2. TOML — 更易手写编辑，层级更清晰

**Decision:** TOML (Tommy 库)
**Rationale:** 用户明确要求 TOML；配置文件可能需要手写编辑
**Trade-offs:** 引入 Tommy NuGet 依赖

### Decision 2: 相对路径存储
**Context:** 路径存储方式影响项目可移植性
**Decision:** 所有路径相对于 .toml 文件所在目录存储，使用 `/` 分隔符
**Rationale:** 整个项目目录可以直接复制到其他位置，路径仍然有效

### Decision 3: 模态弹窗向导而非连续文件对话框
**Context:** New Project 的 UX 流程
**Decision:** ImGui `BeginPopupModal` 模态弹窗
**Rationale:** 用户明确要求模态弹窗

---

## What Worked ✅

1. **Tommy TOML 库**
   - API 简洁，Parse/WriteTo 一读一写
   - `IsString`/`IsInteger` 属性判断类型比 enum 更直观

2. **Plan agent 规划**
   - 详细的分步实现计划，节省了大量来回调整

3. **路径相对化方案**
   - `Path.GetRelativePath` + `ResolvePath` 配合，跨盘符自动回退绝对路径

---

## What Didn't Work ❌

1. **Tommy API 最初猜测错误**
   - 最初用了 `TomlType.String` 枚举（不存在），实际应用 `IsString` 属性
   - 需要用 `dotnet-script` 检查实际 API 才发现
   - **Don't try this again because:** 对不熟悉的 NuGet 库，先写个探测程序查看 API 再写业务代码

2. **Edit 工具的部分替换导致文件重复**
   - 替换 NewProjectWizard 的 Render 方法时，只匹配了部分文本，导致方法体被重复
   - **Don't try this again because:** 对大段方法替换，优先用 Write 整体重写而非 Edit 局部替换

---

## Code Review 结果

通过 general-purpose agent 进行了完整的 code review，发现并修复了：

| 问题 | 严重度 | 修复 |
|------|--------|------|
| ReadFrom 缺少 key 存在性检查 | Critical | 添加 HasKey + 类型检查 |
| FileDialog PadRight 用空格 | Major | 改为 PadRight(260, '\0') |
| LoadConfig 冗余读盘 | Major | 优先返回缓存 |
| LoadProject 不清理旧状态 | Major | 添加 RemoveCurrentTerrain + ClearAll |
| CreateProject 路径计算顺序 | Major | 先设 ProjectFilePath 再创建目录 |
| Open/New/Exit 无 dirty 检查 | Major | 记录为后续优化（需确认弹窗基础设施） |

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 添加未保存确认弹窗 — Open/New/Exit 前检查 IsDirty
2. TomlProjectConfig.WriteTo 原子写入 — 先写临时文件再替换
3. 植被编辑系统继续

### Questions to Resolve
1. 确认弹窗应该用 ImGui 还是 Win32 MessageBox？

---

## Session Statistics

**Files Changed:** 7 (3 new + 4 modified)
**New Files:** TomlProjectConfig.cs, NewProjectWizard.cs
**Modified Files:** ProjectManager.cs, TerrainManager.cs, FileDialog.cs, MainWindow.cs, Terrain.Editor.csproj
**Commits:** 0 (未提交)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 项目配置使用 TOML 格式，Tommy 3.1.2 库
- Tommy API: `node.IsString`/`.IsInteger` 判断类型，`node.HasKey("key")` 检查存在
- 所有路径存储为相对路径（相对 .toml 文件目录）
- ProjectManager 是单例，管理 IsDirty 状态
- NewProjectWizard 是模态弹窗，通过事件通知 MainWindow

**Gotchas for Next Session:**
- Tommy 的 `AsInteger` 返回 `long`，需要 `(int)` 强转
- `ImGui.OpenPopup` 不要每帧调用，用 `IsPopupOpen` 检查
- `LoadTerrainAsync` 是 fire-and-forget，后续代码在 terrain 加载完之前执行
- `ProjectManager.CreateProject` 必须先设 `ProjectFilePath` 再创建子目录
- Open/New/Exit 前缺少 dirty 确认弹窗

---

## Links & References

### Code References
- TOML 数据模型: `Terrain.Editor/Services/TomlProjectConfig.cs`
- 项目管理器: `Terrain.Editor/Services/ProjectManager.cs`
- Save/Load 逻辑: `Terrain.Editor/Services/TerrainManager.cs:352-510`
- 新建向导: `Terrain.Editor/UI/Dialogs/NewProjectWizard.cs`
- UI 连接: `Terrain.Editor/UI/MainWindow.cs`
- 保存对话框: `Terrain.Editor/Platform/FileDialog.cs`
