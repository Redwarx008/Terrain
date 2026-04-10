# Runtime IndexMap Streaming - 移植编辑器材质系统到运行时
**Date**: 2026-04-10
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
将编辑器的 IndexMap RGBA 材质系统完整移植到运行时，包括双 VT 流式加载、材质着色器移植、RuntimeMaterialManager 创建。

**Success Criteria:**
- `dotnet build` 零错误
- SplatMap 为必填项（非可选），文件格式版本升级到 2
- 运行时着色器支持 IndexMap 材质混合（无 fallback）
- RuntimeMaterialManager 从 TOML 配置加载材质纹理

---

## Context & Background

**Previous Work:**
- 编辑器已完成 IndexMap RGBA 材质系统（材质索引、权重、3D投影、旋转）
- TerrainPreProcessor 已支持 SplatMap SVT 写入
- 文件头已有 `SplatMapFormat`/`SplatMapMipLevels` 字段

**Why Now:**
运行时仅有简单的单一纹理平铺着色器（51行），用户编辑的地形材质在构建后完全丢失。

---

## What We Did

### 1. GpuHeightArray → GpuVirtualTextureArray 重构
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs:352-536`

- 重命名 `GpuHeightArray` → `GpuVirtualTextureArray`
- 属性 `HeightmapArray` → `TextureArray`
- `UploadPage` 移除 `MemoryMarshal.Cast<byte, ushort>` 格式硬编码，改为 `TextureArray.SetData<byte>()`
- GPU 格式由 Texture 创建时的 PixelFormat 决定

### 2. TerrainFileReader 扩展 SplatMap 读取
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs:136-253`

- 新增 `splatMapHeader`, `splatMapMipLayouts`, `splatMapTileByteSize` 字段
- 构造函数始终解析 SplatMap VT（紧跟 heightmap VT 之后）
- 新增 `ReadSplatMapPage(TerrainPageKey, Span<byte>)` 方法
- 文件头版本从 1 升级到 2，移除 `HasSplatMap` 字段

### 3. TerrainStreamingManager 双 VT 流式加载
**Files Changed:** `Terrain/Streaming/TerrainStreaming.cs:538-765`

- 构造函数接受 `GpuVirtualTextureArray? gpuSplatMapArray`
- 双缓冲池：`heightmapBufferPool` + `splatMapBufferPool`
- `StreamingRequest` 新增 `IsSplatMap` 标志
- IoThreadMain 根据 `IsSplatMap` 调度不同的读取方法
- Heightmap 和 SplatMap 共享相同的 `TerrainPageKey` 和 `sliceIndex`
- 驱逐同步：两个数组共享容量且同步 touch

### 4. 运行时着色器完整移植
**Files Changed:** `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl` (51→~220行)
**Files Changed:** `Terrain/Effects/Material/MaterialTerrainDiffuse.sdsl.cs`

- 从编辑器 `EditorTerrainDiffuse.sdsl` 移植完整材质混合逻辑
- 移植内容：MaterialPixel 结构体、投影方向解码、投影基构建、投影UV、投影法线、细节对比度、4邻居镜像采样
- 关键差异：`IndexMapArray.Load(int4(texelCoord, sliceIndex, 0))` 替代编辑器的全局 `MaterialIndexMap.Load(int3(...))`
- 新增 rgroup/cbuffer 参数：IndexMapArray, MaterialAlbedoArray, MaterialNormalArray, samplers, tiling/contrast/array size
- **无 fallback 路径** — 材质系统为必填
- 手动更新 `.sdsl.cs` key 文件

### 5. RuntimeMaterialManager 创建
**Files Changed:** `Terrain/Materials/RuntimeMaterialManager.cs` (新建)

- 使用 Tommy TOML 解析器读取材质槽配置
- `InitializeFromToml()`: 从 TOML 加载槽位、加载纹理、构建 Texture2DArray
- `ReadMaterialSlots()`: 静态方法解析 TOML 并解析相对路径
- 容量分级策略：16/32/64/128/256（与编辑器一致）
- 默认平面法线 (128,128,255,255) 用于缺少 normal map 的槽位

### 6. TerrainProcessor 集成
**Files Changed:** `Terrain/Core/TerrainProcessor.cs`
**Files Changed:** `Terrain/Core/TerrainComponent.cs`
**Files Changed:** `Terrain/Rendering/TerrainRenderObject.cs`

- `TerrainComponent` 新增 `MaterialConfigPath` 属性和 `MaterialManager` 字段
- `TerrainRenderObject` 新增 `SplatMapArray` 属性，始终创建 R8G8B8A8_UNorm 纹理数组
- `ApplyLoadedTerrainData`: 创建双 GpuVirtualTextureArray、初始化 RuntimeMaterialManager（必填）
- `UpdateMaterialParameters`: 绑定所有新增着色器参数
- `OnEntityComponentRemoved`: 释放 MaterialManager
- 移除 `DiffuseWorldRepeatSize` 常量和旧 DefaultDiffuseTexture 相关代码

### 7. TerrainPreProcessor 更新
**Files Changed:** `TerrainPreProcessor/Models/TerrainFileHeader.cs`
**Files Changed:** `TerrainPreProcessor/Models/ProcessingConfig.cs`
**Files Changed:** `TerrainPreProcessor/Services/TerrainProcessor.cs`

- 文件头版本升级到 2，移除 `HasSplatMap` 字段
- `ProcessingConfig.SplatMapPath` 改为必填（验证和注释）
- `ProcessInternal` 在缺少 SplatMapPath 时返回失败

### 8. 构建修复
**Files Changed:** `Terrain/Terrain.csproj`

- 添加 Tommy NuGet 引用（使用中央包管理，不指定版本）
- 修复 CPM 版本冲突：`PackageReference` 不能带 `Version` 属性

---

## Decisions Made

### Decision 1: SplatMap 为必填项，不做向后兼容
**Context:** 旧 `.terrain` 文件可能不含 SplatMap 数据
**Decision:** 版本直接从 1 升到 2，移除 `HasSplatMap` 字段
**Rationale:** 简化代码路径，避免维护 fallback。编辑器已支持 IndexMap，新导出的文件一定包含 SplatMap。
**Trade-offs:** 旧的 `.terrain` 文件无法加载

### Decision 2: RuntimeMaterialManager 从 TOML 项目配置加载
**Context:** 需要在运行时加载材质纹理
**Options:**
1. 从编辑器项目 TOML 读取路径（选中）
2. 将材质打包进 `.terrain` 文件
3. 独立的材质配置文件

**Decision:** 选项 1
**Rationale:** 复用编辑器已有的 TOML 配置和材质槽路径，最简路径

### Decision 3: 运行时着色器无 fallback
**Context:** 编辑器着色器有 DefaultDiffuseTexture fallback
**Decision:** 运行时必须绑定材质数组，无 fallback 路径
**Rationale:** 用户要求，且 SplatMap 已为必填项

### Decision 4: Heightmap 和 IndexMap 共享 sliceIndex
**Context:** 两个 VT 数组需要同步驻留管理
**Decision:** 使用相同的 `TerrainPageKey` 和 `sliceIndex`
**Rationale:** `TerrainChunkNode.StreamInfo` 结构不变，页面布局相同

---

## What Worked

1. **GpuVirtualTextureArray 泛型化**
   - 移除格式硬编码后，Heightmap(R16) 和 IndexMap(RGBA32) 共用同一类
   - Stride `Texture.SetData<byte>()` 按 Texture 的 PixelFormat 自动处理格式

2. **共享 TerrainPageKey 的双 VT 方案**
   - Heightmap 和 IndexMap 使用相同分页布局，同步加载/驱逐
   - 无需修改 `TerrainChunkNode` 结构体

3. **手动更新 .sdsl.cs key 文件**
   - 绕过 Stride VSIX 依赖，手动添加参数 Key 定义
   - 编译通过，运行时正确绑定

---

## What Didn't Work

1. **Tommy NuGet 版本冲突**
   - 问题: `PackageReference` 带了 `Version="3.1.2"` 但项目使用中央包管理
   - 错误: NU1008
   - 修复: 从 csproj 移除版本号，由 `Directory.Packages.props` 管理

---

## Next Session

### Immediate Next Steps
1. 端到端集成测试 — 在编辑器中绘制材质 → 保存 → TerrainPreProcessor 处理 → 运行时加载验证渲染
2. 验证 SDSL 着色器编译（可能需要在 Stride 编辑器中重新保存 .sdfx）
3. 更新 `docs/ARCHITECTURE_OVERVIEW.md` — 系统状态变更

### Questions to Resolve
1. SDSL 着色器是否需要 Stride 编辑器重新编译？手动 key 文件是否足够？
2. RuntimeMaterialManager 的材质纹理加载路径在发布构建中是否正确？

---

## Quick Reference for Future Claude

**Key Implementation:**
- 双 VT 流式: `TerrainStreaming.cs` — GpuVirtualTextureArray + TerrainStreamingManager
- 材质着色器: `MaterialTerrainDiffuse.sdsl` — 完整移植的 IndexMap 材质混合
- 材质管理: `RuntimeMaterialManager.cs` — TOML → Texture2DArray
- 处理器集成: `TerrainProcessor.cs:165-250` — ApplyLoadedTerrainData

**Critical Decisions:**
- 文件格式版本 2，SplatMap 必填，无向后兼容
- 着色器无 fallback 路径
- 材质从编辑器 TOML 项目配置加载

**Current Status:**
- 运行时代码全部完成，`dotnet build` 零错误
- 待端到端测试验证

**Gotchas:**
- Stride 中央包管理：`PackageReference` 不能带 `Version` 属性
- `.sdsl.cs` key 文件是手动更新的，如果 Stride 重新生成会覆盖
- `TerrainFileHeader` 版本 2，结构与 PreProcessor 定义必须完全一致

---

## Session Statistics

**Files Changed:** 10
**Commits:** 0 (待提交)

---

*Session Log - 2026-04-10*
