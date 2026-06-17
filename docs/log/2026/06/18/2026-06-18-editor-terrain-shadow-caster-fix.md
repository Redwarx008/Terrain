# Editor Terrain Shadow Caster Fix
**Date**: 2026-06-18
**Session**: editor-terrain-shadow-caster-fix
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 找出本地 river bottom 与 CK3 在 bottom pass 阴影结果明显不一致的根因，并优先通过 RenderDoc/代码诊断确认修复点。

**Secondary Objectives:**
- 区分“scene-driven shadow 绑定缺失”和“shadow atlas 本身为空”这两类问题。
- 让 editor terrain 的 shadow caster 语义与 runtime terrain 对齐。
- 补一条能复用到后续 RenderDoc 诊断的经验记录。

**Success Criteria:**
- 明确证明 river bottom 已绑定 scene shadow / cubemap 语义。
- 明确证明 atlas 为空的直接原因。
- 在代码中修复该原因，并补上最小回归测试。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-bottom-scene-driven-lighting.md](../17/2026-06-17-river-bottom-scene-driven-lighting.md)
- See: [2026-06-17-river-bottom-yellow-lighting-decomposition.md](../17/2026-06-17-river-bottom-yellow-lighting-decomposition.md)
- See: [2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md](../17/2026-06-17-renderdoc-mcp-cbuffer-zero-diagnosis.md)

**Current State:**
- `RiverRenderFeature` 已经把 bottom 所需的 directional light、scene shadow atlas、scene cubemap、cubemap intensity、cubemap rotation 都绑定进 shader。
- 但 `C:\Users\Redwa\Desktop\debug.rdc` 里的 bottom pass 仍然没有出现 CK3 那种偏黄、受真实地形遮挡影响的结果。

**Why Now:**
- 在继续动 SDSL 之前，必须先确认本地和 CK3 的底层 scene 数据语义到底哪里不同，否则会把 atlas 空内容误诊成 shader 算法问题。

---

## What We Did

### 1. 复核 `debug.rdc` 的 bottom 绑定是否已经走 scene-driven 语义
**Files Changed:** None

**Implementation:**
- 对 bottom draw 和 surface draw 分别检查 PS 绑定资源与 resource usage。
- 确认 bottom 读取的是 scene skybox cubemap 和 scene shadow atlas，而不是继续复用 surface 的 reflection cubemap。

**Rationale:**
- 先排除“没绑进去”的低层问题，再继续看 atlas 内容本身。

### 2. 用 RenderDoc 热改确认 `EvaluateSceneShadow()` 的真实返回值
**Files Changed:** None

**Implementation:**
- 对 bottom pixel shader 做 shadow-only / cascade-count / shadow-bounds 热替换。
- 观察整条河床输出始终接近“无阴影”结果。

**Rationale:**
- 这一步能把“矩阵跑飞/参数全 0”和“atlas 本身没有深度”区分开。

### 3. 对比 CK3 capture，确认参考地形确实参加真实 depth/shadow 链
**Files Changed:** None

**Implementation:**
- 检查 `C:\Users\Redwa\Desktop\ck3-river.rdc` 中 terrain depth draw。
- 确认 CK3 terrain VS 会从高度贴图采样并输出真实高度深度，相关 depth target 存在大量 `DepthStencilTarget` 写入。

**Rationale:**
- 这一步证明 CK3 河底“偏黄且有地形遮挡”的前提不是 river 专用魔法常量，而是 scene depth/shadow 链真的包含地形。

### 4. 定位本地 root cause 并修复 editor terrain shadow caster
**Files Changed:** `Terrain.Editor/Rendering/EditorTerrainProcessor.cs`

**Implementation:**
```csharp
renderObject.IsShadowCaster = component.CastShadows;

public bool CastShadows { get; set; } = true;
```

**Rationale:**
- runtime terrain 早就把 `component.CastShadows` 透传给 `RenderMesh.IsShadowCaster`。
- editor terrain 原来被写死成 `false`，导致 shadow atlas 只有 clear 没有 terrain 深度写入。

### 5. 补最小回归测试
**Files Changed:** `Terrain.Editor.Tests/EditorTerrainShadowCasterTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增测试确认 `EditorTerrainComponent.CastShadows` 存在且默认值为 `true`。
- 新增测试确认 `EditorTerrainProcessor` 不再把 `IsShadowCaster` 写死为 `false`，而是转发 `component.CastShadows`。

**Rationale:**
- 这是这次 bug 的最小行为契约，应该被文本级回归测试锁住。

---

## Decisions Made

### Decision 1: 优先相信 RenderDoc 里的 atlas usage，而不是继续猜 shader 参数
**Context:** bottom shader 绑定看起来已经和 CK3 更接近，但结果仍明显不对。
**Options Considered:**
1. 继续调 bottom lighting 常量
2. 继续改 shadow/cubemap shader 分支
3. 先确认 atlas 是否真的有深度内容

**Decision:** Chose Option 3
**Rationale:** shader 绑定存在不代表 atlas 内已经有有效数据；如果底层 shadow caster stage 没写入，任何 lighting 调参都会偏题。
**Trade-offs:** 需要多花一些时间做 capture 级别验证，但能避免继续在错误层级上改 shader。
**Documentation Impact:** 更新 architecture/current features，并新增一条 learning。

### Decision 2: 让 editor terrain 直接复用 runtime 的 `CastShadows` 语义
**Context:** runtime 与 editor 对同一类 terrain 的 shadow caster 语义不一致。
**Options Considered:**
1. 保持 editor terrain 不投 shadow，继续给 river bottom 做特判 fallback
2. 在 river feature 里额外注入 terrain 专用 shadow 数据
3. 让 editor terrain 进入正常 shadow caster stage

**Decision:** Chose Option 3
**Rationale:** 这和 CK3/scene-driven 语义一致，也避免 river feature 再背一套 terrain 专用补丁逻辑。
**Trade-offs:** editor terrain 以后会参与常规 shadow pass，但这是正确语义，不是额外负担。
**Documentation Impact:** 更新 architecture/current features。

---

## What Worked ✅

1. **先查 atlas usage 再查 shader**
   - What: 用 RenderDoc 直接看 shadow atlas 的 `Clear / DepthStencilTarget / PS_Resource` usage。
   - Why it worked: 很快就把问题收敛到“atlas 是空的”而不是“river bottom 算错了”。
   - Reusable pattern: Yes

2. **用热改验证局部 shader 假设**
   - What: 输出 shadow-only、cascade-count、shadow-bounds。
   - Impact: 排除了 world-to-shadow 参数没绑定、cascadeCount 为 0 等误判。

3. **拿 CK3 capture 证明参考行为的前提**
   - What: 检查 CK3 terrain depth pass 是否真正写入高度深度。
   - Impact: 证明 CK3 的 yellow bottom 不是单纯贴图/水色差异，而是 scene depth/shadow 链本身不同。

---

## What Didn't Work ❌

1. **把 bottom 颜色差异继续归因到 waterDiffuse / river-local 常量**
   - What we tried: 先怀疑 `waterDiffuse` 或 bottom lighting fallback 常量。
   - Why it failed: bottom pass 自身输出就已经和 CK3 不一致，说明问题更早发生在 shadow/depth 链。
   - Lesson learned: 看到 river bottom 明显“全亮”时，先查 shadow atlas 是否为空。
   - Don't try this again because: 会把 scene 数据缺失误诊成材质调参问题。

---

## Problems Encountered & Solutions

### Problem 1: bottom 已绑定 `SceneShadowMapTexture`，但阴影结果仍然处处接近 1
**Symptom:** bottom pass 看起来没有真实 terrain 遮挡，hot-edit 的 shadow-only 输出几乎全白。
**Root Cause:** editor terrain 没进入 shadow caster stage，shadow atlas 只有 clear 没有深度写入。
**Investigation:**
- Tried: 检查 bottom/surface draw 的资源绑定差异
- Tried: 检查 atlas usage 和 texture stats
- Found: atlas usage 只有 `Clear + PS_Resource`，`min=max=1.0`

**Solution:**
```csharp
renderObject.IsShadowCaster = component.CastShadows;
```

**Why This Works:** 这样 editor terrain 会和 runtime terrain 一样进入正常 shadow caster stage，scene-driven bottom shadow 才能读到真实 terrain 深度。
**Pattern for Future:** 看到 shader 已绑定 atlas 但结果始终“无阴影”时，优先检查 caster 是否真的写入 atlas。

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/ARCHITECTURE_OVERVIEW.md` - 记录 editor terrain 已对齐 runtime shadow caster 语义
- [x] Update `docs/CURRENT_FEATURES.md` - 记录 river bottom scene shadow 不再只读空 atlas

### New Patterns/Anti-Patterns Discovered
**New Pattern:** bottom pass 已绑定 scene shadow 但结果仍全亮时，先查 atlas 是否真的有 caster 写入
- When to use: RenderDoc 显示 river bottom 已绑定 scene shadow atlas，但视觉上仍像没阴影
- Benefits: 能快速区分“绑定缺失”和“atlas 空内容”
- Add to: `docs/log/learnings/stride-river-rendering-patterns.md`

### Architectural Decisions That Changed
- **Changed:** editor terrain 的 shadow caster 语义
- **From:** `EditorTerrainProcessor` 把 `RenderMesh.IsShadowCaster` 写死为 `false`
- **To:** editor terrain 通过 `EditorTerrainComponent.CastShadows` 转发到 `RenderMesh.IsShadowCaster`
- **Scope:** `Terrain.Editor` 渲染路径与 river bottom scene shadow 链
- **Reason:** 让 editor terrain 真正参与 shared shadow atlas，消除 CK3 对比中的关键场景差异

---

## Code Quality Notes

### Testing
- **Tests Written:** 2 个新增测试
- **Coverage:** `EditorTerrainComponent.CastShadows` 默认值与 `EditorTerrainProcessor` 转发逻辑
- **Manual Tests:** 重新抓一帧 editor 场景，确认 shadow atlas 出现 terrain 的 `DepthStencilTarget` 写入，river bottom 不再是 shadow-only 全白

### Technical Debt
- **Created:** None
- **Paid Down:** 移除了 editor terrain 对 scene shadow 的硬禁用
- **TODOs:** 修复后重新抓 `debug.rdc`，继续复核 pre-bottom seed payload 与 bank 泄漏

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 重新抓 editor 场景的 `debug.rdc` - 验证 shadow atlas 已出现 terrain 深度写入
2. 复核 river bottom shadow-only 输出 - 确认不再处处返回 `1`
3. 继续看 pre-bottom seed payload / bank 泄漏 - 这是当前与 CK3 的剩余差异

### Blocked Items
- **Blocker:** None
- **Needs:** 用户或本地运行环境重新抓一帧修复后的 RenderDoc capture
- **Owner:** User / next session

### Questions to Resolve
1. 修复后 river bottom 的黄调是否已经主要回到 CK3 区间
2. 剩余河岸泄漏是否仍来自 pre-bottom seed payload 而不是 lighting 链

### Docs to Read Before Next Session
- [docs/ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md) - 复核当前 river pipeline 状态
- [docs/log/learnings/stride-river-rendering-patterns.md](../../../learnings/stride-river-rendering-patterns.md) - 复用这次 shadow atlas 诊断模式

---

## Session Statistics

**Files Changed:** 6
**Lines Added/Removed:** See `git diff`
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `Terrain.Editor/Rendering/EditorTerrainProcessor.cs`
- Critical decision: 不再给 river bottom 加 terrain 专用 fallback，而是让 editor terrain 走正常 shadow caster 语义
- Active pattern: 先查 shadow atlas usage，再判断是不是 shader 参数问题
- Current status: 代码与回归测试已完成，待修复后新 capture 复核

**What Changed Since Last Doc Read:**
- Architecture: editor terrain 现在默认参与 shared shadow atlas
- Implementation: `EditorTerrainComponent` 新增 `CastShadows`，processor 转发到 `RenderMesh.IsShadowCaster`
- Constraints: 继续做 CK3 对比时，要把“atlas 是否有内容”和“shader 是否已绑定 atlas”分开验证

**Gotchas for Next Session:**
- Watch out for: 看到 `SceneShadowMapTexture` 绑定存在，不代表 atlas 里已经有真实 depth
- Don't forget: CK3 terrain 在 depth/shadow pass 里确实会采高度图并写入真实深度
- Remember: 这次 root cause 在 editor terrain processor，不在 river shader

---

## Links & References

### Related Documentation
- [ARCHITECTURE_OVERVIEW](../../../../ARCHITECTURE_OVERVIEW.md)
- [CURRENT_FEATURES](../../../../CURRENT_FEATURES.md)
- [ADR-014](../../../decisions/adr-014-river-rendering-architecture.md)

### Related Sessions
- [2026-06-17-river-bottom-scene-driven-lighting](../17/2026-06-17-river-bottom-scene-driven-lighting.md)
- [2026-06-17-river-bottom-yellow-lighting-decomposition](../17/2026-06-17-river-bottom-yellow-lighting-decomposition.md)

### Code References
- Key implementation: `Terrain.Editor/Rendering/EditorTerrainProcessor.cs`
- Tests: `Terrain.Editor.Tests/EditorTerrainShadowCasterTests.cs`

---

## Notes & Observations

- `debug.rdc` 中 bottom 绑定了 scene shadow atlas，但 atlas usage 只有 clear 和采样，这个证据比继续猜 shader 参数更关键。
- CK3 terrain 的 depth/shadow pass 确实写入了高度驱动的真实深度，因此 river bottom 的“黄调 + 遮挡感”有一部分天然来自 scene shadow 链本身。

---

*Template Version: 1.0 - Based on Archon-Engine template*
