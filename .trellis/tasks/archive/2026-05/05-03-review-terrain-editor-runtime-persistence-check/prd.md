# review: terrain editor runtime persistence check

## Goal

对当前工作区做一次完整质量检查，重点覆盖 Terrain Editor / Runtime 最近关于持久化、biome 重载、导出，以及 CPU `MaterialIndexMap` 删除的改动，找出行为回归、遗漏测试、spec 不一致和残留风险。

## What I already know

* 当前工作区没有激活任务，需要补建 review 任务上下文。
* 本次检查至少需要查看 `git diff` / `git status`、读取 editor/runtime 相关 spec，并运行 `dotnet build Terrain.sln`。
* 用户要求不提交代码，输出以 findings 和 residual risks / testing gaps 为主。

## Requirements

* 检查当前工作区改动及受影响文件。
* 读取并遵循 editor/runtime 相关 Trellis spec，重点是 index 与 persistence 文档。
* 运行必要验证，至少 `dotnet build Terrain.sln`。
* 以 code review 方式审查行为回归、测试覆盖、spec 同步和残留风险。

## Acceptance Criteria

* [ ] 已查看 `git diff` / `git status`
* [ ] 已读取相关 `.trellis/spec`
* [ ] 已运行 `dotnet build Terrain.sln`
* [ ] 已输出按严重性排序的 findings 或明确写明无发现

## Out of Scope

* 不提交代码
* 不做与本轮改动无关的泛化重构

## Technical Notes

* 重点 spec: `editor/index.md`, `runtime/index.md`, `editor/project-persistence.md`, `runtime/terrain-runtime-persistence.md`
