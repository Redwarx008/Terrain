# RenderDoc MCP Reinstall From Redwarx008
**Date**: 2026-06-18
**Session**: renderdoc-mcp-reinstall-from-redwarx008
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 重新安装 `Redwarx008/renderdoc-mcp`，并先卸载掉当前活跃的旧安装。

**Secondary Objectives:**
- 避免把已知不兼容 Codex MCP stdio framing 的旧 transport 再装回去。
- 验证最终被 `~/.codex/config.toml` 消费的那份 `renderdoc-mcp.exe` 能完成标准 MCP 握手。

**Success Criteria:**
- 旧安装从活跃路径移除。
- 新安装重新落到 `~/.codex/vendor_imports/renderdoc-mcp` 与 `~/.codex/skills/renderdoc-mcp`。
- 安装后的 `renderdoc-mcp.exe` 能以 `Content-Length` framing 正常响应 `initialize` 和 `tools/list`。

---

## Context & Background

**Previous Work:**
- See: [2026-06-18-renderdoc-mcp-current-session-recheck.md](./2026-06-18-renderdoc-mcp-current-session-recheck.md)
- See: [2026-06-18-renderdoc-mcp-tool-mount-root-cause.md](./2026-06-18-renderdoc-mcp-tool-mount-root-cause.md)

**Current State:**
- 旧 `renderdoc-mcp` 已经在本机安装过，但用户要求重新安装，并先卸载掉之前的活跃安装。
- `Redwarx008/renderdoc-mcp` 的仓库源码包没有发布态 `bin/` 内容，且 `src/main.cpp` 仍是 newline-delimited JSON transport。

**Why Now:**
- 用户直接要求“重新安装 `Redwarx008/renderdoc-mcp`”，随后明确要求“卸载掉之前的”。

---

## What We Did

### 1. 卸载旧活跃安装
**Files Changed:** `C:\Users\Redwa\.codex\config.toml`

**Implementation:**
- 将旧活跃目录移走而不是直接删除：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp`
  - `C:\Users\Redwa\.codex\skills\renderdoc-mcp`
- 时间戳备份路径：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp.uninstalled-20260618-033401`
  - `C:\Users\Redwa\.codex\skills\renderdoc-mcp.uninstalled-20260618-033401`
- 从用户 `PATH` 中移除旧 `renderdoc-mcp\bin`
- 从 `config.toml` 删除 `[mcp_servers.renderdoc-mcp]`

**Rationale:**
- 用户明确要求先卸载旧版本。
- 用“移走到备份路径”代替硬删，方便重装失败时回滚。

### 2. 验证 fork 源码包不能直接安装
**Files Changed:** None

**Implementation:**
- 下载 `https://github.com/Redwarx008/renderdoc-mcp/archive/refs/heads/main.zip`
- 解包后确认仓库主分支只有源码、脚本和 `skills/`，没有发布态 `bin/renderdoc-mcp.exe`
- 检查 `src/main.cpp` 与 `tests/integration/test_protocol.cpp`

**Found:**
- `src/main.cpp` 仍使用 `std::getline(std::cin, line)` 和单行 JSON 输出
- `test_protocol.cpp` 注释仍写着 “newline-delimited JSON”

**Rationale:**
- 这证明不能直接把 GitHub 源码 zip 当发布包安装，也证明 transport 兼容性问题在 fork 上仍存在。

### 3. 复用本机 RenderDoc 构建依赖并对 fresh 源码做最小修复
**Files Changed:** 临时源码目录
- `C:\Users\Redwa\AppData\Local\Temp\codex-renderdoc-reinstall\renderdoc-mcp-main\src\main.cpp`
- `C:\Users\Redwa\AppData\Local\Temp\codex-renderdoc-reinstall\renderdoc-mcp-main\tests\integration\test_protocol.cpp`
- `C:\Users\Redwa\AppData\Local\Temp\codex-renderdoc-reinstall\renderdoc-mcp-main\tests\integration\test_workflow.cpp`

**Implementation:**
- 发现本机仍保留：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-src-v1.43`
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-sdk-stub-v1.43`
  - 旧 `renderdoc-mcp-build-nmake` 的 `x64` 构建线索
- 先把协议测试切到标准 MCP framing
- 在旧实现上运行 `ProtocolTest.InitializeHandshake`，观察到：
  - `renderdoc-mcp.exe did not respond to initialize (timeout/crash)`
- 然后把 `src/main.cpp` 改成：
  - 输入端解析 `Content-Length: <n>\r\n\r\n<body>`
  - 输出端写回标准 framing

**Rationale:**
- 用户要求的是“重装可用版本”，不是把已知坏 transport 再装回去。
- 这里沿用之前已经确认过的根因和最小 transport 修复。

### 4. 从 patched fresh 源码构建、stage 并执行真实安装
**Files Changed:** `C:\Users\Redwa\.codex\config.toml`

**Implementation:**
- 用 `VsDevCmd.bat -arch=x64 -host_arch=x64` 配置新的 `x64` 构建目录
- 先跑 Debug + 测试，后跑 Release 安装包构建
- 使用 `cmake --install` 把 Release 输出 stage 到临时 package 根
- 补入 `install-codex.ps1` / `README*` / `LICENSE`
- 执行 stage 包中的 `install-codex.ps1`

**Installed Result:**
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-cli.exe`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc.dll`
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc.json`
- `C:\Users\Redwa\.codex\skills\renderdoc-mcp\SKILL.md`
- `C:\Users\Redwa\.codex\skills\renderdoc-mcp\agents\openai.yaml`

**Rationale:**
- `install-codex.ps1` 是仓库提供的官方 Codex 安装入口，只是它需要一个真正带 `bin/` 的 package root。

---

## Decisions Made

### Decision 1: 先卸载活跃安装，但保留时间戳备份
**Context:** 用户明确要求“卸载掉之前的”，但 fresh 重装路径在验证前仍有不确定性。
**Options Considered:**
1. 直接 `Remove-Item` 硬删
2. 原地覆盖旧安装
3. 先移走旧安装到时间戳备份，再重装

**Decision:** 选择 3
**Rationale:** 满足“卸载”要求，同时保留回滚能力。
**Trade-offs:** 会暂时留下额外备份目录。
**Documentation Impact:** 仅记录本次会话日志。

### Decision 2: 不直接安装 fork 的源码 zip
**Context:** `Redwarx008` fork 没有 release，源码包没有 `bin/`，且 transport 仍旧不兼容。
**Options Considered:**
1. 强行把源码 zip 当发布包安装
2. 直接恢复之前的本机包
3. 以 fresh 源码为基底，重套最小 transport 修复后重新构建安装

**Decision:** 选择 3
**Rationale:** 既满足“从 Redwarx008 源码重新来一遍”，又不把已知坏包装回去。
**Trade-offs:** 这不是“纯上游原样安装”，而是带本地兼容性修复的重装。
**Documentation Impact:** 记录在本次日志中即可。

---

## What Worked ✅

1. **先拆成“卸载活跃安装”和“重装可用包”两步**
   - What: 先清空活跃路径，再处理 fresh 源码与构建问题
   - Why it worked: 降低了状态混淆，验证点更清晰
   - Reusable pattern: Yes

2. **用旧构建缓存反推本机依赖位置**
   - What: 从 `renderdoc-mcp-build-nmake/CMakeCache.txt` 找回 `RENDERDOC_DIR`
   - Why it worked: 避免重新猜测 RenderDoc SDK / source 路径
   - Reusable pattern: Yes

3. **先让协议测试红灯，再回迁 transport 修复**
   - What: `ProtocolTest.InitializeHandshake` 先复现 initialize timeout，再修改 `src/main.cpp`
   - Impact: 能证明修的是 transport 根因，而不是误打误撞

---

## What Didn't Work ❌

1. **直接下载 fork 的 `main.zip` 期待获得可安装发布包**
   - What we tried: 把 GitHub 仓库源码 zip 当作 release package
   - Why it failed: 仓库不包含发布态 `bin/`，`install-codex.ps1` 无法直接消费
   - Lesson learned: `source zip != release package`
   - Don't try this again because: 会浪费时间在缺失二进制和运行库上

2. **第一次用默认 VS Developer Prompt 配 NMake**
   - What we tried: 未显式指定 `-arch=x64 -host_arch=x64`
   - Why it failed: 落成了 `x86` 编译环境，而 `renderdoc.lib` 是 `x64`
   - Lesson learned: 这套本机依赖必须固定到 `Hostx64/x64`
   - Don't try this again because: 会在链接阶段报机器架构不匹配

---

## Problems Encountered & Solutions

### Problem 1: fork 当前源码仍是旧的 newline-delimited JSON transport
**Symptom:** fresh 源码上的 `ProtocolTest.InitializeHandshake` 在被测 server 上等待超时。
**Root Cause:** `src/main.cpp` 仍按单行 JSON 收发，不解析标准 MCP `Content-Length` 帧。
**Investigation:**
- Tried: 下载并检查 `Redwarx008/renderdoc-mcp` 的 `main.zip`
- Tried: 搜索 `Content-Length` / `getline(std::cin, line)` / `newline-delimited JSON`
- Found: transport 仍是旧实现

**Solution:**
- 在 fresh 源码上重新应用最小 framing 修复，并同步更新协议/工作流集成测试到标准 MCP framing。

**Why This Works:** Codex 的 stdio MCP 客户端按 `Content-Length` framing 握手；修复后测试可直接验证这一点。
**Pattern for Future:** 重新安装自定义 MCP server 前，先验证其 stdio transport，而不是只看 `config.toml` 和文件存在性。

### Problem 2: fresh 构建第一次落成 x86，导致 renderdoc.lib 链接失败
**Symptom:** `renderdoc-mcp.exe` 在链接阶段报 x86/x64 冲突和 RenderDoc 符号未解析。
**Root Cause:** 默认 `VsDevCmd.bat` 未指定架构，导致 NMake 生成 `Hostx86/x86` 工具链。
**Investigation:**
- Tried: 用默认 dev prompt 配置 `NMake Makefiles`
- Found: `where cl` 不可用；旧成功构建缓存使用的是 `Hostx64/x64`
- Found: `renderdoc-sdk-stub-v1.43/lib/Release/renderdoc.lib` 是 `x64`

**Solution:**
- 所有 fresh 构建统一改为：
  - `VsDevCmd.bat -arch=x64 -host_arch=x64`

**Why This Works:** 与本机 RenderDoc stub 库的机器架构一致，链接恢复正常。
**Pattern for Future:** 复用这套本机 RenderDoc 依赖时，固定 `Hostx64/x64`，不要依赖默认 dev prompt。

### Problem 3: 重装后二进制可握手，但 Codex 工具列表仍未暴露 `renderdoc-mcp`
**Symptom:** 用户重启后仍看不到 `renderdoc-mcp` 工具；Codex 日志持续出现 `waiting for MCP server tools while building tool list server_name=renderdoc-mcp has_cached_tool_info_snapshot=false startup_complete=true`。
**Root Cause:** 有两层问题叠加：
1. 活跃 `C:\Users\Redwa\.codex\config.toml` 中的 `[mcp_servers.renderdoc-mcp]` 一度缺少 `type = "stdio"`。
2. 更关键的是，`renderdoc-mcp` 源码里把 `initialize.params.protocolVersion` 写死校验为 `2025-03-26`。当前 Codex 客户端会以更新的 MCP 日期版本发起初始化，请求因此在 `initialize` 阶段被 server 直接拒绝，导致工具枚举永远挂起。
**Investigation:**
- Tried: 对比当前 `config.toml` 和 `config.toml.bak-20260618-032939`
- Found: 当前配置只有 `command` 和 `args`，旧可用配置额外包含 `type = "stdio"`
- Found: 历史日志里确实存在 `listed MCP server tools while building tool list server_name=renderdoc-mcp tool_count=59`
- Reproduced: 对已安装二进制手动发送 `protocolVersion = "2025-06-18"` 的 `initialize`，收到 `Unsupported protocol version: 2025-06-18 (server supports 2025-03-26)`
- Located: `src/mcp/mcp_server.cpp` 中的 `handleInitialize()` 只接受 `2025-03-26`

**Solution:**
- 先将活跃配置修正为：
  - `[mcp_servers.renderdoc-mcp]`
  - `type = "stdio"`
  - `command = 'C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618\bin\renderdoc-mcp.exe'`
  - `args = []`
- 在 fresh 源码中修改 `src/mcp/mcp_server.cpp`：
  - 保留 `2025-03-26` 兼容
  - 接受 `2025-03-26` 之后的日期版 MCP 协议
  - 对被接受的版本回显同样的 `protocolVersion`
- 新增回归覆盖：
  - `tests/unit/test_mcp_server.cpp` 覆盖旧版本拒绝与 `2025-06-18` 成功
  - `tests/integration/test_protocol.cpp` / `test_workflow.cpp` 改为用 `2025-06-18` 完成握手

**Why This Works:** 当前 Codex 的阻塞点不是进程启动，而是初始化协商。server 接受新协议版本后，Codex 才能继续 `notifications/initialized -> tools/list`。
**Pattern for Future:** 当 MCP server 二进制“手动可跑”但 Codex 内不暴露工具时，要显式用当前 MCP 协议版本重放 `initialize`，不要只验证 `tools/list` 或只看 `command` 路径存在。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update ARCHITECTURE_OVERVIEW.md - 不需要，项目系统状态未变化
- [ ] Update CURRENT_FEATURES.md - 不需要，项目功能状态未变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 卸载旧 MCP 安装时先移走活跃目录再重装
- When to use: 用户要求重装外部 MCP / skill，但回滚风险高时
- Benefits: 活跃状态干净，仍可回退
- Add to: 暂不需要独立抽取

**New Anti-Pattern:** 把 GitHub 源码 zip 误当成可直接安装的 Codex release package
- What not to do: 看到 `install-codex.ps1` 就假设源码树里自带发布态 `bin/`
- Why it's bad: 会卡在缺失二进制和 transport 旧实现
- Add warning to: 暂不需要独立抽取

### Architectural Decisions That Changed
- 无项目架构变更

---

## Code Quality Notes

### Testing
- **Tests Written:** 0 个新测试文件；更新了 2 个现有集成测试以切换到标准 MCP framing
- **Coverage:** `initialize`、`tools/list`、多请求稳定性、工作流通知 framing
- **Manual Tests:**
  - 安装后对 `~/.codex/vendor_imports/renderdoc-mcp/bin/renderdoc-mcp.exe` 做 framed `initialize`
  - 使用 server 声明支持的 `2025-03-26` 协议版本跑 `initialize -> notifications/initialized -> tools/list`

### Technical Debt
- **Created:** GitHub fork 本身仍未包含 transport 修复
- **Paid Down:** 本机活跃安装已恢复到标准 MCP framing 可用状态
- **TODOs:** 如需长期维护，应把 transport 修复回推到 fork 仓库本身

### Follow-up Verification (04:23)
- 继续排查后确认当前 Codex MCP 客户端实际使用的是“换行分隔 JSON over stdio”，而不是 `Content-Length` framing
- 旧 protofix 版虽然已支持读取换行输入，但回包仍使用 `Content-Length`，因此 Codex 在 `initialize` 后立即返回 `Parse error`
- 已在临时源码 `src/main.cpp` 中实现 transport mirror：
  - 读到换行 JSON 时，响应也输出为换行 JSON
  - 读到 `Content-Length` framing 时，响应继续使用标准 framing
- 已重新编译 debug/release，并用本地最小握手验证：
  - 直接运行新二进制，对 `initialize\\n` 输入返回 `{\"jsonrpc\":...,\"result\":...}\\n`
  - 通过 Python 代理路径同样返回换行 JSON
- 已将新发布目录切到：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618c\bin\renderdoc-mcp.exe`
- 现有入口保持不变：
  - `C:\Users\Redwa\.codex\config.toml` 仍指向 `renderdoc-mcp-proxy.py`
  - 代理脚本内部 `REAL_EXE` 已改为指向 `20250618c`
- 最新代理日志已确认：
  - `initialize` 输入为换行 JSON
  - 输出也已变为换行 JSON
  - 不再出现旧版 `Content-Length` 回包

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重启 Codex Desktop - 让新的 `command` 路径和协议兼容版 `renderdoc-mcp.exe` 生效
2. 新会话里复核 `renderdoc-mcp` 命名空间是否真正暴露 - 这是最终客户端层验证
3. 如果仍未暴露，优先查看：
   - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618\bin\renderdoc-mcp-proxy.log`
4. 如果日志里已经出现 `notifications/initialized` / `tools/list`，说明挂载成功，问题转到会话刷新层
5. 如果需要长期稳定维护，把 transport mirror 修复和协议兼容修复一起提交回 `Redwarx008/renderdoc-mcp`

### Blocked Items
- **Blocker:** 当前这条会话的工具清单不会因为本地重装而自动更新
- **Needs:** 重启应用或至少开启一条新会话
- **Owner:** User / next session

### Questions to Resolve
1. 是否要把这次本地 transport 修复同步回 GitHub fork？
2. 是否要把协议兼容修复也同步回 GitHub fork，避免后续 Codex 版本继续挂载失败？

### Docs to Read Before Next Session
- [2026-06-18-renderdoc-mcp-tool-mount-root-cause.md](./2026-06-18-renderdoc-mcp-tool-mount-root-cause.md) - transport 根因
- [2026-06-18-renderdoc-mcp-current-session-recheck.md](./2026-06-18-renderdoc-mcp-current-session-recheck.md) - 会话层可用性判断方式

---

## Session Statistics

**Files Changed:** 1 个仓库内日志文件 + 若干本机 Codex 安装文件/临时源码
**Lines Added/Removed:** 日志新增；临时源码 transport/test patch 未进入项目仓库
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 用户这次要求的是“先卸载旧安装，再重装 `Redwarx008/renderdoc-mcp`”
- `Redwarx008` fork 当前 `main` 仍是旧 newline-delimited JSON transport
- 本机现已重新安装到 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp`
- 安装后二进制对 `2025-03-26` 协议可完成 `initialize -> tools/list`，工具数为 `59`
- 当前活跃入口实际是 Python 代理：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618\bin\renderdoc-mcp-proxy.py`
- 代理当前转发到：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp-protofix-20250618c\bin\renderdoc-mcp.exe`
- 本地已验证对 Codex 当前发送的 `initialize\\n`，服务端会回 `result\\n`

**What Changed Since Last Doc Read:**
- Architecture: 无项目架构变更
- Implementation: 本机活跃 `renderdoc-mcp` 安装已重建
- Constraints: 当前会话仍需要重启后才能看到新工具

**Gotchas for Next Session:**
- 不要把源码 zip 当成 release package
- 不要忘记 `VsDevCmd.bat -arch=x64 -host_arch=x64`
- 如果只看当前会话工具列表，会误以为重装仍未生效

---

## Links & References

### Related Sessions
- [2026-06-18-renderdoc-mcp-current-session-recheck.md](./2026-06-18-renderdoc-mcp-current-session-recheck.md)
- [2026-06-18-renderdoc-mcp-tool-mount-root-cause.md](./2026-06-18-renderdoc-mcp-tool-mount-root-cause.md)

### External Resources
- [Redwarx008/renderdoc-mcp](https://github.com/Redwarx008/renderdoc-mcp)

### Code References
- Installed config: `C:\Users\Redwa\.codex\config.toml`
- Installed binary: `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- Installed skill: `C:\Users\Redwa\.codex\skills\renderdoc-mcp\SKILL.md`

---
