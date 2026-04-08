# 修复 Claude Code Hooks 工作流
**Date**: 2026-04-08
**Session**: 1
**Status**: Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复 settings.local.json 中错误的 hooks 配置格式，使工作流规则真正生效

**Success Criteria:**
- hooks 使用正确的 Claude Code 嵌套格式
- SessionStart 自动注入上下文
- Stop 阻止未完成日志的会话结束
- PreToolUse 检查 git commit 前的日志

---

## What We Did

### 1. 重写 hooks 配置格式
**Files Changed:** `.claude/settings.local.json:34-70`

将扁平格式 (`"session-start": { "command": "echo ..." }`) 替换为正确的嵌套格式：
- `SessionStart` → PowerShell 脚本注入 `additionalContext`
- `Stop` → 检查日志/完整性/code review，`decision: "block"` 阻止
- `PreToolUse` + `Bash(git commit*)` → 提交前检查日志

### 2. 修正 session-start.ps1 输出格式
**Files Changed:** `.claude/hooks/session-start.ps1`

- 改用 `hookSpecificOutput.additionalContext` 替代无效的 `systemMessage`
- 使用 `$CLAUDE_PROJECT_DIR` 环境变量

### 3. 修正 stop.ps1 输入输出
**Files Changed:** `.claude/hooks/stop.ps1`

- 正确从 stdin 读取 JSON 获取 `transcript_path`
- 添加 `stop_hook_active` 检查防止无限循环
- 使用标准 `decision: "block"` + `reason` 格式

### 4. 新建 pre-commit-check.ps1
**Files Changed:** `.claude/hooks/pre-commit-check.ps1`

- git commit 前检查当日日志是否存在
- 使用 `hookSpecificOutput.permissionDecision: "deny"` 阻止提交

---

## Decisions Made

### Decision 1: 使用 PowerShell 而非 Bash
**Context:** Windows 环境下 PowerShell 原生支持 JSON 和文件操作
**Decision:** 所有 hook 脚本使用 PowerShell，配置中 `"shell": "powershell"`

### Decision 2: 移除旧的 session-end hook
**Context:** `session-end` 不是有效的 Claude Code 事件名，且 SessionEnd 不支持 decision control
**Decision:** 用 `Stop` 事件替代，支持 `decision: "block"` 阻止停止

### Decision 3: 用 PreToolUse 替代不存在的 pre-commit
**Context:** Claude Code 没有 `pre-commit` 事件
**Decision:** 用 `PreToolUse` + matcher `Bash` + if `Bash(git commit*)` 实现

---

## What Worked

1. **查阅官方文档**
   - 读取 code.claude.com/docs/en/hooks 获得完整 schema
   - 避免了猜测配置格式

---

## Next Session

### Immediate Next Steps
1. 验证 hooks 是否正确注册 (`/hooks` 命令)
2. 测试 SessionStart hook 是否注入上下文
3. 测试 Stop hook 阻止效果

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Hooks 格式: `EventName: [{ matcher?, hooks: [{ type, command, shell?, if? }] }]`
- SessionStart 输出: `hookSpecificOutput.additionalContext`
- Stop 阻止: `{ "decision": "block", "reason": "..." }`
- PreToolUse 拒绝: `hookSpecificOutput.permissionDecision: "deny"`

**Gotchas for Next Session:**
- SessionEnd 事件不支持 decision control，用 Stop 替代
- `pre-commit` 不是有效事件名
- Stop hook 必须检查 `stop_hook_active` 防无限循环
- `additionalContext` 有 10000 字符上限
