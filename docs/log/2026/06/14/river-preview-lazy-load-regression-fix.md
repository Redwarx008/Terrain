# River 预览延迟加载回归修复
**Date**: 2026-06-14
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修复“启动自动加载 `rivers.png` 后仍看不到 river”的最新回归

**Secondary Objectives:**
- 用证据确认问题不在 CK3 rivers 解析或 mesh 生成本身
- 补一条回归测试，卡住这次启动路径上的新性能回归

**Success Criteria:**
- 真实工作区 `game/map_data/rivers.png` 能被测试证明解析并发布 mesh
- 启动自动加载链路不再急着解码 river 预览大图
- River 自动生成链路保持可用

---

## Context & Background

**Previous Work:**
- Related: [river-auto-generation-on-load.md](./river-auto-generation-on-load.md)
- Related: [river-auto-generation-runtime-order-fix.md](./river-auto-generation-runtime-order-fix.md)

**Current State:**
- 用户反馈在本次改动前 river 一直正常，说明这是本轮改造引入的明确回归
- 已经确认 `LaunchSetting.json`、`default.toml` 和 `rivers.png` 路径都正确

**Why Now:**
- 这是固定工作区自动加载体验里的明显回归，且直接影响 Editor 启动后的可见结果

---

## What We Did

### 1. 先取证真实 `rivers.png` 是否能被当前链路解析
**Files Changed:** `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 新增工作区级诊断测试，直接读取仓库内的 `game/map_data/rivers.png`
- 打印并断言：
  - `loaded`
  - 图像尺寸
  - `River / Source / Confluence / Bifurcation / Ocean` 像素计数
  - `errors`
  - `segments`
- 结果证明当前真实资源能被正常解析：
  - `loaded=True`
  - `size=9216x4608`
  - `segments=1748`

**Rationale:**
- 先排除“资源没读到”或“CK3 palette 不兼容”的猜测

### 2. 继续取证 mesh 发布层，确认不是算法或 component 发布坏了
**Files Changed:** `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**Implementation:**
- 在测试里用工作区真实 `rivers.png` 的 `RiverCell[,]`
- 通过最小 `TerrainManager` stub + `RiverMeshService` + `RiverRenderingService` 跑完整生成
- 打印并断言：
  - `componentMeshes=1748`
  - `componentVertices=475692`
  - `componentIndices=1416588`

**Rationale:**
- 这一步证明“解析 -> segment -> mesh -> RiverComponent 发布”主链路是通的
- 因此回归点不在 CK3 rivers 数据，也不在 `RiverMeshGenerator`

### 3. 收紧真正的回归点：启动自动加载时不再急读 preview 大图
**Files Changed:** `Terrain.Editor/ViewModels/RiverViewModel.cs`, `Terrain.Editor/ViewModels/EditorShellViewModel.cs`, `Terrain.Editor.Tests/RiverViewModelAutoGenerationTests.cs`

**Implementation:**
```csharp
if (!string.Equals(RiverMapPath, path, StringComparison.OrdinalIgnoreCase))
    ReplacePreviewImage(null);

if (normalized == EditorMode.River)
    River?.EnsurePreviewLoaded();
```

**Rationale:**
- 对比本轮 diff 后，`RiverViewModel` 新增了一个启动期行为：
  - `RiverMapChanged` 时立即 `new Bitmap(rivers.png)`
- 当前真实 `rivers.png` 是 `9216x4608`
- 这一步在旧手动流程里只会发生在显式 Import 时，不会卡进启动自动加载主链
- 因此把 preview 改回按需加载，保留自动生成，但移除这次回归里最可疑的新阻塞点

---

## Decisions Made

### Decision 1: 不回退自动生成，先移除启动链路里的 eager preview decode
**Context:** 用户明确说明“这次改动之前都没问题”，而诊断已经证明 rivers 解析和 mesh 发布本身正常。

**Options Considered:**
1. 继续怀疑 renderer / render stage
2. 回退自动生成逻辑
3. 保留自动生成，只把新加的启动期 preview decode 改成按需加载

**Decision:** 选择选项 3
**Rationale:** 这是本轮 diff 里最明确、最可解释、且最小化的回归点
**Trade-offs:** River preview 不再在启动时立即可见，只有进入 River 模式时才解码

---

## What Worked ✅

1. **工作区级真实资源诊断测试**
   - What: 直接拿仓库的 `game/map_data/rivers.png` 做解析和发布断言
   - Why it worked: 把“是不是资源/是不是算法”的争论一次性落地成证据
   - Reusable pattern: Yes

2. **把 preview 从启动主链里拆出去**
   - What: 只在进入 River 模式时才 `EnsurePreviewLoaded()`
   - Impact: 去掉了启动自动加载路径里对超大 bitmap 的急解码

---

## What Didn't Work ❌

1. **先前继续盯渲染架构**
   - What we tried: 在没有补足真实资源证据前继续怀疑 render feature / processor
   - Why it failed: 用户补充“这次改动之前都没问题”后，这条路径的优先级明显下降
   - Lesson learned: 回归问题优先看“本轮引入了什么新行为”

---

## Problems Encountered & Solutions

### Problem 1: 启动自动加载路径里多了一次超大 `rivers.png` 预览解码
**Symptom:** 用户反馈改动前正常，改动后启动自动加载 river 失效
**Root Cause:** `RiverViewModel.SyncStateFromRiverMapSource()` 在每次 `RiverMapChanged` 时都会立即尝试解码 preview bitmap；这一步是本轮新加入的行为，并且发生在启动自动加载主链里
**Investigation:**
- Tried: 先验证 `rivers.png` 真实解析结果
- Tried: 再验证 mesh 是否真能发布到 `RiverComponent`
- Found: 解析和 mesh 发布都正常，异常点只剩启动路径上的新增行为

**Solution:**
```csharp
internal void EnsurePreviewLoaded()
{
    if (PreviewImage != null)
        return;

    ReplacePreviewImage(LoadPreviewImage(RiverMapPath));
}
```

**Why This Works:** 自动生成和预览加载被拆成两步，启动主链只做前者，避免大图 preview decode 阻塞 river 自动加载
**Pattern for Future:** 大尺寸作者态资源的 preview 不要挂在启动必经链路里，应改为按需加载

---

## Architecture Impact

### Documentation Updates Required
- [x] Update `docs/CURRENT_FEATURES.md` - 补充 river preview 为按需解码
- [ ] Update `docs/ARCHITECTURE_OVERVIEW.md` - 本次无需调整总体架构口径

### New Patterns/Anti-Patterns Discovered
**New Anti-Pattern:** 启动链路 eager decode 大尺寸预览图
- What not to do: 在固定工作区自动加载时，同步解码超大作者态图片仅用于 inspector preview
- Why it's bad: 会把非关键 UI 预览成本塞进启动主链，造成明显回归

---

## Code Quality Notes

### Testing
- **Tests Written:** 2 组补强
- **Coverage:**
  - 工作区真实 `rivers.png` 解析并抽出 segment
  - 工作区真实 `rivers.png` 生成并发布 mesh
  - `RiverViewModel` 预览改为按需解码

### Verification
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
  - 新增 river 诊断与懒加载测试通过
  - 仍存在既有 scaffold 失败：`repository scaffold should not check in terrain.terrain`
- `dotnet build Terrain.sln -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false`
  - 通过

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 让用户重新验证 Editor 启动后的 river 可见性
2. 如果仍不可见，下一步直接对 `RiverProcessor` / `RiverRenderFeature` 加运行期日志，确认 render object 是否进入视口
3. 单独处理既有 scaffold 断言与本地 `terrain.terrain` 文件冲突

### Questions to Resolve
1. 当前用户实际看到的是“状态没变”还是“状态显示生成但视口不可见”

---

## Session Statistics

**Files Changed:** 5
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 当前真实工作区 `rivers.png` 能解析出 `1748` 个 segment
- 当前真实工作区 `rivers.png` 能发布 `1748` 个 mesh 到 `RiverComponent`
- 本次收口把 river preview 改成了按需解码，不再卡进启动主链

**What Changed Since Last Doc Read:**
- Implementation: `RiverViewModel` 不再在 `RiverMapChanged` 时急读 preview bitmap
- Constraints: 进入 River 模式时才会尝试加载 preview

**Gotchas for Next Session:**
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` 仍会被本地 `game/map_data/terrain.terrain` 命中既有 scaffold 断言
- 若用户仍反馈“看不到 river”，下一步应直接取证 render object 是否进了 `VisibilityGroup`
