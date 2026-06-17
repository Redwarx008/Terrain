# River Bottom World-UV Capture Alignment Design

## Goal

把 current `RiverBottom.sdsl` 从仓库当前锁定的 `advanced/tangentUv` 主路径，回退到 `ck3-river.rdc` 这帧实际使用的 `CalcRiverBottom` 语义：`worldUv` 主采样、`tangentUv` 只负责 parallax/depth/profile。

## Evidence

- `ck3-river.rdc` `event 336` 的 disasm 明确显示：
  - `worldUv = worldPos.xz + worldSpaceParallax * width`
  - `BottomDiffuse / BottomProperties / BottomNormal` 都用 `worldUv`
  - alpha 不依赖 `Diffuse.a` 或 `smoothstep(bank)`，而是 `FadeOut * FadeToConnection * saturate(Depth * 13.0f)`
- current `debug.rdc` `event 184` 上，仅把 `BottomDiffuse` 改成 `worldUv` 采样并保留原 distance packing，河心 final 像素就从约 `(0.125, 0.142, 0.138)` 降到 `(0.046, 0.039, 0.019)`，已经接近 CK3 代表像素 `(0.046, 0.042, 0.036)`。
- 当前仓库中的 `RiverBottom.sdsl`、`RiverShaderTextTests.cs` 和 `2026-06-16-river-ck3-parity-design.md` 仍把 parity 目标锁在 `CalcRiverBottomAdvanced`，与 capture 事实冲突。

## Decision

本轮优先修正 shader 分支和测试目标，不先处理 pre-bottom payload：

1. `RiverBottom.sdsl` 改为 CK3 capture 对齐的 non-advanced/world-UV 主采样路径。
2. `tangentUv` 继续保留，用于 parallax 偏移和横截面 depth/profile。
3. `BottomDiffuse / BottomProperties / BottomNormal` 主采样统一改为 `worldUv`。
4. alpha 改回 capture 对齐的 `FadeOut * FadeToConnection * saturate(depth * 13.0f)`。
5. 文本测试从 “advanced/tangentUv parity” 改为 “capture-aligned worldUv parity”。

## Scope

- 修改 `Terrain.Editor/Effects/RiverBottom.sdsl`
- 修改 `Terrain.Editor.Tests/RiverShaderTextTests.cs`
- 更新相关架构/日志文档中的当前实现状态

## Non-Goals

- 本轮不改 `RiverSurface.sdsl`
- 本轮不实现 CK3 风格独立 pre-bottom payload
- 本轮不接 `ShadowTexture` 或完整 CK3 lighting 函数

## Verification

1. 先改文本测试，确认 RED。
2. 改 `RiverBottom.sdsl`，确认文本测试转 GREEN。
3. 跑 `RiverShaderCompileTests`，确保 shader front-end/back-end 都能编译。
4. 按 Stride shader 工作流执行：
   - `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles`
   - `StrideCleanAsset`
   - `StrideCompileAsset`
5. 最后跑 `Terrain.Editor.Tests`，确认既有 river 相关测试不回归。
