# 河流水面 bank fade 黑边根因诊断
**Date**: 2026-06-16
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 重新分析 `C:\Users\Redwa\Desktop\debug.rdc` 中 event `271` 附近的河流输出，并与 CK3 同期河流水面输出对照，找出仍然明显偏黑的真实原因。

**Success Criteria:**
- 明确 draw/event 对应关系，不把非根因 pass 当最终问题。
- 用 RenderDoc pixel history / shader debug 证明黑色来自哪个 pass 的哪个输出。
- 修复方向不能依赖 `_BottomDiffuseMultiplier` 这类亮度补偿。

---

## Context & Background

**Previous Work:**
- See: [2026-06-16-river-surface-black-output-fix.md](2026-06-16-river-surface-black-output-fix.md)
- See: [2026-06-16-river-bottom-pass-refraction-scale-fix.md](2026-06-16-river-bottom-pass-refraction-scale-fix.md)
- Related: [stride-river-rendering-patterns](../../learnings/stride-river-rendering-patterns.md)

**Current State:**
- 河流资源和 CK3 风格 bottom/surface shader 已接入。
- bottom pass 已包含 environment cubemap IBL 路径，不能再用“底部缺 lighting”作为所有黑色问题的默认解释。
- 用户指出 draw `271` 与 CK3 仍差距明显，需要继续逐 pass 对照。

---

## What We Found

### 1. 用户说的 draw 271 实际是 eventId 271
**Files Changed:** none

**RenderDoc Evidence:**
- 当前 `debug.rdc` 为 D3D11 capture，69 draws / 82 events。
- Event `271` 是 surface pass，写全分辨率 RT `ResourceId::4055`，PS 为 `ResourceId::7787`，索引数 `1326`。
- 与它配对的 bottom/refraction draw 是 event `182`，同样为 `1326` indices。
- Event `182` bottom pass 写半分辨率 refraction RT `ResourceId::7770`。
- Surface pass 读取 `ResourceId::7770`，并绑定 `FlowNormalTexture` / `FoamTexture` / `FoamRampTexture` / `FoamMapTexture` / `FoamNoiseTexture` / `WaterColorTexture` / `ReflectionSpecularTexture` 等资源。

**Conclusion:**
- event `271` 的资源绑定不是空槽问题。
- 继续只盯 event `271` 会漏掉后续 surface draw 的可见覆盖。

### 2. 可见黑色主要由 event 307 写出，不是 event 271 本身
**Files Changed:** none

**RenderDoc Evidence:**
- 对 surface pass 做 RT 差分：
  - event `253 -> 271` 在 8-bit PNG preview 上基本无可见变化。
  - event `289 -> 307` 有 `92,163` 个像素变化，bbox 为 `(0,283)-(1671,663)`。
- 这说明当前截图中大面积黑暗河段主要由 event `307` 贡献。

**Conclusion:**
- event `271` 是同一类 surface shader 的有效样本，但实际黑带不能只按 `271` 的画面差分判断。
- 后续排查以 event `307` 的 pixel history / shader debug 为主。

### 3. 黑色是 surface shader 自己输出的 opaque black
**Files Changed:** none

**RenderDoc Evidence:**
- 像素 `(1668,284)`：
  - terrain event `119` 先写入 `RGB≈(3.9316,2.9629,2.0566), A=1`。
  - surface event `307` 通过测试并写入 shader output / post-blend `RGB=(0,0,0), A=1`，primitive `322`。
- 像素 `(1600,292)`：
  - surface event `307` 写入 `RGB≈(0.1483,0.1375,0.0972), A=1`，primitive `318`。
- 对 `(1668,284)` debug pixel：
  - 输入 `v2=(0.5812,0.0405866,4.06488e-05,1.0)`，其中 `v2.y` 是 cross-section UV，靠近岸边。
  - trace 显示 `RefractionTexture.Sample` 为 `(0,0,0,0)`。
  - `waterFade` 把主水色乘到 0，最终输出 `o0=(0,0,0,1)`。

**Conclusion:**
- 黑色不是 blend state、HDR 目标、贴图缺失或 bottom pass 单独造成。
- surface shader 在近岸区域自己输出了 alpha=1 的黑色。

### 4. 根因是 bank alpha fade 太窄，没覆盖 near-shore waterFade 黑斜坡
**Files Changed:**
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Root Cause:**
- 当前 `_BankFade = 0.02f`。
- 在黑色像素 `UV.y≈0.0406` 处，`edgeFade = smoothstep(0, 0.02, UV.y)` 已经约等于 `1`。
- 但同一位置 `waterFade` 仍然为 `0`，主水色被清零。
- 结果就是“颜色已经被 waterFade 压黑，alpha 却已经完全不透明”，覆盖了亮地形。

**Fix:**
```sdsl
stage float _BankFade = 0.15f;
```

**Rationale:**
- CK3 surface shader 也先用 water/refraction 计算颜色，再用 `EdgeFade1 * EdgeFade2` 乘最终 alpha。
- bank fade 必须覆盖 near-shore waterFade 的黑色过渡区，否则会出现 opaque black edge。
- `0.15f` 与当前 shader 已使用的 `RiverFoamMask(..., 0.15f)` 岸边宽度一致，比 `0.02f` 更符合当前窄 ribbon 的视觉尺度。
- 对旧黑点 `UV.y≈0.0406`，`smoothstep(0,0.15,0.0406)≈0.18`，不再以 alpha=1 覆盖地形。

---

## Decisions Made

### Decision 1: 不用 brightness multiplier 修复
**Context:** 黑色像素是 surface pass 输出 `RGB=(0,0,0), A=1`。

**Decision:** 不恢复 `_BottomDiffuseMultiplier`，也不通过调亮 bottom/refraction 掩盖问题。

**Rationale:** 即使 bottom 更亮，岸边 waterFade 与 edge alpha 的错位仍会产生错误的不透明覆盖。

### Decision 2: bank fade 默认值提高到 0.15
**Context:** `_BankFade=0.02f` 让 `UV.y≈0.04` 已完全不透明，而 waterFade 仍处在黑色区域。

**Decision:** `RiverBottom` / `RiverSurface` / `RiverRenderSettings.BankFade` 默认统一为 `0.15f`。

**Trade-offs:** 岸边透明过渡更宽；这是修正 alpha/fade 时序，不是增加水体亮度。

---

## Code Quality Notes

### Testing
- 新增回归测试：`RiverShadersKeepCk3StyleBankFadeWideEnoughForWaterFadeRamp`。
- 测试锁定：
  - `RiverBottom.sdsl` 默认 `_BankFade = 0.15f`
  - `RiverSurface.sdsl` 默认 `_BankFade = 0.15f`
  - `RiverRenderSettings.BankFade` 默认 `0.15f`
  - 禁止 `RiverSurface` 回退到 `_BankFade = 0.02f`

### Verification
- 已按 Stride shader asset workflow 运行：
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj "/t:_StridePrepareAssetCompiler;StrideAssetUpdateGeneratedFiles" /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCleanAsset /p:Configuration=Debug`
  - `dotnet msbuild Terrain.Editor\Terrain.Editor.csproj /t:StrideCompileAsset /p:Configuration=Debug`
- Asset compile 结果：`907 succeeded, 0 failed`。
- 生成 key 已更新为 `ParameterKeys.NewValue<float>(0.15f)`。
- 自动用 RenderDoc 启动 Editor 截新帧时超时，没有得到 `debug-codex-bankfade.rdc`；需要人工重新截帧做最终 GPU 画面对照。

---

## Next Session

### Immediate Next Steps
1. 重新截 `debug.rdc`，优先检查 event `307` 同类 surface draw，而不是只看 event `271`。
2. 对旧黑点附近像素重新跑 pixel history，期望 surface alpha 不再为 `1` 覆盖 `RGB=(0,0,0)`。
3. 若仍偏暗，再比较 CK3 surface 的 cubemap/specular/fog/shadow 输入，而不是回退到 bottom brightness multiplier。

### Gotchas
- RenderDoc 中 UI 说的 draw 编号和 eventId 可能不一致，本次 `271` 是 eventId。
- PNG preview 对 HDR/alpha-packed RT 不可靠，判断黑色来源必须用 pixel history / debug pixel。
- `get_cbuffer_contents` 在 CK3 capture 上曾返回不可信零值，常量值应优先从 shader trace/disasm 和实际输出反推。

---

## Quick Reference for Future Claude

**Root Cause:**
- `_BankFade=0.02f` 太窄，导致 `waterFade=0` 的黑色近岸区域已经 `edgeFade=1`，surface pass 输出 opaque black。

**Key RenderDoc Events:**
- Current bottom paired with event 271: `182`
- Current surface sample: `271`
- Current visible black contributor: `307`
- CK3 surface reference: `464`

**Key Fix:**
- `RiverBottom.sdsl` / `RiverSurface.sdsl` / `RiverRenderSettings.BankFade` 默认统一为 `0.15f`。

**Manual Verification Still Needed:**
- 新 capture 没有自动截到；下次需要人工提供更新后的 `debug.rdc` 来确认 event `307` 不再输出 `RGB=(0,0,0), A=1`。
