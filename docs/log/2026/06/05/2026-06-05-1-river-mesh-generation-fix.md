# River Mesh 生成修复
**Date**: 2026-06-05
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 诊断并修复 river mesh 生成不正确的问题：wireframe 中河流边缘出现异常三角形/锯齿，连接处不贴合河流语义节点。

**Secondary Objectives:**
- 建立可自动运行的最小回归测试。
- 保留对 Source/Confluence/Bifurcation 语义端点的覆盖。

**Success Criteria:**
- Source → Confluence 的 segment 必须包含两端 special pixel 的中心点。
- T-junction / confluence 每条分支都应包含 confluence 端点。
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` 通过。
- `dotnet build Terrain.sln` 通过。

---

## Context & Background

**Previous Work:**
- Related: [Vic3 河流 RenderDoc 调研归档](../../05/16/2026-05-16-1-vic3-river-renderdoc-research.md)
- Related: [Vic3 道路与河流渲染技术调研](../../../learnings/vic3-road-river-rendering.md)
- Related: [river-system-from-color-map change](../../../../openspec/changes/river-system-from-color-map/design.md)

**Current State:**
- 河流系统已能从 `river.png` 颜色索引解析 `RiverCell[,]`，提取 `RiverSegment`，再生成 Catmull-Rom centerline 与 ribbon mesh。
- 用户截图显示生成出的 river mesh wireframe 不正确，尤其边缘/节点附近出现不自然三角形。

**Why Now:**
- 当前河流 mesh 是后续水面/河底 shader 的几何基础，几何错误会放大为所有视觉 pass 的错误。

---

## What We Did

### 1. 建立最小回归测试入口
**Files Changed:** `Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`, `Terrain.Editor.Tests/Program.cs`, `Terrain.sln`

**Implementation:**
- 新增轻量 console 测试工程 `Terrain.Editor.Tests`，引用 `Terrain.Editor` 和 ImageSharp。
- 用临时 PNG fixture 构造最小河网：
  - Source → River → River → Confluence
  - 三分支 T confluence
- 将测试工程加入 `Terrain.sln`，让 solution build 能覆盖它。

**Rationale:**
- 当前 repo 没有测试项目；river mesh bug 属于数据解析/几何生成 seam，适合用小 PNG fixture 做 deterministic 反馈 loop。

### 2. 修复 segment 端点漏掉 special pixel
**Files Changed:** `Terrain.Editor/Services/RiverMapService.cs`

**Implementation:**
- `TracePath` 现在在开始追踪前先把 `fromX/fromY`（Source/Confluence/Bifurcation special pixel）加入 `seg.Cells`。
- 原逻辑只从 special pixel 的相邻 River pixel 开始加入 cells，导致 centerline 不经过语义节点中心。

**Rationale:**
- Source / Confluence / Bifurcation 不是普通 River 颜色，但它们是路径几何的真实端点。漏掉它们会让 ribbon 在节点前断开，再被 taper 缩窄，wireframe 上表现为不贴合与三角形尖刺。

### 3. 提高 centerline 采样密度
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`

**Implementation:**
- `CurveSampleSpacing` 从 `2.0f` 改为 `1.0f`。

**Rationale:**
- river.png 一个像素映射到 2 world units；原 2.0 的采样间距在像素级转角处过稀，wireframe 中每段 ribbon 较长，视觉上更容易显得锯齿和三角形粗糙。1.0 让 ribbon 转角更细分，改善 wireframe 和后续水面几何质量。

### 4. 修复 review 发现的宽度与方向问题
**Files Changed:** `Terrain.Editor/Services/RiverMapService.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- `ComputeAvgWidth` 只统计 `RiverPixelType.River` 像素，避免 Source/Confluence/Bifurcation 的默认 `Width=0` 拉低短段宽度。
- 新增 `NormalizeDirection`，将 `Confluence -> Source` 的段规范化为 `Source -> Confluence`，保证后续 `TaperStart/TaperEnd` 语义正确。
- 回归测试增加方向断言与宽度平均断言。

**Rationale:**
- 将 special pixel 纳入几何端点后，必须避免它们污染宽度统计；同时 T-junction 从 confluence 出发追到 source 的分支需要规范方向，否则 taper 会反向。

### 5. 对照 CK3 Draw(580) 修正 mesh 组织与拐角 offset
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 用 RenderDoc 打开 `C:\Users\Redwa\Desktop\ck3-river.rdc`，检查 event 460：`Draw(580)` 导出为 `vertexCount=580`、`faceCount=578`，face 顺序为 `(0,1,2)`, `(2,1,3)`, `(2,3,4)`，即 triangle-strip 展开顺序。
- `BuildRibbonMesh` 保持 left/right 交错顶点序列；CK3 导出的 strip winding 不能直接照搬到 Stride 当前 culling 状态，否则整条河会被背面剔除，因此最终使用 Stride 可见 winding：`a,c,b` 与 `b,c,d`。
- 将简单平均 tangent 改为真正 miter offset：`miter = normalize(side(prev)+side(next))`，再用 `halfWidth / dot(miter, side(next))` 做长度校正，并 clamp 到 `2x halfWidth` 防止锐角无限尖刺。
- `BuildCenterlines` 在没有 `TerrainManager` 时 fail-fast；测试仍可直接测不依赖地形高度的 `BuildRibbonMesh`。
- special endpoint 校验恢复为必须至少邻接一个真实 `River` 像素，避免 special-to-special 拓扑导入成功但生成空段。

**Rationale:**
- 用户第二张截图说明核心问题不是单纯端点，而是拐角 ribbon 组织/offset。CK3 的 460 Draw 证明其网格按 strip 边界序列组织；同时真正的 miter 长度校正能避免 90° 弯道被平均 tangent “掐腰”。

### 6. 增加中心线简化去除像素级 stair-step
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`, `Terrain.Editor/Properties/AssemblyInfo.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 在 `BuildCenterlines` 中加入 Ramer-Douglas-Peucker 简化：`rawPoints -> SimplifyCenterline -> CatmullRomInterpolate`。
- 默认容差 `CenterlineSimplificationTolerance = 1.5f` world units，用于去掉 river.png 像素级左右抖动，同时保留端点和大方向拐点。
- 新增 `Terrain.Editor/Properties/AssemblyInfo.cs` 暴露 internal helper 给测试工程。
- 增加 `centerline simplification removes pixel stair steps` 回归测试，确认 stair-step 控制点被压缩且起终点保留。

**Rationale:**
- 用户第三张截图显示 mesh 已可见且 miter/winding 正常，但中心线仍沿像素阶梯抖动。Catmull-Rom 会经过所有控制点，因此必须在插值前先简化像素路径；否则平滑曲线仍会忠实跟随锯齿。

### 7. 增加 Chaikin corner cutting 削圆硬转折
**Files Changed:** `Terrain.Editor/Services/RiverMeshService.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- 在 RDP 简化后加入 `SmoothCenterline(..., CenterlineSmoothingIterations)`，默认 2 轮 Chaikin corner cutting。
- Chaikin smoothing 保留起终点，但会把中间硬角替换成沿相邻边的 25% / 75% 点，不再强制曲线穿过原始角点。
- 增加 `centerline smoothing cuts hard corners` 回归测试，确认硬角控制点被削掉且生成新的圆滑过渡点。

**Rationale:**
- RDP 只能去掉低于容差的小锯齿，但会保留大于容差的角点；要接近 CK3 的平滑河道，需要在插值前对简化后的控制线做 corner cutting。

---

## Decisions Made

### Decision 1: special pixel 必须进入 RiverSegment.Cells
**Context:** `RiverSegment.Cells` 同时被 centerline 与 mesh 生成消费。

**Options Considered:**
1. 保持 cells 只含 River palette 像素，在 centerline 阶段另行拼接端点。
2. 在 `TracePath` 中直接把 special endpoint 纳入 cells。

**Decision:** 选择选项 2。
**Rationale:** segment 的语义是“从节点到节点的一段河流”，端点节点应是几何路径的一部分；在数据提取阶段保证不丢端点比后续修补更清晰。
**Trade-offs:** `ComputeAvgWidth` 需要跳过 special pixel，只统计 River palette 像素；否则短 segment 会被 Source/Confluence/Bifurcation 的默认宽度系统性拉窄。
**Documentation Impact:** 本 session log 记录；无需更新架构概览，系统能力未变化。

### Decision 2: 用 console 测试项目作为当前回归 seam
**Context:** repo 没有 xUnit/NUnit/MSTest 测试项目。

**Options Considered:**
1. 引入完整测试框架。
2. 新增轻量 console 测试项目，失败时设置非零 exit code。

**Decision:** 选择选项 2。
**Rationale:** 最小化依赖和工程改动，能立即覆盖 bug seam，并可被 `dotnet run --project ...` 和 solution build 使用。
**Trade-offs:** 测试报告不如标准测试框架；后续测试增多时应迁移到正式 test SDK。

---

## What Worked ✅

1. **小 PNG fixture 作为反馈 loop**
   - What: 直接生成 5x3 / 5x5 临时 PNG，走真实 `RiverMapService.Load()` 与 `ExtractSegments()`。
   - Why it worked: 覆盖真实颜色解析、validation、segment 提取路径，而不是只测内部 helper。
   - Reusable pattern: Yes

2. **先列可证伪假设再修**
   - What: 将问题分为端点丢失、visited 截断、Catmull-Rom 过冲、tangent 不连续、bounds 剔除等假设。
   - Impact: 快速锁定最可能导致截图中“节点附近不对”的端点丢失问题。

---

## What Didn't Work ❌

1. **没有真实截图自动视觉断言**
   - What we tried: 本次只建立了数据/拓扑级测试。
   - Why it failed: 当前没有稳定 headless Stride viewport/renderdoc 视觉断言流程。
   - Lesson learned: 河流 mesh 这类几何问题可以先用拓扑/端点不变量兜底，但最终仍需要一次人工或截图验证。
   - Don't try this again because: 不能把“测试通过”误认为所有视觉问题都已消除。

---

## Problems Encountered & Solutions

### Problem 1: Segment 不包含 Source/Confluence/Bifurcation 端点
**Symptom:** 生成的 river wireframe 在节点附近不贴合，taper 后形成断裂/尖刺感。

**Root Cause:**
- `ExtractSegments()` 从 special pixel 相邻的 River pixel 调用 `TracePath(nx, ny, sp.X, sp.Y, ...)`。
- `TracePath()` 原来只把 `cx/cy` 加入 `seg.Cells`，没有加入 `fromX/fromY`。
- 结果 centerline 起点不是 source/confluence 中心，而是其旁边第一个 River 像素中心。

**Solution:**
```csharp
var seg = new RiverSegment();
seg.Cells.Add((fromX, fromY));
int cx = startX, cy = startY, px = fromX, py = fromY;
```

**Why This Works:**
- Centerline 的 raw control points 由 `seg.Cells` 映射到 world-space pixel centers。加入 special endpoint 后，ribbon 几何真实覆盖到语义节点中心。

**Pattern for Future:**
- 对从 indexed map 提取的路径数据，special/control 像素虽然不是普通 path palette，也必须作为路径拓扑节点参与几何生成。

---

## Architecture Impact

### Documentation Updates Required
- [x] Create this session log.
- [ ] No update to `docs/ARCHITECTURE_OVERVIEW.md` required: system status unchanged.
- [ ] No ADR required: this is a bug fix within existing river-system design, not a new architecture decision.

### New Patterns/Anti-Patterns Discovered
**New Pattern:** Indexed-map path fixtures for geometry regressions
- When to use: 调试从颜色索引 PNG / mask / map 提取路径或网格的 bug。
- Benefits: fixture 极小、可 deterministic 复现、覆盖真实解析链路。
- Add to: 若后续同类问题重复出现，可沉淀到 `docs/log/learnings/`。

---

## Code Quality Notes

### Testing
- **Tests Written:** 8 个 console 回归测试。
- **Coverage:**
  - Source-to-Confluence segment 包含 semantic endpoints。
  - T confluence 生成三条 Source-to-Confluence 分支，且每条分支以 confluence endpoint 结束。
  - Semantic endpoints 不参与 `AvgHalfWidth` 平均，短段宽度不会被默认 special 宽度拉窄。
  - Special endpoint 必须邻接真实 `River` 像素，避免校验与提取契约不一致。
  - RDP centerline simplification 去除像素级 stair-step 并保留端点。
  - Chaikin smoothing 削掉原始硬角控制点并生成圆滑过渡点。
  - Ribbon index 顺序保留 CK3 strip 组织并使用 Stride 可见 winding。
  - 90° miter corner 对前后两段都保持 half-width。
- **Manual Tests:** 建议在编辑器中重新导入用户当前 river.png，点击 Generate，并用 Wireframe 模式确认节点与边缘不再出现原截图中的异常断裂。

### Verification
- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj` ✅ PASS
- `dotnet build Terrain.sln` ✅ PASS
- 构建仍有既有 NuGet vulnerability warnings 与少量 nullable/unused warnings；本次未处理。

### Technical Debt
- **Created:** 临时采用 console 测试项目而非标准 test framework。
- **Paid Down:** 河流 segment endpoint 不变量现在有自动测试覆盖。
- **TODOs:** 后续可把 `Terrain.Editor.Tests` 迁移到正式 `Microsoft.NET.Test.Sdk` + xUnit/NUnit。

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在真实编辑器 viewport 中重新生成用户的 river map 并截图确认。
2. 若仍有局部三角形交叉，继续验证 Catmull-Rom 过冲/采样策略，尤其像素级连续 90° 转角。
3. 视需要将 CK3 Draw 460 的更多顶点属性（v1/v2/v5/v6）映射到自定义 river vertex 格式，用于 shader fade/flow。

### Questions to Resolve
1. Catmull-Rom 是否需要 centripetal 参数化或过冲 clamp？这会影响大角度转弯的视觉质量。
2. 是否要把当前 console 测试项目迁移到正式测试框架？这会影响 CI/IDE test runner 集成。

### Docs to Read Before Next Session
- [river-system-from-color-map design](../../../../openspec/changes/river-system-from-color-map/design.md)
- [Vic3 河流 RenderDoc 调研归档](../../05/16/2026-05-16-1-vic3-river-renderdoc-research.md)

---

## Session Statistics

**Files Changed:** 5
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Root cause: `TracePath` previously dropped the starting semantic endpoint from `RiverSegment.Cells`.
- Fix: `RiverMapService.TracePath` now prepends `(fromX, fromY)`.
- Test command: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`.
- Full build: `dotnet build Terrain.sln`.

**What Changed Since Last Doc Read:**
- Added `Terrain.Editor.Tests` console regression project and included it in `Terrain.sln`.
- Increased `RiverMeshService.CurveSampleSpacing` density from 2.0 to 1.0 world unit.
- `ComputeAvgWidth` now ignores special endpoints; `NormalizeDirection` keeps confluence branches oriented Source → Confluence where applicable.
- `BuildRibbonMesh` now follows CK3 Draw 460 triangle-strip winding expanded to TriangleList and uses miter length correction at corners.

**Gotchas for Next Session:**
- Build warnings about NuGet vulnerabilities are pre-existing from transitive packages; do not confuse them with this fix.
- Passing topology tests does not replace manual visual verification in Stride viewport.
- If visual artifacts remain, next likely hypotheses are Catmull-Rom overshoot and ribbon tangent/miter handling.

---
