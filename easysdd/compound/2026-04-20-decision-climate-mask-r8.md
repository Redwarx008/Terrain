---
doc_type: decision
status: current
tags: [climate-mask, material, performance, architecture]
created: 2026-04-20
---

# ClimateMask R8 1/4 高度图分辨率间接映射

## 背景

直接绘制 MaterialIndexMap 效率低，难以表达海拔/坡度/季节等程序化规则。需要一种间接映射方式。

## 决定

使用 ClimateMask（R8 格式，1/4 高度图分辨率）存储气候 ID，通过规则栈（海拔/坡度/季节）求值生成 MaterialIndexMap。

## 备选方案

| 方案 | 优点 | 缺点 |
|---|---|---|
| **直接绘制 MaterialIndexMap** | 所见即所得 | 无法表达海拔/坡度规则、手动绘制低效 |
| **ClimateMask 1:1 分辨率** | 精度高 | 内存浪费（气候 ID 不需要高分辨率） |
| **ClimateMask R8 1/4 分辨率（选用）** | 内存小、规则驱动 | 间接映射增加复杂度 |

## 理由

1. 1/4 分辨率足够：气候区域通常是大面积连续的，不需要像素级精度
2. 内存节省：R8 单通道 × 1/4 面积 = 极低内存占用
3. 规则驱动：海拔 + 坡度 + 季节规则自动生成材质，大幅减少手动绘制
4. GPU Compute 生成：规则求值在 GPU 完成，实时更新

## 权衡

- 坐标转换额外层：ClimateMask→Heightmap ×4，ClimateMask→MaterialIndexMap ×2
- 规则优先级和冲突需要手动管理

## 影响

- ClimateMask 成为材质管线的输入端
- ClimateRuleService 管理规则栈
- GPU Compute shader（EditorTerrainBuildSplatMap）是核心转换逻辑
- 1 个 ClimateMask 像素映射到 2×2 MaterialIndex 像素