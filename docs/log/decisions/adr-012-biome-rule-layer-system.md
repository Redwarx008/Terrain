# ADR-012: Biome 规则层体系

**Date**: 2026-04-30
**Status**: ✅ Accepted
**Decision ID**: ADR-012

---

## Context

编辑器需要从手动画笔（PaintEditor/PaintMaterialTool）切换到规则驱动的纹理分布。需要设计 Biome → RuleLayer → Modifier 的层级体系。

## Decision

采用三层规则体系：

- **BiomeDefinition**: 定义 biome ID 和名称（如沙漠、草地、雪地）
- **BiomeRuleLayer**: 每个 biome 一个层，绑定一个 material slot，包含优先级和 modifier 列表
- **BiomeModifier**: 6 种类型（HeightRange, SlopeRange, CurvatureRange, DirectionRange, Noise, TextureMask），5 种混合模式（Multiply, Add, Subtract, Min, Max）

Climate→Biome 全面重命名。纹理分布完全由规则驱动，移除手动画笔。

## Options Considered

### Option 1: 保留手动画笔 + 规则叠加
两者共存，规则处理大面积分布，画笔处理细节。

**Pros:** 灵活性最高
**Cons:** 两种系统交互复杂，规则结果可能被画笔覆盖

### Option 2: 纯规则驱动（选中）
移除手动画笔，所有纹理分布由规则生成。

**Pros:** 架构简洁，GPU Compute Shader 批量生成
**Cons:** 细节控制不如画笔灵活

### Option 3: 纯画笔（原方案）
继续使用 PaintEditor/PaintMaterialTool。

**Pros:** 已有实现
**Cons:** 大面积纹理分布效率低，无程序化生成能力

## Rationale

参考 Unity ProceduralTerrainPainter 的 RuleLayer 系统设计。纯规则驱动可以让 Compute Shader 批量生成 SplatMap，避免逐像素 CPU 计算。手动画笔可以后续作为特殊工具重新引入。

## Trade-offs

**What we gain:**
- GPU Compute Shader 批量生成 SplatMap
- 程序化纹理分布（海拔、坡度、曲率、方向）
- Noise 修改器提供自然变化

**What we give up:**
- 无法手动绘制细节纹理
- Modifier 参数较复杂

## Known Issues

- H1: Noise 修改器 FBM 被替换为单八度，Octaves 参数无效
- H2: Modifier Opacity 在 UI 中无编辑入口，锁死 100%
- H3: LayerHeatmap 调试图 >3 层时始终错误
- H4: TextureMask 修改器暴露在 UI 但功能链路未闭合

## Implementation Notes

- `BiomeRuleService` 是单例，管理 Biome/Layer/Modifier CRUD
- `BiomeEditor` 是笔刷服务，将 biome ID 写入 `BiomeMask`（R8, 1/2 分辨率）
- `EditorTerrainBuildSplatMap.sdsl` 是 GPU Compute Shader，从 BiomeMask + LayerBuffer + ModifierBuffer 生成材质权重
- Falloff 语义对齐 Unity：向范围外延伸

## References

- [开发时间线](../development-timeline.md)
- [Unity RuleLayer 参考](../learnings/unity-rulelayer-reference.md)
- [纹理管线](../learnings/terrain-texturing-pipeline.md)
- [Biome 审查发现](../learnings/biome-rule-layer-review-findings.md)

---

*ADR Version: 1.0*