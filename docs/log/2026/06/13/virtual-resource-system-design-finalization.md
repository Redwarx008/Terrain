# Virtual Resource System Design Finalization
**Date**: 2026-06-13
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按用户已拍板的约束，收口 Editor / Runtime 共用虚拟资源系统设计稿

**Secondary Objectives:**
- 去掉旧路径兼容思路
- 明确 `.terrain`、`heightmap`、`biome_mask`、`biome_settings`、`materials descriptor` 的职责边界

**Success Criteria:**
- 设计稿可直接进入用户 review，不再保留关键歧义

---

## Context & Background

**Current State:**
- 旧草稿仍保留过时内容，包括多 descriptor、旧路径字段、兼容桥接思路等

**Why Now:**
- 用户要求先完成设计，不进入实现，并指出需要遵守 Superpowers brainstorming 流程

---

## What We Did

### 1. 按已确认对话决策重写设计稿
**Files Changed:** `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`

**Implementation:**
- 整份替换旧草稿
- 写死以下约束：
- `LaunchSetting.json` 只决定 mod 顺序
- `base` 隐式存在
- Runtime 定义唯一 resolver，Editor 复用
- `map_data/default.toml` 为唯一地图入口
- `map_data/biome_mask.png` 固定路径
- `map_data/biome_settings.toml` 固定路径
- `map_data/materials/descriptor.toml` 固定路径
- `TerrainComponent` 不再保留任何资源路径字段
- `Save` 不自动更新 `.terrain`
- `Export .terrain` 固定写回当前命中的 `map_data/terrain.terrain`

**Rationale:**
- 让设计与用户最终口径完全一致，避免实现阶段再次返工

### 2. 做一轮轻量 spec 自检
**Files Changed:** `无`

**Implementation:**
- 检查占位词残留
- 回看设计稿前半段与关键规则段，补充 `default.toml` 路径基准说明

**Rationale:**
- 在进入用户 review 前先清掉明显歧义

---

## Decisions Made

### Decision 1: 不保留任何旧路径兼容层
**Context:** 用户明确要求“之前的路径不要保留”
**Options Considered:**
1. 保留兼容桥接 - 风险低但会留下双模型
2. 彻底切断旧路径模型 - 改造更彻底

**Decision:** 选择 Option 2
**Rationale:** 与新的固定入口、固定伴随资源、统一 resolver 设计一致
**Trade-offs:** 实现时迁移面更直接，但后续结构更干净

### Decision 2: `.terrain` 只通过显式 Export 更新
**Context:** 用户要求 `.terrain` 主要存运行时二进制 VT 数据，不能与作者态保存混淆
**Options Considered:**
1. Save 自动重建 `.terrain`
2. Save 不碰 `.terrain`，显式 Export 单独写回

**Decision:** 选择 Option 2
**Rationale:** 明确分离作者态资源与运行时消费物
**Trade-offs:** 用户需要显式执行 Export，但模型更清晰

---

## What Worked ✅

1. **逐项拍板再落文档**
   - What: 先把关键分歧点逐个问清，再整稿
   - Why it worked: 减少了“边写边猜”的偏差
   - Reusable pattern: Yes

2. **先停实现、先收设计**
   - What: 用户指出流程偏离后，立即回到 brainstorming 设计阶段
   - Impact: 避免未定设计提前落代码

---

## Problems Encountered & Solutions

### Problem 1: 旧草稿与最新口径偏差较大
**Symptom:** 旧文档仍包含多 descriptor、旧路径字段、兼容桥接等内容
**Root Cause:** 设计在多轮讨论中持续收缩，但文档没有同步重写
**Investigation:**
- Tried: 对照当前草稿与本轮决策
- Found: 局部修补不如整稿重写可靠

**Solution:**
- 删除旧草稿并按最终决策重建完整设计稿

**Why This Works:** 可以一次性清理掉历史残留假设
**Pattern for Future:** 当设计口径整体变化超过半数结构时，优先整稿重写而不是修补

---

## Architecture Impact

### Documentation Updates Required
- [ ] 用户 review 通过后，可在实现阶段再决定是否同步到 `ARCHITECTURE_OVERVIEW.md`
- [ ] 当前仅设计文档更新，无需修改系统状态总览

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 设计未收口就提前进入实现
- What not to do: 在资源入口、保存语义、兼容策略未拍板前启动代码改造
- Why it's bad: 极易在实现中固化错误假设

---

## Code Quality Notes

### Testing
- **Manual Tests:** 无，本次为文档设计与自检

### Technical Debt
- **Created:** 无代码债务
- **TODOs:** 待用户 review 通过后，再进入 writing-plans / implementation 阶段

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 用户 review 设计稿
2. 若有修改意见，继续迭代设计稿
3. 设计稿批准后，再进入 implementation plan

### Questions to Resolve
1. 设计稿是否还有需要调整的命名或边界

### Docs to Read Before Next Session
- `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`

---

## Session Statistics

**Files Changed:** 2
**Lines Added/Removed:** 文档重写，未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本次只收设计，不进实现
- 用户明确不要擅自提交
- 旧路径字段和旧 TOML 主链路已经在设计上被彻底否定

**Gotchas for Next Session:**
- 不要再把 `BiomeConfigPath`、`TerrainDataPath`、`MapDefinitionPath` 留在组件里
- 不要让 Save 自动更新 `.terrain`
- `provinces.png` 在 v1 只是资源位，不是实现范围

---

## Links & References

### Related Documentation
- `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md`

---

## Notes & Observations

- 本次遵循用户要求，没有进行代码实现
- 本次遵循用户偏好，没有创建 git commit

