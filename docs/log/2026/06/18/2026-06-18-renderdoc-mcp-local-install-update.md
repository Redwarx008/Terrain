# RenderDoc MCP Local Install Update
**Date**: 2026-06-18
**Session**: renderdoc-mcp-local-install-update
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 把 Codex 当前实际使用的本地 `renderdoc-mcp` 从旧版 vendor_import 安装切到已经验证可用的新版本。

**Secondary Objectives:**
- 避免继续命中旧的 `get_cbuffer_contents(..., ResourceId(), 0, 0)` 逻辑。
- 用安装后的真实二进制重新验证 `debug.rdc` 上的 DX11 cbuffer 读取。

**Success Criteria:**
- `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp` 中的源码和二进制都更新到新版本。
- 安装路径下的 `renderdoc-mcp.exe` 能在 `C:\Users\Redwa\Desktop\debug.rdc` 上读出非零 cbuffer 值。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md)

**Current State:**
- 远程 `Redwarx008/renderdoc-mcp` 主线已经能正确读取 DX11 cbuffer。
- Codex 当前实际挂载的 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp` 仍是旧版实现。

**Why Now:**
- 如果不替换本地安装，后续所有 RenderDoc 分析仍会继续看到“cbuffer 全 0”的假结果。

---

## What We Did

### 1. 确认本机安装路径与配置指向
**Files Changed:** None

**Implementation:**
- 检查 `C:\Users\Redwa\.codex\config.toml`
- 确认 `[mcp_servers.renderdoc-mcp]` 仍指向：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`

**Rationale:**
- 先确认真正生效的是哪个安装目录，避免只修了临时 clone。

### 2. 用已验证仓库重新构建 Release 二进制
**Files Changed:** None

**Implementation:**
- 在 `C:\Users\Redwa\AppData\Local\Temp\renderdoc-mcp-redwarx008` 配置并构建：
  - `out/build/nmake-release-codex/renderdoc-mcp.exe`
  - `out/build/nmake-release-codex/renderdoc-cli.exe`
  - `renderdoc.dll`
  - `renderdoc.json`

**Rationale:**
- 不直接拿 Debug 构建覆盖本地工具链，保证后续使用的是正式 Release 二进制。

### 3. 手工替换本地 vendor_import 与 skill 目录
**Files Changed:** None

**Implementation:**
- 组装最小完整安装包到：
  - `C:\Users\Redwa\AppData\Local\Temp\renderdoc-mcp-package-codex`
- 对旧目录做时间戳备份：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp.bak-20260618-004726`
  - `C:\Users\Redwa\.codex\skills\renderdoc-mcp.bak-20260618-004726`
- 用新包覆盖：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp`
  - `C:\Users\Redwa\.codex\skills\renderdoc-mcp`
- 保持 `config.toml` 原样，不运行安装脚本去改写 MCP 配置格式

**Rationale:**
- 安装脚本会重写 `renderdoc-mcp` 配置块，存在把当前 `type = "stdio"` 等字段改回旧格式的风险。
- 直接替换安装目录更稳。

### 4. 重新验证安装后的真实二进制
**Files Changed:** None

**Implementation:**
- 直接调用安装路径：
  - `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`
- 对 `C:\Users\Redwa\Desktop\debug.rdc` 发送真实 MCP 请求：
  - `open_capture`
  - `get_cbuffer_contents(stage=vs,index=1,eventId=186)`
  - `get_cbuffer_contents(stage=ps,index=0,eventId=186)`
- 返回结果确认：
  - `VS PerView.View_id66` 为单位矩阵
  - `PS Globals._BankFade_id24 = 0.15`
  - `PS Globals._Depth_id25 = 0.15`
  - `PS Globals._DepthWidthPower_id26 = 2.0`
  - `PS Globals._MapExtent_id27 = 18431`
  - `PS Globals._CameraWorldPosition_id28 = (4392.7773, 27.84846, 267.17557)`

**Rationale:**
- 这一步证明“安装后的实际调用链”已经不再命中旧版全 0 逻辑。

### 5. 清理仍在内存中的旧 MCP 进程
**Files Changed:** None

**Implementation:**
- 发现有两个 2026-06-17 晚上启动的 `renderdoc-mcp.exe` 旧进程仍在运行
- 停掉这些旧实例，避免后续会话继续复用内存中的旧代码

**Rationale:**
- 替换磁盘文件后，如果旧进程不退出，后续调用仍可能继续落到旧实现。

---

## Decisions Made

### Decision 1: 不运行 `install-codex.ps1`，改为手工替换安装目录
**Context:** 安装脚本会重写 `config.toml` 的 `renderdoc-mcp` 配置块。
**Options Considered:**
1. 直接运行安装脚本
2. 手工替换 vendor_import 和 skill 目录，保留配置文件不动

**Decision:** 选择 2
**Rationale:** 当前配置已正确指向目标路径，只需要换内容，不需要冒配置回退风险。

### Decision 2: 安装 Release 构建而不是 Debug 构建
**Context:** 之前的功能验证用的是临时 Debug 构建。
**Options Considered:**
1. 直接装 Debug 构建
2. 重新构建 Release 再安装

**Decision:** 选择 2
**Rationale:** 本地长期使用的工具链应当基于 Release 产物，避免额外调试开销与不必要差异。

---

## What Worked ✅

1. **先定位实际生效路径再更新**
   - What: 先检查 `config.toml` 和 `vendor_imports` 指向
   - Why it worked: 避免只更新临时 clone 却没有触达 Codex 真正调用的二进制
   - Reusable pattern: Yes

2. **替换后直接用安装路径做 MCP 复验**
   - What: 不依赖“应该生效了”的推断，直接对安装后的 exe 发真实 JSON-RPC
   - Impact: 立即确认这次更新已经从根本上消除了 DX11 cbuffer 全 0 问题

---

## What Didn't Work ❌

1. **继续依赖旧进程自动热更新**
   - What we tried: 先只换磁盘内容
   - Why it failed: 旧 `renderdoc-mcp.exe` 进程仍在内存中运行
   - Lesson learned: 替换 MCP 二进制后还要确认旧实例已经退出
   - Don't try this again because: 否则“文件已换新、行为仍旧旧”的现象会继续误导排查

---

## Problems Encountered & Solutions

### Problem 1: 本地安装版本与远程已验证版本脱节
**Symptom:** Codex 当前调用的 `renderdoc-mcp` 仍把 DX11 cbuffer 读成全 0。
**Root Cause:** `vendor_imports/renderdoc-mcp` 仍是旧版 `GetCBufferVariableContents(..., ResourceId(), 0, 0)` 实现。
**Investigation:**
- Tried: 检查本地 `src/core/cbuffer.cpp`
- Tried: 检查 `config.toml` 实际指向
- Found: 远程主线和本地安装版本不是同一实现

**Solution:**
- 重新构建远程已验证版本的 Release 包
- 手工覆盖 `vendor_imports/renderdoc-mcp` 与 `skills/renderdoc-mcp`
- 停掉内存中仍在运行的旧 `renderdoc-mcp.exe`

**Why This Works:** 配置路径不变，但磁盘内容和进程实例都切到了新实现。
**Pattern for Future:** 任何 MCP 工具行为异常，先区分“源码仓库状态”和“本地实际安装版本”。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，项目架构未变
- [ ] Update `CURRENT_FEATURES.md` - 不需要，项目功能未变

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先验证实际 MCP 安装路径，再替换二进制
- When to use: 调试本地 Codex skill / MCP 版本漂移问题时
- Benefits: 能快速区分“仓库已修复”和“本地仍在跑旧版”
- Add to: 暂不需要独立抽取

### Architectural Decisions That Changed
- 无

---

## Code Quality Notes

### Testing
- **Manual Tests:** 直接对安装后的 `renderdoc-mcp.exe` 发送 `open_capture` 和 `get_cbuffer_contents` 请求
- **Coverage:** D3D11 capture 打开、VS cbuffer 读取、PS cbuffer 读取、安装路径复验

### Technical Debt
- **Remaining:** `renderdoc-cli` 的 `cbuffer --index` 参数解析冲突仍未修复，这是与 MCP 零值问题独立的另一个 CLI bug。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 如果还要继续用 RenderDoc MCP 调 shader，优先直接使用当前已更新的安装版本
2. 如有必要，补修 `renderdoc-cli` 的 `cbuffer --index` 参数解析冲突
3. 若再次出现“行为像旧版”，先检查是否又有旧 `renderdoc-mcp.exe` 进程残留

### Questions to Resolve
1. 是否要顺手把 `renderdoc-cli` 的 `cbuffer --index` 冲突也修掉并提交到 fork？

### Docs to Read Before Next Session
- [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md) - 前一轮定位结论与证据链

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +1 log / -0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Codex 当前实际安装的 `renderdoc-mcp` 已替换到新版本：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp`
- 旧安装备份在：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp.bak-20260618-004726`
- 旧 skill 备份在：`C:\Users\Redwa\.codex\skills\renderdoc-mcp.bak-20260618-004726`
- 安装后的真实 exe 已在 `debug.rdc` / `eventId 186` 上验证通过

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 本地 `renderdoc-mcp` 安装已升级
- Constraints: 如果 Codex 里还有旧 `renderdoc-mcp.exe` 进程，要先停掉再继续怀疑代码

**Gotchas for Next Session:**
- 不要把“临时 clone 已验证”误认为“本地安装已生效”
- 不要忘记检查旧 MCP 进程是否还在内存中
- 记住 CLI 的 `cbuffer --index` 仍有独立参数解析 bug

---

## Links & References

### Related Sessions
- [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md)

### Code References
- 安装源码：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\core\cbuffer.cpp`
- 安装二进制：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\bin\renderdoc-mcp.exe`

---
