# River Bottom Light Binding Fix
**Date**: 2026-06-17
**Session**: River Bottom Light Binding Fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 基于 `debug-latest_frame1887.rdc` 的 RenderDoc 热替换证据，修正 current river bottom 的光照输入链。

**Secondary Objectives:**
- 在不修改 `RiverBottom.sdsl` 的前提下，让 bottom pass 改为吃 scene skybox 环境图，并补齐 bottom lighting 参数绑定链。
- 用测试锁住本轮行为，避免后续 parity 改动再次退回“shader 默认值 + 错误 cubemap 来源”。

**Success Criteria:**
- `RiverRenderSettings -> RiverRenderObject -> RiverRenderFeature` 能把 bottom lighting 参数喂进 shader。
- bottom 的 `EnvironmentMapTexture` 优先绑定 `Skybox texture`，surface 继续使用 `reflection-specular`。
- `Terrain.Editor.Tests` 全绿。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)
- Related: [2026-06-17-river-bottom-light-binding-design.md](../../../superpowers/specs/2026-06-17-river-bottom-light-binding-design.md)
- Related: [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- RenderDoc 热替换已证明 current bottom 的能量主要来自 IBL。
- current `RiverRenderFeature` 没有绑定 `_BottomSun* / _BottomEnvironmentIntensity / _BottomSpecularIntensity / _BottomNormalStrength`。
- bottom 仍绑定 `River/Environment/reflection-specular`，与 editor scene 的 `Skybox texture` / `LightSkybox` 脱节。

**Why Now:**
- 在继续动 SDSL 或回退到 non-advanced world-UV 路径前，先修正明显错误的 C# 输入链，成本最低，且直接对应本轮抓帧证据。

---

## What We Did

### 1. 写 design spec 和红测
**Files Changed:** `docs/superpowers/specs/2026-06-17-river-bottom-light-binding-design.md`, `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 新增短 spec，明确本轮只动 C# 绑定链，不改 SDSL。
- 新增文本测试，锁定：
  - bottom lighting 参数必须进入 `RiverRenderSettings / RiverRenderObject / RiverRenderFeature`
  - bottom environment source 必须改为 `Skybox texture`
  - surface 继续使用 `reflection-specular`

**Rationale:**
- 先把行为钉死，避免实现时又滑回“只修一点常量”。

### 2. 补齐 bottom lighting 参数链
**Files Changed:** `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`, `Terrain.Editor/Rendering/River/RiverRenderObject.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```csharp
public float BottomNormalStrength { get; set; } = 1.0f;
public Vector3 BottomSunDirection { get; set; } = new(0.35f, 0.75f, -0.55f);
public Vector3 BottomSunColor { get; set; } = new(1.0f, 0.92f, 0.82f);
public float BottomSunIntensity { get; set; } = 1.35f;
public float BottomEnvironmentIntensity { get; set; } = 1.0f;
public float BottomSpecularIntensity { get; set; } = 0.35f;
```

```csharp
effect.Parameters.Set(RiverBottomKeys._BottomNormalStrength, riverObject.BottomNormalStrength);
effect.Parameters.Set(RiverBottomKeys._BottomSunDirection, riverObject.BottomSunDirection);
effect.Parameters.Set(RiverBottomKeys._BottomSunColor, riverObject.BottomSunColor);
effect.Parameters.Set(RiverBottomKeys._BottomSunIntensity, riverObject.BottomSunIntensity);
effect.Parameters.Set(RiverBottomKeys._BottomEnvironmentIntensity, riverObject.BottomEnvironmentIntensity);
effect.Parameters.Set(RiverBottomKeys._BottomSpecularIntensity, riverObject.BottomSpecularIntensity);
```

**Rationale:**
- 让 bottom lighting 不再只依赖 shader 默认值。

### 3. 切换 bottom environment source 到 scene skybox
**Files Changed:** `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`, `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`

**Implementation:**
```csharp
public const string BottomEnvironmentUrl = "Skybox texture";
public Texture? BottomEnvironment { get; private set; }
BottomEnvironment = LoadRequiredTexture(content, BottomEnvironmentUrl);
```

```csharp
SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, riverResources.BottomEnvironment);
SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, riverResources.ReflectionSpecular);
```

**Rationale:**
- 优先绑定 scene skybox，必要时再回退到旧 river cubemap。
- surface 仍单独绑定 `ReflectionSpecularTexture`，保持水面语义不变。

### 4. 给 editor river settings 注入场景默认光照
**Files Changed:** `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

**Implementation:**
```csharp
_riverComponent.Settings.BottomSunIntensity = 2.0f;
_riverComponent.Settings.BottomEnvironmentIntensity = 1.0f;
```

并同步了 `BottomSunDirection / BottomSunColor / BottomSpecularIntensity / BottomNormalStrength`。

**Rationale:**
- 至少让 river bottom 的默认 lighting closer to 当前 editor 场景，而不是继续停留在纯 shader fallback。

---

## Decisions Made

### Decision 1: 先修 C# 输入链，不改 SDSL
**Context:** RenderDoc 已证明当前主问题在 lighting input/source，不在 UV 单点公式。

**Options Considered:**
1. 继续改 `RiverBottom.sdsl`
2. 先修 C# 绑定链

**Decision:** 选择 2
**Rationale:** 与抓帧证据直接对应，改动更小，也符合用户“改 SDSL 前先热验证”的要求。

### Decision 2: bottom 和 surface 不再共用同一张 reflection cubemap
**Context:** current bottom 和 surface 都在吃 `reflection-specular`。

**Options Considered:**
1. 继续共用
2. bottom 用 scene skybox，surface 继续用 river reflection/spec

**Decision:** 选择 2
**Rationale:** bottom 的 IBL 应该跟 scene skybox 对齐，surface 的 reflection/spec 仍是水面材质资源。

---

## What Worked ✅

1. **RenderDoc 先定性，再写 C# 修复**
   - What: 先用 hot-replace 拆 direct / IBL / x3 energy，再决定改动面
   - Why it worked: 避免继续在 UV 或 surface 上浪费时间
   - Reusable pattern: Yes

2. **文本测试锁结构**
   - What: 用 `RiverShaderTextTests` 先写红测，再补字段和绑定
   - Impact: 很快确认缺口只在参数链和资源来源

---

## What Didn't Work ❌

1. **继续期待 shader 默认值“自然够亮”**
   - What we tried: 本轮前的 current 代码实际上一直依赖这条路径
   - Why it failed: RenderDoc 证明 bottom 主要能量来自 IBL，而错误 cubemap 来源和缺失参数绑定会把结果整体压暗
   - Lesson learned: bottom lighting 问题不能只看 shader 公式，还要看 C# 输入源

---

## Problems Encountered & Solutions

### Problem 1: bottom lighting 参数没有进入运行时绑定链
**Symptom:** `RiverBottom` 虽然声明了 `_BottomSun* / _BottomEnvironmentIntensity / _BottomSpecularIntensity`，但 `RiverRenderFeature` 不绑定。
**Root Cause:** 参数只存在于 shader / generated keys，没有进入 `Settings -> RenderObject -> RenderFeature`。
**Investigation:**
- Tried: `rg` 检查 `RiverRenderFeature`、`RiverRenderObject`、`RiverRenderSettings`
- Found: 当前只绑定 `_ShadowTermFallback / _CloudMaskFallback`

**Solution:**
```csharp
effect.Parameters.Set(RiverBottomKeys._BottomSunIntensity, riverObject.BottomSunIntensity);
effect.Parameters.Set(RiverBottomKeys._BottomEnvironmentIntensity, riverObject.BottomEnvironmentIntensity);
```

**Why This Works:** 把 bottom lighting 从“shader 默认值”提升为正式运行时输入。

### Problem 2: bottom IBL 来源与 scene skybox 脱节
**Symptom:** current bottom 绑定的是 `reflection-specular`，不是 scene skybox。
**Root Cause:** `RiverResourceLoader` 只加载 river 专用环境资源，没有加载 `Skybox texture`。
**Investigation:**
- Tried: 对比 `RiverResourceLoader`、`EmbeddedStrideViewportGame`
- Found: editor scene 自己已经能加载 `Skybox texture`

**Solution:**
```csharp
public const string BottomEnvironmentUrl = "Skybox texture";
BottomEnvironment = LoadRequiredTexture(content, BottomEnvironmentUrl);
SetTexture(bottomEffect.Parameters, RiverBottomKeys.EnvironmentMapTexture, riverResources.BottomEnvironment);
```

**Why This Works:** bottom IBL 至少先回到和 scene skybox 同一来源。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录 bottom env source 与 lighting 参数绑定链
- [x] Update `docs/CURRENT_FEATURES.md` - 记录 bottom skybox binding / surface reflection 语义分离
- [x] Update `docs/log/learnings/stride-river-rendering-patterns.md` - 记录新的 pattern / anti-pattern

### New Patterns/Anti-Patterns Discovered
**New Pattern:** bottom lighting 参数走完整绑定链
- When to use: 自定义 river/water shader 需要吃 scene light/IBL 参数时
- Benefits: 不再依赖 shader 默认值，RenderDoc 热验证能直接映射到运行时代码
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

**New Anti-Pattern:** bottom 和 surface 共用同一张 river reflection cubemap
- What not to do: 让 bottom 的 `EnvironmentMapTexture` 直接复用 `reflection-specular`
- Why it's bad: 会让 bottom IBL 和 scene skybox 脱节，尤其在 current 抓帧里会进一步压暗 riverbed
- Add warning to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** river bottom 的环境光输入来源
- **From:** `reflection-specular`
- **To:** `Skybox texture` 优先，`reflection-specular` 回退
- **Scope:** `RiverResourceLoader` + `RiverRenderFeature`
- **Reason:** 让 bottom IBL 回到 scene lighting 语义

---

## Code Quality Notes

### Performance
- **Measured:** 未做 GPU 性能测量
- **Target:** 本轮目标是语义修正，不是性能收敛
- **Status:** ⚠️ Close

### Testing
- **Tests Written:** 扩展 `RiverShaderTextTests`
- **Coverage:** bottom lighting 参数传播、bottom env source、surface reflection source
- **Manual Tests:** 尚未重新截取新 `.rdc` 验证视觉抬升

### Technical Debt
- **Created:** 无新的阻断债务
- **Paid Down:** bottom lighting 依赖 shader 默认值；bottom env source 错绑
- **TODOs:** 下一轮用新 capture 验证 scene skybox binding 后的 bottom RT 实际亮度

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新截取最新 river `.rdc` - 验证 bottom RT 是否从近黑抬到更接近 CK3 的暖亮量级
2. 若仍偏暗，继续量化 `Skybox texture` 与 `reflection-specular` 的 IBL 差异
3. 只有在 bottom input/source 修正后仍不足，再回到 `RiverBottom.sdsl` 路径选择问题

### Blocked Items
- **Blocker:** 无代码阻断
- **Needs:** 新 capture 做 GPU 视觉复核
- **Owner:** Codex / 用户

### Questions to Resolve
1. `Skybox texture` 绑定后，bottom RT 的实际能量是否已经足够接近 CK3？
2. current bottom 是否还需要进一步接 scene directional-light 方向/颜色的运行时同步，而不是只吃初始化默认值？

### Docs to Read Before Next Session
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md) - 本轮改动的 RenderDoc 证据前置
- [2026-06-17-river-bottom-light-binding-design.md](../../../superpowers/specs/2026-06-17-river-bottom-light-binding-design.md) - 本轮设计边界

---

## Session Statistics

**Files Changed:** 9
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `RiverResourceLoader`, `RiverRenderSettings`, `RiverRenderObject`, `RiverRenderFeature`, `EmbeddedStrideViewportGame`
- Critical decision: bottom env map 改为优先吃 scene skybox，而不是继续和 surface 共用 `reflection-specular`
- Active pattern: `RenderDoc hot-validate -> text red test -> minimal C# binding fix`
- Current status: 代码与测试已完成，等待新 capture 做视觉复核

**What Changed Since Last Doc Read:**
- Architecture: bottom env source 与 surface reflection source 现在语义分离
- Implementation: bottom lighting 参数已进完整绑定链
- Constraints: 还没有新的 `.rdc` 证明视觉最终效果

**Gotchas for Next Session:**
- Watch out for: 不要因为 shader 里有默认值，就忽略 C# 绑定链
- Don't forget: `reflection-specular` 现在只是 bottom fallback，不是主 env source
- Remember: 本轮没有改 `RiverBottom.sdsl`

---

## Links & References

### Related Documentation
- [2026-06-17-river-bottom-light-binding-design.md](../../../superpowers/specs/2026-06-17-river-bottom-light-binding-design.md)
- [stride-river-rendering-patterns.md](../../learnings/stride-river-rendering-patterns.md)

### Related Sessions
- [2026-06-17-river-renderdoc-pass-analysis.md](./2026-06-17-river-renderdoc-pass-analysis.md)

### Code References
- `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`

---

## Notes & Observations

- 本轮最重要的变化不是“调亮一个常量”，而是把 bottom lighting 从错误资源来源和默认值依赖里解耦出来。
- 当前仍缺最后一步 GPU 复核：需要新的 capture 看 scene skybox binding 后的实际 bottom RT。

---

*Template Version: 1.0 - Based on Archon-Engine template*
