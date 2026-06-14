# Runtime Virtual Resource Migration
**Date**: 2026-06-13
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 实现 Task 4：Runtime 迁移到新的虚拟资源系统，并移除旧 `RuntimeBiomeConfig` / `Shared` 依赖。

**Secondary Objectives:**
- 保持 `.terrain` 运行时读取路径，材质/detail map 改由 `biome_settings.toml` 与 `materials/descriptor.toml` 驱动。
- 添加文本回归测试，防止旧路径字段和旧 Shared Link 回流。
- 根据复核反馈补齐材质贴图虚拟解析、固定 `map_data/biome_mask.png` 消费，以及旧 Biome Config 导出入口清理。
- 追加清理 Editor 顶部/菜单/快捷键中的旧 New/Open Project 入口。
- 删除旧 `ProjectManager` / `TomlProjectConfig` 项目 TOML 持久化链路、Save/Save As 入口和 `heightmaps` / `splatmaps` 旧目录写入逻辑。

**Success Criteria:**
- `TerrainComponent` 不再包含 `TerrainDataPath` / `BiomeConfigPath`。
- Runtime 从 `AppContext.BaseDirectory/LaunchSetting.json` 加载资源 bundle。
- `RuntimeBiomeConfig.cs` 删除，`Terrain.Shared` 和 `..\Shared\*.cs` Link 清零。
- Editor 不再暴露 `NewProjectCommand` / `OpenProjectCommand`、`Ctrl+N` / `Ctrl+O`。
- Editor 不再暴露 `SaveProjectCommand` / `SaveProjectAsCommand`、`Ctrl+S` / `Ctrl+Shift+S`，也不再写旧项目 TOML。
- 材质贴图路径必须经 `GameResourceResolver` 解析，允许 mod 覆盖/回退。
- Runtime detail map 生成直接消费固定 `map_data/biome_mask.png`，不再从 `.terrain` 的 biome mask VT 段读取源蒙版。
- `Terrain.Editor.Tests` 和 `Terrain.sln` 构建通过。

---

## Context & Background

**Previous Work:**
- Related: `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`
- Related: `docs/log/2026/06/13/virtual-resource-system-design-finalization.md`

**Current State:**
- `Terrain/Resources/` 已有 `LaunchSettingsService`、`GameResourceResolver`、`TerrainRuntimeBootstrap`、descriptor/settings reader。
- 旧 Runtime 主链路仍从组件路径字段和 `RuntimeBiomeConfig` 读取。

**Why Now:**
- 设计已拍板，需要把 Runtime 消费路径切到统一 resolver/bootstrap。

---

## What We Did

### 1. Runtime 加载链路迁移
**Files Changed:** `Terrain/Core/TerrainComponent.cs`, `Terrain/Core/TerrainProcessor.cs`, `Terrain/Assets/MainScene.sdscene`

**Implementation:**
- 移除 `TerrainComponent.TerrainDataPath` / `BiomeConfigPath`，`TerrainConfig` 只捕获容量类重建配置。
- `TerrainProcessor` 固定读取 `AppContext.BaseDirectory/LaunchSetting.json`，用 base 隐式层 + enabled mods 构建 `GameResourceResolver`。
- 通过 `TerrainRuntimeBootstrap.Load()` 获取 bundle，并用 `bundle.TerrainDataPath` 创建 `TerrainFileReader`。
- 用 `bundle.HeightScale` 更新组件运行时高度缩放；bootstrap diagnostics 仅 `Log.Warning`。

**Rationale:**
- Runtime 不再依赖场景序列化路径字段，资源覆盖顺序统一由 LaunchSetting + resolver 决定。

### 2. 材质与 DetailMap 改为新资源模型
**Files Changed:** `Terrain/Materials/RuntimeDetailMapBuilder.cs`, `Terrain/Materials/RuntimeMaterialManager.cs`, `Terrain/Materials/RuntimeBiomeConfig.cs`

**Implementation:**
- `RuntimeDetailMapBuilder.Generate` 接收 `RuntimeBiomeSettings`、`RuntimeMaterialDescriptor`、`heightScale`。
- `material_id` 映射到 descriptor 中的 material `index`。
- modifier `type` / `blend_mode` 按 enum 名称解析，失败时降级到 `HeightRange` / `Multiply`。
- `RuntimeMaterialManager` 删除 `InitializeFromToml` 和旧 `ReadMaterialSlots(string tomlFilePath)`，新增 descriptor + materialsDirectory 初始化入口。
- 复核后调整为由 `TerrainRuntimeBootstrap` 解析 descriptor 中的贴图相对路径，`RuntimeMaterialManager` 只接收已解析实体路径，避免绕过 mod 覆盖。
- descriptor 贴图路径限制为 `map_data/materials/descriptor.toml` 所在目录下的文件名（如 `xxx.png`），material index 限制为 `0..254`。
- `RuntimeBiomeSettingsReader` 保留已有高级 modifier 参数（radius/angle/scale/noise/invert/texture_mask 等），避免 Noise/Direction/Curvature 规则静默退化。
- 新增 `RuntimeBiomeMaskReader` 从固定 `map_data/biome_mask.png` 读取 L8 biome id，并在 Runtime 中校验尺寸匹配 `.terrain` splatmap 尺寸。
- 删除 `RuntimeBiomeConfig.cs`。

**Rationale:**
- 新格式只有单个 `biome_settings.toml` 和单个 `materials/descriptor.toml`，不再保留旧 BiomeConfig TOML 兼容。

### 3. Shared 目录依赖移除
**Files Changed:** `Terrain/Materials/TerrainDetailGeneration.cs`, `Terrain/Streaming/VirtualTextureLayout.cs`, `Terrain/Terrain.csproj`, `Terrain.Editor/Terrain.Editor.csproj`, `Terrain/Streaming/TerrainStreaming.cs`, `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

**Implementation:**
- 将 `Shared/TerrainDetailGeneration.cs` 移入 Terrain 项目 namespace `Terrain`，作为 Runtime 内部类型。
- 将 `Shared/VirtualTextureLayout.cs` 移入 Terrain 项目 namespace `Terrain`，并保留 public API 供 Editor exporter 使用。
- 移除 `..\Shared\*.cs` Link 和 `using Terrain.Shared`。

**Rationale:**
- Terrain 和 Editor 不再通过源码 Link 共享 `Shared` 目录，Editor 通过 Terrain 项目引用使用公开 layout helper。

### 4. 回归测试
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`, `Terrain.Editor.Tests/VirtualResources/RuntimeBiomeMaskReaderTests.cs`, `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs`, `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 添加文本测试覆盖：无旧 shared namespace、csproj 无 Shared Link、`RuntimeBiomeConfig.cs` 不存在、组件/场景无旧路径字段、材质管理器无旧 TOML 入口。
- 添加测试覆盖旧 Biome Config 导出入口移除、材质贴图经 resolver 覆盖/回退、descriptor 平铺路径和 index 上限、biome mask PNG 读取。
- 添加测试覆盖旧 New/Open Project UI 与命令入口移除。
- 先运行测试确认红灯，再实现迁移并跑绿灯。

**Rationale:**
- 这些约束容易被后续重构误加回来，用轻量文本测试能快速拦截。

---

## Problems Encountered & Solutions

### Problem 1: 移入 `Terrain` namespace 后与 Editor 同名 enum 冲突
**Symptom:** Editor ViewModel 编译错误，`Terrain.BiomeModifierType` 与 `Terrain.Editor.Services.BiomeModifierType` 混用。
**Root Cause:** `TerrainDetailGeneration` 类型从 `Terrain.Shared` 移到根 `Terrain` 后，public enum 污染了 Editor 可见命名空间。
**Solution:**
- 将 detail generation 相关 rule/modifier 类型收回为 `internal`。

**Why This Works:** 这些类型只供 Terrain Runtime 内部生成 detail map 使用；Editor 只需要公开的 `VirtualTextureLayout`。

### Problem 2: 材质贴图绕过虚拟资源解析
**Symptom:** descriptor 本身可被 mod 覆盖，但 descriptor 内的 `grass_a.png` 会被直接拼到 descriptor 所在实体目录。
**Root Cause:** `RuntimeMaterialManager` 负责相对路径拼接，脱离了 `GameResourceResolver`。
**Solution:**
- 将贴图实体路径解析上移到 `TerrainRuntimeBootstrap`，每个贴图按 `map_data/materials/{relative}` 走 resolver。

**Why This Works:** descriptor、贴图文件都遵循同一套 base + mod 覆盖顺序；mod 缺贴图时可回退 base。

### Problem 3: 固定 biome mask 没有被 Runtime 消费
**Symptom:** `map_data/biome_mask.png` 被 bootstrap 解析，但 detail map 生成仍从 `.terrain` 读取 biome mask VT 数据。
**Root Cause:** Task 4 首版只替换了 settings/descriptor，没有替换 biome mask 数据源。
**Solution:**
- 新增 `RuntimeBiomeMaskReader`，Runtime detail map 生成从 `bundle.BiomeMaskPath` 读取 L8 PNG。

**Why This Works:** Editor 修改的固定 `map_data/biome_mask.png` 可以通过 resolver 覆盖并直接影响 Runtime detail map 生成；`.terrain` 继续承担 VT 二进制数据职责。

### Problem 4: 旧 Biome Config 导出仍可被用户触发
**Symptom:** 菜单仍暴露 `Export > Biome Config...`，ViewModel 仍注册旧 exporter。
**Root Cause:** Runtime 主链路移除旧 TOML 后，Editor 导出入口未同步清理。
**Solution:**
- 删除 `BiomeConfigExporter.cs`，移除菜单项和 ViewModel 注册/命令。

**Why This Works:** 避免用户继续导出 Runtime 不再消费的旧格式 TOML。

### Problem 5: 旧 New/Open Project 入口仍在 UI 中
**Symptom:** 顶部工具栏、File 菜单和快捷键仍绑定 `NewProjectCommand` / `OpenProjectCommand`。
**Root Cause:** 前序迁移聚焦 Runtime 和 BiomeConfig 出口，没有清理通用项目生命周期入口。
**Solution:**
- 删除 `Ctrl+N` / `Ctrl+O`、工具栏 New/Open 按钮、File 菜单 New/Open 项，以及 ViewModel 中对应命令方法。
- 空项目标签从 `No project` 改为固定 `Terrain`。

**Why This Works:** Editor 启动后不再提供旧的“创建/打开项目”用户路径，符合固定 Terrain 工作区加载方向。

### Problem 6: 旧 ProjectManager/TomlProjectConfig 路径体系仍在代码里
**Symptom:** 即使 New/Open UI 被移除，`ProjectManager`、`TomlProjectConfig`、Save/Save As、`heightmaps`/`splatmaps` 目录写入仍存在。
**Root Cause:** 前序清理只处理入口，没有移除旧项目持久化实现。
**Solution:**
- 删除 `ProjectManager.cs` 和 `TomlProjectConfig.cs`。
- 删除 `TerrainManager` 的旧 TOML 项目保存/加载代码。
- 删除 Save/Save As 菜单、快捷键和 ViewModel 命令。
- 新增 `EditorDirtyState` 只跟踪 dirty 状态，不携带路径。

**Why This Works:** Editor 不再保留旧 `.toml` 项目文件或旧目录组织方式的代码路径。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 替换旧 BiomeConfig 架构描述。
- [x] Update `docs/CURRENT_FEATURES.md` - Runtime 状态改为虚拟资源 Bootstrap / descriptor 驱动。

### Architectural Decisions That Changed
- **Changed:** Runtime 地形资源入口。
- **From:** 组件路径字段 + `RuntimeBiomeConfig` TOML。
- **To:** `LaunchSetting.json` + resolver/bootstrap + fixed companion resources。
- **Scope:** Terrain Runtime 加载、材质数组初始化、detail map 生成、Editor exporter layout helper 引用。
- **Reason:** 支持 base 隐式根和 mod 覆盖顺序，移除旧路径兼容。

---

## Code Quality Notes

### Testing
- **Tests Written:** `RuntimeMigrationTextTests`、`RuntimeBiomeMaskReaderTests`，并扩展 descriptor/bootstrap reader tests。
- **Verification:** `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj` 通过。
- **Build:** `dotnet build Terrain.sln` 通过。
- **Old Path Sweep:** `rg` 未再命中 `ProjectManager`、`TomlProjectConfig`、`SaveProject`、`LoadProject`、`heightmaps`、`splatmaps` 等旧项目路径/命令符号。

### Technical Debt
- **Paid Down:** 删除旧 `RuntimeBiomeConfig` 和 `Shared` 源文件 Link。
- **Remaining Warnings:** 构建仍报告既有 NuGet vulnerability warnings、EditorGlobalLodMap 未赋值字段、TerrainManager 未使用事件，以及 WinForms DPI manifest 警告。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 如需真实运行验证，准备 Runtime 输出目录下的 `LaunchSetting.json` 与 `map_data/` 资源树。
2. 后续 Editor Export 可接入新 `map_data/terrain.terrain` 写回语义。
3. 若要处理构建警告，单独升级/审查 NuGet 包和现有可空警告。

### Docs to Read Before Next Session
- `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`
- `docs/log/2026/06/13/runtime-virtual-resource-migration.md`

---

## Session Statistics

**Files Changed:** 约 17 个任务相关文件
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Runtime 入口现在是 `Terrain/Core/TerrainProcessor.cs` 的 `LoadRuntimeResourceBundle()`。
- `.terrain` 仍通过 `bundle.TerrainDataPath` 读取，不从组件字段读取。
- `RuntimeDetailMapBuilder` 把 `RuntimeBiomeSettings.layers[].material_id` 映射到 descriptor material `index`。
- Runtime detail map 生成读取固定 `bundle.BiomeMaskPath`，要求 PNG 尺寸匹配 `.terrain` splatmap 尺寸。
- 材质贴图路径由 `TerrainRuntimeBootstrap` 通过 resolver 解析，`RuntimeMaterialManager` 不再拼相对路径。
- `Shared/*.cs` 已删除，项目不再 Link `..\Shared\*.cs`。

**Gotchas for Next Session:**
- 不要重新添加 `TerrainComponent.TerrainDataPath` / `BiomeConfigPath`。
- 不要重新创建 `RuntimeBiomeConfig` 或旧 TOML 兼容入口。
- 不要重新添加 `BiomeConfigExporter` 或 `ExportBiomeConfigCommand`。
- 不要重新添加 `NewProjectCommand` / `OpenProjectCommand`、`Ctrl+N` / `Ctrl+O`。
- 不要重新添加 `ProjectManager` / `TomlProjectConfig`、`SaveProjectCommand` / `SaveProjectAsCommand` 或旧 `heightmaps` / `splatmaps` 项目目录写入。
- `VirtualTextureLayout` 需要保持 public，因为 Editor exporter 通过 Terrain 项目引用它。

---

## Links & References

### Related Documentation
- `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`
- `docs/log/2026/06/13/virtual-resource-system-design-finalization.md`

### Code References
- Runtime bootstrap usage: `Terrain/Core/TerrainProcessor.cs`
- Resource bundle: `Terrain/Resources/TerrainRuntimeBootstrap.cs`
- Detail generation: `Terrain/Materials/RuntimeDetailMapBuilder.cs`
- Material arrays: `Terrain/Materials/RuntimeMaterialManager.cs`

---

*Template Version: 1.0 - Based on Archon-Engine template*
