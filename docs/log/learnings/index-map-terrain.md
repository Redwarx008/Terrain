# Index Map 技术学习

**来源：** Unity IndexMapTerrain 项目 (`E:\reference\IndexMapTerrain`)
**日期：** 2026-04-07

---

## 概述

IndexMapTerrain 是一个 Unity 地形渲染项目，展示了如何使用 **Index Map（索引图）** 替代传统的 Splatmap 来实现地形纹理混合。

---

## 核心概念

### Index Map vs 传统 Splatmap

| 特性 | 传统 Splatmap | Index Map |
|------|--------------|-----------|
| **存储方式** | 每通道存权重（4层/图） | 材质索引 + 权重 + 投影方向 + 旋转 |
| **图层数量** | 受通道数限制（4层/纹理） | 理论支持 256 种材质 |
| **纹理投影** | 仅顶向下 | 支持任意方向 3D 投影 |
| **旋转控制** | 通常不支持 | 每材质独立旋转 |

### 数据结构 (R8G8B8A8)

| 通道 | 用途 | 说明 |
|------|------|------|
| **R** | 材质索引 | 0-255，指向纹理数组 |
| **G** | 权重 | 控制混合权重和过渡位置 |
| **B** | 投影方向编码 | 4:4 格式编码法线投影方向 |
| **A** | 旋转角度 | 0-255 = 0°-360° |

---

## 关键技术

### 1. 3D 投影方向编码 (4:4 格式)

**用途：** 解决陡峭悬崖面的纹理拉伸问题

**编码（存储时）：**
```hlsl
// 从法线计算投影方向
float2 proj = 0.5f * (normal.xz / normal.y);
proj = clamp(floor(proj * 7.0f + 7.25f), 0, 15);
float encoded = proj.y * 16.0f + proj.x;  // [0, 255]
```

**解码（渲染时）：**
```hlsl
float proj = floor(raw.b * 255.0f + 0.5f);
float projDX = frac(proj / 16.0f) * 16.0f - 7.0f;
float projDY = ((proj - floor(proj / 16.0f) * 16.0f) / 16.0f) * 16.0f - 7.0f;
float3 projDir = normalize(float3(projDX, 0.5f, projDY));
```

**原理：**
- 将法线的 XZ 分量相对于 Y 分量编码
- 4:4 格式意味着 X 和 Z 各用 4 位 (0-15)
- 总共 256 种投影方向

### 2. 纹理数组 (Texture2DArray)

**优势：**
- 统一存储所有地形纹理
- 减少纹理绑定切换
- 通过索引直接访问

**采样方式：**
```hlsl
// Texture2DArray 采样：float3(uv, sliceIndex)
float4 color = MaterialAlbedoArray.Sample(sampler, float3(uv, materialIndex));
```

### 3. 旋转 UV 计算

**用途：** 打破纹理平铺重复

```hlsl
float2 RotateUV(float2 uv, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
}
```

### 4. 3D 投影 UV 计算

```hlsl
float2 GetProjectedUV(float3 worldPos, float3 projDir, float scale)
{
    // 构建投影基向量
    float3 up = abs(projDir.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
    float3 projU = normalize(cross(up, projDir));
    float3 projV = cross(projDir, projU);
    return float2(dot(projU, worldPos), dot(projV, worldPos)) * scale;
}
```

---

## 在 Stride Terrain 中的应用

### 实现位置

| 文件 | 用途 |
|------|------|
| `MaterialIndexMap.cs` | RGBA 数据结构 |
| `EditorTerrainDiffuse.sdsl` | 着色器解码和采样 |
| `BrushParameters.cs` | 绘制参数控制 |
| `RightPanel.cs` | UI 控制面板 |

### 新增功能

1. **权重控制** - 控制材质混合的过渡位置
2. **随机旋转** - 打破纹理平铺重复
3. **3D 投影** - 解决悬崖纹理拉伸问题

---

## 参考文件

| 文件 | 说明 |
|------|------|
| `IndexMap.cs` | Index Map 数据结构和序列化 |
| `MaterialManager.cs` | 材质管理和运行时更新 |
| `IndexedTerrainShader.shader` | 主地形渲染着色器 |
| `PaintIndex.shader` | 绘制工具着色器 |
| `ArrayTools.cs` | 纹理数组操作工具 |

---

## 相关文档

- [2026-04-07-2-index-map-enhancement](../2026/04/07/2026-04-07-2-index-map-enhancement.md) - 实施记录
