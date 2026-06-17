# RenderDoc MCP CBuffer Zero Diagnosis
**Date**: 2026-06-17
**Session**: renderdoc-mcp-cbuffer-zero-diagnosis
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 解释为什么 RenderDoc MCP 的 `get_cbuffer_contents` 在当前 `debug.rdc` 上把 river shader 的 cbuffer 读成全 0。

**Secondary Objectives:**
- 验证这是不是“运行时常量真没绑定”，还是“工具读取路径不可靠”。
- 把本轮证据补成会话日志，避免后续继续把零值当真。

**Success Criteria:**
- 给出基于当前 capture 和已有日志的结论。
- 明确后续应优先使用的排查手段。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-bottom-world-uv-renderdoc-fix.md](../16/2026-06-16-river-bottom-world-uv-renderdoc-fix.md)
- Related: [2026-06-16-river-surface-bank-fade-renderdoc-diagnosis.md](../16/2026-06-16-river-surface-bank-fade-renderdoc-diagnosis.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 用户继续问“为什么 renderdoc mcp 读出的 cbuffer 全是 0”。
- 当前本机 capture 为 `C:\Users\Redwa\Desktop\debug.rdc`，时间戳 2026-06-17 22:28:33。

**Why Now:**
- 如果把这些 0 当成真实运行时值，会直接把 shader 调试方向带偏。

---

## What We Did

### 1. 重新打开当前 capture 并确认可枚举 draw
**Files Changed:** None

**Implementation:**
- 用 `renderdoc-mcp.open_capture` 打开 `C:\Users\Redwa\Desktop\debug.rdc`
- 确认 capture 为 `D3D11`
- 确认事件/绘制可枚举，river 相关 draw 对应无 marker 名的 indexed draw

**Rationale:**
- 先排除 capture 本身损坏或空帧。

### 2. 对 river bottom/surface draw 读取 bindings 与 cbuffer
**Files Changed:** None

**Implementation:**
- 检查 bottom draw：`eventId 186/200`
- 检查 surface draw：`eventId 229/247`
- `get_bindings` 显示：
  - bottom PS `Globals` constant buffer 已绑定，大小 `592 bytes`，`31` 个变量
  - surface PS `Globals` constant buffer 已绑定，大小 `176 bytes`，`29` 个变量
  - VS `PerDraw` / `PerView` 也都已绑定
- `get_cbuffer_contents` 却同时把：
  - bottom PS `Globals`
  - surface PS `Globals`
  - VS `PerDraw`
  - VS `PerView`
  全部读成 0

**Rationale:**
- 这说明问题不是“某一个 river 参数没设”，而是“这条 cbuffer 值读取路径整体不可靠”。

### 3. 与历史诊断结论交叉验证
**Files Changed:** None

**Implementation:**
- 对照 2026-06-16 日志：
  - 之前已记录 `get_cbuffer_contents` 在该类 capture 上“不可靠”
  - 当时通过 shader trace 反推出 `_MapExtent≈18431`，证明运行时并不是真 0
- 对照 learnings：
  - 已明确要求“常量值优先从 shader trace/disasm 和实际输出反推”

**Rationale:**
- 当前现象不是新 bug，而是已出现过的 RenderDoc/MCP 读取局限。

### 4. 对远程 `renderdoc-mcp` 主线源码做交叉验证
**Files Changed:** None

**Implementation:**
- 检查用户 fork `Redwarx008/renderdoc-mcp` 的 `origin/main`
- 确认主线已经包含 `ef29edb fix: OpenGL analysis tools and shader debug diagnostics`
- 该提交在 `src/core/cbuffer.cpp` 中不再把 `GetCBufferVariableContents()` 固定喂成 `ResourceId(), 0, 0`，而是先经 `GetDescriptorAccess()` + `GetDescriptors()` 显式解析 constant buffer 的真实 `resource/offset/size`
- 用该主线代码重新本地构建 `renderdoc-mcp.exe`，对同一个 `C:\Users\Redwa\Desktop\debug.rdc` 走真实 MCP 链路：
  - `open_capture`
  - `list_cbuffers(stage=vs,eventId=186)`
  - `get_cbuffer_contents(stage=vs,index=1,eventId=186)`
  - `get_cbuffer_contents(stage=vs,index=0,eventId=186)`
  - `get_cbuffer_contents(stage=ps,index=0,eventId=186)`
- 实测返回非零值：
  - `VS PerView.View_id66/ViewInverse_id67/Projection_id68` 均为单位矩阵
  - `VS PerDraw.World_id64` 为单位矩阵
  - `PS Globals._BankFade_id24 = 0.15`
  - `PS Globals._Depth_id25 = 0.15`
  - `PS Globals._DepthWidthPower_id26 = 2.0`
  - `PS Globals._MapExtent_id27 = 18431`
  - `PS Globals._CameraWorldPosition_id28 = (4392.7773, 27.84846, 267.17557)`

**Rationale:**
- 这一步把“当前已安装 MCP 读成全 0”和“远程主线源码构建后能读出真实值”明确区分开了。
- 结论不再是“fork 里还缺 DX11 修复”，而是“远程主线已修好，本机实际在用的版本更可能落后”。

### 5. 对当前 Codex 实际使用的本地 vendor_import 版本做核对
**Files Changed:** None

**Implementation:**
- 检查本地挂载源码：`C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\core\cbuffer.cpp`
- 确认该版本的 `getCBufferContents()` 仍是旧实现：
  - 直接调用 `GetCBufferVariableContents(..., ::ResourceId(), 0, 0)`
  - 没有远程主线里的 `GetDescriptorAccess()` + `GetDescriptors()` 显式解析逻辑

**Rationale:**
- 这直接解释了“为什么当前 Codex 里调用 renderdoc-mcp 还是全 0”：
  - 当前实际运行的是旧 vendor_import 版本
  - 用户 fork / 远程主线上的新代码并没有进入当前本机调用路径

---

## Decisions Made

### Decision 1: 把问题定性为“工具读取不可靠”，不是“river 常量没绑定”
**Context:** bindings 显示 constant buffer 已绑定，但 contents 在多个 stage/block 上全部变成 0。
**Options Considered:**
1. 认为 river 代码没有给 shader 传值
2. 认为只有某一个 block 布局错位
3. 认为 `get_cbuffer_contents` 对当前 capture / 优化后常量块解码不可靠

**Decision:** 选择 3
**Rationale:** 同一 capture 上 PS/VS 多个 block 同时全 0，而绑定/资源/输出链路正常，不符合“只有单个参数没传”的症状。

### Decision 2: 后续常量验证以 trace / disasm / pixel history 为主
**Context:** 继续直接看 cbuffer 零值会误导 shader 分析。
**Options Considered:**
1. 继续把 `get_cbuffer_contents` 当主证据
2. 以 shader trace、寄存器值、pixel history 和输出结果为主

**Decision:** 选择 2
**Rationale:** 这是当前 capture 上唯一已被实践证明可靠的证据链。

### Decision 3: 不再把问题定性为“远程 fork 缺 DX11 修复”
**Context:** 重新构建远程 `origin/main` 后，同一 `debug.rdc` 上 `get_cbuffer_contents` 已能读出非零值。
**Options Considered:**
1. 继续在 fork 上追加一个“DX11 修复”补丁
2. 认定远程主线已经修好，问题在本机仍在使用旧构建/旧安装

**Decision:** 选择 2
**Rationale:** 同一 capture、同一 API、同一工具链，在 fresh build 上可稳定读出非零值，说明核心读取逻辑并没有在当前主线上继续损坏。

---

## What Worked ✅

1. **先看 bindings 再看 contents**
   - What: 先确认 block 是否真的 bound，再看值
   - Why it worked: 能把“没绑定”与“读不出来”明确区分开
   - Reusable pattern: Yes

2. **把当前 capture 与旧日志交叉验证**
   - What: 用当前 `debug.rdc` 复现，再对照 2026-06-16 的结论
   - Impact: 直接确认这是重复出现的工具限制，不是新回归

---

## Problems Encountered & Solutions

### Problem 1: `get_cbuffer_contents` 在多个 block 上全部返回 0
**Symptom:** `_MapExtent`、`PerDraw.World`、`PerView.ViewProjection` 等都显示为 0。
**Root Cause:** 当前“已安装在本机并被调用的 RenderDoc MCP 版本”与远程主线行为不一致；远程主线 fresh build 已能在同一 capture 上读出真实值，因此更可能是本机安装版本落后，而不是当前 fork 主线仍然缺 DX11 修复。
**Investigation:**
- Tried: 读取 bottom PS `Globals`
- Tried: 读取 surface PS `Globals`
- Tried: 读取 VS `PerDraw` / `PerView`
- Found: 绑定元数据正常，但值统一塌成 0
- Cross-check: 用远程 `origin/main` fresh build 复测同一 capture，得到非零矩阵和 river 参数

**Solution:**
- 确认远程 fork 主线无需再补 DX11 cbuffer 修复
- 如果要继续在本机用 MCP 调试，优先更新/替换到远程主线对应构建
- 在更新本机构建前，仍然用 shader trace / disasm / pixel history / 实际输出做兜底验证

**Why This Works:** fresh build 已证明同一 capture 上 RenderDoc API 可以给出真实 cbuffer 值，剩余差异只可能来自“本机实际运行的构建版本”。
**Pattern for Future:** “本机安装版读 0，但远程主线 fresh build 正常”时，优先升级工具版本，不要先在 fork 上重复造补丁。

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本次会话日志
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 不需要，系统状态未变
- [ ] Update `CURRENT_FEATURES.md` - 不需要，功能状态未变

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 把 `get_cbuffer_contents == 0` 直接当成运行时真值
- What not to do: 看到 RenderDoc MCP 把 cbuffer 展开成 0，就立即去改参数绑定代码
- Why it's bad: 会把调试从“工具解码缺陷”误判成“引擎传参 bug”
- Add warning to: 已存在于 `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- 无

---

## Code Quality Notes

### Testing
- **Manual Verification:** 使用 RenderDoc MCP 重新验证当前 `debug.rdc`
- **Coverage:** `open_capture` / `get_bindings` / `list_cbuffers` / `get_cbuffer_contents`

### Technical Debt
- **Remaining:** 仍缺一个稳定、自动化的方法在 RenderDoc 中恢复这些 block 的真实常量值；当前只能依赖 trace / 实际输出推断。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 把本机实际使用的 RenderDoc MCP 更新到 `origin/main` 对应构建
2. 更新前如果还要确认某个 river 参数，优先命中对应像素并跑 `debug_pixel` / `debug_vertex`
3. 必要时结合 shader disasm，按寄存器加载链反推常量

### Questions to Resolve
1. Codex/本机当前接入的 renderdoc-mcp 二进制具体来自哪个旧提交？
2. 是否要顺手修掉 `renderdoc-cli` 里 `cbuffer --index` 与 vertex debug 共用 `--index` 导致参数被前一个分支吞掉的问题？

### Docs to Read Before Next Session
- [2026-06-16-river-bottom-world-uv-renderdoc-fix.md](../16/2026-06-16-river-bottom-world-uv-renderdoc-fix.md) - 同类问题首次定性
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md) - 已沉淀的 RenderDoc 调试规则

---

## Session Statistics

**Files Changed:** 1
**Lines Added/Removed:** +1 log / -0
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前 `debug.rdc` 的 river bottom draw 是 `186/200`，surface draw 是 `229/247`
- 远程 `Redwarx008/renderdoc-mcp` 的 `origin/main` 已能在同一 `debug.rdc` 上读出非零 `PerView` / `PerDraw` / `Globals`
- 当前 Codex 实际挂载的 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp` 仍是旧版 `ResourceId(),0,0` 逻辑
- 本机当前被调用的 MCP 若仍返回全 0，更可能是安装版本落后
- 更新本机构建前，遇到全 0 时仍优先相信 trace / disasm / pixel history，而不是单独相信旧版 cbuffer 展开面板

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无
- Constraints: 新增一条针对当前 `debug.rdc` 的复核日志

**Gotchas for Next Session:**
- 不要把“block 已绑定”和“值被成功解码”混为一谈
- 不要再把 `_MapExtent==0` 这种读数直接当成引擎传参失败
- 记住：这个问题在当前 capture 和旧 CK3 capture 上都出现过

---

## Links & References

### Related Documentation
- [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES.md](../../../../CURRENT_FEATURES.md)

### Related Sessions
- [2026-06-16-river-bottom-world-uv-renderdoc-fix.md](../16/2026-06-16-river-bottom-world-uv-renderdoc-fix.md)
- [2026-06-16-river-surface-bank-fade-renderdoc-diagnosis.md](../16/2026-06-16-river-surface-bank-fade-renderdoc-diagnosis.md)

### Code References
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`

---
