# CK3 风格河流贴图驱动水体
**Date**: 2026-06-15
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 将河流视觉方向从 procedural 占位效果推进到 CK3 风格的贴图驱动水体路线。

**Secondary Objectives:**
- 保留 ADR-014 的 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature` 双 pass 架构。
- 让 bottom pass 采样底部 diffuse/normal/properties/depth 资源。
- 让 surface pass 采样 flow normal、ambient normal、foam、foam ramp、foam map、foam noise 和 reflection/specular 资源。
- 增加测试锁住 shader 资源声明、采样和 RenderFeature 绑定。

**Success Criteria:**
- 新增测试先失败，证明当前 shader/RenderFeature 没有资源驱动链路。
- shader key 文件通过 Stride generator 更新。
- `StrideAssetUpdateGeneratedFiles`、`StrideCleanAsset`、`StrideCompileAsset` 和普通 build 均通过。

---

## Context & Background

**Previous Work:**
- Related: [ADR-014 河流渲染架构](../../decisions/adr-014-river-rendering-architecture.md)
- Related: [Stride 河流渲染分层与标准变换链](../../learnings/stride-river-rendering-patterns.md)
- Related: CK3 `river_surface` / `river_bottom` RenderDoc 分析，本次对齐用户选择的 C 路线：CK3 式动态水体。

**Current State:**
- 河流已经有正式双 pass 渲染架构。
- 实现前 `RiverBottom.sdsl` 主要用常量色和 procedural pattern；`RiverSurface.sdsl` 主要用 sin/noise 模拟流动与泡沫。
- `RiverResourceLoader` 已预留 CK3 风格贴图槽位，但 `RiverRenderFeature` 没有绑定这些资源。
- 本轮后续复查发现：仅复制 DDS 到 `Assets/River/` 不足以保证 `Content.Load<Texture>("River/...")` 可用，必须为每张 DDS 创建 `.sdtex` Stride texture asset descriptor。

**Why Now:**
- 用户确认目标是 C：继续做 CK3 式水体，而不是参考图里的沙地湿痕/浅水泥沙河。

---

## What We Did

### 1. 增加 shader 文本回归测试
**Files Changed:** `Terrain.Editor.Tests/RiverShaderTextTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增测试要求 `RiverBottom.sdsl` 声明并采样 `BottomDiffuseTexture` / `BottomNormalTexture` / `BottomPropertiesTexture` / `BottomDepthTexture`。
- 新增测试要求 `RiverSurface.sdsl` 声明并采样 `AmbientNormalTexture` / `FlowNormalTexture` / `FoamTexture` / `FoamRampTexture` / `FoamMapTexture` / `FoamNoiseTexture` / `ReflectionSpecularTexture`。
- 新增测试要求 `RiverRenderFeature` 使用 `RiverResourceLoader` 并绑定全部新增 keys。

**Rationale:**
- SDSL 视觉逻辑很难用普通单元测试执行，文本测试可防止资源采样链路退回 procedural 占位。

### 2. 河底 pass 改为底部贴图驱动
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Effects/RiverBottom.sdsl.cs`, `Terrain.Editor/Effects/RiverCommon.sdsl`

**Implementation:**
- `RiverBottom` 新增底部 diffuse/normal/properties/depth 纹理和 sampler。
- 河底颜色由 diffuse 贴图乘深浅 tint，properties/depth 控制暗化和深度变化。
- normal 贴图通过 `RiverUnpackNormal` 参与河底明暗。
- RT0 alpha 改为压缩世界信息，RT1 alpha 作为 dual-source blend 混合权重。

**Rationale:**
- CK3 的 bottom pass 不是单纯底色，而是“水下颜色 + 世界信息 + dual-source alpha”的中间折射输入。

### 3. 水面 pass 改为水体贴图驱动
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Effects/RiverSurface.sdsl.cs`

**Implementation:**
- `RiverSurface` 新增 ambient normal、flow normal、foam、foam ramp、foam map、foam noise、reflection/specular 纹理和 sampler。
- flow normal 双层滚动采样，驱动折射偏移和泡沫输入。
- ambient normal 双层世界 UV 采样，增加水面细波变化。
- foam 由 bank/connection mask、flow alpha、foam noise、foam map、foam detail 和 foam ramp 合成。
- reflection/specular 贴图提供高光/反射变化。

**Rationale:**
- 这比继续调 `sin` 和颜色参数更接近 CK3 的真实水体路径。

### 4. RenderFeature 绑定河流资源
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
- `RiverRenderFeature` 初始化时通过 `ContentManager` 加载 `RiverResourceLoader`。
- 每帧 draw 前绑定 bottom 和 surface 所需纹理资源。
- destroy 时卸载资源并清空 loader 状态。

**Rationale:**
- 资源已经存在于 `Terrain.Editor/Assets/River/`，缺的是正式绑定链路。

### 5. 补齐 CK3 DDS 与 Stride `.sdtex` 资产描述
**Files Changed:** `Terrain.Editor/Assets/River/**/*.sdtex`, `Terrain.Editor/Assets/River/README.md`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, `Terrain.Editor/Effects/RiverSurface.sdsl`

**Implementation:**
- 从 CK3 安装目录复制并校验 12 张 DDS：bottom diffuse/normal/properties/depth，water ambient normal/flow normal/foam/foam ramp/foam map/foam noise/water-color，以及 environment reflection-specular。
- 为每张 DDS 增加同目录 `.sdtex`，使用 `!ColorTextureType` 和 `UseSRgbSampling: false` 保留 CK3 打包通道，不让 normal-map importer 改写 alpha/packed data。
- 新增 `Water/WaterColorTexture` 路径，采样 `watercolor_rgb_waterspec_a.dds`，RGB 参与水色，A 参与 specular 调制。
- 文本测试新增 `river ck3 texture assets have Stride descriptors`，防止以后只复制 DDS、漏掉 `.sdtex`。

**Rationale:**
- Stride 内容加载使用 asset URL，不是直接扫 DDS 文件。`.sdtex` 是让 `River/Water/flow-normal` 这类 URL 成为有效内容资产的必要描述。

### 6. RenderDoc 定位并修正水面沙色泄漏
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Terrain.Editor.sdpkg`, `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 分析 `C:\Users\Redwa\Desktop\debug.rdc` 的 208 `DrawIndexed(696)`。
- `pixel_history(1010,390)` 显示 208 shader 输出为深蓝黑 `[0, 0.007, 0.022, 0.849]`，但 post-blend 变成沙色 `[0.769, 0.577, 0.408, 1]`，原因是和高亮 HDR 地形 `[5.098, 3.778, 2.558, 1]` 做普通 alpha blend。
- `RiverSurface` alpha 改为 `edgeFade * connectionFade * transparency * saturate(_ZoomBlendOut)`，不再乘 water-color alpha，保证河流中心水面接近替换目标颜色，边缘仍淡出。
- `get_resource_usage` 还显示 CK3 贴图没有在 166/208 上作为真实 `PS_Resource` 使用；`get_bindings` 只有 shader slot 反射名，不能证明 C# 已绑定 texture。
- 将 12 个 river texture asset 加入 `Terrain.Editor.sdpkg` `RootAssets`，确保动态 `Content.Load<Texture>("River/...")` 资产进入 bundle。
- `RiverResourceLoader` 不再静默吞掉加载异常，失败会抛出包含 URL 的 `InvalidOperationException`。

**Rationale:**
- 沙色问题的直接原因是 HDR 目标上的 alpha blend 泄漏；贴图未绑定是独立的资源链路问题，会导致 bottom RGB 全黑和水面贴图无效。

---

## Decisions Made

### Decision 1: 先补贴图驱动链路，不先追完整 CK3 BRDF
**Context:** CK3 水体包含更多环境反射、Fresnel、泡沫和折射细节。
**Options Considered:**
1. 只调现有颜色/alpha 参数 - 快，但不会解决 procedural 占位根因。
2. 一次性完整复刻 CK3 water common - 风险高，SDSL/Stride 适配面太大。
3. 先打通贴图采样和资源绑定，再逐步细化水体 BRDF。

**Decision:** 选择选项 3。
**Rationale:** 这是最小可验证闭环，也最符合当前已有资源和双 pass 架构。
**Trade-offs:** 视觉仍需要视口截图调参；本次不是最终 CK3 parity。
**Documentation Impact:** 已更新架构概览和当前功能清单。

### Decision 2: 用文本测试锁 shader 结构
**Context:** shader 输出视觉难以在当前测试工程中自动截图比对。
**Decision:** 增加文本测试锁住声明、采样和绑定。
**Rationale:** 能覆盖本次最容易回退的结构性行为：资源链是否存在。
**Trade-offs:** 不能证明最终画面好看，只证明关键路径不会被移除。

---

## What Worked ✅

1. **Stride shader workflow**
   - What: 运行 `_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles` 更新 `.sdsl.cs` keys，再运行 `StrideCleanAsset` 和 `StrideCompileAsset`。
   - Why it worked: 新增 shader texture 参数后，C# key 文件和 asset compiler 都保持同步。
   - Reusable pattern: Yes

2. **RenderDoc 结论转实现边界**
   - What: 依据 CK3 bottom/surface 双 pass 分析，优先迁移资源采样、dual-source alpha 和折射缓冲语义。
   - Impact: 避免继续在 procedural 占位 shader 上调参。

---

## What Didn't Work ❌

1. **把“DDS 已在目录中”误认为“资源链路已完成”**
   - What we tried: 先完成 shader 采样和 RenderFeature 绑定，只检查了 `Assets/River/` 有 DDS。
   - Why it failed: Stride runtime 通过 `.sdtex` asset URL 加载内容；没有 descriptor 时，DDS 不等价于可被 `Content.Load<Texture>` 解析的资产。
   - Lesson learned: 贴图驱动 shader 的完成标准必须包括源 DDS、`.sdtex`、Content URL、asset compiler 四者闭环。
   - Don't try this again because: 这会让 shader 参数绑定看起来完整，但运行期资源可能仍为空或加载失败。

1. **视觉辅助 server 需要 Windows 路径修正**
   - What we tried: 直接调用 `bash start-server.sh`。
   - Why it failed: PowerShell 环境没有 `bash`，随后 MSYS bash 也缺少 `/usr/bin` PATH。
   - Lesson learned: Windows 下需要显式使用 `C:\msys64\usr\bin\bash.exe` 并把 `C:\msys64\usr\bin` 加入 PATH。
   - Don't try this again because: 默认 bash 路径在当前环境不可靠。

---

## Problems Encountered & Solutions

### Problem 1: shader key 文件需要生成器刷新
**Symptom:** 新增 SDSL texture 参数后，C# 绑定需要对应 `RiverSurfaceKeys` / `RiverBottomKeys`。
**Root Cause:** `.sdsl.cs` 是生成文件。
**Solution:**
```powershell
dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug
```
**Why This Works:** Stride key generator 读取 SDSL 参数并更新 ObjectParameterKey。
**Pattern for Future:** SDSL 参数变更后先刷新 key，再写 RenderFeature 绑定。

### Problem 2: `RiverEffect.sdfx.cs` 被生成器触碰
**Symptom:** `git status` 显示 `RiverEffect.sdfx.cs` modified，但普通 diff 无实质内容差异；`core.autocrlf=false` 下显示错误文本换行差异。
**Root Cause:** 该 `.sdfx` 当前本身会生成 parse error 文本，生成器重写了错误输出文件。
**Solution:** 本次不依赖 `RiverEffect.sdfx.cs`，且 `Terrain.Editor.csproj` 已 `Compile Remove="Effects\RiverEffect.sdfx.cs"`。
**Why This Works:** 运行时直接创建 `DynamicEffectInstance("RiverBottom")` / `("RiverSurface")`，不使用该生成错误文件。
**Pattern for Future:** 后续如要清理，应独立修正或移除过时 `RiverEffect.sdfx`，不要把它混进水体视觉改动。

### Problem 3: CK3 贴图缺少 Stride asset descriptor
**Symptom:** 用户指出相关纹理贴图并未完整进入工程资源链路。
**Root Cause:** DDS 文件存在不代表 Stride 内容库存在对应 asset URL。
**Solution:**
```yaml
!Texture
Source: !file flow-normal.dds
Type: !ColorTextureType
    UseSRgbSampling: false
```
**Why This Works:** `.sdtex` 让 `ContentManager` 可以按 `River/Water/flow-normal` URL 加载编译后的 texture asset，asset compiler 也会把 DDS 源文件纳入 bundle。
**Pattern for Future:** 任何外部 DDS 导入都要同时提交 `.sdtex`，并用 asset compiler 验证。

### Problem 4: shader slot 反射存在但实际 texture 未绑定
**Symptom:** RenderDoc 208 `get_bindings` 显示 `WaterColorTexture_id49` 等 slot，但 `get_resource_usage` 没有任何 CK3 texture 在 166/208 作为 `PS_Resource` 使用；bottom refraction buffer RGB 全 0。
**Root Cause:** `.sdtex` 虽被 asset compiler 扫描，但动态 `Content.Load` 的资源没有列入 `RootAssets`，且 `RiverResourceLoader` 静默吞掉加载异常并返回 null。
**Solution:**
```yaml
RootAssets:
    - a734d0f8-42ac-44f7-a2e6-de5ebfd17f93:River/Bottom/bottom-diffuse
```
并让 loader 抛出带 URL 的加载异常。
**Why This Works:** Stride bundle 会保留动态加载资产；若 URL 或 bundle 仍错误，运行时不再无声降级为空 texture。
**Pattern for Future:** 对代码动态加载的 Stride asset，除了 `.sdtex` 还必须检查 `RootAssets` 或真实引用链，并用 RenderDoc resource usage 验证。

### Problem 5: HDR 地形通过 surface alpha blend 泄漏
**Symptom:** 208 draw 的 shader output 是深蓝黑，但 post-blend 变成沙色。
**Root Cause:** 水面 alpha 被 water-color alpha 限制在约 0.85；目标地形 HDR 颜色高达 `[5.1, 3.8, 2.6]`，普通 alpha blend 留下的 15% 地形仍非常亮。
**Solution:**
```csharp
float alpha = edgeFade * connectionFade * transparency * saturate(_ZoomBlendOut);
```
**Why This Works:** 中心水面不再保留亮沙地底色；边缘和连接处仍按 mesh fade/transparency 淡出。
**Pattern for Future:** HDR render target 上调水体透明度时必须看 post-blend，不只看 shader output。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录河流资源绑定和 bottom/surface 贴图驱动职责。
- [x] Update `docs/CURRENT_FEATURES.md` - 记录路径渲染-河流已绑定 `Assets/River/` CK3 风格资源。

### Architectural Decisions That Changed
- **Changed:** 河流 shader 视觉实现
- **From:** procedural bottom/surface 占位效果
- **To:** CK3 风格贴图驱动 bottom/surface，包含 DDS 源文件与 `.sdtex` 内容描述
- **Scope:** `Terrain.Editor/Effects/River*.sdsl` 与 `RiverRenderFeature`
- **Reason:** 用户确认目标是 CK3 式水体路线。

---

## Code Quality Notes

### Testing
- **Tests Written:** 3 个文本回归测试。
- **Coverage:** bottom shader 贴图声明/采样、surface shader 水体贴图声明/采样、RenderFeature 资源绑定。
- **Manual Tests:** 仍需要打开 editor 视口截图确认视觉参数是否过亮/过暗、泡沫是否过强、水色是否接近 CK3。

### Verification
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` ✅ PASS
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug` ✅ PASS
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug` ✅ PASS
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug` ✅ PASS
- `dotnet build Terrain.Editor/Terrain.Editor.csproj -c Debug` ✅ PASS
- CK3 源 DDS 与项目 DDS SHA256 校验：12/12 match ✅
- 追加修正后 `StrideCompileAsset`：907 succeeded, 0 failed ✅
- RenderDoc evidence: 208 shader output 深色但 post-blend 被 HDR 沙地冲亮；166/208 未见 CK3 texture resource usage，已通过 `RootAssets` + loader fail-fast 修正。

**Known Warnings:**
- 既有 NuGet vulnerability warnings。
- `StrideCompileAsset` 报 shader loop unroll warning，但 asset build 成功。
- 既有 C# warnings：`EditorGlobalLodMap` 字段未赋值、`TerrainManager.ProjectNotificationRaised` 未使用、WinForms high DPI manifest warning。

### Technical Debt
- **Created:** 当前 water BRDF 仍是 CK3-inspired 近似，不是完整移植 `jomini_water_default.fxh`。
- **Paid Down:** 河流贴图资源 loader 不再闲置，shader 不再主要依赖 procedural 占位。
- **TODOs:** 后续应视口截图调参，并考虑修正/删除过时 `RiverEffect.sdfx`。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在 editor 视口实测河流画面，截图对比 CK3 捕获，调 `WaterColorShallow/Deep`、foam、refraction offset、normal strength。
2. 如果水体发黑或透明异常，优先检查 bottom RT alpha/dual-source blend 与 surface refraction blend。
3. 如需更高保真，继续移植 CK3 `CalcWater` 的 Fresnel、reflection、foam ramp 和 ambient normal 权重。

### Questions to Resolve
1. 当前 `ReflectionSpecularTexture` 是否应改为 cube/latlong 专用采样路径？ - 取决于资产实际格式和视口效果。
2. `RiverEffect.sdfx` 是否仍需要保留？ - 当前自定义 RenderFeature 不依赖它，但生成器会反复写错误文件。

### Docs to Read Before Next Session
- [ADR-014 河流渲染架构](../../decisions/adr-014-river-rendering-architecture.md)
- [Stride 河流渲染分层与标准变换链](../../learnings/stride-river-rendering-patterns.md)

---

## Session Statistics

**Files Changed:** 12 logical files including this log.
**Lines Added/Removed:** Code/doc diff approximately +247/-29 before this log.
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `RiverBottom.sdsl` now samples bottom diffuse/normal/properties/depth and writes compressed world info to RT0 alpha.
- `RiverSurface.sdsl` now samples flow/ambient normals, foam textures, foam ramp/map/noise, reflection/specular, and refraction buffer.
- `RiverRenderFeature` owns `RiverResourceLoader` and binds textures via generated shader keys.
- Each river DDS now has a sibling `.sdtex`; `Content.Load<Texture>` should use URLs without extension, for example `River/Water/water-color`.
- `RiverShaderTextTests` locks this structure.

**What Changed Since Last Doc Read:**
- River visual implementation moved from procedural placeholder to CK3-inspired asset-driven water.
- Architecture docs now mention `Assets/River/` resources as part of the river rendering feature.

**Gotchas for Next Session:**
- Do not trust normal `dotnet build` alone after SDSL changes; run Stride key generation and asset compile targets.
- `RiverEffect.sdfx.cs` may show as modified due generated error text; it is excluded from compile and should be handled separately.
- Text tests prove structure, not final visual quality; visual screenshot pass is still required.

---

*Template Version: 1.0 - Based on Archon-Engine template*
