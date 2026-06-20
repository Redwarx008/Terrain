## 核心规则
- **请始终使用简体中文与我对话**
- **引擎源码在 E:\WorkSpace\stride**

## 会话延续（必须遵守）

### 启动流程
每次新会话开始时，按顺序读取以下文件恢复上下文：
1. `docs/ARCHITECTURE_OVERVIEW.md` — 当前系统状态
2. `docs/log/` 目录下最新的会话日志 — 上次做了什么、从哪里继续
3. `docs/CURRENT_FEATURES.md` — 功能完成度总览
4. 根据当前任务读取 `docs/design/` 或 `docs/log/decisions/` 中的相关文档

### 结束流程
每次会话结束时必须：
1. 创建会话日志到 `docs/log/YYYY/MM/DD/` 目录（使用 `docs/log/TEMPLATE.md` 模板）
2. 如果做出了重大架构决策，提取到 `docs/log/decisions/` 作为独立 ADR
3. 如果发现了可复用模式或常见陷阱，记录到 `docs/log/learnings/`
4. 如果系统状态发生变化，更新 `docs/ARCHITECTURE_OVERVIEW.md` 和 `docs/CURRENT_FEATURES.md`

### 经验沉淀规则
- 同一决策在 3 个以上会话中被引用 → 提取为独立 ADR
- 同一模式被验证 3 次以上 → 提取到 `docs/log/learnings/`
- 同一失败被记录 2 次 → 记录"不要再尝试"到 `docs/log/learnings/` 的"Common Mistakes"
- 每周回顾：归档旧会话到 `docs/log/archive/YYYY-MM/`，更新架构文档

## 着色器开发
使用 `stride-shader-asset-workflow` skill 处理 SDSL 着色器开发。