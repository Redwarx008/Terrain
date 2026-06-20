# RenderDoc MCP Tool Mount Root Cause
**Date**: 2026-06-18
**Session**: renderdoc-mcp-tool-mount-root-cause
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 查明为什么 `renderdoc-mcp` 已安装却没有在当前 Codex 会话中挂成可调用工具。

**Secondary Objectives:**
- 区分配置错误、进程启动失败、协议不兼容这几类可能根因。

**Success Criteria:**
- 给出单一主根因，并留下可复现证据链。

---

## Context & Background

**Previous Work:**
- See: [2026-06-18-renderdoc-mcp-availability-check.md](./2026-06-18-renderdoc-mcp-availability-check.md)
- See: [2026-06-18-renderdoc-mcp-current-session-recheck.md](./2026-06-18-renderdoc-mcp-current-session-recheck.md)

**Current State:**
- `C:\Users\Redwa\.codex\config.toml` 中存在 `[mcp_servers.renderdoc-mcp]`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe` 存在
- 当前 Codex 会话依旧没有暴露 `renderdoc-mcp` 工具

**Why Now:**
- 用户要求查明“为什么没把 `renderdoc-mcp` 挂成可调用工具”。

---

## What We Did

### 1. 核对本机配置与二进制
**Files Changed:** None

**Implementation:**
- 读取 `C:\Users\Redwa\.codex\config.toml`
- 确认 `[mcp_servers.renderdoc-mcp]` 指向本机 `renderdoc-mcp.exe`
- 检查 `bin/` 目录，确认 `renderdoc-mcp.exe`、`renderdoc-cli.exe`、`renderdoc.dll` 存在

**Rationale:**
- 先排除“没装好”或“路径写错”这类低层问题。

### 2. 直接手动握手 `renderdoc-mcp.exe`
**Files Changed:** None

**Implementation:**
- 用最小 Python 探针直接启动 `renderdoc-mcp.exe`
- 按它自己的 stdin/stdout 预期发送单行 JSON `initialize`
- 成功收到 `initialize` 响应，并在 `tools/list` 中返回 59 个工具

**Rationale:**
- 证明服务本身不是坏的，也不是工具注册为空。

### 3. 对标准 MCP 帧格式做兼容性探针
**Files Changed:** None

**Implementation:**
- 向 `renderdoc-mcp.exe` 发送标准 MCP stdio 帧：
  - `Content-Length: <n>\r\n\r\n<json>`
- 服务先返回：
  - `Parse error ... invalid literal; last read: 'C'`
- 随后才把后面的 JSON body 当成下一行继续处理

**Rationale:**
- 这证明服务把 `Content-Length:` 头行当成普通 JSON 文本在解析，而不是按 MCP 帧读取。

### 4. 复核源码、测试和 Codex 日志
**Files Changed:** None

**Implementation:**
- 读取 `src/main.cpp`
- 读取 `tests/integration/test_protocol.cpp`
- 查询 `C:\Users\Redwa\.codex\logs_2.sqlite`

**Found:**
- `src/main.cpp` 使用 `std::getline(std::cin, line)` 逐行读取请求，并用 `std::cout << line << "\n"` 逐行输出响应
- `tests/integration/test_protocol.cpp` 明确把协议写成 “JSON-RPC over newline-delimited JSON”
- Codex 日志中 `codex_mcp::connection_manager` 对 `renderdoc-mcp` 一直停留在：
  - `waiting for MCP server tools ... server_name=renderdoc-mcp has_cached_tool_info_snapshot=false startup_complete=true`
- 同一时刻 `blender-mcp` / `node_repl` 都能出现 `listed MCP server tools`

**Rationale:**
- 把“服务自身可运行”与“Codex 不能完成工具发现”的断点固定在协议层。

---

### 5. 实现标准 MCP stdio framing 并本地验证
**Files Changed:** `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\main.cpp`, `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\tests\integration\test_protocol.cpp`, `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\tests\integration\test_workflow.cpp`

**Implementation:**
- 把 `src/main.cpp` 从 `getline`/`\n` 单行 JSON 收发改成：
  - 输入端解析 `Content-Length: <n>\r\n\r\n<body>`
  - 输出端按相同 framing 写回 JSON-RPC 响应
- 先把两个集成测试夹具改成标准 framing，作为 TDD 的红灯前置
- 使用本地 NMake + VS 2026 工具链重编译 `renderdoc-mcp.exe`
- 跑 `ctest -R Protocol --output-on-failure`
- 用配置实际指向的 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe` 手工发送 framed `initialize`

**Rationale:**
- 必须先让测试和客户端都说“标准 MCP”，才能证明修的是根因而不是某个旁路。

**Result:**
- `ProtocolTest.InitializeHandshake` / `ToolsListComplete` / `ParseError_MalformedJson` / `MethodNotFound_UnknownMethod` / `BatchRequest_ArrayResponse` / `ProcessStable_MultipleRequests` 全部通过
- 配置路径下的 `renderdoc-mcp.exe` 能返回标准响应头：
  - `Content-Length: 150`
  - `{"id":1,"jsonrpc":"2.0","result":...}`
- 旧 `bin\renderdoc-mcp.exe` 被 Codex 已启动实例占用；停止旧实例后已覆盖为修复版

---

## Decisions Made

### Decision 1: 认定根因在 stdio 协议实现，不在配置或安装
**Context:** 配置、二进制、进程启动都已通过。
**Options Considered:**
1. `config.toml` 写错
2. `renderdoc-mcp.exe` 启动失败
3. `renderdoc-mcp.exe` 的 stdio 协议与 Codex MCP 客户端不兼容

**Decision:** 选择 3
**Rationale:** 只有协议不兼容，才能同时解释：
- 手动单行 JSON 握手成功
- 标准 `Content-Length` 帧先触发 parse error
- Codex 进程已启动但始终拿不到工具列表
**Trade-offs:** 需要调整现有“单行 JSON”测试夹具，但这是必要的纠偏。
**Documentation Impact:** 更新本次会话日志即可。

### Decision 2: 先做最小 transport 修复，不扩展双协议兼容
**Context:** 当前唯一证据链都指向 stdio framing 不兼容。
**Options Considered:**
1. 仅支持标准 MCP `Content-Length` framing
2. 同时兼容旧单行 JSON 和标准 framing

**Decision:** 先做 1
**Rationale:** Codex 需要的是标准 MCP，先用最小变更把挂载打通，再决定是否保留历史兼容层。
**Trade-offs:** 旧的非标准客户端如果仍按单行 JSON 通信，会失效。
**Documentation Impact:** 暂不需要额外 ADR。

---

## What Worked ✅

1. **把问题拆成三层验证**
   - What: 配置层、服务层、Codex 客户端层分别检查
   - Why it worked: 很快排除了“安装没成功”的伪线索
   - Reusable pattern: Yes

2. **用标准 MCP 帧直接探针**
   - What: 手工发送 `Content-Length` 帧
   - Why it worked: 一次就把协议不兼容钉死
   - Reusable pattern: Yes

3. **先改测试夹具再改生产代码**
   - What: 把集成测试切到标准 framing 后再修 `main.cpp`
   - Why it worked: 能明确证明修复前后的协议行为差异
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **继续把问题理解成“会话没刷新”**
   - What we tried: 之前多次仅凭 `tool_search` 和重开会话判断
   - Why it failed: 根因不是刷新，而是 server 根本没实现 Codex 期望的 stdio 帧协议
   - Lesson learned: “启动了但没有工具”不等于“只要重开会话就会好”
   - Don't try this again because: 会反复卡在表象层

---

## Problems Encountered & Solutions

### Problem 1: `renderdoc-mcp` 已启动但 Codex 永远拿不到工具列表
**Symptom:** 当前会话没有 `renderdoc-mcp` 工具名，日志显示 connection manager 一直等待 tool snapshot。
**Root Cause:** `renderdoc-mcp.exe` 使用 newline-delimited JSON，而不是标准 MCP stdio 的 `Content-Length` 帧格式。
**Investigation:**
- Tried: 检查 `config.toml`
- Tried: 手动启动并用单行 JSON 握手
- Tried: 发送标准 MCP `Content-Length` 帧
- Found: 服务会把 `Content-Length:` 头行当成普通 JSON，直接报 parse error

**Solution:**
- 后续需要修改 `renderdoc-mcp` 的 stdio transport：
  - 输入端按 `Content-Length` 读取完整 JSON message
  - 输出端按 `Content-Length` + body 写回
  - 不再把“单行 JSON”当作 MCP stdio 协议

**Why This Works:** Codex 的 MCP 客户端才能完成 `initialize -> tools/list` 标准握手并生成可调用工具。
**Pattern for Future:** 自定义 MCP server 接入 Codex 前，必须先验证 stdio framing，而不只验证 JSON-RPC payload 本身。

### Problem 2: 构建产物与配置实际使用的二进制不是同一份
**Symptom:** 测试里的新 `renderdoc-mcp.exe` 已通过，但 `config.toml` 指向的仍是 `bin\renderdoc-mcp.exe`。
**Root Cause:** 本地修复默认产出在 `renderdoc-mcp-build-nmake\`，不会自动覆盖 `bin\`。
**Investigation:**
- Tried: 直接运行协议测试
- Found: 通过的是构建目录，不是配置路径
- Found: `bin\renderdoc-mcp.exe` 被两个已启动的旧进程占用

**Solution:**
- 停止旧的 `renderdoc-mcp` 进程
- 用新构建产物覆盖 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- 保留一份时间戳备份

**Why This Works:** Codex 配置无需修改，只要重连 MCP server 就会使用修复版二进制。
**Pattern for Future:** 本地验证通过后，要确认“被配置消费的产物”也同步更新。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，项目系统状态未变化
- [ ] Update `CURRENT_FEATURES.md` - 不需要，功能状态未变化

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 newline-delimited JSON 当作 MCP stdio
- What not to do: 用 `getline`/`\n` 直接承载 MCP 请求
- Why it's bad: 在 Codex 中会表现为服务启动但工具永远不挂载
- Add warning to: 如后续继续维护本地 MCP，可考虑提炼到 learnings

---

## Code Quality Notes

### Testing
- **Manual Tests:**
  - 配置检查
  - 二进制存在性检查
  - 单行 JSON `initialize` / `tools/list`
  - 标准 `Content-Length` 帧兼容性探针
  - Codex `logs_2.sqlite` 查询
- **Coverage:** 已覆盖配置、服务启动、协议解析、Codex 工具发现四层

### Technical Debt
- **Remaining:** 当前 Codex 会话是否会热重连未知，仍需要重启或新会话验证工具真正出现在工具列表中
- **Paid Down:** `renderdoc-mcp` 本地二进制已实现标准 MCP stdio transport

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重启 Codex 或开启新会话，验证 `renderdoc-mcp` 是否真正挂成可调用工具
2. 成功后再决定是否整理并提交上游/个人仓库 PR
3. 如需兼容历史客户端，再评估是否补双协议支持

### Questions to Resolve
1. 当前 Codex 桌面端是否支持在不重启应用的情况下热重连该 stdio MCP server
2. 修复后是否还需要兼容旧的单行 JSON 调试模式

### Docs to Read Before Next Session
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\main.cpp` - 当前错误 transport 实现
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\tests\integration\test_protocol.cpp` - 当前错误测试假设

---

## Session Statistics

**Files Changed:** 4
**Lines Added/Removed:** 协议修复 + 测试夹具更新 + 日志补充
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `renderdoc-mcp` 没挂上不是因为没装，而是因为 transport 不兼容
- 现在本地源码和 `bin\renderdoc-mcp.exe` 都已经改成标准 `Content-Length` framing
- 协议集成测试 6/6 通过，手工 `initialize` 探针也返回了标准 MCP 响应
- 还差一步应用级验证：重启 Codex 或新会话确认工具列表真正出现

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 已完成 transport 修复并更新测试
- Constraints: 当前会话工具清单不会自动刷新，不能在本线程里直接看到新工具挂载

**Gotchas for Next Session:**
- 不要只看构建目录通过，还要确认 `config.toml` 指向的 `bin\renderdoc-mcp.exe` 已替换
- 如果本会话仍看不到工具，优先做应用重启/新会话验证，不要回头怀疑 framing 修复

---

## Links & References

### Related Sessions
- [2026-06-18-renderdoc-mcp-availability-check.md](./2026-06-18-renderdoc-mcp-availability-check.md)
- [2026-06-18-renderdoc-mcp-current-session-recheck.md](./2026-06-18-renderdoc-mcp-current-session-recheck.md)

### Code References
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\main.cpp`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\tests\integration\test_protocol.cpp`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\tests\integration\test_workflow.cpp`
- `C:\Users\Redwa\.codex\config.toml`

---
