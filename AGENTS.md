## 核心规则
- **请始终使用简体中文与我对话**
- **引擎源码在 E:\WorkSpace\stride**

## 工作流优先级与验证策略
- **项目指令与用户明确要求优先于通用 agent / superpowers 工作流**
- 不要把 TDD 机械套用到所有任务，尤其是游戏开发、渲染、shader、编辑器交互、视觉调参和探索性原型。
- 对确定性逻辑、数据转换、资源解析、mesh 生成不变量、可复现 bug，应优先使用测试先行或至少补充自动化回归测试。
- 对视觉或 GPU 行为，应使用更能证明问题的验证方式：RenderDoc 截帧对比、shader 编译验证、shader 文本回归、截图/视觉回归、运行时 smoke test 或明确的手动编辑器验证。
- 如果用户明确要求“不要 TDD”“先探索”“先热替换验证”“先看 RenderDoc”，按用户要求执行，并在最终说明采用了哪种验证方式。

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
