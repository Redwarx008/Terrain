# Terrain Runtime Persistence

> Runtime 侧 `.terrain` 文件格式与 biome 重建链路的可执行合同。适用于修改 `TerrainExporter`、`TerrainFileReader`、`TerrainProcessor`、`TerrainStreamingManager` 或 biome 规则读取器时。

## Scenario: Runtime Rebuilds Detail Maps From Biome Sources

### 1. Scope / Trigger

- Trigger: 修改 `.terrain` 二进制格式、Runtime 读取逻辑、biome 规则读取，或 detail map 生成责任边界。
- 目标：保证 Editor 与 Runtime 以同一组真源工作，避免把 `MaterialIndexMap` 这类派生缓存错误持久化。

### 2. Signatures

- `TerrainExporter.ExportAsync(string outputPath, IProgress<ExportProgress> progress, CancellationToken ct)`
- `TerrainFileReader.ReadAllHeightData()`
- `TerrainFileReader.ReadAllBiomeMaskData()`
- `RuntimeBiomeRuleSet.ReadFromToml(string tomlFilePath)`
- `RuntimeDetailMapBuilder.Generate(...)`
- `TerrainStreamingManager(...)`

### 3. Contracts

- `.terrain v6` 文件头：
  - `Version == 6`
  - `SplatMapFormat == VTFormat.R8`
  - `SplatMapMipLevels` / `SplatMapResolutionRatio` 描述的是 `BiomeMask`，不是 detail index map
- `.terrain` 允许持久化的数据：
  - heightmap VT
  - biome mask VT
  - MinMaxErrorMap
- `.terrain` 禁止持久化的数据：
  - detail index VT
  - detail weight VT
  - `MaterialIndexMap`
- Runtime 加载合同：
  - 先从 `.terrain` 读回完整 `heightmap + biome mask`
  - 再从 `TerrainComponent.BiomeConfigPath` 指向的 TOML 读取 `material_slots + biome_layers + biome_modifiers`
  - 最后在 Runtime 侧生成 detail index / weight 页并上传 GPU

### 4. Validation & Error Matrix

| 条件 | 处理 |
|---|---|
| `.terrain` 版本不是 `6` | `TerrainFileReader` 直接拒绝加载 |
| `heightmapHeader.BytesPerPixel != 2` | 抛 `InvalidDataException` |
| `BiomeMask` VT `BytesPerPixel` 与读取类型不匹配 | 抛 `InvalidDataException` |
| `BiomeConfigPath` 为空或文件不存在 | 记录 warning，Runtime 允许回退到默认材质结果 |
| detail page 请求发生时缺少运行时重建数据 | 抛 `InvalidOperationException`，视为实现错误 |

### 5. Good / Base / Bad Cases

- Good:
  - Editor 导出 `.terrain v6` 后，Runtime 使用 `.terrain + BiomeConfigPath` 指向的 TOML 得到与 Editor biome 规则一致的材质混合。
- Base:
  - `BiomeConfigPath` 丢失时，Runtime 仍能加载高度和 biome mask，但 detail maps 回退到默认结果。
- Bad:
  - Runtime 继续尝试从 `.terrain` 读取 detail weight block，或导出器继续写入 `MaterialIndexMap` 派生图。

### 6. Tests Required

- Build:
  - `dotnet build Terrain.sln`
- Manual regression:
  - 导出一个含 biome 规则的地形，确认 `.terrain` 能被 Runtime 打开。
  - 删除旧的 detail map 持久化假设后，确认 Runtime 仍能正确显示材质。
  - 修改 `BiomeConfigPath` 指向的 TOML biome 规则后不重导出 `.terrain`，仅重新加载 Runtime，断言材质结果会跟随规则变化。

### 7. Wrong vs Correct

#### Wrong

```csharp
// 把运行时派生缓存写进 .terrain
WriteStruct(writer, ref detailIndexHeader);
StreamMipLevels(writer, materialIndexMap.GetIndexRawData(), ...);
```

#### Correct

```csharp
// 只写真源；Runtime 再读 TOML 规则重建派生图
WriteStruct(writer, ref biomeMaskHeader);
StreamMipLevels(writer, biomeMaskData, ...);
```
