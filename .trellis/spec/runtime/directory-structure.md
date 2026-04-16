# 运行时目录结构

> Terrain 运行时库及启动器的文件组织规范

---

## 项目总览

| 项目 | 目标框架 | 输出类型 | 作用 |
|------|----------|----------|------|
| Terrain | net10.0-windows | Library | 核心运行时库（渲染、流式加载、LOD） |
| Terrain.Windows | net10.0-windows | WinExe | 最小运行时启动器，仅引用 Terrain |

---

## Terrain 运行时布局

```
Terrain/
  Core/
    TerrainComponent.cs        -- 实体组件（序列化数据）
    TerrainProcessor.cs        -- EntityProcessor，驱动渲染与初始化
  Effects/
    Build/                     -- 计算着色器（LOD 查找、LOD 贴图、邻居遮罩）
      *.sdsl + *.sdsl.cs
    Material/                  -- 表面着色器（漫反射、位移）
      *.sdsl + *.sdsl.cs
    Stream/                    -- 着色器流/参数声明
      *.sdsl + *.sdsl.cs
    TerrainForwardShadingEffect.sdfx + .sdfx.cs
  Materials/
    RuntimeMaterialManager.cs  -- 从 TOML 加载材质纹理到 GPU 数组
  Rendering/
    Materials/
      MaterialTerrainDiffuseFeature.cs      -- C# 着色器特性桥接
      MaterialTerrainDisplacementFeature.cs
    TerrainComputeDispatcher.cs             -- 派发计算着色器
    TerrainQuadTree.cs                      -- LOD 选择/裁剪
    TerrainRenderFeature.cs                 -- RootEffectRenderFeature（管线）
    TerrainRenderObject.cs                  -- GPU 资源持有者
    TerrainWireframeModeController.cs
    TerrainWireframeStageSelector.cs
  Streaming/
    PageBufferAllocator.cs     -- 流式加载的原生内存池（含 NativePageBufferOwner 内部类）
    TerrainStreaming.cs        -- 文件读取器、VT 数组、流式管理器（含 TerrainFileReader、GpuVirtualTextureArray、TerrainPageKey 等内部类型）
  Utilities/
    TextureBlockEncoder.cs     -- BC1/BC3 块压缩辅助
```

### Terrain.Windows 布局

```
Terrain.Windows/
  TerrainApp.cs                -- 顶级语句入口 + TerrainGame 类（合并在一个文件中）
```

> 注意：没有单独的 `Program.cs` 和 `Game.cs`，入口和 Game 子类都在 `TerrainApp.cs` 中。

---

## 运行时根目录辅助文件

```
Terrain/
  BasicCameraController.cs     -- 示例相机控制器（用于 Terrain.Windows）
  GameProfiler.cs              -- 运行时性能分析覆盖层
```

---

## 着色器 3 文件模式

每个着色器功能由 3 个文件组成：

1. **`.sdsl`** — Stride 着色器语言源码
2. **`.sdsl.cs`** — 自动生成的参数 Key 类
3. **`MaterialFeature.cs`** — C# 桥接类（仅在 Material/ 下）

示例：
```
Effects/Material/MaterialTerrainDiffuse.sdsl          -- SDSL 着色器
Effects/Material/MaterialTerrainDiffuse.sdsl.cs       -- 自动生成：MaterialTerrainDiffuseKeys
Rendering/Materials/MaterialTerrainDiffuseFeature.cs  -- C# MaterialFeature 子类
```

**规则**：修改 `.sdsl` 文件时，使用 `stride-shader-asset-workflow` skill 确保注册和重建流程正确。

---

## 命名空间规则

- **Terrain 项目使用扁平命名空间** `Terrain`，不论文件在哪个子目录
- 类名前缀 `Terrain*` 表示运行时类（如 `TerrainComponent`、`TerrainProcessor`）
- **例外**：`Utilities/TextureBlockEncoder.cs` 使用 `namespace Terrain.Utilities;`（历史遗留，不应扩展）

```csharp
// Terrain/Core/TerrainComponent.cs
namespace Terrain;

// Terrain/Streaming/TerrainStreaming.cs
namespace Terrain;

// Terrain/Rendering/TerrainRenderFeature.cs
namespace Terrain;

// Terrain/Utilities/TextureBlockEncoder.cs — 唯一例外
namespace Terrain.Utilities;
```

---

## Shared/ 跨项目链接代码

`Shared/VirtualTextureLayout.cs` 被 Terrain 和 Terrain.Editor 通过 `<Compile Include>` 链接引用：

```xml
<!-- Terrain.csproj / Terrain.Editor.csproj -->
<Compile Include="..\Shared\VirtualTextureLayout.cs" Link="Shared\VirtualTextureLayout.cs" />
```

该文件使用 `internal` 可见性，命名空间为 `Terrain.Shared`。

---

## 反模式

- 不要创建 `Utils/` 或 `Helpers/` 大杂烩目录——按功能归属放入对应子系统
- 不要在运行时项目中混入编辑器代码——编辑器变体使用 `Editor*` 前缀放在 Terrain.Editor 项目中
- 不要给 Terrain 项目添加子命名空间——保持扁平的 `Terrain` 命名空间
