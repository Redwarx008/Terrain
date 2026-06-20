# RenderDoc MCP Availability Check
**Date**: 2026-06-18
**Session**: renderdoc-mcp-availability-check
**Status**: ✅ Complete
**Priority**: Low

---

## Session Goal

**Primary Objective:**
- 确认当前 Codex 会话里 `renderdoc-mcp` 是否真的可用。

**Secondary Objectives:**
- 区分“本机已安装并配置”与“本会话可直接调用”这两个状态。

**Success Criteria:**
- 给出明确结论：当前会话是否能直接使用 `renderdoc-mcp` MCP 工具。

---

## Context & Background

**Previous Work:**
- See: [2026-06-18-renderdoc-mcp-local-install-update.md](./2026-06-18-renderdoc-mcp-local-install-update.md)
- See: [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md)

**Current State:**
- 仓库日志已记录本机 `renderdoc-mcp` 于 2026-06-18 完成升级并验证。
- 本次需要确认的是“当前这条 Codex 会话”是否拿到了可调用的 MCP 工具，而不是仅仅确认磁盘上装过。

**Why Now:**
- 用户直接询问当前 `renderdoc mcp` 是否可用。

---

## What We Did

### 1. 恢复仓库上下文并读取相关 RenderDoc 日志
**Files Changed:** None

**Implementation:**
- 读取 `docs/ARCHITECTURE_OVERVIEW.md`
- 读取最新会话日志 `docs/log/2026/06/18/2026-06-18-river-review-followup-editor-shadow-behavior-test.md`
- 读取 `docs/CURRENT_FEATURES.md`
- 读取 `docs/log/2026/06/18/2026-06-18-renderdoc-mcp-local-install-update.md`

**Rationale:**
- 按仓库会话规则恢复上下文，并确认最近一次 `renderdoc-mcp` 安装更新的证据链。

### 2. 核对 skill 与当前会话工具暴露情况
**Files Changed:** None

**Implementation:**
- 读取 `C:/Users/Redwa/.codex/skills/renderdoc-mcp/SKILL.md`
- 使用 `tool_search` 搜索 `renderdoc-mcp` 相关工具
- 结果未获得 `renderdoc-mcp` 命名空间工具，反而只暴露出 `mcp__blender_mcp` 工具

**Rationale:**
- 这一步用于判断“当前会话是否真的挂载了 RenderDoc MCP”，而不是仅凭安装状态推断。

### 3. 核对本机配置与二进制存在性
**Files Changed:** None

**Implementation:**
- 检查 `C:\Users\Redwa\.codex\config.toml`
- 确认 `[mcp_servers.renderdoc-mcp]` 存在，且指向：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- 确认该 exe 存在，最后修改时间为 `2026-06-18 00:46:33`
- 检查当前未发现正在运行的 `renderdoc-mcp` 进程

**Rationale:**
- 区分“当前会话工具未暴露”和“本机根本没装/没配”。

---

## Decisions Made

### Decision 1: 以“当前会话是否暴露可调用工具”作为可用性判断标准
**Context:** 用户问的是“现在可用吗”，需要回答实际能不能在本会话里直接用。
**Options Considered:**
1. 只看磁盘安装状态
2. 只看仓库日志
3. 同时看安装状态与当前会话工具暴露状态

**Decision:** 选择 3
**Rationale:** 只有这样才能区分“已安装”与“当前会话可调用”。
**Trade-offs:** 需要额外做一次工具发现核对。
**Documentation Impact:** 无

---

## What Worked ✅

1. **把安装状态和会话状态拆开看**
   - What: 同时检查 `config.toml`、exe 存在性和 `tool_search` 结果
   - Why it worked: 可以直接定位问题落在“会话工具注册/暴露”而不是“本地安装缺失”
   - Reusable pattern: Yes

2. **优先读取最近 RenderDoc 日志**
   - What: 先读 2026-06-18 的安装更新日志
   - Impact: 快速确认本机安装本身并没有回退

---

## What Didn't Work ❌

1. **直接通过 `tool_search` 找到 `renderdoc-mcp` 工具**
   - What we tried: 用 `tool_search` 以 `renderdoc-mcp`、`open_capture` 等关键词搜索
   - Why it failed: 当前会话没有暴露出对应命名空间工具，搜索结果只带出了 `blender-mcp`
   - Lesson learned: `tool_search` 无结果时，不能把它误判为“本地未安装”
   - Don't try this again because: 这会混淆“注册问题”和“安装问题”

---

## Problems Encountered & Solutions

### Problem 1: 已安装的 RenderDoc MCP 没有在本会话中暴露为可调用工具
**Symptom:** 搜索不到 `renderdoc-mcp` 工具命名空间。
**Root Cause:** 当前会话的工具暴露状态与本机安装状态不一致。
**Investigation:**
- Tried: 读取 skill 文档
- Tried: 使用 `tool_search`
- Tried: 检查 `config.toml` 与 exe 路径
- Found: 本机安装和配置存在，但当前会话没有实际暴露 RenderDoc MCP 工具

**Solution:**
- 本次给出明确判定：
  - 本机层面：`renderdoc-mcp` 已安装且配置存在
  - 当前会话层面：不可直接调用，因未暴露对应 MCP 工具

**Why This Works:** 结论与证据一一对应，不会把不同层面的状态混为一谈。
**Pattern for Future:** 回答 MCP 可用性时，始终分成“安装状态”和“当前会话可调用状态”两层。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，系统状态未变化
- [ ] Update `CURRENT_FEATURES.md` - 不需要，功能状态未变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** MCP 可用性分层判断
- When to use: 用户询问某个本地 MCP“现在能不能用”时
- Benefits: 能快速区分安装层问题和会话挂载层问题
- Add to: 暂不需要独立抽取

### Architectural Decisions That Changed
- 无

---

## Code Quality Notes

### Testing
- **Manual Tests:** `tool_search` 工具发现、`config.toml` 检查、二进制存在性检查、进程检查
- **Coverage:** 会话可调用性与本机安装状态

### Technical Debt
- **Remaining:** 仍需进一步定位为什么当前会话没有暴露 `renderdoc-mcp` 命名空间工具

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新确认新会话或工具刷新后是否会暴露 `renderdoc-mcp` 命名空间
2. 若仍不可用，继续排查 Codex 的 MCP 注册/加载链路
3. 对比 `blender-mcp` 与 `renderdoc-mcp` 的配置差异

### Questions to Resolve
1. 当前会话的 MCP 工具暴露是否需要重开会话后才刷新？
2. `tool_search` 结果为什么会错误偏向 `blender-mcp`？

### Docs to Read Before Next Session
- [2026-06-18-renderdoc-mcp-local-install-update.md](./2026-06-18-renderdoc-mcp-local-install-update.md) - 本地安装更新证据链

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +1 log / -0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `renderdoc-mcp` 在本机配置中存在：`C:\Users\Redwa\.codex\config.toml`
- 安装二进制存在：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- 当前这条会话没有暴露 `renderdoc-mcp` 命名空间工具，无法直接调用

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无
- Constraints: 当前会话工具暴露状态与本机安装状态不一致

**Gotchas for Next Session:**
- 不要把“本地安装存在”等同于“本会话可直接用”
- 先看工具命名空间是否真的被暴露出来
- `tool_search` 结果异常时要回到配置和二进制做二次验证

---

## Links & References

### Related Sessions
- [2026-06-18-renderdoc-mcp-local-install-update.md](./2026-06-18-renderdoc-mcp-local-install-update.md)
- [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md)

### Code References
- MCP config: `C:\Users\Redwa\.codex\config.toml`
- Installed binary: `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`

---
