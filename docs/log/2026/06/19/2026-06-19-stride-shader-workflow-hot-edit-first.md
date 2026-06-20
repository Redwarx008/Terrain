# Stride shader workflow 补充“先热修改验证”规则
**Date**: 2026-06-19
**Session**: stride shader workflow hot-edit first
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 更新 `stride-shader-asset-workflow` skill，在 shader debug 场景下明确要求优先用热修改/热替换即时验证假设是否有效。

**Secondary Objectives:**
- 清理该 skill frontmatter 中不符合当前校验规则的历史字段。
- 记录本次验证方式与限制。

**Success Criteria:**
- skill 明确区分“资产注册/未进模块”与“已在 GPU 上运行但结果不对”两类问题。
- 对后者新增 hot-edit-first 调试顺序、CLI 限制和回写源码要求。
- 完成一次等价于 `quick_validate.py` 的本地校验。

---

## Context & Background

**Previous Work:**
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/2026/06/19/2026-06-19-river-bottom-shadow-port-and-balance-recheck.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

**Current State:**
- 现有 `C:\Users\Redwa\.codex\skills\stride-shader-asset-workflow\SKILL.md` 重点覆盖 `.sdpkg` / `.csproj` / generated keys / Stride asset rebuild。
- skill 缺少“shader 已经在目标 draw 上运行时，应先热修改验证假设”的明确调试优先级。

**Why Now:**
- 当前河流 shader 调试高度依赖 RenderDoc hot-replace 验证；若不把这条经验写进 skill，后续 agent 仍容易回到盲改源码 + 长 rebuild 循环。

---

## What We Did

### 1. 更新 shader workflow skill 的调试分流
**Files Changed:** `C:\Users\Redwa\.codex\skills\stride-shader-asset-workflow\SKILL.md`

**Implementation:**
- 在 overview 中补充：需要先判断当前问题属于“资产重建”还是“live GPU hot edit”。
- 在 `When To Use` 中加入“shader 已经执行在目标 draw 上，需要验证候选逻辑修改是否真的改善 GPU 结果”。
- 将 workflow 第 7 步改成“先分流再继续改源码”。
- 新增 `Live GPU Debugging Order (Prefer Hot Edit First)`：
  - 先挂到准确 draw/pixel
  - 一次只验证一个假设
  - 热修改只回答“方向是否对”
  - 有效后必须回写 `.sdsl` 并重新走 asset rebuild
- 在 `Failure Signatures`、`Agent Rules`、`Quick Checklist` 中补充：
  - 已进 GPU 时优先 hot-edit/hot-replace
  - `renderdoc-cli shader-replace` 不是持久会话，只能做 compile/theory gate
  - 成功的热修改必须回写源码并重新验证

**Rationale:**
- 这条规则能把“假设错误”和“改动没进 GPU”两类问题拆开，显著缩短 shader 调试回路。

**Architecture Compliance:**
- ✅ 没有修改项目运行时代码或渲染架构
- ✅ 只更新外部 skill 工作流，不改变 `docs/ARCHITECTURE_OVERVIEW.md` / `docs/CURRENT_FEATURES.md` 的系统状态

### 2. 清理历史 frontmatter 残留并完成本地校验
**Files Changed:** `C:\Users\Redwa\.codex\skills\stride-shader-asset-workflow\SKILL.md`

**Implementation:**
- 删除 frontmatter 中旧的 `compatibility` 字段。
- 尝试运行：
  - `python ...quick_validate.py ...`
  - bundled Python 同一路径
- 两者都因缺少 `PyYAML` 失败。
- 随后按 `quick_validate.py` 的当前规则，执行等价静态校验：
  - frontmatter 存在
  - 顶层 key 只包含允许集合
  - `name` 满足 hyphen-case / 长度约束
  - `description` 不含 angle brackets 且长度合法

**Rationale:**
- `compatibility` 不在当前 validator 允许键集合里，属于历史残留；顺手清理后，skill 才能通过等价校验。

---

## Decisions Made

### Decision 1: 保留“资产重建优先”主流程，同时新增“已进 GPU 时先热修改”分流
**Context:** 原 skill 的价值在于防止把 stale asset/module 问题误判成 shader 代码问题。
**Options Considered:**
1. 直接把整个 skill 改成 hot-edit-first
2. 保持原样，只靠会话经验记忆
3. 保留资产注册主流程，并新增 live GPU debug 分流

**Decision:** 选择 Option 3
**Rationale:** 两类问题都真实存在，必须先分清故障类别，而不是用单一流程覆盖所有 shader 问题。
**Trade-offs:** skill 稍长，但调试路径更清晰。

### Decision 2: 不更新项目架构总览与功能总览
**Context:** 本次只更新外部 skill 文本，没有改变 Terrain 项目的实现状态。
**Options Considered:**
1. 同步修改架构/功能总览
2. 仅写会话日志

**Decision:** 选择 Option 2
**Rationale:** 避免把“agent 工作流变化”误记成“项目系统状态变化”。

---

## What Worked ✅

1. **用基线搜索确认 skill 缺口**
   - What: 对 `SKILL.md` 搜索 `hot|replace|热替换|shader-replace`
   - Why it worked: 直接证明现有 skill 没有这条调试规则
   - Reusable pattern: Yes

2. **把 hot-edit-first 写成分流规则而不是替代主流程**
   - What: 只在“shader 已在目标 draw 上运行”时触发热修改优先级
   - Why it worked: 不会破坏原 skill 对 stale asset/module 问题的防误诊价值
   - Reusable pattern: Yes

3. **按 validator 代码做等价校验**
   - What: 直接读取 `quick_validate.py` 后按相同约束做静态检查
   - Why it worked: 在本地缺少 `PyYAML` 时仍能完成规则级验证
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **直接运行 `quick_validate.py`**
   - What we tried: 默认 Python 与 bundled Python 各跑一次
   - Why it failed: 两个环境都缺少 `yaml` / `PyYAML`
   - Lesson learned: 这个校验脚本当前依赖外部包，不能假设任意 Python 环境可直接运行
   - Don't try this again because: 不先解决依赖时只会重复报同一个错误

---

## Architecture Impact

### Documentation Updates Required
- [x] 创建本次会话日志
- [ ] 更新 `docs/ARCHITECTURE_OVERVIEW.md` - 不需要，本次无系统状态变化
- [ ] 更新 `docs/CURRENT_FEATURES.md` - 不需要，本次无功能状态变化

### New Patterns/Anti-Patterns Discovered
**Pattern reuse check:**
- `docs/log/learnings/stride-river-rendering-patterns.md` 已经覆盖大量 hot-replace 经验，本次不再重复写一份 learning。

---

## Code Quality Notes

### Testing
- **Tests Written:** 无
- **Coverage:** skill 文本新增规则 + frontmatter 允许键集合
- **Manual Tests:** 读取新增段落并执行 quick-validate 等价静态校验

### Technical Debt
- **Paid Down:** 移除 `compatibility` 历史残留字段
- **TODOs:** 若后续要恢复官方 `quick_validate.py` 直接运行，需要为其提供 `PyYAML`

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在真实 shader 调试会话里使用更新后的 skill，确认触发词和流程足够自然
2. 若需要正式依赖 `quick_validate.py`，为本地 skill 校验环境补齐 `PyYAML`

### Docs to Read Before Next Session
- `docs/log/learnings/stride-river-rendering-patterns.md`
- `C:\Users\Redwa\.codex\skills\stride-shader-asset-workflow\SKILL.md`

---

## Session Statistics

**Files Changed:** 2
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `stride-shader-asset-workflow` 现在明确要求：shader 已经运行在目标 draw 上时，优先热修改验证，而不是继续盲改源码。
- `renderdoc-cli shader-replace` 被明确标成非持久会话，只能做 compile/theory gate。
- `compatibility` frontmatter 字段已移除，以符合当前 validator 允许键集合。

**What Changed Since Last Doc Read:**
- External skill: 新增 `Live GPU Debugging Order (Prefer Hot Edit First)`
- Validation: `quick_validate.py` 运行受 `PyYAML` 缺失限制，改为等价静态校验

**Gotchas for Next Session:**
- Watch out for: 不要把 hot edit 结果当最终修复，必须回写 `.sdsl` 并重新编译资产
- Don't forget: 只有在 shader 已经实际跑到目标 draw 时，hot-edit-first 才成立
- Remember: asset/module/stale cache 问题仍应先走原有 Stride rebuild 工作流

---

*Template Version: 1.0 - Based on Archon-Engine template*
