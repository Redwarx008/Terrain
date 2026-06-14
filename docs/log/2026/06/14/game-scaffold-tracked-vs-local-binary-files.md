# Game Scaffold 二进制资源跟踪语义修正
**Date**: 2026-06-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 修复 `repository game scaffold boots editor without terrain data or biome mask` 测试失败

**Success Criteria:**
- 测试不再把本地存在的 `.terrain` / `biome_mask.png` 误判为 scaffold 错误
- 保留 Editor bootstrap 对缺失目标路径的解析断言

---

## Context & Background

**Current State:**
- 工作区里存在本地生成的 `game/map_data/terrain.terrain`
- 旧测试使用 `File.Exists(...) == false` 断言“仓库 scaffold 不应 check in `.terrain` / `biome_mask.png`”

**Why This Failed:**
- “仓库默认不提交这些文件”被错误实现成了“工作区里不能存在这些文件”
- 这会把合法的本地导出产物误判成 scaffold 错误

---

## What We Did

### 1. 收紧测试语义到“是否被 git 跟踪”
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/GameResourceScaffoldTextTests.cs`

**Implementation:**
- 删除 `File.Exists` 断言
- 改为通过 `git ls-files --error-unmatch` 判断：
  - `game/map_data/terrain.terrain`
  - `game/map_data/biome_mask.png`
  是否被仓库跟踪

**Rationale:**
- 这才对应“repository scaffold should not check in ...”的真实含义
- 本地存在未跟踪的二进制导出物不应导致测试失败

### 2. 撤回错误的 `.gitignore` 方向
**Files Changed:** `Terrain.Editor.Tests/VirtualResources/GameResourceScaffoldTextTests.cs`

**Implementation:**
- 删除我临时加入的 “必须被 `.gitignore` 忽略” 测试

**Rationale:**
- `.terrain` / `biome_mask.png` 是正经游戏资源产物，不应被全局 `.gitignore` 规则直接否定
- 是否跟踪应由具体资源策略决定，不应在这个测试里被强行约束成 ignore

---

## Decisions Made

### Decision 1: 用 “git tracked” 而不是 “file exists” 表达 scaffold 约束
**Context:** 用户明确指出 `.terrain` / `biome_mask.png` 是游戏二进制资源，不能简单用 `.gitignore` 语义处理

**Options Considered:**
1. 继续要求文件不存在
2. 要求文件被 `.gitignore`
3. 只要求仓库默认不跟踪

**Decision:** Chose Option 3
**Rationale:** 它准确对应当前产品语义，也兼容本地导出/保存工作流
**Trade-offs:** 测试依赖当前环境可执行 `git`

---

## Problems Encountered & Solutions

### Problem 1: 本地导出物污染了 scaffold 测试
**Symptom:** 测试失败提示 `repository scaffold should not check in terrain.terrain`
**Root Cause:** 本地存在未跟踪的 `game/map_data/terrain.terrain`，而测试错误地把“存在”当成“被提交”

**Solution:**
- 通过 `git ls-files --error-unmatch` 判断路径是否被跟踪

**Why This Works:** `git` 能直接表达“仓库是否收录此文件”，不会把本地未跟踪产物误判成 scaffold 错误

---

## Code Quality Notes

### Testing
- **Tests Updated:** `GameResourceScaffoldTextTests`
- **Verification:** `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` 全部通过

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `.terrain` / `biome_mask.png` 在当前 repo scaffold 语义里是“默认不跟踪”，不是“本地绝不能存在”
- 不要再把这个问题往 `.gitignore` 方向带

**Gotchas for Next Session:**
- 如果以后需要约束这些文件是否纳入版本控制，应在资源策略层明确，不要复用 scaffold bootstrap 测试去表达 ignore 规则

