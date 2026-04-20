---
doc_type: trick
type: organization
status: current
tags: [refactor, state-management, struct]
created: 2026-04-20
---

# 配置聚合：散落字段合并为 IEquatable 结构体

## 处方

多个散落的 `Loaded*` 字段合并为一个 `TerrainConfig` 结构体，实现 `IEquatable<TerrainConfig>`，用一次相等比较替代多次字段判断。

## 做法

```csharp
// Before: 3 个散落字段 + 4 个条件判断
if (LoadedHeightmapTexture == other.LoadedHeightmapTexture
    && LoadedBaseChunkSize == other.LoadedBaseChunkSize
    && LoadedMaxVisibleChunkInstances == other.LoadedMaxVisibleChunkInstances)

// After: 1 个结构体 + 1 次比较
internal struct TerrainConfig : IEquatable<TerrainConfig>
{
    public Texture? HeightmapTexture;
    public int BaseChunkSize;
    public int MaxVisibleChunkInstances;
}

if (currentConfig != loadedConfig)
```

## 反模式

- ❌ 3+ 个 `Loaded*` 字段散落在 Component 上，每次判断写一长串 `&&`
- ❌ 添加新触发重建的参数时需要在多处补字段

## 适用

- 多个字段构成一个"配置组"，需要整体判断是否变化
- 变化时触发重建（rebuild）逻辑