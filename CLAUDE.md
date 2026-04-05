# AI 协作规则

## 基础规则
- 请始终使用简体中文与我对话
- 引擎源码在 E:\WorkSpace\stride
---

## 会话流程（强制执行）

### 会话开始时
**必须使用 Explore subagent 读取上下文：**

调用方式：使用 Agent tool，设置 subagent_type 为 "Explore"

**读取内容：**
1. `docs/ARCHITECTURE_OVERVIEW.md` - 当前系统状态
2. `docs/log/` 目录下最新的会话日志 - 上次工作内容和 Next Session
3. 根据用户任务读取相关的 `docs/design/` 文档

**返回格式：**
- 当前系统状态（哪些已完成、进行中、规划中）
- 上次会话的关键决策和未完成任务
- 相关设计文档的要点
- 需要注意的 Gotchas

### 会话结束时
**必须完成以下步骤：**

1. 创建会话日志文件：
   - 路径：`docs/log/YYYY/MM/DD/YYYY-MM-DD-[seq]-[description].md`
   - 使用模板：`docs/log/TEMPLATE.md`

2. 日志必须包含：
   - [ ] Session Goal - 本次目标
   - [ ] What We Did - 完成的工作（包含文件引用）
   - [ ] Decisions Made - 做出的决策
   - [ ] What Worked / What Didn't Work - 成功/失败经验
   - [ ] Next Session - 下次任务
   - [ ] Quick Reference for Future Claude - 给下次会话的快速参考

3. Git 提交时引用日志：
   ```bash
   git commit -m "实现 X (见 docs/log/2026/04/06/2026-04-06-x.md)"
   ```

---

## Critical Rules

### ❌ NEVER DO THESE
1. **不要直接修改 Stride 引擎源码** → 如需修改，在项目中扩展或提 PR
2. **不要在会话结束时跳过构建验证** → 必须确保 `dotnet build` 成功
3. **不要复制粘贴整个文件到日志** → 使用文件链接 `File.cs:Line`
4. **不要忽略上次日志中的 "Don't try this again"** → 避免重复错误
5. **不要在 GPU 热路径使用 CPU 处理** → 使用 Compute Shader

### ✅ ALWAYS DO THESE
1. **会话开始时使用 subagent 读取上下文** → 确保连续性
2. **会话结束时创建日志文件** → 记录决策和经验
3. **记录失败方法及原因** → "Don't try this again because..."
4. **链接到具体代码位置** → `File.cs:LineStart-LineEnd`
5. **确保构建成功后再结束** → `dotnet build` 零错误

---

## 文档更新规则

### 何时更新 ARCHITECTURE_OVERVIEW.md
- 新系统实现完成时
- 系统状态变更时（✅ → 🚧 → 📋）
- 关键文件路径变更时

### 何时创建决策记录 (ADR)
- 选择方案 A 而非方案 B 时
- 接受某种权衡时
- 改变架构方向时
- 存放路径：`docs/log/decisions/[decision-name].md`

### 何时创建学习文档
- 发现可复用的模式时
- 遇到值得记录的陷阱时
- 找到特定问题的解决方案时
- 存放路径：`docs/log/learnings/[topic].md`

---

## 目录结构

```
docs/
├── ARCHITECTURE_OVERVIEW.md   # 架构概览（AI 首先读取）
├── design/                    # 设计文档
├── specs/                     # 规格文档
└── log/                       # 会话日志
    ├── README.md              # 日志系统说明
    ├── TEMPLATE.md            # 会话模板
    ├── YYYY/MM/DD/            # 按日期组织
    ├── decisions/             # 架构决策记录 (ADR)
    └── learnings/             # 技术学习文档
```

---

## 快速参考

| 场景 | 行动 |
|------|------|
| 新会话开始 | 用 Explore subagent 读取上下文 |
| 会话结束 | 创建日志，填写 Quick Reference |
| 做架构决策 | 创建 ADR 在 decisions/ |
| 发现模式/陷阱 | 创建学习文档在 learnings/ |
| Git 提交 | 提交信息引用日志路径 |
