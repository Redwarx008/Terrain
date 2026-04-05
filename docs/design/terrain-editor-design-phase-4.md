# 阶段四：高级功能

## 概述

**目标：** 提升编辑效率和输出质量

**范围：**
- 更多笔刷形状
- 笔刷预设系统
- 地形侵蚀模拟
- 程序化生成

> **注意：** 此阶段为未来扩展，优先级较低。详细设计将在前三阶段完成后补充。

## 功能规划

### 4.1 笔刷形状扩展

**目标：** 提供更多笔刷形状选择

| 形状 | 描述 | 用途 |
|------|------|------|
| 圆形 | 标准圆形（已实现） | 通用编辑 |
| 方形 | 矩形区域 | 创建平台、台阶 |
| 平滑 | 边缘更柔和的圆形 | 自然过渡 |
| 噪点 | 带随机扰动的形状 | 自然效果 |
| 自定义 | 从灰度图导入 | 特殊形状 |

**实现要点：**
- 扩展 `BrushShape` 枚举
- 创建 `IBrushFalloff` 接口
- 实现不同形状的衰减函数
- 更新 UI 选择器

### 4.2 笔刷预设系统

**目标：** 保存和加载笔刷配置

```csharp
public class BrushPreset
{
    public string Name { get; set; }
    public BrushShape Shape { get; set; }
    public float Size { get; set; }
    public float Strength { get; set; }
    public float Falloff { get; set; }
    public string? CustomShapePath { get; set; }
}

public class BrushPresetManager
{
    public List<BrushPreset> Presets { get; }
    public BrushPreset? CurrentPreset { get; set; }
    public void SavePreset(BrushPreset preset);
    public void LoadPreset(string name);
    public void DeletePreset(string name);
}
```

**UI 设计：**
- 预设列表面板
- 保存/删除按钮
- 快速切换下拉菜单

### 4.3 地形侵蚀模拟

**目标：** 模拟自然侵蚀效果

**算法选项：**

1. **热力侵蚀（Thermal Erosion）**
   - 模拟岩石风化
   - 基于坡度移动物质

2. **水力侵蚀（Hydraulic Erosion）**
   - 模拟水流冲刷
   - 沉积物运输

**实现方案：**
- CPU 计算（适合预览）
- GPU Compute Shader（适合实时）

```csharp
public interface IErosionSimulator
{
    void Apply(ushort[] heightData, int width, int height, int iterations);
}

public class ThermalErosion : IErosionSimulator { ... }
public class HydraulicErosion : IErosionSimulator { ... }
```

### 4.4 程序化生成

**目标：** 基于噪声生成地形

**功能：**
- Perlin/Simplex 噪声生成
- 多层噪声叠加（分形）
- 参数化控制（频率、振幅、种子）

```csharp
public class TerrainGenerator
{
    public int Seed { get; set; }
    public float Frequency { get; set; } = 0.01f;
    public int Octaves { get; set; } = 4;
    public float Persistence { get; set; } = 0.5f;
    public float Lacunarity { get; set; } = 2.0f;

    public ushort[] Generate(int width, int height);
}
```

**UI 设计：**
- 生成参数面板
- 实时预览
- 种子随机化按钮

## 实现优先级

| 功能 | 优先级 | 预计工作量 |
|------|--------|-----------|
| 笔刷形状扩展 | 高 | 3-5 天 |
| 笔刷预设系统 | 中 | 2-3 天 |
| 程序化生成 | 中 | 3-5 天 |
| 地形侵蚀模拟 | 低 | 5-7 天 |

## 技术难点

### 笔刷形状
- 自定义形状的纹理加载
- GPU 端形状采样优化

### 侵蚀模拟
- 算法参数调优
- 性能优化（可能需要 GPU 加速）

### 程序化生成
- 噪声库选择/实现
- 与现有高度图系统集成

## 未来扩展

以下功能在当前范围外，可作为未来迭代方向：

1. **高级渲染**
   - 网格细分（Tessellation）
   - 细节法线贴图
   - 动态地形变形

2. **协作功能**
   - 多人同时编辑
   - 变更历史可视化

3. **跨引擎支持**
   - Unity Terrain 格式导出
   - Unreal Landscape 格式导出

## 验证方案

### 笔刷形状
- 切换不同形状，验证视觉效果
- 测试自定义形状导入

### 笔刷预设
- 保存预设，重新加载验证参数正确
- 快速切换预设验证即时生效

### 程序化生成
- 调整参数，验证地形变化
- 使用相同种子验证结果可复现

### 侵蚀模拟
- 应用侵蚀，验证自然效果
- 对比前后高度图差异
