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
- 对每像素/texel 互不依赖的 bake，可按行并行；每个 worker 必须持有自己的固定缓冲，输出只能写当前行的唯一目标区间，并用阈值避免小图并行开销。
- 并行化前先确认错误路径语义：`Parallel.For` 会把 worker 异常包装成 `AggregateException`；如果错误是否可见取决于逐像素控制流，应在失败时串行重放来保持原异常类型和首个异常顺序。

## Avoid

- 在热路径里创建 `List<T>` / `Dictionary<TKey,TValue>`。
- 在热路径里用 LINQ `OrderByDescending().ThenBy().Take().ToArray()`。
- 为了性能加入近似比较 epsilon，除非原行为也如此；top4 排序应保持原来的精确 tie-break。
- 在线程之间共享可变贡献缓冲。

## Example

- `Terrain.Editor/Services/Export/BakedDetailMapBuilder.cs`
- `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`

## Validation

本次修复新增测试确认：

- `ushort[]` 快路径和 `Func<int,int,float>` 高度路径在混合 modifier 下逐 texel 输出一致。
- baked detail builder 和 detail mip downsample 不再包含逐 texel collection/LINQ 排序模式。
- 大图路径保留按行 `Parallel.For`，并继续通过 `.terrain` roundtrip / mip 聚合测试验证输出；`Func<int,int,float>` 高度回调保持串行，避免把线程安全要求外溢到测试/外部调用方。
