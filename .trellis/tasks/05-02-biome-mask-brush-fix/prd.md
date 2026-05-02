# fix biome mask brush not applying selected biome

## Goal

修复 Landscape/Biome Mask 笔刷看起来没有生效的问题，使用户在选择某个 biome 后，笔刷能稳定地把对应区域写成该 biome，并让后续 biome layer 规则组合按该 biome 生效。

## What I already know

* 输入入口在 `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`，Landscape 模式会把当前 `EditorState.CurrentBiomeId` 传给 `BiomeEditor.ApplyStroke(...)`。
* `BiomeEditor` 当前直接写 `BiomeMask`，然后调用 `TerrainManager.MarkBiomeMaskDirty()` 和 `RegenerateMaterialIndices(...)`。
* `BiomeMask` 是离散分类图，每个 texel 只存一个 biome id，不是连续权重贴图。
* 当前 `BiomeEditor` 使用 `Random.Shared.NextSingle() < strength` 的概率式写入；默认 `BrushParameters.Strength = 0.5` 时，连笔刷中心都可能不写入目标 biome。
* `BiomeMask` 与 splatmap 共用半分辨率空间，相关坐标换算需要遵守 `.trellis/spec/guides/cross-layer-thinking-guide.md` 中的多分辨率空间约定。

## Assumptions (temporary)

* 用户的目标是“选中 biome N 后，笔刷就把刷过的区域分类为 biome N”，而不是做随机/半透明式的 biome 混合喷涂。
* 这次主要问题在 biome 写入策略本身，而不是 UI 选中状态完全丢失。

## Open Questions

* 若实现过程中发现笔刷预览半径与实际生效半径明显不一致，是否需要在本任务顺手修正。

## Requirements

* Landscape 模式下，当前选中的 biome 必须稳定写入 `BiomeMask`。
* biome 笔刷不能依赖随机概率决定是否写入离散 biome id。
* biome 笔刷写入后，BiomeMask 脏标记和材质重建链路必须继续正常触发。
* 实现必须保持 heightmap space 与 splatmap space 的坐标换算一致，不引入新的 1/2 分辨率错位。
* 用户交互上不再要求单独的 `Landscape` 模式；现有 biome 绘制入口统一收敛到 `Paint` 模式，并将其作为 biome 编辑模式对外呈现。

## Acceptance Criteria

* [ ] 选择 biome 1 并使用笔刷时，笔刷覆盖区域会被写成 biome 1，而不是随机部分生效。
* [ ] 不同 biome 的区域切换后，对应 biome layer 规则组合能够按 mask 分类结果参与后续材质生成。
* [ ] 不引入新的越界写入、坐标空间错位或脏标记遗漏。

## Definition of Done (team quality bar)

* 代码修改符合 editor 状态管理与跨层数据流规范
* 相关验证已运行并记录结果
* 如本次修复产出新经验，评估是否需要更新 spec

## Out of Scope (explicit)

* 重新设计 biome layer 规则系统
* 新增独立的 biome 权重混合贴图工作流
* 实现真正的材质 paint/erase 贴图工作流

## Technical Notes

* 重点文件：
  * `Terrain.Editor/Services/BiomeEditor.cs`
  * `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
  * `Terrain.Editor/Services/EditorState.cs`
  * `Terrain.Editor/Services/TerrainManager.cs`
* 相关规范：
  * `.trellis/spec/editor/state-management.md`
  * `.trellis/spec/editor/quality-guidelines.md`
  * `.trellis/spec/guides/cross-layer-thinking-guide.md`
