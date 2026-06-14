# Biome 删除按钮空绑定修复
**Date**: 2026-06-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 修复 Avalonia 绑定错误：`CommandParameter` 绑定到 `Biome.SelectedBiome.Id` 时，`SelectedBiome` 为 `null` 会报错

**Secondary Objectives:**
- 为这个回归加自动测试

**Success Criteria:**
- XAML 不再直接解引用 `SelectedBiome.Id`
- 删除 biome 按钮的命令可执行状态与选中状态一致

---

## Context & Background

**Current State:**
- `MainWindow.axaml` 中 biome 删除按钮使用 `CommandParameter="{Binding Biome.SelectedBiome.Id}"`
- `BiomeViewModel.SelectedBiome` 在集合刷新/切换瞬间可能为 `null`

**Why Now:**
- 该绑定错误会持续污染输出日志，且本质上是 UI 对可空状态处理不当

---

## What We Did

### 1. 改掉 XAML 中对 `SelectedBiome.Id` 的直接解引用
**Files Changed:** `Terrain.Editor/Views/MainWindow.axaml`, `Terrain.Editor/ViewModels/BiomeViewModel.cs`

**Implementation:**
- 将删除按钮的参数改为 `CommandParameter="{Binding Biome.SelectedBiome}"`
- `RemoveBiome` 命令改为接收 `BiomeDefinitionViewModel?`
- 新增 `CanRemoveSelectedBiome`，用 `RelayCommand(CanExecute = ...)` 控制按钮可执行状态
- 在 `RefreshCommandStates()` 中补 `RemoveBiomeCommand.NotifyCanExecuteChanged()`

**Rationale:**
- `SelectedBiome` 本身允许为 `null`，但绑定到整个对象不会触发中间属性解引用错误
- 是否能删除应由 ViewModel 统一决定，而不是依赖 XAML 参数永远非空

---

## Testing

- 新增文本回归测试：
  - `editor biome remove command does not dereference selected biome in XAML`
- 验证结果：
  - `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
    - 新增回归测试通过
    - 全量命令最终仍失败，但原因为本地未跟踪文件 `game/map_data/terrain.terrain` 触发既有 scaffold 断言，与本次修复无关
  - `dotnet build Terrain.sln -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false`
    - 通过

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 这个问题的根因不是命令实现，而是 XAML 对可空 `SelectedBiome` 的链式绑定
- 若类似问题再次出现，优先避免在 XAML 中解引用可能为空的中间对象

**Gotchas for Next Session:**
- 本地存在未跟踪生成物 `game/map_data/terrain.terrain`
- 若要让当前全量测试集重新全绿，需要先处理这个本地生成文件

