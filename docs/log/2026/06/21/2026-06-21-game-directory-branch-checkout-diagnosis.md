# Game Directory Branch Checkout Diagnosis
**Date**: 2026-06-21
**Session**: 2
**Status**: Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Explain why `E:\Stride Projects\Terrain\game` disappears or becomes empty after Git branch changes.

---

## Context & Background

- Architecture docs already state that `game/` is no longer Git-tracked and is intended to be managed as an SVN/local resource workspace.
- `.gitignore` contains `/game/`.
- Current Git index has no tracked `game` paths.

---

## What We Did

### Git State Inspection
**Files Changed:** none in source code

- Checked `git status --short --branch`.
- Checked whether `game` exists locally.
- Checked tracked files with `git ls-files --stage -- game .gitmodules .gitignore`.
- Checked ignore rules with `git check-ignore -v game game/*`.
- Checked reflog and historical trees.

### Local Git Record Cleanup
**Files Changed:** local `.git` metadata only

- Confirmed no reachable branch, remote ref, or tag still tracks `game/`.
- Found old `game/` records only in local reflog commits.
- Ran `git reflog expire --expire=now --expire-unreachable=now --all`.
- Ran `git gc --prune=now --aggressive`.
- Verified `git reflog`, `git for-each-ref`, `git log --all -- game`, and `git fsck --unreachable --no-reflogs` no longer expose `game/` records.

---

## Findings

### Root Cause

`game/` was intentionally moved out of Git ownership. Current `.gitignore` line 19 ignores `/game/`, and current `HEAD` tracks no files under `game`.

The confusing behavior comes from branch/history transitions:
- Older reflog commits still had `game/map/...` files tracked by Git.
- Current history/branch state ignores `game/` and expects it to be populated externally.
- When switching from a commit or branch where `game` files are tracked to one where they are removed, Git removes those tracked files from the working tree as part of checkout.
- After that, because `/game/` is ignored, Git will not restore or show missing contents in `git status`.

No active non-sample Git hooks were found.

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `game/` disappearing on branch switch is expected if the previous branch/commit tracked those files and the target branch does not.
- The canonical resource source should be SVN/local restore, not Git checkout.
- Old local reflog records that still exposed tracked `game/` files were cleaned on 2026-06-21 with reflog expiration and garbage collection.
- Do not re-add `game/` to Git unless the architecture decision changes.

**Gotchas for Next Session:**
- `git status` will not show missing `game` resources because `/game/` is ignored.
- Branch switching is not safe as a resource preservation mechanism for `game/`.
