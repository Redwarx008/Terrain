# Align Modifier Falloff Semantics to Reference

## Goal

将 Modifier stack 中 HeightRange/SlopeRange/CurvatureRange 的 MinFalloff/MaxFalloff 语义对齐到 Unity ProceduralTerrainPainter 参考实现，使 falloff 向范围外延伸而非向范围内收缩，同时修正默认值和 UI 范围。

## What I already know

* 当前实现的 falloff 在 Min/Max 范围**内部**渐变（值超出范围严格为0）
* Unity 参考的 falloff 在 Min/Max 范围**外部**渐变（值超出范围仍可有权重过渡）
* 三处需要修改：shader `ComputeRangeModifier`、C# 默认值、UI slider 范围
* Unity 默认值：Height min=1/max=1, Slope min=10/max=10, Curvature min=0.001/max=0.001
* Unity UI 范围：Height `[0.001, ∞)`, Slope `[0.001, 90]`, Curvature `[0.001, 1]`
* 当前 UI 使用统一 0-100 范围，对各 modifier 类型不适用

## Requirements

* shader `ComputeRangeModifier` 改为 Unity 语义：falloff 向范围外延伸
  - MinFalloff：值在 `[Min - MinFalloff, Min]` 区间线性渐变
  - MaxFalloff：值在 `[Max, Max + MaxFalloff]` 区间线性渐变
* 修正各 modifier 类型的 MinFalloff/MaxFalloff 默认值，与 Unity 参考一致
  - HeightRange: minFalloff=1, maxFalloff=1
  - SlopeRange: minFalloff=10, maxFalloff=10
  - CurvatureRange: minFalloff=0.001, maxFalloff=0.001（已是此值，不变）
* 修正 UI slider 范围，按 modifier 类型设定独立范围
  - HeightRange: 0 - 1000（与高度最大值匹配）
  - SlopeRange: 0.001 - 90（与度数域匹配）
  - CurvatureRange: 0.001 - 1（与曲率域匹配）
* CurvatureRange shader 中的旧数据兼容代码（-1..1 remap）需要同步更新 falloff 计算逻辑
* TOML 配置读写中已有的 min_falloff/max_falloff 字段保持不变，仅语义变化
* BiomeRuleLayer.BlendRange 属性（legacy 兼容）逻辑需适配新语义

## Acceptance Criteria

- [ ] shader `ComputeRangeModifier` 函数逻辑与 Unity `HeightMask`/`SlopeMask`/`CurvatureMask` 一致
- [ ] 新建 HeightRange modifier 时 MinFalloff=1, MaxFalloff=1
- [ ] 新建 SlopeRange modifier 时 MinFalloff=10, MaxFalloff=10
- [ ] CurvatureRange 默认值仍为 MinFalloff=0.001, MaxFalloff=0.001
- [ ] UI slider 范围按 modifier 类型分别设置，不再是统一的 0-100
- [ ] CurvatureRange 的 -1..1 旧数据 remap 代码与新 falloff 逻辑兼容
- [ ] BiomeRuleLayer.BlendRange 语义正确适配
- [ ] TOML 配置读写（min_falloff/max_falloff）仍正常工作

## Definition of Done

* 构建通过，无编译错误
* 视觉验证：设置非零 falloff 后材质过渡区域平滑，无突变
* Lint / typecheck green

## Out of Scope

* DirectionRange / Noise / TextureMask modifier（它们不使用 falloff）
* 修改 ModifierGpu 结构体布局（字段名和类型不变）
* 修改 TOML 配置文件格式
* 新增 falloff 曲线类型（如 smoothstep、ease in/out 等）

## Technical Notes

### 关键文件

* `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl` — shader `ComputeRangeModifier` (L198-210)
* `Terrain.Editor/Services/BiomeRuleService.cs` — 默认值 (L156-208), BlendRange (L106-119)
* `Terrain.Editor/ViewModels/ModifierViewModel.cs` — HasFalloff (L95), slider range helpers
* `Terrain.Editor/Views/MainWindow.axaml` — falloff slider UI (L830-843)
* `Terrain.Editor/Rendering/EditorTerrainEntity.cs` — ModifierGpu.FromModifier 传输层
* `Terrain.Editor/Services/TomlProjectConfig.cs` — TOML 读写

### Unity 参考算法（Filters.hlsl）

```hlsl
// HeightMask (L15-29)
float minEnd = (MIN_HEIGHT - MIN_HEIGHT_FALLOFF);
float minWeight = saturate((minEnd - (heightmap - MIN_HEIGHT)) / (minEnd - MIN_HEIGHT));
float maxEnd = MAX_HEIGHT + MAX_HEIGHT_FALLOFF;
float maxWeight = saturate((maxEnd - (heightmap - MAX_HEIGHT)) / (maxEnd - MAX_HEIGHT));
return saturate(maxWeight * minWeight);

// SlopeMask (L126-153) — 同样模式，falloff 归一化到 0-1 域
// CurvatureMask (L156-173) — 同样模式
```

### 当前算法（需替换）

```hlsl
// EditorTerrainBuildSplatMap.sdsl L198-210
float lower = saturate((value - minValue) / minFalloff);  // 内部渐变
float upper = saturate((maxValue - value) / maxFalloff);   // 内部渐变
return saturate(min(lower, upper));
```