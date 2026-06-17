# River Bottom View-Light Selection And Optional Env Fallback
**Date**: 2026-06-17
**Session**: River bottom review follow-up
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 按 review 收口 river bottom 的 scene-driven 绑定语义，避免继续从全局 `CurrentLights` 和强制 fallback cubemap 取数据。

**Secondary Objectives:**
- 删除 bottom 已失效的 river-local sun 参数链。
- 重新跑 Stride shader 生成、资产编译、构建和测试。

**Success Criteria:**
- bottom lighting 改为按当前 `LightingView` 取 scene sun / skybox。
- `Skybox texture` 只作为场景 skybox 缺失时的 lazy optional fallback。
- `RiverBottom` / CPU 绑定链中不再保留 dead `BottomSun*` 参数。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-scene-driven-lighting.md](./2026-06-17-river-bottom-scene-driven-lighting.md)
- Related: [2026-06-17-river-renderfeature-initializecore-rendersystem-null.md](./2026-06-17-river-renderfeature-initializecore-rendersystem-null.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- 上一轮已经把 bottom lighting 切到 scene-driven 主语义，但 review 指出三处收口问题：
  - `RiverRenderFeature` 仍直接从全局 `CurrentLights` 取第一盏 directional/skybox。
  - `RiverResourceLoader` 仍会在启动时强制加载 `Skybox texture`。
  - bottom shader / settings / render-object 链还残留 dead `BottomSun*` 参数。

**Why Now:**
- 这些问题会让多 view / 多灯 / 缺失 legacy cubemap 的场景再次偏离 CK3 目标语义，也会让维护者继续误判 bottom 的真实输入源。

---

## What We Did

### 1. bottom lighting 改成按当前 `LightingView` 选 scene lights
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- 在 `PrepareBottomSceneLighting(...)` 中先解析 `var lightingView = renderView.LightingView ?? renderView;`
- 优先从 `ForwardLightingRenderFeature` 的 per-view `renderViewDatas` 读 `VisibleLights`
- 如果拿不到内部缓存，再本地复刻一遍与 Stride `CollectVisibleLights()` 一致的 frustum 过滤 fallback
- 用 `lightingView` 调 `FindShadowMap(lightingView, directionalLight)`

**Rationale:**
- 这样 river bottom 绑定到的是当前 view 真正参与 forward lighting 的可见光集合，而不是 visibility group 里的全量 lights。

### 2. `Skybox texture` 改成 lazy optional fallback
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`

**Implementation:**
- `RiverResourceLoader.Load(...)` 不再启动时加载 `BottomEnvironment`
- 新增 `EnsureBottomEnvironment(ContentManager content)`，仅在 scene `LightSkybox` cubemap 为空时才尝试按需加载
- `LoadRequiredTexture(...)` 继续服务真正必需的 river 纹理；`BottomEnvironment` 单独走 `LoadOptionalTexture(...)`

**Rationale:**
- 这让 scene skybox 真正成为 primary source，同时避免缺失 legacy cubemap 时整个 feature 启动失败。

### 3. 清掉 bottom 侧 dead river-local sun 参数链
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl.cs`, `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`, `Terrain.Editor/Rendering/River/RiverRenderObject.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 从 `RiverBottom.sdsl` 删除：
  - `_BottomSunDirection`
  - `_BottomSunColor`
  - `_BottomSunIntensity`
  - `_ShadowTermFallback`
  - `_CloudMaskFallback`
- 从 `RiverRenderSettings` / `RiverRenderObject` / `ApplyBottomParameters(...)` 删除 `BottomSun*`
- 保留 `ShadowTermFallback / CloudMaskFallback` 给 `RiverSurface`
- 文本测试改成断言：
  - bottom shader / CPU 链不再保留 `BottomSun*`
  - bottom pass 不再绑定 `RiverBottomKeys._ShadowTermFallback/_CloudMaskFallback`

**Rationale:**
- 这些参数已经不参与 bottom 的 scene-driven 受光语义，继续保留只会误导后续诊断。

---

## Decisions Made

### Decision 1: 优先复用 forward-lighting 的 per-view 可见灯缓存，取不到再本地 fallback
**Context:** review 明确指出不能再从全局 `CurrentLights` 直接拿第一盏灯。

**Options Considered:**
1. 继续用全局 `CurrentLights` 并在本地硬筛
2. 反射读取 `ForwardLightingRenderFeature` 的 per-view `VisibleLights`
3. 重做一套独立 light culling

**Decision:** 选择 2，并在读取失败时退回 1 的本地 frustum fallback
**Rationale:** 优先与 Stride 当前 forward-lighting 语义保持同一份可见灯集合，同时避免完全依赖 internal field 成为单点故障。
**Trade-offs:** `renderViewDatas` 仍是 private field，后续若引擎内部改名，需要 fallback 路径兜底。

### Decision 2: `Skybox texture` 不再视作启动必需资源
**Context:** scene skybox 已经是 primary source，review 指出 legacy cubemap 不应继续阻止 feature 启动。

**Options Considered:**
1. 保持 required load
2. 改成启动时 optional load
3. 改成真正 lazy optional load

**Decision:** 选择 3
**Rationale:** 这最贴近当前架构语义，也最容易解释“什么时候会读 legacy cubemap”。

---

## What Worked ✅

1. **per-view visible-light 绑定**
   - What: bottom 改成先看 `LightingView` 的 `VisibleLights`
   - Why it worked: 直接消掉 review 最担心的“拿错太阳/skybox”路径
   - Reusable pattern: Yes

2. **只把 legacy cubemap 当 fallback**
   - What: `EnsureBottomEnvironment(...)` 按需加载
   - Impact: 缺失 `Skybox texture` 不再阻止 scene-driven bottom 启动

3. **先定位 bundle 占用再重跑资产编译**
   - What: `StrideCompileAsset` 失败时先查进程，确认是运行中的 `Terrain.Editor.exe` 占住旧 bundle
   - Why it worked: 关掉占用进程后，`StrideCleanAsset/StrideCompileAsset` 立即恢复正常

---

## Problems Encountered & Solutions

### Problem 1: `StrideCompileAsset` 因 bundle 文件被占用而失败
**Symptom:** `default.36d189a57982f806da71512cbec62a85.bundle` 删除失败，asset compiler 在 bundle 生成阶段抛 `IOException`
**Root Cause:** 运行中的 `E:\Stride Projects\Terrain\Bin\Editor\Debug\win-x64\Terrain.Editor.exe` 正在持有旧 bundle
**Investigation:**
- 先看 `StrideCleanAsset` warning 与 `StrideCompileAsset` 错误路径
- 用 `Get-Process` 确认当前活跃的 `Terrain.Editor` 进程

**Solution:**
- 关闭占用进程后重新执行：
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`

**Why This Works:** Stride bundle 生成需要先删除旧 bundle；只要运行中的 editor 持有句柄，就无法覆盖。
**Pattern for Future:** 资产编译失败若落在 `Bin\Editor\...\data\db\bundles\default*.bundle` 删除阶段，先查并关闭运行中的 editor/test 进程。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`

### Architectural Decisions That Changed
- **Changed:** river bottom scene-light selection 与 env fallback 语义
- **From:** 全局 `CurrentLights` 直接取第一盏 directional/skybox，`Skybox texture` 启动时强制加载
- **To:** 当前 `LightingView` 的可见 directional/skybox + scene skybox 缺失时才 lazy optional fallback
- **Scope:** `RiverRenderFeature`、`RiverResourceLoader`、`RiverBottom` 参数链、相关测试

---

## Code Quality Notes

### Testing
- **Verified Commands:**
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
  - `dotnet build Terrain.Editor\Terrain.Editor.csproj -c Debug -p:UseSharedCompilation=false`
  - `dotnet run --project Terrain.Editor.Tests\Terrain.Editor.Tests.csproj -c Debug`
- **Result:** 全部通过；仍有既有 NuGet/WinForms warnings，以及 `RiverBottom` 的既有 HLSL loop-unroll warning。

### Technical Debt
- **Paid Down:** bottom 侧 dead `BottomSun*` 参数链
- **Remaining:** custom 5x5 PCF 与 Stride 原生 shadow receiver 的完全一致性仍是后续第二阶段问题

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 用新的 runtime 再抓一帧 RenderDoc，确认 bottom 在多 view / scene skybox 缺失场景下仍绑定到正确的 current-view lights
2. 继续评估 `lighting_x3` 是否还能下调
3. 如需更进一步对齐 CK3，再收敛 custom shadow receiver 与 Stride 原生 directional shadow 语义差异

### Questions to Resolve
1. 当前 per-view 取灯是否已经覆盖项目里可能出现的多 camera / split lighting view 场景？
2. `RiverSurface` 的 `_ShadowTermFallback/_CloudMaskFallback` 是否也该进入下一轮 scene-driven 收口？

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverRenderFeature` 现在优先从 forward-lighting 的 per-view `VisibleLights` 选 scene sun/skybox
- `Skybox texture` 现在只会在 scene skybox cubemap 缺失时通过 `EnsureBottomEnvironment(...)` 按需尝试加载
- bottom shader / CPU 链已经不再保留 `BottomSun*`

**Gotchas for Next Session:**
- `StrideCompileAsset` 如果卡在 bundle 删除，先查是否有运行中的 `Terrain.Editor.exe`
- `RiverSurface` 仍然保留 neutral-lighting fallback，不要误以为 bottom 和 surface 已经完全同构

---

## Code References

- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---
