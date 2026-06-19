# `game/map` 目录重命名
**Date**: 2026-06-20
**Session**: Terrain resource path rename
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 将 `game/map_data` 重命名为 `game/map`，并同步更新代码、测试与当前架构文档中的路径引用。

**Success Criteria:**
- 真实目录已从 `game/map_data` 迁移到 `game/map`。
- 运行时代码、Editor、测试中的硬编码路径已切到 `map/...`。
- `docs/ARCHITECTURE_OVERVIEW.md` 和 `docs/CURRENT_FEATURES.md` 反映新路径。
- 不影响历史日志与设计归档。

---

## Context & Background

**Previous Work:**
- 最近会话主要在收敛河流渲染与水体资源链路。

**Current State:**
- 仓库中原本存在大量 `map_data` 路径常量和测试虚拟路径。
- `game/` 资源根本身是当前运行时资源入口。

**Why Now:**
- 用户要求把 `game/map_data` 改成 `game/map`，因此需要同步资源根、运行时 bootstrap 和作者态写回路径。

---

## What We Did

### 1. 重命名真实资源目录
**Files Changed:** `game/map_data` -> `game/map`

**Implementation:**
- 直接在工作区把目录从 `game/map_data` 迁移到 `game/map`。

### 2. 替换代码与测试中的虚拟路径
**Files Changed:** `Terrain/Resources/GameRuntimeResourceBootstrap.cs`, `Terrain/Resources/GameResourceRootLocator.cs`, `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`, `Terrain.Editor/Services/Resources/EditorMapDataScaffoldService.cs`, `Terrain.Editor/Services/Resources/EditorMaterialRecoveryService.cs`, 多个 `Terrain.Editor.Tests/VirtualResources/*` 测试

**Implementation:**
- 把运行时与 Editor 的虚拟路径从 `map_data/...` 改为 `map/...`。
- 更新测试中所有资源根、writer 目标、诊断消息和 fake resource path。

### 3. 更新当前状态文档
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`

**Implementation:**
- 把当前状态文档里描述的资源目录从 `map_data` 改为 `map`。

---

## Verification

Ran:
```powershell
rg -n "map_data" Terrain Terrain.Editor Terrain.Editor.Tests game docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md
dotnet test Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GameResourceRootLocatorTests"
git diff --check
```

Results:
- 代码、测试和当前状态文档范围内已没有 `map_data` 命中。
- `game` 目录下当前实际为 `map`。
- `dotnet test` 对测试项目完成了成功生成。
- `git diff --check` 只给出行尾换行提示，没有格式错误。

---

## Notes

- 历史日志和设计归档中的 `map_data` 仍保留原文，不做批量回写。

