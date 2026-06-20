# 河流 bottom/surface 目标着色器语义对齐
**Date**: 2026-06-18
**Session**: 目标截帧复核与落地
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 让 `RiverBottom` 与 `RiverSurface` 的 shader 语义按 CK3 目标截帧实际命中的 pass 对齐，而不是保留本地补偿或近似实现。

**Secondary Objectives:**
- 用 RenderDoc 热替换和新截帧验证修改是否进入 GPU。
- 清理业务命名中的 `CK3Parity` / `Reference` 风格名称，资源临时放在现有 `Assets/River` 路径下。
- 更新 shader key、测试和项目状态文档。

**Success Criteria:**
- bottom pass 对齐目标截帧实际使用的 non-advanced branch。
- surface pass 对齐目标截帧实际使用的 `CalcRiverAdvanced -> CalcWater` branch。
- 新截帧中旧双 flow、旧 see-through、临时 depth adapter、`safeDenom` parallax 等模式不再出现。

---

## Context & Background

**Previous Work:**
- 2026-06-18 多轮日志已定位 scene seed、bottom lighting、ribbon basis、surface waterFade/refraction 等问题。
- `ck3-river.rdc` 与本地 `debug*.rdc` 对比表明，旧实现虽然部分数值接近，但 surface composition 和 bottom branch 仍不是目标 shader 语义。

**Current State:**
- 新本地截帧：`C:\Users\Redwa\Desktop\debug-river-target-after.rdc`
- 目标约束：修改 SDSL 前先用 RenderDoc hot-replace 归因；落地代码不得使用本地亮度/depth 补偿冒充目标语义。

---

## What We Did

### 1. Surface shader 对齐目标 `CalcRiverAdvanced -> CalcWater`
**Files Changed:** `Terrain.Editor/Effects/RiverSurface.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- `SampleFlowNormal` 改为单次 `FlowNormalTexture` 采样，不再使用双 flow / sine x-offset。
- 增加三层 `_WaterWave1/2/3` ambient normal 采样参数与 `_WaterFlowNormalFlatten`、`_FlattenMult`、`_WaterHeight` 等目标 cbuffer 输入。
- `CalcRefraction` 改为 CK3 base sample -> shore mask -> offset sample -> `step` 回退 -> see-through final depth 路径。
- `WaterFade` 独立重采 base refraction，使用 `min(InputDepth, RefractionDepth)`，删除 cross-section visual depth adapter。
- C# 绑定 `_ViewMatrix`、`_GlobalTime`、wave/flatten/water-height 等参数，避免 GPU 继续使用旧默认。

**Rationale:**
- 目标 surface draw 的 disasm 明确是 `CalcRiverAdvanced -> CalcWater`，旧 handwritten composition 会把 raw refraction 推成高饱和蓝青。

### 2. Bottom shader 对齐目标 non-advanced branch
**Files Changed:** `Terrain.Editor/Effects/RiverBottom.sdsl`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- depth profile 改为 `1 - pow(cos(UV.y * 2PI) * 0.5 + 0.5, 2.0)`，不再使用 bank/width power/clamp。
- steep parallax 使用固定 min/max layer `2/10` 与 CK3 interpolation，删除 `safeDenom` 和本地 saturate。
- diffuse/properties/normal 继续用 world-UV，depth/profile 用 tangent-UV。
- shadow compare 使用 water-surface projection 排除水面自身投影到底部。
- under ocean fade 绑定并使用 `_WaterHeight`。

**Rationale:**
- 目标 bottom draw 的 disasm 命中 `CalcRiverBottom` non-advanced；把 advanced 或补偿参数落地会偏离实际目标分支。

### 3. 命名、资源和文档收尾
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`, `Terrain.Editor/Assets/River/README.md`, `Terrain.Editor/Models/RiverPixelType.cs`, `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/learnings/stride-river-rendering-patterns.md`

**Implementation:**
- 业务命名改为中性 map/target 语义，不使用 `CK3Parity` 或 `Reference` 风格名称。
- 更新功能清单和架构概览，移除“surface 待截帧复核”和“visual depth adapter 是当前方案”的旧结论。
- 经验文档新增规则：同一源码存在多个 pass branch 时，以目标截帧实际 draw/disasm 为准。

---

## Decisions Made

### Decision 1: bottom 和 surface 分支分别以目标截帧实际命中为准
**Context:** CK3 同一源码包含 bottom advanced/non-advanced 与 surface advanced 路径。
**Decision:** bottom 落地 non-advanced；surface 落地 `CalcRiverAdvanced -> CalcWater`。
**Rationale:** RenderDoc disasm/cbuffer 比源码猜测更可靠。
**Trade-offs:** 代码不会追求逐字复制全部未命中分支，只锁定目标 frame 实际运行语义。
**Documentation Impact:** 已更新 `ARCHITECTURE_OVERVIEW.md`、`CURRENT_FEATURES.md`、`stride-river-rendering-patterns.md`。

### Decision 2: 删除本地补偿，不保留“看起来接近”的 adapter
**Context:** 早期 hot-replace 证明一些 depth floor / brightness multiplier 可把单帧推近目标。
**Decision:** 不落地这些补偿；以 CK3 cbuffer 和 disasm 公式为准。
**Rationale:** 用户要求 bottom/surface shader 完全参考目标语义，补偿会制造下一轮偏差。
**Trade-offs:** 如果后续视觉仍有差异，应继续查资源、scene mask 或几何输入，而不是先加 shader 常量。

---

## What Worked ✅

1. **RenderDoc hot-replace 先归因再落地**
   - direct-refraction replacement 证明 surface 偏蓝来自 handwritten water composition。
   - cbuffer/disasm 复核直接确认新 shader 是否进入 GPU。

2. **文本测试锁住 shader 语义**
   - 测试同时断言目标模式存在、旧模式不存在，避免重新引入双 flow、`safeDenom` 或 capped-depth see-through。

---

## Problems Encountered & Solutions

### Problem 1: surface `_GlobalTime` 与 wave 参数旧路径未进 GPU
**Symptom:** 旧截帧中 `_GlobalTime=0` 且缺少 CK3 water wave 参数。
**Root Cause:** surface 参数绑定不完整，shader 仍走本地简化 composition。
**Solution:** `RiverRenderFeature` 显式绑定 `_GlobalTime`、`_ViewMatrix`、`_FlattenMult`、wave/flow/water 参数，并重新生成 shader keys。

### Problem 2: bottom branch 误判风险
**Symptom:** 源码中存在 `CalcRiverBottomAdvanced`，但目标截帧实际 bottom draw 不使用它。
**Root Cause:** 以源码相邻函数猜目标分支，而不是看实际 draw disasm。
**Solution:** 以 `ck3-river.rdc` bottom event 的实际 disasm 为准，落地 non-advanced profile/parallax/shadow 语义。

---

## Code Quality Notes

### Testing
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
- `dotnet msbuild Terrain.Editor/Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj --no-build`

### Manual / GPU Verification
- RenderDoc capture: `C:\Users\Redwa\Desktop\debug-river-target-after.rdc`
- Surface event `305`: cbuffer 包含 `_GlobalTime=17.777660369873047`、`_FlattenMult=1`、三层 `_WaterWave*`、`_WaterFlowNormalFlatten=1.5`、`_WaterHeight=3`、`_WaterColorMapTintFactor=0.010695934`。
- Bottom event `276`: cbuffer 包含 `_WaterHeight=3`。
- Shader search 确认旧模式 `flowUv1`、`SampleRefractionSeeThrough`、`effectiveDepth`、`safeDenom`、`flowUv0.x` 均为 0。

### Known Warnings
- `StrideCompileAsset` 仍有 HLSL warning X3557（loop doesn't seem to do anything, forcing loop to unroll），资产编译结果为 succeeded。
- .NET build 仍有既有 NuGet vulnerability / C# warnings，本轮未处理。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md`
- [x] Update `docs/CURRENT_FEATURES.md`
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md`

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 目标截帧 draw/disasm 决定 pass branch。

**New Anti-Pattern:** 用临时 depth/fade adapter 或 brightness multiplier 代替目标 shader 语义。

### Architectural Decisions That Changed
- 无新的独立 ADR；这是 ADR-014 河流渲染架构下的 shader 语义收敛。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 用用户当前画面对 `debug-river-target-after.rdc` 进行视觉复核，确认剩余差异是在 shader、资源、scene mask 还是几何输入。
2. 若仍有明显差异，优先比较 surface 的 bound textures/resource stats 与 CK3 对应资源，不先加 shader 补偿。
3. 如果需要继续缩小差距，按 pass 顺序分别比较 bottom RT、refraction seed、surface final pixel history。

### Docs to Read Before Next Session
- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/learnings/stride-river-rendering-patterns.md`

---

## Session Statistics

**Files Changed:** 约 14 个代码/测试/文档文件
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Bottom 必须按目标截帧 non-advanced branch，不要重新引入 advanced-only bank/depth-width 参数。
- Surface 必须按 `CalcRiverAdvanced -> CalcWater`，不要回到 handwritten composition。
- 新 GPU 证据在 `C:\Users\Redwa\Desktop\debug-river-target-after.rdc`。

**Gotchas for Next Session:**
- 工作树里有许多非本轮遗留改动和未跟踪日志，不要回滚。
- `ReferenceEquals` 这类 .NET API 名称不是业务命名问题，不要机械替换。
- 继续调视觉差异时先用 RenderDoc 证明 pass 和资源差异，不要先加本地补偿常量。

---

## Links & References

### Code References
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

---

## Notes & Observations

- 本轮最关键的纠偏是：bottom 与 surface 不共享同一 advanced 判断；必须逐 pass 看目标截帧。
- 后续视觉差异如果仍存在，优先怀疑资源/scene mask/输入几何，而不是 shader 里再加亮度或深度补偿。
