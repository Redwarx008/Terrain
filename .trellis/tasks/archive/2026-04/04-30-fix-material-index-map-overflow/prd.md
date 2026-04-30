# 向 CK3 方案靠拢：半分辨率 SplatMap + 手动双线性索引匹配 + Height Blend

## Goal

将编辑器侧的 MaterialIndexMap 从高度图 1:1 分辨率改为 1/2 分辨率（与 CK3 一致），解决大地形数组溢出问题，同时确保材质边界平滑过渡（手动双线性 + Height Blend 已有，无需新增）。

## What I already know

### CK3 参考实现
- CK3 heightmap: 18432×9216，detail_index/detail_intensity: 9216×4608（**恰好 1/2**）
- WorldSpaceToDetail 独立缩放因子，不与高度图 1:1
- Shader 中手动 2×2 双线性采样 Index + Mask，索引去重，权重累加
- CalcHeightBlendFactors 做 Height Blend

### 我们项目现状
- **运行时 Shader 已完整实现** CK3 方案：`MaterialTerrainDiffuse.sdsl` 已有 AccumulateControlTexel + CalcHeightBlendFactors + splatPageLocalPos
- **编辑器 Shader 也有** AccumulateControlTexel + CalcHeightBlendFactors，但用 `streams.TerrainLocalPos`（1:1 坐标）
- **编辑器 C# 侧**：MaterialIndexMap 和 ClimateMask 都是 1:1 高度图分辨率创建
- 27153×25216 地形，MaterialIndexMap 每个数组 2.55GB 超出 .NET 单数组上限
- 半分辨率后 0.64GB，不溢出

### 现有设计文档
- `docs/superpowers/specs/2026-04-14-splatmap-half-resolution-design.md` — 完整全链路设计
- `easysdd/compound/2026-04-20-decision-splatmap-half-resolution.md` — 决策记录

## Assumptions

- 运行时侧 `splatPageLocalPos` 机制已就绪，半分辨率只需要调整 SplatInfo 中的 stride/offset
- ClimateMask 保持 1:1 不变（0.64GB 不溢出）
- 不做 v2 向后兼容，旧 .terrain 文件需重新预处理

## Open Questions

(none — all resolved)

## Decision (ADR-lite)

**Context**: MaterialIndexMap 1:1 分辨率导致大地形数组溢出，且 CK3 已验证 1/2 分辨率视觉差异可忽略。
**Decision**: 全链路实施半分辨率，不做 v2 向后兼容。文件格式直接升 v3。
**Consequences**: 旧 .terrain 文件需重新预处理；运行时和编辑器统一 1/2 比例，简化坐标逻辑。

## Requirements

### 1. 文件格式
- .terrain 版本 v2→v3
- Reserved1 → SplatMapResolutionRatio（值 2 = 半分辨率）
- 不做 v2 向后兼容，加载 v2 文件直接报错/跳过

### 2. 预处理 (TerrainPreProcessor)
- 加载 splatmap 后降采样到 1/2（复用 CoordinateConsistentMipmap）
- 从降采样后尺寸计算 splatMapMipLevels
- 写入 header 设置 SplatMapResolutionRatio = 2

### 3. 编辑器 C# 层
- MaterialIndexMap 创建为半尺寸：`(heightWidth+1)/2 × (heightHeight+1)/2`
- 解决 27153×25216 大地形数组溢出
- EditorTerrainBuildSplatMap compute shader 输出纹理尺寸相应减半
- ClimateMask 保持 1:1 分辨率

### 4. 编辑器 Shader 层
- `EditorTerrainHeightParameters.sdsl` 中坐标 ÷2（或新增 SplatScale 常量）
- EditorTerrainBuildSplatMap 输出尺寸减半

### 5. 编辑器画笔层
- PaintBrushCore、PaintTool、EraseTool 画笔坐标 ÷2
- 撤销/重做区域捕获在 splatmap 空间操作

### 6. 运行时流式加载
- TerrainFileReader 新增 SplatMapResolutionRatio 属性
- TerrainChunkNode 新增 SplatInfo 字段（splatSliceIndex, splatPageOffsetX, splatPageOffsetY, splatPageTexelStride）
- Page key 转换（heightmap key → splatmap key）
- TerrainStreamingManager 分离 heightmap/splatmap 页面请求

### 7. 运行时 Shader
- TerrainHeightStream.sdsl: 已有 TerrainSplatSliceIndex + TerrainSplatPageLocalPos streams
- MaterialTerrainDisplacement.sdsl: 从 SplatInfo 计算 splatPageLocalPos（已有框架，需填充 SplatInfo 数据）
- MaterialTerrainDiffuse.sdsl: 已完整实现，无需修改

## Acceptance Criteria (evolving)

- [ ] 27153×25216 地形可以正常加载，不再抛出 OverflowException
- [ ] 材质边界平滑过渡，无马赛克/锯齿
- [ ] 画笔编辑 + 撤销/重做正常工作
- [ ] 小地形行为无回归
- [ ] `dotnet build` 零错误

## Definition of Done

- 手动加载大地形成功
- 画笔编辑 + 撤销/重做正常
- 材质边界视觉质量不降级
- `dotnet build` 零错误

## Out of Scope (explicit)

- ClimateMask 分辨率变更
- v2 .terrain 文件向后兼容
- MaterialProperties 材质属性贴图半分辨率（独立优化项）

## Technical Notes

### 关键文件清单

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| **文件格式** | | |
| `Terrain.Editor/Models/TerrainFileFormat.cs` | C# | SupportedVersion 4→5, SplatMapResolutionRatio 写入 |
| `Terrain/Streaming/TerrainStreaming.cs:103-123` | C# | TerrainFileHeader 版本同步 |
| **预处理** | | |
| `Terrain/Core/TerrainProcessor.cs` | C# | splatmap 降采样 + 写入 ratio=2 |
| **编辑器 C#** | | |
| `Terrain.Editor/Services/TerrainManager.cs:170-172` | C# | MaterialIndexMap 创建尺寸减半 |
| `Terrain.Editor/Services/MaterialIndexMap.cs` | C# | Fill() 等 int 溢出修复 |
| `Terrain.Editor/Services/PaintBrushCore.cs` | C# | 画笔坐标 ÷2 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | C# | 纹理创建尺寸调整 |
| `Terrain.Editor/Rendering/EditorTerrainSplatMapComputeDispatcher.cs` | C# | dispatch 尺寸调整 |
| **编辑器 Shader** | | |
| `Terrain.Editor/Effects/EditorTerrainHeightParameters.sdsl` | Shader | LoadIndexMapAtGlobal 坐标 ÷2 |
| `Terrain.Editor/Effects/EditorTerrainBuildSplatMap.sdsl` | Shader | 输出尺寸减半 |
| **运行时** | | |
| `Terrain/Streaming/TerrainStreaming.cs` | C# | 版本校验、SplatMapResolutionRatio 读取 |
| `Terrain/Rendering/TerrainQuadTree.cs` | C# | SplatInfo 填充（ratio 缩放） |
| **运行时 Shader** | | |
| `Terrain/Effects/Stream/TerrainHeightStream.sdsl` | Shader | 已有，无需修改 |
| `Terrain/Effects/Material/MaterialTerrainDisplacement.sdsl` | Shader | 已有 SplatInfo 框架，需确认 stride 计算正确 |
| `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` | Shader | 已完整实现，无需修改 |

### 运行时已有基础设施（无需新建）

- `TerrainChunkNode.SplatInfo` (Vector4) — 已存在
- `GetSplatMapPageKey()` — 已实现 page key 转换（含 ratio 缩放）
- `SplatMapResolutionRatio` 属性 — 已存在（version >= 3 时读取）
- `TerrainHeightStream` splat streams — 已声明
- `MaterialTerrainDiffuse` AccumulateControlTexel + CalcHeightBlendFactors — 已实现

### CK3 Shader 参考
- `E:/SteamLibrary/steamapps/common/Crusader Kings III/clausewitz/gfx/FX/cw/pdxterrain.fxh` — CalculateDetails 函数
- `E:/SteamLibrary/steamapps/common/Crusader Kings III/game/gfx/FX/pdxterrain.shader` — 主 shader

### 现有设计文档
- `docs/superpowers/specs/2026-04-14-splatmap-half-resolution-design.md`
- `easysdd/compound/2026-04-20-decision-splatmap-half-resolution.md`
