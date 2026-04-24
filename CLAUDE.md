## 核心规则
- **请始终使用简体中文与我对话**
- **引擎源码在 E:\WorkSpace\stride**
## Trellis 工作流
- 每次会话开始时，必须先检查 `<task-status>` 中的任务状态（已由钩子注入）
- 如果没有活跃任务且用户描述了需要实现的功能，**必须先调用 `trellis-brainstorm` skill** 创建任务，不得跳过
- 如果有活跃任务，必须按当前阶段执行工作流，不得跳步
- 研究、规划、实现、检查各有对应 skill，按需调用
## 着色器开发
使用 `stride-shader-asset-workflow` skill 处理 SDSL 着色器开发。