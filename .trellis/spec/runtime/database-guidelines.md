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