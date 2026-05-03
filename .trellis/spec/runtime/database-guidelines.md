# Database Guidelines

> How data persistence is handled in this project.

---

## Overview

本项目不使用传统数据库，而是通过文件系统和 Stride 资源系统进行数据持久化：

- **配置文件** - TOML 格式，使用 Tommy 库读写
- **地形数据** - 自定义二进制格式 (`.dat` 扩展名)
- **纹理资源** - Stride 资源系统 (`.sdpkg`)

---

## TOML Configuration

### 使用 Tommy 库

项目使用 [Tommy](https://github.com/irmen/Tommy) 库读写 TOML 配置文件。

### 读取模式

```csharp
using Tommy;

// 使用 using 模式确保流被正确关闭
using var reader = new StreamReader(configPath);
var toml = TOML.Parse(reader);

// 访问值
string? path = toml["Terrain"]["DataPath"]?.AsString;
int maxChunks = toml["Streaming"]["MaxChunks"]?.AsInt64 ?? 1024;
```

### 写入模式

```csharp
using var writer = new StreamWriter(configPath);
toml.WriteTo(writer);
```

### 关键约定

1. 始终使用 `using var` 模式
2. 对可选值使用 null 合并运算符
3. 使用 `AsString`, `AsInt64`, `AsFloat` 等方法进行类型转换
4. 路径使用 `StringComparison.OrdinalIgnoreCase` 进行比较

---

## Terrain Data Format

地形数据使用自定义二进制格式存储在 `Streaming/` 目录：

- `.dat` 文件包含高度图、splat 图、LOD 数据
- 使用 `BinaryReader`/`BinaryWriter` 进行读写
- 大端字节序存储

## Scenario: `.terrain` v6 Runtime Detail Reconstruction

### 1. Scope / Trigger

- Trigger: 修改 `.terrain` 导出格式、`TerrainFileReader` / `TerrainStreamingManager` / `TerrainProcessor` 读取链路，或修改运行时从项目 TOML 读取 biome 规则的逻辑。
- 目标：保持 editor 导出真源和 runtime 生成真源一致，避免再把预烘焙的 detail index / weight 写进 `.terrain`。

### 2. Signatures

- `Terrain.Editor.Services.Export.Exporters.TerrainExporter.ExportAsync(...)`
- `Terrain.TerrainFileReader.ReadAllHeightData()`
- `Terrain.TerrainFileReader.ReadAllBiomeMaskData()`
- `Terrain.RuntimeBiomeConfig.ReadFromToml(string tomlFilePath)`
- `Terrain.RuntimeDetailMapBuilder.Generate(...)`
- `Terrain.TerrainProcessor.BuildRuntimeDetailMaps(...)`

### 3. Contracts

- `.terrain` v6 header:
  - `Version == 6`
  - `HeightMap*` 字段描述 height VT
  - `SplatMap*` 字段在 v6 中描述 authored biome-mask VT，而不是 detail index VT
  - `SplatMapResolutionRatio` 是 biome-mask texel 到 height texel 的分辨率比；当前导出为 `2`
- `.terrain` payload 顺序:
  - header
  - `MinMaxErrorMap[]`
  - height VT header + data
  - biome-mask VT header + data
- 运行时 detail 真源:
  - detail index / weight **不再**从 `.terrain` 读取
  - runtime 必须从 `TerrainComponent.BiomeConfigPath` 指向的 TOML 配置读取：
    - `terrain.height_scale`
    - `biome_layers`
    - `biome_modifiers`
  - runtime 使用 `.terrain` 中的 heightmap + biome mask，再结合 TOML 规则生成 detail index / weight

### 4. Validation & Error Matrix

| 条件 | 处理 |
|---|---|
| `.terrain` 版本不是 `6` | `TerrainFileReader` 拒绝加载 |
| 缺少 biome-mask VT block | `TerrainFileReader` 读取失败 |
| `BiomeConfigPath` 为空或文件不存在 | 记录 warning；材质纹理不初始化；detail 生成退化为默认 slot 0 |
| TOML 没有 `biome_layers` | `RuntimeDetailMapBuilder` 生成默认 detail 数据 |
| TOML 中 modifier / layer 字段非法 | 使用默认枚举值或默认数值，不能让 runtime 崩溃 |

### 5. Good / Base / Bad Cases

- Good:
  - editor 导出的 `.terrain` 仅包含 height VT 与 biome-mask VT；runtime 重开后仍能按当前项目 TOML 规则生成 detail 数据。
- Base:
  - 缺少 biome 规则时，runtime 仍能生成全 slot-0 的默认 detail 图，不阻断地形加载。
- Bad:
  - 继续把 detail index / weight 序列化进 `.terrain`，同时 runtime 又从 TOML 重新生成 detail，导致双真源漂移。

### 6. Tests Required

- `dotnet build Terrain.sln`
- 手动回归：
  - 导出 `.terrain` 后确认文件能被 runtime 加载。
  - 修改 `BiomeConfigPath` 指向的 TOML 中 biome 规则、保持 `.terrain` 不变，重新启动 runtime 后 detail 混合结果应随 TOML 变化。
  - 修改 biome mask 并重新导出，runtime 生成的 detail 边界应随 mask 变化。

### 7. Wrong vs Correct

#### Wrong

```csharp
// 导出时把 detail index / weight 也写进 .terrain
// 运行时又从 TOML 重新生成 detail，形成双真源
```

#### Correct

```csharp
// .terrain v6 只持久化 height + biome mask
// runtime 从项目 TOML 读取 biome 规则并生成 detail index / weight
```

---

## Asset Management

Stride 资源通过 `.sdpkg` 文件组织：

```csharp
// 加载资源
var texture = Content.Load<Texture>("Grid Gray 128x128");
var scene = Content.Load<Scene>("MainScene");
var compositor = Content.Load<GraphicsCompositor>("GraphicsCompositor");
```

---

## File Paths

- 配置路径存储在 `TerrainComponent.TerrainDataPath`
- 使用 `Environment.SpecialFolder` 定位用户目录
- 使用 `Path.Combine` 构建路径，避免字符串拼接

---

## Examples

参见 [TerrainComponent.cs](Terrain/Core/TerrainComponent.cs) 中的 `TerrainConfig` 结构体实现。
