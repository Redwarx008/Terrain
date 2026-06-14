# Git 历史中的 game 资源清理
**Date**: 2026-06-14
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 清理本地 Git 历史里残留的 `game/**` 对象，并确认正式分支历史中不再可达

**Secondary Objectives:**
- 删除本地 `worktree-agent-*` 分支及其工作树
- 删除 `refs/codex/*` 辅助引用
- 保留一份仓库镜像备份与 `game/` 磁盘备份，防止清理过程误伤

**Success Criteria:**
- `git rev-list --objects --all` 不再出现 `game/**` 路径
- 本地仓库执行 `git gc --prune=now` 后体积明显回落
- 正式分支无需再为 `game/**` 做远端 force-push

---

## Context & Background

**Previous Work:**
- See: [local-launch-settings-and-svn-game-root.md](./local-launch-settings-and-svn-game-root.md)
- See: [local-launch-settings-review-followup.md](./local-launch-settings-review-followup.md)

**Current State:**
- `game/` 已退出 Git 跟踪，但本地对象库中仍残留历史里的 `game/**` blob
- 推送失败问题此前已通过干净提交规避，远端 `master` 已不再携带这些大文件历史

**Why Now:**
- 用户要求彻底处理本地所有分支/标签及辅助引用里的 `game/**` 历史残留

---

## What We Did

### 1. 建立双重备份
**Files Changed:** none

**Implementation:**
- 备份当前磁盘上的 `game/` 到：
  `E:\Stride Projects\Terrain-history-cleanup-backups\game-backup-20260614-225449`
- 备份整个仓库镜像到：
  `E:\Stride Projects\Terrain-history-cleanup-backups\repo-mirror-backup-20260614-225449.git`

**Rationale:**
- 后续需要删除 refs、移除 worktree、执行 reflog/gc，必须先留可回滚源

### 2. 在独立 mirror 中验证正式分支历史
**Files Changed:** none

**Implementation:**
- 创建工作 mirror：
  `E:\Stride Projects\Terrain-history-cleanup-backups\repo-mirror-rewrite-work-20260614-225507.git`
- 在 mirror 中先删除：
  - `refs/codex/*`
  - `refs/remotes/origin/*`
  - `refs/heads/worktree-agent-*`
- 再把 `origin/fix/viewport-input-focus` 提升为本地分支 `fix/viewport-input-focus`
- 运行：
  `git filter-branch --force --index-filter "git rm -r --cached --ignore-unmatch -- game" --prune-empty --tag-name-filter cat -- --branches --tags`

**Rationale:**
- 在 mirror 中动历史，比直接改当前工作树更安全
- 先删辅助引用，才能分辨到底是正式分支还是临时 refs 在挂住旧对象

### 3. 确认正式分支其实早已不含 `game/**`
**Files Changed:** none

**Implementation:**
- `filter-branch` 对所有保留的正式分支都报告 `unchanged`
- `git rev-list --objects --branches --tags` 对 `game/**` 无任何输出

**Rationale:**
- 这说明需要清理的不是远端正式分支历史，而是本地辅助引用与 agent 分支

### 4. 清理当前仓库的本地辅助引用并回收对象
**Files Changed:** none

**Implementation:**
- 删除 `.claude/worktrees/agent-*` 工作树
- 删除所有 `worktree-agent-*` 本地分支
- 删除所有 `refs/codex/*`
- 运行：
  - `git reflog expire --expire=now --expire-unreachable=now --all`
  - `git gc --prune=now`

**Rationale:**
- 这些本地引用才是旧 `game/**` 对象仍然可达或仍然滞留在对象库中的原因

---

## Decisions Made

### Decision 1: 不对远端正式分支做额外 force-push
- **Context:** 用户要求历史重写清理 `game/**`
- **Decision:** 先在 mirror 中验证正式分支是否真的含有 `game/**`
- **Rationale:** 避免对远端做不必要的历史改写
- **Outcome:** 正式分支全部 `unchanged`，因此没有执行新的远端 force-push

### Decision 2: 清理本地辅助引用，而不是继续改公开历史
- **Context:** 旧对象仍存在于本地对象库
- **Decision:** 删除 `worktree-agent-*` 与 `refs/codex/*`，再配合 `reflog expire + gc`
- **Rationale:** 这是实际持有旧对象的来源，且风险显著低于重写公开历史

---

## Verification

- `git rev-list --objects --all | Select-String "game/..."`
  - 结果：无输出
- `git count-objects -vH`
  - 清理前：
    - `count: 6262`
    - `size: 350.97 MiB`
  - 清理后：
    - `count: 0`
    - `in-pack: 4501`
    - `size-pack: 16.73 MiB`
- `git status --short`
  - 在写本日志前：无输出，工作区干净

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 若后续再出现本地大对象残留，优先检查 `refs/codex/*` 与临时 agent worktree
2. 若要彻底避免 `game/` 被 Git 操作误删，仍应考虑把 `game/` 移出 Git 工作树

### Questions to Resolve
1. 是否要把当前残留的 detached Codex worktree 也纳入统一清理策略

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 正式分支历史其实已经不再包含 `game/**`
- 真正持有旧对象的是本地 `worktree-agent-*` 分支和 `refs/codex/*`
- 本次没有对远端公开分支做新的 force-push

**What Changed Since Last Doc Read:**
- Local repo state: agent worktree 分支与 `refs/codex/*` 已清理
- Storage: 本地对象库已从约 `350.97 MiB` loose objects 回收到约 `16.73 MiB` pack

**Gotchas for Next Session:**
- `game/` 仍然在 Git 工作树里，只是已忽略，不等于绝对安全
- 如果以后再次出现类似问题，先区分“正式分支历史”与“辅助引用/工作树残留”

---

*Template Version: 1.0 - Based on Archon-Engine template*
