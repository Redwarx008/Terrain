# 河流本地 Water 纹理文件设计

**Date:** 2026-06-20
**Status:** Approved for implementation planning

## 背景

河流渲染目前通过 `Terrain.Editor/Assets/River/Bottom` 与 `Terrain.Editor/Assets/River/Water` 下的 `.sdtex` 资产描述，把底部和水面 DDS 编译进 Stride 内容系统，再由 `RiverResourceLoader` 使用 `ContentManager.Load<Texture>("River/...")` 加载。

新的资源边界是：河流底部纹理和水面纹理属于 `game/map/water` 下的普通地图资源，不再通过 Stride `.sdtex`、`Terrain.Editor.sdpkg RootAssets` 或 `ContentManager` 管理。`River/Environment` 不在本次迁移范围内，继续保留现有 Stride 内容资源路径。

## 目标

1. 将 `River/Bottom` 和 `River/Water` 使用的 DDS 文件统一迁移到 `game/map/water`。
2. 磁盘文件名使用下划线命名格式。
3. `RiverResourceLoader` 直接从文件系统读取 DDS 并创建 GPU `Texture`。
4. `River/Environment/reflection-specular` 保持现有 `ContentManager` 加载方式。
5. 测试和当前架构文档反映新的资源边界。

## 非目标

- 不改河流 shader 的采样语义、参数名或贴图 slot。
- 不迁移 `Terrain.Editor/Assets/River/Environment`。
- 不引入 mod 覆盖解析或虚拟资源 resolver。
- 不保留 Bottom/Water 的 `.sdtex` 兼容加载路径。

## 文件布局

目标目录：

```text
game/map/water/
```

目标文件：

```text
bottom_diffuse.dds
bottom_normal.dds
bottom_properties.dds
bottom_depth.dds
ambient_normal.dds
flow_normal.dds
foam.dds
foam_ramp.dds
foam_map.dds
foam_noise.dds
shadow_color.dds
water_color.dds
```

旧目录中的 Bottom/Water DDS 与对应 `.sdtex` 不再作为运行时资源入口。`Terrain.Editor.sdpkg` 也不再列出 `River/Bottom/*` 或 `River/Water/*` RootAssets。

## 加载设计

`RiverRenderFeature.InitializeCore` 仍负责初始化河流资源，但调用边界改为：

- 传入 `GraphicsDevice` 用于创建纹理。
- 传入 `ContentManager` 仅用于加载 `River/Environment/reflection-specular`。
- `RiverResourceLoader` 通过 `GameResourceRootLocator` 找到工作区 `game/` 根目录，然后读取 `map/water/*.dds`。

每个 DDS 使用 `Texture.Load(graphicsDevice, stream, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable, loadAsSrgb: ...)` 创建纹理。

色彩空间规则保留当前渲染语义：

- `bottom_diffuse.dds`：sRGB 加载。
- `shadow_color.dds`：sRGB 加载。
- `water_color.dds`：sRGB 加载，保留 BC7 DDS 数据并通过 GPU sRGB texture view 自动 decode；`RiverSurface.sdsl` 不再手动 decode，避免 double decode。
- 其余 normal、properties、depth、foam、noise、map 类资源：线性加载。

## 错误处理

缺失或加载失败的本地 DDS 必须抛出包含绝对路径的 `InvalidOperationException`。错误消息要明确说明资源来自 `game/map/water`，避免继续提示 `.sdtex` 或 `Terrain.Editor.sdpkg RootAssets`。

`reflection-specular` 的错误处理仍可以保留 Stride 内容 URL 语义，因为它仍由 `ContentManager` 管理。

## 测试

更新现有 `RiverShaderTextTests`：

- 验证 `game/map/water` 下 12 个下划线命名 DDS 存在。
- 验证 `RiverResourceLoader` 包含这些本地文件名。
- 验证 loader 对 Bottom/Water 不再使用 `River/Bottom/*`、`River/Water/*` 内容 URL。
- 验证 `Terrain.Editor.sdpkg` 不再包含 `River/Bottom/*` 或 `River/Water/*` RootAssets。
- 保留 `River/Environment/reflection-specular` 是 cubemap 且仍在 Stride 内容包中的测试。
- 验证 water-color 使用 `loadAsSrgb:true`，并禁止 `RiverSurface` 重新引入手动 `DecodeWaterColorSrgb`。

## 文档更新

实现完成后更新：

- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- 本次会话日志 `docs/log/2026/06/20/...`

如本次迁移暴露出可复用模式，例如“渲染私有贴图改为 game 普通文件资源”，再补充到 `docs/log/learnings/`。
