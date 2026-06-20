# 河流 bottom `debug.rdc` parity 复核
**Date**: 2026-06-19
**Session**: bottom `debug.rdc` parity recheck
**Status**: ⚠️ Partial
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 回答用户关于 `C:\Users\Redwa\Desktop\debug.rdc` 的 bottom 问题：为什么 CK3 看起来更黄，而本地更棕黑；确认 shader 和输入参数是否已经与 CK3 一致。

**Success Criteria:**
- 重新复核 current `debug.rdc` 的 bottom draw、资源和输出趋势。
- 明确哪些 bottom 语义已经对齐，哪些仍不等价。
- 把本轮代码/测试状态整理清楚，避免下一轮重复排查。

---

## Context & Background

**Previous Work:**
- `docs/log/2026/06/17/2026-06-17-river-bottom-yellow-lighting-decomposition.md`
- `docs/log/2026/06/18/2026-06-18-river-bottom-scene-lighting-renderdoc-recheck.md`
- `docs/log/2026/06/19/2026-06-19-river-debug-still-black-hotedit.md`

**Current State:**
- 用户继续聚焦 bottom，并要求优先解释 CK3 黄褐色来源、当前棕黑来源，以及 shader / 参数是否真正与 CK3 一样。
- `renderdoc-mcp` 在本轮仍然 `Transport closed`，只能继续使用本地 `renderdoc-cli.exe` 和既有 RenderDoc 证据链。

**Why Now:**
- 前一轮 surface 排查容易把问题重心带偏；需要把 bottom 本身的对齐状态重新讲清楚。

---

## What We Did

### 1. 重新核对当前代码与测试状态
**Files Changed:**
- `Terrain.Editor.Tests/RiverShaderTextTests.cs`

**Implementation:**
- 修正 `BottomLightingInputsComeFromSceneBindings()` 的一条误伤断言。
- 旧断言直接禁止 `RiverRenderFeature.cs` 中出现 `riverResources.ReflectionSpecular`，把 surface 正常资源绑定也误判成 bottom fallback。
- 新断言改为只禁止已删除的 bottom fallback 代码片段：`bottomEnvironment ??= riverResources.ReflectionSpecular`。

**Rationale:**
- 实现本身已经删除 bottom fallback；失败的是测试粒度，不是功能行为。

### 2. 重新验证 build / test
**Files Changed:** none

**Findings:**
- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug` 通过。
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-build` 通过。
- 当前保留的只有既有 NuGet vulnerability warning、少量既有 C# warning。

### 3. 复核 `debug.rdc` 与本地 build 时间
**Files Changed:** none

**Findings:**
- `C:\Users\Redwa\Desktop\debug.rdc` 修改时间：`2026-06-19 02:24:33`
- `E:\Stride Projects\Terrain\Bin\Editor\Debug\win-x64\Terrain.Editor.exe` 修改时间：`2026-06-19 02:49:10`
- 当前 capture 早于当前本地 exe 大约 25 分钟。

**Rationale:**
- 这意味着本轮代码仓库状态与该 capture 不是完全同一 build，结论必须明确区分“capture 中看到的 GPU 状态”和“当前源码状态”。

### 4. 用 `renderdoc-cli` 复核 bottom 输出和资源证据
**Files Changed:** none

**Findings:**
- current `debug.rdc` draws 仍包含：
  - bottom: `EID 274`
  - surface: `EID 312`
- bottom RT `ResourceId::7814` 在 `EID 274` 的统计：
  - `Min: [0.0124664, 0.0150299, 0.0161285]`
  - `Max: [1.26367, 1.17969, 1.06055]`
- CK3 bottom RT `ResourceId::49006` 在 `EID 338` 的统计：
  - `Min: [0.0068779, 0.0073204, 0.00388718]`
  - `Max: [1.32031, 1.27637, 1.51562]`
- 代表像素复核仍符合上一轮趋势：
  - current `debug.rdc` bottom hit 点有不少在 `~[0.19..0.21, 0.14..0.16, 0.09..0.10]`
  - CK3 对位点约 `~[0.19..0.21, 0.12..0.14, 0.07..0.08]`
  - current 的蓝通道系统性偏高，因此更冷、更灰。

**Rationale:**
- bottom 不是“整张 pass 纯黑”；更准确地说，是 current bottom 整体更冷，且局部暗点存在，但 CK3 bottom 自身也有很低的 min。

### 5. 重新确认资源是否拿错
**Files Changed:** none

**Findings:**
- 本地 bottom 贴图与 CK3 `gfx/map/rivers` 资源 hash 仍一致：
  - `bottom-diffuse.dds`
  - `bottom-normal.dds`
  - `bottom-properties.dds`
- 本地 scene environment `Terrain.Editor/Assets/Scene/Environment/jomini-environment-terrain-sunny.dds` 与 CK3
  `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\environment\environment_terrain_sunny.dds`
  的 SHA256 也一致。

**Rationale:**
- “CK3 黄、本地棕黑”不是因为 bottom 贴图或 environment 源 DDS 拿错了。

---

## Decisions Made

### Decision 1: 当前 bottom 不应再被描述成“整张 pass 输出黑掉”
**Context:** 用户直觉上看到 bottom 很黑，但 current/CK3 的 bottom RT 都存在低值像素。
**Decision:** 用 bottom RT stats 和代表像素解释真实问题是“current 更冷、更灰，局部暗点存在”，而不是整张 bottom 都是 0。
**Rationale:** 这样才能把修复方向继续收敛到 lighting balance / scene input / surface 后段，而不是继续做全局提亮。
**Trade-offs:** 需要接受“视觉更黑”不一定等于“bottom pass 本身更黑”。

### Decision 2: 仍然维持“bottom shader 还不是 100% CK3 等价”的结论
**Context:** 资源链已经基本对齐，但 current bottom 语义仍和 CK3 有一个关键缺口。
**Decision:** 继续把 bottom shadow 路径视为未完成移植。
**Rationale:** CK3 bottom capture 绑定真实 `ShadowTexture_Texture` 并走目标 shadow 投影 / kernel / bias / fade；本地 `RiverBottom.sdsl` 目前仍是 `float shadow = 1.0f;`，这不是 100% 语义等价。
**Trade-offs:** 当前 direct light 不会被非等价 Stride cascade helper 压黑，但也还没回到 CK3 真正的 shadow 语义。

---

## What Worked ✅

1. **先修测试再继续诊断**
   - What: 把误伤的文本断言改成检查已删除的具体 fallback 代码。
   - Why it worked: 让测试重新反映真实实现状态。
   - Reusable pattern: Yes

2. **用 RT stats 纠正“整张 bottom 黑掉”的描述**
   - What: 直接对 current / CK3 bottom RT 跑 `tex-stats`。
   - Why it worked: 快速排除“整张输出接近 0”的误判。
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **本轮继续依赖 `renderdoc-mcp`**
   - What we tried: 通过 MCP 重新 `open_capture`
   - Why it failed: 仍然 `Transport closed`
   - Lesson learned: 当前 session 下不能把 MCP 作为稳定工具依赖
   - Don't try this again because: 同样的失败不会提供新信息

2. **尝试在 CLI 上继续走 runtime shader replace**
   - What we tried: 使用 `shader-build` / `shader-replace` 路径复用旧 hot-edit
   - Why it failed: 现有 CLI 是分步单进程接口，本轮没有稳定的持久替换会话
   - Lesson learned: 没有可用 MCP session 时，当前这套 CLI 不能像之前的定制 hot-edit 一样直接复用
   - Don't try this again because: 会继续卡在工具会话保持问题上

---

## Architecture Impact

### Documentation Updates Required
- [x] 新增本会话日志
- [ ] 更新 `ARCHITECTURE_OVERVIEW.md` - 无系统状态变化，本轮不需要
- [ ] 更新 `CURRENT_FEATURES.md` - 无功能状态变化，本轮不需要

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 先用更具体的文本断言表达“禁止 bottom fallback”
- When to use: 需要同时允许 surface 使用某资源、但禁止 bottom fallback 误用同一资源时
- Benefits: 避免把整个文件中合法的 surface 绑定误判为 bottom 问题
- Add to: 暂不单独沉淀

---

## Testing

- `dotnet build Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug`
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj -c Debug --no-build`

**Result:**
- 全部通过

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新抓一份晚于当前 `Terrain.Editor.exe` 的新 `debug.rdc`
2. 继续把 CK3 bottom shadow 路径从 capture / loose shader 精确移植进 `RiverBottom.sdsl`
3. 抓新帧后再做一次 bottom `direct / diffuse IBL / specular IBL` 分解，确认 current 是否仍然是冷 IBL 偏重

### Blocked Items
- **Blocker:** `renderdoc-mcp` 当前 transport 不稳定
- **Needs:** 可重连的 MCP session，或重新恢复之前的定制 hot-edit CLI 会话
- **Owner:** 调试工具环境

### Questions to Resolve
1. 当前 capture 里的 scene cubemap 在 Stride runtime prefilter 后，与 CK3 runtime environment resource 是否仍有剩余语义差异？
2. bottom shadow 正式移植后，current 与 CK3 的暖黄差异还会剩多少？

### Docs to Read Before Next Session
- `docs/log/2026/06/17/2026-06-17-river-bottom-yellow-lighting-decomposition.md`
- `docs/log/2026/06/18/2026-06-18-river-bottom-scene-lighting-renderdoc-recheck.md`
- `docs/log/2026/06/19/2026-06-19-river-debug-still-black-hotedit.md`

---

## Session Statistics

**Files Changed:** 2
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 本轮真正改的代码只有一条文本测试断言；runtime 功能实现没有新增改动。
- current `debug.rdc` 的 bottom 不能简单描述成“整张 pass 黑掉”；它更接近“整体偏冷、蓝通道偏高，局部暗点存在”。
- bottom 资源链基本已对齐 CK3，但 shader 语义仍不是 100% CK3，因为正式 shadow 路径还没移植回来。

**What Changed Since Last Doc Read:**
- Implementation: `RiverShaderTextTests` 的 bottom fallback 断言已收紧到具体代码片段。
- Constraints: `renderdoc-mcp` 在本轮依然不可用。

**Gotchas for Next Session:**
- Watch out for: `debug.rdc` 时间戳早于当前本地 exe，别把它当成“当前源码 build 的最终证据”。
- Don't forget: surface 仍然会继续压暗 bottom/refraction，不能只盯着 bottom 单独看最终画面。
- Remember: 资源 hash 对齐不等于最终 lighting 语义已经对齐。
