# River Review Fixes: MapWorldSize, Reflection Cubemap, And Refraction Payload Alpha
**Date**: 2026-06-17
**Session**: River review fixup after subagent findings
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 收口 subagent review 指出的 3 个实质性问题：矩形地图 UV 被 `_MapExtent` 压扁、surface 把 cubemap 当 2D 贴图采样、以及 refraction payload alpha 与 coverage 混写导致边缘像素错误解压。

**Secondary Objectives:**
- 用测试把三条修复锁住。
- 把新的 river 语义写回架构文档、功能总览和 learnings。

**Success Criteria:**
- surface 的 map-space UV 改成按轴 world span 归一化。
- `reflection-specular` 在 surface 侧按 cubemap 语义采样。
- bottom RT alpha 不再被 coverage 混合，surface 对 seed-only `a<=0` 像素不再解压到 camera space。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-review-fixup-fallback-memoization-and-light-priority.md](./2026-06-17-river-bottom-review-fixup-fallback-memoization-and-light-priority.md)
- Related: [2026-06-17-river-bottom-view-light-selection-and-optional-env-fallback.md](./2026-06-17-river-bottom-view-light-selection-and-optional-env-fallback.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 上一轮提交已经把 river 大部分 CK3 parity 改动合并进来，但 code review 又抓到三处仍然会直接影响画面的语义问题。

**Why Now:**
- 这三条都不是“风格优化”，而是会真实改变 water-color、reflection 或 refraction 结果的 bug，必须在继续下一轮 RenderDoc 对齐前先修掉。

---

## What We Did

### 1. 拆开 `MapExtent` 与 `MapWorldSize`
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`, `Terrain.Editor/Rendering/River/RiverMeshData.cs`, `Terrain.Editor/Rendering/River/RiverRenderObject.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- `RiverMeshService` 现在同时生成：
  - `MapExtent = max(width - 1, height - 1)`，只给河宽/深度归一化
  - `MapWorldSize = float2(width - 1, height - 1)`，给 map-space UV 使用
- `RiverRenderObject` / `RiverRenderFeature` 把 `MapWorldSize` 继续传给 surface shader
- `RiverSurface.ComputeMapWorldUv()` 改成按 `MapWorldSize` 分轴归一化

**Rationale:**
- `_MapExtent` 是正确的宽度标量，但错误的 map UV 标量。矩形地图必须保留 X/Y 独立 span。

### 2. 把 surface 的 `reflection-specular` 改回 cubemap 语义
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `ReflectionSpecularTexture` 从 `Texture2D<float4>` 改为 `TextureCube<float4>`
- 新增 `ReflectionSpecularSampler`
- surface 现在先算 `reflectionVector = reflect(-toCameraDir, waterNormal)`，再采 cubemap
- 新增测试直接读取 DDS header，锁定 `reflection-specular.dds` 仍然是 cubemap 资产

**Rationale:**
- 这张资源本来就是 cubemap；继续按 2D map-space strip 采样会直接错维度。

### 3. 让 refraction payload alpha 脱离 coverage blend
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- bottom dual-source blend 现在：
  - RT0 RGB 仍按 `SecondarySourceAlpha` 混色
  - RT0 alpha 改为 `One/Zero`，直接写 compressed bottom distance
- surface 新增 `DecodeRefractionWorldPosition(...)`
  - 若 `compressedDistance <= 0.0001f`，直接回退到 `surfaceWorldPosition`
  - 否则才走 `RiverDecompressWorldSpace(...)`

**Rationale:**
- 这样部分 coverage 不会再把 payload alpha 混脏；而 seed-only 边缘像素也不会再被错误解压到 camera space。

### 4. 跑 Stride shader 生成、资产编译、构建与测试
**Files Changed:** generated `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Implementation:**
- 运行：
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build Terrain.Editor\\Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
  - `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj -c Debug`

**Rationale:**
- 这轮包含 SDSL 参数变更，必须按 Stride 资产/生成链完整走一遍。

---

## Decisions Made

### Decision 1: 保留 `_MapExtent`，新增 `MapWorldSize`
**Context:** 宽度归一化需要单标量，但 map-space UV 在矩形地图上不能复用这个标量。
**Options Considered:**
1. 全部改回 `float2 MapSize`
2. 保留 `_MapExtent` 做宽度，再额外传 `MapWorldSize`
3. 用条件分支在 shader 里反解高度图宽高

**Decision:** 选择 2
**Rationale:** 侵入最小，同时保留现有 width/depth 语义。

### Decision 2: surface 侧按 cubemap 直接修，不再继续容忍 2D 采样
**Context:** `reflection-specular.dds` 的 DDS header 已明确是 cubemap。
**Options Considered:**
1. 继续把它当 2D 变化贴图
2. 临时换一张 2D 资产
3. 让 shader 回到 cubemap 反射采样

**Decision:** 选择 3
**Rationale:** 这才和实际资源维度一致，也更接近 CK3 的 reflection 语义。

### Decision 3: payload alpha 与 coverage 分离修，而不是只在 surface 端容错
**Context:** 只做 `a<=0` guard 仍会留下“部分 coverage 混脏 payload”的问题。
**Options Considered:**
1. 只在 surface 端加 invalid-alpha guard
2. 改 blend state，让 RT0 alpha 直接写 payload，再在 surface 端补 guard
3. 另外开一张 payload RT

**Decision:** 选择 2
**Rationale:** 改动最小，同时能同时解决“部分 coverage 混脏”和“seed-only 边缘像素”两类问题。

---

## What Worked ✅

1. **先核对 CK3 本地 shader 再修**
   - What: 修前先查 `jomini_river_surface.fxh` / `jomini_water_default.fxh`
   - Why it worked: 直接确认了 `MapSize` 是 `float2`，reflection 是 cubemap，避免靠印象乱修
   - Reusable pattern: Yes

2. **dual-source blend 只让颜色吃 coverage**
   - What: RT0 RGB 继续 coverage blend，RT0 alpha 改成 direct write
   - Impact: payload 语义和颜色 coverage 终于分层

---

## Problems Encountered & Solutions

### Problem 1: 测试第一次重跑被 `Terrain.dll` 文件锁住
**Symptom:** `dotnet run` 首次失败，报 `Terrain.dll` 被 `.NET Host` 占用。
**Root Cause:** 残留的 MSBuild/编译服务器节点没有及时退出。
**Investigation:**
- 检查活跃 `dotnet` 进程
- 确认是 MSBuild node，而不是 river 代码编译错误

**Solution:**
- 执行 `dotnet build-server shutdown`
- 之后顺序重跑测试

**Why This Works:** 释放残留编译服务器对 `obj`/`bin` 里中间产物的句柄占用。
**Pattern for Future:** 遇到 `.NET Host` / `dotnet.exe` 锁 `Terrain.dll` 或 `obj` 中间件时，先 `dotnet build-server shutdown` 再重跑。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`

### New Patterns/Anti-Patterns Discovered
**New Pattern:** `MapExtent` / `MapWorldSize` 分离
- When to use: 同时存在“宽度/深度归一化”和“按地图 world UV 采样”的 river/water shader
- Benefits: 矩形地图不会再把 map texture 压扁
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** 让 refraction payload alpha 和 coverage 共用 blend
- What not to do: RT0 RGB/alpha 一起按 `SecondarySourceAlpha` 混合
- Why it's bad: 会把 payload 解压语义直接污染成 coverage 混色结果
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** river surface map UV / reflection / payload-alpha 语义
- **From:** `_MapExtent` 同时负责 map UV；reflection cubemap 被当 2D 贴图；payload alpha 随 coverage 混合
- **To:** `MapWorldSize` 负责 map UV；reflection 走 cubemap 采样；payload alpha direct write + invalid-alpha guard
- **Scope:** `RiverMeshService`, `RiverRenderObject`, `RiverRenderFeature`, `RiverSurface`, river 测试与文档

---

## Code Quality Notes

### Testing
- **Verified Commands:**
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build Terrain.Editor\\Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
  - `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj -c Debug`
- **Result:** 通过；仍有既有 NuGet/WinForms/nullability warnings

### Technical Debt
- **Paid Down:** 矩形地图 map UV 压扁；cubemap/2D 纹理维度错用；refraction payload alpha 混脏
- **Remaining:** pre-bottom seed payload 与 CK3 仍未完全等价，bank 泄漏根因还没有彻底结束

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 再抓一帧最新 RenderDoc，确认 bank-edge payload 修复后 `RefractionTexture.a` 在边缘不再解压到 camera space
2. 观察 cubemap 反射改正后，surface 最终色是否还需要继续向 CK3 reflection/light model 收敛
3. 若仍有 bank 泄漏，继续拆 pre-bottom seed payload 与 surface downstream 的差异

### Questions to Resolve
1. current `reflection-specular` cubemap 是否还需要引入 mip/roughness 选择，而不是先固定默认 sample？
2. bank-edge 的剩余差异中，有多少还来自 pre-bottom seed payload 本身而不是 surface shader？

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `_MapExtent` 现在只负责宽度/深度；map UV 改走 `MapWorldSize`
- `reflection-specular.dds` 是 cubemap，surface 不再把它当 2D 贴图
- RT0 alpha 现在 direct write payload；`a<=0` 的 seed-only 像素会在 surface 端被视为 invalid payload

**What Changed Since Last Doc Read:**
- Architecture: river surface 的 UV / reflection / payload-alpha 三条语义都变了
- Implementation: 新增 `MapWorldSize` 绑定链与 `DecodeRefractionWorldPosition`
- Constraints: 仍要继续用 RenderDoc 复核边缘像素，不要只看最终截图

**Gotchas for Next Session:**
- 不要再把 `max(width,height)` 标量直接拿去做 map texture UV
- 不要再把 cubemap 资源临时塞进 `Texture2D` 采样路径
- 不要让 refraction payload alpha 和 coverage 混成一条语义

---

## Code References

- `Terrain.Editor/Services/RiverMeshService.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---
