# Fixed Buffer Top4 Hot Paths

**Date:** 2026-06-22

## Context

`Export Terrain` 的 baked detail 生成会按 detail texel 扫描整张半分辨率图，并在每个 texel 上聚合多个 biome layer 的材质贡献。这个路径天然是 O(width * height * layers)，任何逐 texel 堆分配都会被放大。

## Pattern

在每像素、每 texel、每顶点这类固定小集合热路径中，如果只需要 top N：

- 使用固定大小 `Span<T>` / 预分配数组保存候选项。
- 插入时做线性合并和 top N 选择。
- 明确保留原来的 tie-break 规则，例如权重降序后按首次出现顺序或材质编号升序。
- 用行为测试覆盖排序、重复项聚合、fallback 和格式 roundtrip。

## Avoid

- 在热路径里创建 `List<T>` / `Dictionary<TKey,TValue>`。
- 在热路径里用 LINQ `OrderByDescending().ThenBy().Take().ToArray()`。
- 为了性能加入近似比较 epsilon，除非原行为也如此；top4 排序应保持原来的精确 tie-break。

## Example

- `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`
- `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

## Validation

本次修复新增测试确认：

- `ushort[]` 快路径和 `Func<int,int,float>` 高度路径在混合 modifier 下逐 texel 输出一致。
- baked detail builder 和 detail mip downsample 不再包含逐 texel collection/LINQ 排序模式。

