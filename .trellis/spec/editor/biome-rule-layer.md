# Biome Rule Layer — Modifier Falloff Semantics

> Range 类型 Modifier 的 falloff 行为约定。

---

## Falloff 语义（对齐 Unity ProceduralTerrainPainter）

MinFalloff / MaxFalloff 控制 Range 类型 modifier（Height、Slope、Curvature）在范围**边界向外**的渐变过渡：

- **MinFalloff**：值在 `[Min - MinFalloff, Min]` 区间线性过渡（0→1），`< Min - MinFalloff` 为 0
- **MaxFalloff**：值在 `[Max, Max + MaxFalloff]` 区间线性过渡（1→0），`> Max + MaxFalloff` 为 0
- 最终权重 = `minWeight * maxWeight`

Shader 实现在 `ComputeRangeModifier`（EditorTerrainBuildSplatMap.sdsl）。

公式必须直接使用 value 的绝对值计算：

```csharp
minWeight = saturate((value - (Min - MinFalloff)) / MinFalloff);
maxWeight = saturate(((Max + MaxFalloff) - value) / MaxFalloff);
```

## 默认值

| Modifier 类型 | MinFalloff | MaxFalloff |
|---|---|---|
| HeightRange | 1.0 | 1.0 |
| SlopeRange | 10.0 | 10.0 |
| CurvatureRange | 0.001 | 0.001 |

## 约束

- Falloff 最小值 = 0.001（不允许 0，始终有微弱过渡）
- UI slider 范围按 modifier 类型独立设置（Height 0-1000, Slope 0.001-90, Curvature 0.001-1）
- TOML 配置字段 `min_falloff` / `max_falloff` 读写默认值 = 0.001

## CurvatureRange 旧数据兼容

Curvature 支持 -1..1 旧域到 0..1 新域的 remap，falloff 值同步缩放 0.5 倍。此逻辑在 shader `EvaluateModifier` 的 `ModifierTypeCurvatureRange` 分支中处理。

## Slider 精度与手动输入

Modifier 参数的 Slider 和 TextBox 必须按类型适配精度：

| Modifier 类型 | Min/Max 步进 | Falloff 步进 | 显示精度 |
|---|---|---|---|
| HeightRange | 1.0 | 1.0 | F1 |
| SlopeRange | 0.5 | 0.5 | F1 |
| CurvatureRange | 0.001 | 0.001 | F3 |
| DirectionRange | 1.0 | — | F1 |
| Noise | 0.01 | — | F1 |

- Falloff slider 最小值统一为 0.001（不允许 0）
- Falloff slider 最大值按类型独立：Height 1000, Slope 90, Curvature 1
- CurvatureRange 使用 3 位小数（F3）显示，其他类型使用 1 位小数（F1）
- TextBox 必须支持手动输入（TwoWay 绑定），不能 IsReadOnly
- Display 属性必须可读写，setter 解析字符串并写回对应的 float 属性
- slider 必须设置 `SmallChange` 绑定，确保小范围域（如 Curvature 0-1）可用

## Editor 预览生成链路

- Editor 侧 biome 规则编辑、新建 biome、新建 modifier、加载 biome mask 时：
  - 只能标记 `BiomeBuffer/LayerBuffer/ModifierBuffer` 与 `BiomeSplatDirty`
  - 由 `EditorTerrainBuildSplatMap` compute shader 直接重建 `DetailIndexMapTextures` / `DetailWeightMapTextures`
- 禁止在这些交互里触发 CPU 全图 detail-map 重建
- `Terrain.Editor` 不再维护 CPU `MaterialIndexMap` 作为预览真源
- 高度编辑仍允许继续走 CPU height cache，但材质控制图必须由 GPU 根据 `height slices + biome mask + rule buffers` 现算

### Wrong

```csharp
biomeRuleService.StateChanged += (_, _) => RegenerateMaterialIndices(); // CPU 全图重建
```

### Correct

```csharp
terrainEntity.MarkBiomeRulesDirty();
terrainEntity.MarkAllBiomeSplatDirty(); // 交给 GPU compute 重建
```

## Biome 基础材质 fallback

`EditorTerrainBuildSplatMap.sdsl` 和 `Shared/TerrainDetailGeneration.cs` 必须保持同一套 layer 混合语义：

- layer 按优先级从高到低处理，逐步消耗 `remainingWeight`
- 如果有效 biome 仍有 `remainingWeight`，剩余权重必须回落到该 biome 的基础 layer 材质槽
- 不能把有效 biome 的剩余权重硬编码到 material slot 0
- biome 无效、没有 layer、或全部 layer 被禁用/隐藏时，允许使用固定 slot 0 作为异常兜底；正常 Default Base 显示不得依赖额外 shader 参数旁路

### Wrong

```csharp
if (remainingWeight > 0.0001f)
    PushTop4(bestIndices, bestWeights, 0, remainingWeight);
```

### Correct

```csharp
int fallbackMaterialSlotIndex = 0;
// 遍历到同 biome 的有效 layer 时更新，循环结束后保留基础 layer 的材质槽。
fallbackMaterialSlotIndex = layer.MaterialSlotIndex;

if (remainingWeight > 0.0001f)
    PushTop4(bestIndices, bestWeights, fallbackMaterialSlotIndex, remainingWeight);
```

**原因**：`Default Biome / Default Base` 这类基础层通常承担未覆盖区域。如果剩余权重写死回 slot 0，替换基础层材质槽后，坡度/高度 modifier 之外的区域仍会显示旧 slot 0，看起来像该 layer 的纹理替换无效。

## Height blend 必须屏蔽 0 权重材质

`EditorTerrainDiffuse.sdsl` / `MaterialTerrainDiffuse.sdsl` 在根据 control map 混合材质时，不能让 `controlWeight == 0` 的材质参与 height blend。

### Wrong

```csharp
float4 mat = materialHeights + materialFactors;
float4 matBlend = max(mat - blendStart, 0.0f);
return matBlend / dot(matBlend, 1.0f);
```

### Correct

```csharp
float4 activeMaterials = step(float4(0.00001f), materialFactors);
float4 mat = lerp(float4(-10000.0f), materialHeights + materialFactors, activeMaterials);
float4 matBlend = max(mat - blendStart, float4(0.0f)) * activeMaterials;
```

**原因**：未使用的 control slot 会被渲染 shader 临时采样成 slot 0。如果它的 alpha/height 继续参与 blend，即使权重为 0，也会把 slot 0 混回最终颜色。基础层只有一个有效材质时最容易暴露这个问题。
