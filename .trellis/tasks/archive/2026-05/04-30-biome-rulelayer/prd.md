# Biome Map + RuleLayer 地形纹理系统

## Goal

将当前的手动刷纹理工作流替换为纯规则驱动的 Biome Map + RuleLayer 程序化纹理生成工作流。用户只画 BiomeMask（R8，原 ClimateMask），每个 biome 由最多 5 个 RuleLayer 组合而成，每个 RuleLayer 通过 BiomeModifier 栈定义纹理出现条件，GPU Compute Shader 自动生成全部材质分布。移除手动画笔（PaintEditor），材质分布完全由 biome 规则控制。

## What I already know

* Biome→Layer→Modifier 三级数据模型已在 `ClimateRuleService` 中实现
* GPU Compute Shader `EditorTerrainBuildSplatMap` 已支持从 ClimateMask + BiomeBuffer/LayerBuffer/ModifierBuffer 生成 top-4 材质权重
* ClimateMask (R8) 刷笔已实现（`ClimateEditor`）
* TOML 持久化已支持 `biome_layers` + `biome_modifiers` 配置节
* 编辑器 UI（ClimateViewModel/RuleViewModel）只暴露了 legacy 属性，BiomeModifier 栈的完整 UI 未实现
* TextureMask 修饰符在着色器端返回硬编码值
* Unity ProceduralTerrainPainter 的 LayerSettings + Modifier 架构可作为 RuleLayer 设计参考，但无 Biome 分组概念

## Assumptions

* "最多 5 个 RuleLayer"指的是每个 Biome 最多 5 个 RuleLayer，这是 UI 层限制
* 当前 Compute Shader 的逻辑（遍历所有匹配 biomeId 的 Layer）已满足需求，不需要大改
* 用户只画 BiomeMask，不画纹理。PaintEditor/PaintMaterialTool 将被移除
* 命名从 "Climate" 迁移到 "Biome" 是本任务的一部分

## Requirements

1. **命名统一（Climate → Biome）**
   - ClimateMask → BiomeMask（类名、文件名、变量名、shader 参数名）
   - ClimateEditor → BiomeEditor
   - ClimateRuleService → BiomeRuleService
   - ClimateDefinition → BiomeDefinition
   - ClimateRuleLayer → BiomeRuleLayer
   - ClimateViewModel → BiomeViewModel
   - ClimateDefinitionViewModel → BiomeDefinitionViewModel
   - UI 文本统一为 "Biome"
   - TOML 配置节名统一
   - GPU shader 中的 Climate 相关命名统一

2. **Biome 定义与编辑**
   - 每个 Biome 有名称、调试颜色、关联的 RuleLayer 列表
   - Biome 可增删改，排序
   - 每个 Biome 最多 5 个 RuleLayer（UI 层限制，超出时提示）

3. **RuleLayer 定义与编辑**
   - 每个 RuleLayer 绑定一个材质槽位（MaterialSlotIndex）
   - 每个 RuleLayer 有优先级（PriorityOrder）
   - 每个 RuleLayer 可启用/禁用
   - RuleLayer 的 BiomeModifier 栈定义了纹理出现的条件

4. **BiomeModifier 栈编辑**
   - 6 种修饰符类型全部可用：HeightRange, SlopeRange, CurvatureRange, DirectionRange, Noise, TextureMask
   - 5 种混合模式：Multiply, Add, Subtract, Min, Max
   - 每个修饰符有 Opacity 控制
   - 修饰符可增删改、排序
   - TextureMask 修饰符需在 GPU 端正确实现

5. **Biome Map 刷笔**
   - 在 BiomeMask（R8）上刷 biome ID
   - 支持软边、概率混合
   - 实时预览 Compute Shader 生成结果
   - 用户只画 biome mask，不直接画纹理

6. **移除手动画笔**
   - 移除 PaintEditor/PaintMaterialTool/EraseTool（直接操作 MaterialIndexMap 的画笔）
   - PaintBrushCore 的通用逻辑（线性衰减、坡度过滤）保留供 BiomeMask 刷笔复用
   - 移除 IPaintTool 接口和 PaintEditCommand 中与手动画笔相关的部分

7. **RuleLayer Heatmap 预览**
   - 在编辑器中可预览每个 RuleLayer 的 modifier 栈合成结果
   - 可切换查看：BiomeMask / 单个 RuleLayer 蒙版 / 最终材质分布
   - 已有 Heatmap 基础（EditorState.SceneDebugViewMode），需扩展

8. **健壮性：边界情况处理**
   - 空 Biome（没有关联任何 RuleLayer）：该区域使用默认材质（material slot 0）或保持透明
   - 同一 Biome 内多个 RuleLayer 映射到同一 MaterialSlotIndex：Compute Shader 自然合并权重（PushTop4 会将同 index 的权重累加）
   - BiomeMask 中的 ID 无对应 Biome 定义：视为"无规则"区域，输出默认材质
   - RuleLayer 全部禁用：等同于空 Biome

## Acceptance Criteria

- [ ] 所有 Climate→Biome 命名迁移完成（类名、文件名、变量名、UI 文本、TOML 节名、shader 参数）
- [ ] 可创建/编辑 Biome，每个 Biome 关联最多 5 个 RuleLayer
- [ ] 每个 RuleLayer 可编辑完整的 BiomeModifier 栈（6 种类型、5 种混合模式）
- [ ] TextureMask 修饰符在 GPU Compute Shader 中正确实现
- [ ] 在 BiomeMask 上刷 biome ID 后，Compute Shader 自动生成正确的材质分布
- [ ] 手动画笔（PaintEditor/PaintMaterialTool/EraseTool）已移除
- [ ] 编辑器 UI 完整暴露 Biome/RuleLayer/Modifier 三级编辑能力
- [ ] RuleLayer Heatmap 预览可用（BiomeMask / 单层蒙版 / 最终分布三种视图）
- [ ] 空 Biome / 无效 ID / 同 MaterialSlot 等边界情况有合理处理
- [ ] TOML 持久化正确保存/加载 biome 配置

## Definition of Done

- Lint / typecheck 通过
- 编辑器可正常运行，无崩溃
- Biome 编辑→刷笔→预览的完整流程可用
- legacy 属性（MinAltitude/MaxAltitude 等）已迁移或移除

## Out of Scope

- Runtime biome 处理（runtime 使用预烘焙数据）
- 网络同步/多人编辑
- Biome 间的平滑过渡（超出当前 top-4 材质混合的能力）
- 手动画笔保留/混合（PaintEditor 将被移除，材质分布完全由规则驱动）
- BiomeMask 超出 R8 范围（256 个 biome 上限）

## Technical Notes

### 核心文件

| 文件 | 当前状态 | 需要改动 |
|---|---|---|
| `ClimateRuleService.cs` | Biome/Layer/Modifier 三级模型已存在 | 重命名为 BiomeRuleService，加 LayerCount UI 限制 |
| `EditorTerrainBuildSplatMap.sdsl` | 已支持完整的 modifier 评估 | TextureMask 修饰符需实现，Climate→Biome 命名 |
| `ClimateEditor.cs` → `BiomeEditor.cs` | 已实现 biome mask 刷笔 | 重命名，改进 |
| `ClimateMask.cs` → `BiomeMask.cs` | R8 数据已实现 | 重命名 |
| `ClimateViewModel.cs` → `BiomeViewModel.cs` | 只暴露 legacy 属性 | 重写为 Biome/RuleLayer/Modifier 编辑 |
| `RuleViewModel.cs` | 只暴露 legacy 属性（MinAltitude 等） | 重写为 Modifier 栈编辑 |
| `EditorTerrainEntity.cs` | GPU buffer 上传已实现 | 适配 TextureMask 资源，命名迁移 |
| `EditorState.cs` | 有 Climate 相关状态 | 重命名属性，扩展 Heatmap 模式 |
| `PaintEditor.cs` | 手动画笔 | 移除 |
| `PaintBrushCore.cs` | 通用画笔逻辑 | 评估是否被 BiomeEditor 复用 |
| `PaintMaterialTool.cs` / `EraseTool.cs` | 画笔工具实现 | 移除 |
| `TomlProjectConfig.cs` | 支持 legacy + new 格式 | 统一为 biome 格式，移除 legacy 节 |

### 边界情况处理策略

| 场景 | 处理方式 |
|---|---|
| 空 Biome（无 RuleLayer） | Compute Shader 输出 material slot 0，weight=1.0 |
| 无效 BiomeMask ID（无对应 Biome 定义） | 等同空 Biome，使用默认材质 |
| 同 Biome 内多 Layer 同 MaterialSlotIndex | PushTop4 自然合并（同 index 权重累加后 normalize） |
| 所有 RuleLayer 禁用 | 等同空 Biome |
| Biome 层数超过 5 | UI 层阻止添加，提示上限 |

### Research References

- [`research/current-texturing-pipeline.md`](research/current-texturing-pipeline.md) — 双管线架构、半分辨率 SplatMap、GPU Compute 生成流程
- [`research/unity-rulelayer-reference.md`](research/unity-rulelayer-reference.md) — Unity LayerSettings + Modifier 栈架构、GPU 混合模式、权重归一化
- [`research/project-context-and-specs.md`](research/project-context-and-specs.md) — 项目结构、spec 文档、已有 biome/climate 代码
