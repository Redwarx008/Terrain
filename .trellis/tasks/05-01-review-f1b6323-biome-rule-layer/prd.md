# Fix biome rule layer regression findings

## Goal

修复对提交 `f1b6323` 的审查中确认的具体缺陷，优先恢复 biome rule layer 迁移后的核心编辑体验和渲染正确性。

## What I already know

* 已完成对 `f1b6323` 的独立审查，结论记录在 `review-findings.md`
* 当前用户要求从审查进入修复，不再保持只读限制
* 已确认的高优先级问题集中在 shader 求值、modifier UI 绑定、TextureMask 配置链路和调试热图语义

## Assumptions (temporary)

* 本轮先修复高优先级缺陷，以及一个直接影响结果正确性的中优先级问题（modifier 迭代顺序）
* 低优先级 UX / 主题问题不在本轮范围内，除非修复高优先级时顺带自然消除
* 用户已明确要求本轮暂不处理 TextureMask；该项保留为后续修复，不在当前实现范围内

## Requirements (evolving)

* 修复 Noise modifier 使用单八度 `Noise2D()` 且忽略 `Octaves` 的回归
* 修复 modifier stack 顺序与参考实现不一致的问题，确保非交换式 BlendMode 结果正确
* 恢复 modifier `Opacity` 的可编辑 UI，并保持 ViewModel → Service → GPU 的同步链路正常
* 修复 LayerHeatmap 调试视图的错误语义，避免把全局 layer index 误当成 top-4 weight channel
* 运行构建与必要检查，确认修复没有破坏 editor/runtime 边界

## Acceptance Criteria (evolving)

* [x] Noise modifier 的 Octaves 滑块重新生效，shader 消费八度参数
* [x] modifier 顺序与参考实现一致，混合 Add/Subtract/Multiply 时结果不再漂移
* [x] 新 biome UI 可以编辑 modifier Opacity，并驱动实时重算
* [x] LayerHeatmap 调试模式不再显示误导性结果
* [x] `dotnet build Terrain.sln` 通过

## Definition of Done (team quality bar)

* 产品代码完成修复
* 构建和必要检查通过
* 审查文档与 spec 如有必要同步更新

## Out of Scope (explicit)

* 处理本次审查中的所有低优先级 UX/主题问题
* 重构整个 biome 系统或引入全新的热图纹理架构

## Technical Notes

* 关键文件预计包括 `EditorTerrainBuildSplatMap.sdsl`、`EditorTerrainDiffuse.sdsl`、`EditorTerrainEntity.cs`、`EditorTerrainSplatMapComputeDispatcher.cs`、`BiomeViewModel.cs`、`ModifierViewModel.cs`、`MainWindow.axaml`
* 需要同时验证 ViewModel/UI 绑定链路与 shader 参数绑定链路
