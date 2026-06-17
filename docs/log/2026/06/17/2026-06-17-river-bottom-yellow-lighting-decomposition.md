# River Bottom Yellow Lighting Decomposition
**Date**: 2026-06-17
**Session**: River bottom yellow lighting decomposition
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 回答用户的精确问题：为什么 `debug1.rdc` 的 `RiverBottom` 输出不是 CK3 那种黄褐色，而 current 是灰青/灰绿。

**Secondary Objectives:**
- 在不先改仓库 SDSL 的前提下，用 RenderDoc hot-edit 把 bottom pass 拆成 `albedo / directDiffuse / IBL` 贡献。
- 判断“资产相同、shader 代码相同”为什么仍会出现巨大视觉差异。

**Success Criteria:**
- 至少拿到 `event 184` river center / bank 的 `albedo-only`、`directDiffuse-only`、`IBL-only` 对照值。
- 给出 current 与 CK3 bottom 颜色差异的具体贡献项归因。

---

## Context & Background

**Previous Work:**
- See: [2026-06-17-river-debug1-post-bottom-validation.md](./2026-06-17-river-debug1-post-bottom-validation.md)
- See: [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)
- Related: [adr-014-river-rendering-architecture.md](../../decisions/adr-014-river-rendering-architecture.md)

**Current State:**
- `debug1.rdc` 已确认命中了新的 `RiverBottom` world-UV 主采样路径。
- 用户已明确纠正：问题焦点不是 `waterDiffuse`，而是 `event 184` bottom pass 自己就和 CK3 不一样。

**Why Now:**
- 需要在真正修改 `RiverBottom.sdsl` 或 CPU 绑定前，把“黄褐色到底被哪一段 lighting 拉没了”说清楚。

---

## What We Did

### 1. 放弃独立 helper，改成扩展一次进程 `renderdoc-cli`
**Files Changed:** 仓库内无；调试工具临时修改 `C:\Users\Redwa\.codex\vendor_imports\renderdoc-mcp\src\cli\main.cpp`

**Implementation:**
- 先尝试自建 `renderdoc_hotedit_helper.exe`，但它稳定崩在 `Session::open()`。
- 随后给已有、可正常 `open_capture` 的 `renderdoc-cli.exe` 增加了原始命令：
  - `hotedit-pick <capture> <shader.hlsl> <replaceEvent> <event x y> ...`
- 重新构建 `renderdoc-cli` 后，成功在同一进程里完成：
  - `buildShader`
  - `replaceShader`
  - `pickPixel before/after`

**Rationale:**
- `renderdoc-cli` 本身已经证明 replay 初始化稳定；把 hot-edit 合并进它，比继续追 helper 的 ABI / 初始化差异更直接。

### 2. 用 `albedo-only` 证明底图资源本身是暖色，不是灰色
**Files Changed:** 仓库内无；临时 HLSL：`C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_albedo_only_clean.hlsl`

**Implementation:**
- 在 `debug1.rdc` 的 `event 184` 上，将：
  - `float3 color = CalculateRiverBottomLighting(...) * 3.0f;`
  改为：
  - `float3 color = bottomDiffuse.rgb;`
- 代表像素结果：
  - center `184 (420,240)`：`[0.0450, 0.0307, 0.0152]`
  - bank `184 (310,150)`：`[0.0410, 0.0306, 0.0152]`

**Rationale:**
- 这两个值明显是暖棕/黄褐，不是 current 原始 `184` 的中性灰。
- 说明用户说得对：bottom pass 的“变灰”不是水面造成的，也不是底图资产本身不黄。

### 3. 用 `directDiffuse-only / noIBL / diffuseIbl-only / ibl-only` 拆出 lighting 结构
**Files Changed:** 仓库内无；临时 HLSL：
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_directdiffuse_only_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_diffuseibl_only_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_noibl_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_ibl_only_clean.hlsl`

**Implementation:**
- `directDiffuse-only`：
  - center `184`：`[0.0653, 0.0445, 0.0220]`
  - bank `184`：`[0.0621, 0.0464, 0.0230]`
- `noIBL`：
  - center `184`：`[0.0653, 0.0445, 0.0220]`
  - bank `184`：`[0.0621, 0.0464, 0.0230]`
- `diffuseIbl-only`：
  - center `184`：`[0.0477, 0.0626, 0.0562]`
  - bank `184`：`[0.0432, 0.0621, 0.0559]`
- `ibl-only (diffuseIbl + specularIbl)`：
  - center `184`：`[0.0757, 0.1075, 0.1226]`
  - bank `184`：`[0.0561, 0.0869, 0.1008]`

**Rationale:**
- `noIBL == directDiffuse-only`，说明 current 这两个代表像素上的 direct specular 几乎可忽略。
- `ibl-only` 的 `G/B` 明显高于 `R`，是冷色贡献。
- `directDiffuse-only` 是暖色，`ibl-only` 是冷色；两者相加后，恰好回到 current 原始 `184`：
  - center 原始：`[0.1410, 0.1520, 0.1447]`
  - center `directDiffuse + ibl`：约 `[0.1410, 0.1520, 0.1446]`
- 这已经把 current bottom 为何发灰钉死在 lighting balance 上，而不是贴图资源上。

### 4. 回到 CK3 capture 对位 bottom 本身的暖色结果
**Files Changed:** 无

**Implementation:**
- 直接读取 CK3 capture bottom 代表像素：
  - center `event 336 (596,316)`：`[0.2766, 0.1758, 0.0862, 57.25]`
  - bank `event 338 (55,369)`：`[0.2712, 0.1851, 0.1000, 81.75]`

**Rationale:**
- CK3 bottom 自身就是明显 `R > G > B` 的暖黄褐。
- 相比之下，current `184` 原始值是：
  - center：`[0.1410, 0.1520, 0.1447]`
  - bank：`[0.1182, 0.1332, 0.1238]`
- 差别不是“稍微偏一点”，而是 current 的冷色 IBL 把暖底几乎完全拉平了。

### 5. 用 `directDiffuse x4` 验证最小修复方向
**Files Changed:** 仓库内无；临时 HLSL：`C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_directdiffuse_x4_clean.hlsl`

**Implementation:**
- 将 `CalculateRiverBottomLighting` 的返回改成：
  - `return directDiffuse * 4.0f;`
- 代表像素结果：
  - center `184 (420,240)`：`[0.2612, 0.1779, 0.0881]`
  - bank `184 (310,150)`：`[0.2485, 0.1854, 0.0920]`
- 与 CK3 bottom 对位：
  - CK3 center `336 (596,316)`：`[0.2766, 0.1758, 0.0862]`
  - CK3 bank `338 (55,369)`：`[0.2712, 0.1851, 0.1000]`

**Rationale:**
- 这组结果已经非常接近 CK3 bottom。
- 说明 current 与 CK3 的主差异不是 `bottomDiffuse` 资源，也不需要先假设 bottom 函数体全错。
- 更像是：current 这帧的有效 bottom 受光能量接近“应当让暖色直射占主导，但现在被冷色 IBL 稀释掉了”。

---

## Decisions Made

### Decision 1: 当前 bottom 的主根因定位为 lighting balance，不再优先怀疑底图资源
**Context:** 用户反复强调 bottom pass 自己就不对，且资产相同。

**Options Considered:**
1. 继续怀疑 `BottomDiffuse` / 资源导入
2. 继续怀疑 `RiverSurface`
3. 直接拆 bottom lighting 贡献

**Decision:** 选择 3
**Rationale:** `albedo-only` 已证明底图本身是暖色；继续追资源本身只会浪费时间。
**Trade-offs:** 暂时没有直接落仓库代码修复。
**Documentation Impact:** 新增本会话日志。

### Decision 2: 下一轮修复优先级应放在 `EnvironmentMapTexture` 与 bottom sun/IBL 参数，不是 `waterDiffuse`
**Context:** `ibl-only` 的冷色贡献已经解释了 current 的灰化。

**Options Considered:**
1. 再回去调 `RiverSurface`
2. 直接在 bottom 的 lighting 输入和权重上收敛

**Decision:** 选择 2
**Rationale:** current `184` 在水面参与前就已经被冷 IBL 拉灰；surface 不是第一现场。
**Trade-offs:** 需要重新审视 CK3 对位时的 environment / sun 参数，而不只是复刻函数体。
**Documentation Impact:** 无系统文档更新需求。

### Decision 3: 当前最小修复候选应优先尝试“降 IBL / 提直射”，不是先改 UV 或底图
**Context:** `directDiffuse x4` 已经能把 current `184` 推近 CK3 bottom。

**Options Considered:**
1. 继续先追 `worldUv` / `tangentUv` 微差
2. 继续先追 `waterDiffuse`
3. 先调 bottom 的 lighting input / weighting

**Decision:** 选择 3
**Rationale:** 这条热替换已经证明 current bottom 的主要色偏来自 lighting balance，不是主采样资源本身。
**Trade-offs:** 仍需要第二轮验证：到底是 cubemap 内容、IBL 强度，还是 `nDotL` / normal 方向让直射项偏弱。
**Documentation Impact:** 无

---

## What Worked ✅

1. **把 hot-edit 合并进已有 `renderdoc-cli`**
   - What: 在现有 CLI 里新增 `hotedit-pick`
   - Why it worked: 避开了独立 helper 的 replay 初始化崩溃
   - Reusable pattern: Yes

2. **`albedo -> directDiffuse -> IBL` 三段拆分**
   - What: 对同一 capture、同一像素做最小 return-line 替换
   - Why it worked: 能把“颜色为什么变灰”量化成贡献项，而不是停留在主观截图判断
   - Reusable pattern: Yes

---

## What Didn't Work ❌

1. **独立 `renderdoc_hotedit_helper.exe`**
   - What we tried: 直接链接 `renderdoc-core.lib` / `renderdoc.lib` 的自建 helper
   - Why it failed: 稳定崩在 `Session::open()`，未继续深挖其工具层初始化差异
   - Lesson learned: 这种场景优先扩展已经稳定的 `renderdoc-cli`
   - Don't try this again because: 继续追 helper 只会把时间耗在工具 ABI 上，而不是渲染根因上

---

## Problems Encountered & Solutions

### Problem 1: 无法在单独 helper 里稳定复用 RenderDoc replay
**Symptom:** 自建 helper 在 `session.open(capture)` 前后崩溃，退出码 `-1073741819`。
**Root Cause:** 未完全查明，但和独立 helper 的 replay 初始化环境有关。
**Investigation:**
- Tried: 链接 `renderdoc.lib`
- Tried: 将 exe 放到 `renderdoc.dll` 同目录
- Found: 崩点始终在 `Session::open()`

**Solution:**
```text
改为在现有 renderdoc-cli.exe 内新增一次进程 hotedit-pick 命令。
```

**Why This Works:** 现有 CLI 已经被本机验证能稳定打开同一批 `.rdc`。
**Pattern for Future:** 需要 `buildShader + replaceShader + query` 的复合工作流时，优先扩展稳定 CLI，而不是另起一个最小 helper。

### Problem 2: “相同资产 + 相同 shader 代码”仍然输出完全不同颜色
**Symptom:** current bottom 是灰青色，CK3 bottom 是暖黄褐。
**Root Cause:** bottom 输出由运行时 lighting 输入主导，不是简单的底图采样。
**Investigation:**
- Tried: `albedo-only`
- Tried: `directDiffuse-only`
- Tried: `diffuseIbl-only`
- Tried: `ibl-only`
- Found:
  - 底图本身是暖色
  - current 直射光也是暖色
  - current IBL 是明显冷色，且量级与直射相当甚至更大

**Solution:**
```text
把 bottom 差异收敛到 lighting input / weighting：
1. Environment cubemap 内容/来源
2. Bottom sun / env / specular 强度
3. 可能的 normal / nDotL 差异
```

**Why This Works:** 这三个入口正是 `RiverBottom.sdsl` 和 `RiverRenderFeature` 当前实际使用的主影响项。
**Pattern for Future:** 当“同资产、同函数体”仍出图不同，先拆运行时输入贡献，不要先否定像素证据。

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update `ARCHITECTURE_OVERVIEW.md` - 无系统状态变化
- [ ] Update `CURRENT_FEATURES.md` - 无功能状态变化

### New Patterns/Anti-Patterns Discovered
**New Pattern:** 用 `hotedit-pick` 做同像素 lighting 分量拆解
- When to use: RenderDoc 中 shader 代码接近参考，但输出色相/能量明显不对时
- Benefits: 能把“视觉感觉不对”收敛成具体贡献项

### Architectural Decisions That Changed
- **Changed:** 无
- **Reason:** 本轮只做 RenderDoc 诊断

---

## Code Quality Notes

### Performance
- **Measured:** 无
- **Target:** 本轮仅关注诊断准确性
- **Status:** ⚠️ 未涉及

### Testing
- **Tests Written:** 无仓库测试改动
- **Coverage:** `debug1.rdc` `event 184` / `213`，CK3 `event 336` / `338`
- **Manual Tests:** RenderDoc hot-edit + pick-pixel

### Technical Debt
- **Created:** 无仓库代码债务
- **Paid Down:** “bottom 变灰是不是贴图问题”这条误判路径
- **TODOs:** 下一轮用最小 hot-edit 验证一个候选修复权重（弱化 IBL、增强暖色直射）是否能直接把 `184` 推近 CK3

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在 RenderDoc 里做一个最小修复型 hot-edit
   - 例如压低 `_BottomEnvironmentIntensity` 等效权重，验证 `184` 是否立刻回暖
2. 检查 `EnvironmentMapTexture` 的实际 cubemap 内容与 CK3 参考是否同语义
3. 只有在 bottom lighting 收敛后，再决定是否需要继续改 `bottomNormal` / sun direction / shadow path

### Blocked Items
- **Blocker:** 无硬阻塞
- **Needs:** 如需精确参数 parity，仍缺可靠的 CK3 cbuffer 数值导出
- **Owner:** Codex

### Questions to Resolve
1. current 偏灰的主因更偏向“cubemap 内容过冷”，还是“IBL 权重过大”？
2. current 直射项偏弱，究竟是 sun intensity 不足，还是 bottom normal / nDotL 方向不对？
3. 现有 `RiverRenderSettings` 的 `BottomEnvironmentIntensity` / `BottomSunIntensity` 是否已经足以收敛，不需要先改 SDSL？

### Docs to Read Before Next Session
- [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)
- [2026-06-17-river-debug1-post-bottom-validation.md](./2026-06-17-river-debug1-post-bottom-validation.md)

---

## Session Statistics

**Files Changed:** 1 个会话日志（仓库内）；若干临时 RenderDoc 工具/HLSL（仓库外）
**Lines Added/Removed:** 未统计
**Commits:** 0

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- current `event 184` 原始值已经可以分解成：
  - 暖色 `directDiffuse`
  - 冷色 `IBL`
- `IBL-only` 已能几乎完整解释 current 为何从暖底被拉成灰青
- CK3 bottom 代表像素本身是强暖色，不是 surface 才把它染黄
- `directDiffuse x4` 已经能把 current bottom 非常接近 CK3 bottom，说明修复方向优先是 energy balance

**What Changed Since Last Doc Read:**
- Architecture: 无
- Implementation: 无仓库代码变更
- Constraints: 独立 helper 不稳定，后续继续用扩展后的 `renderdoc-cli` 更稳

**Gotchas for Next Session:**
- Watch out for: 不要再把焦点转回 `waterDiffuse`
- Don't forget: `noIBL` 与 `directDiffuse-only` 一致，说明 direct specular 不是主矛盾
- Remember: current `EnvironmentMapTexture` 入口在 `RiverRenderFeature`，不是隐藏在 shader 默认值里

---

## Links & References

### Related Documentation
- [ADR-014](../../decisions/adr-014-river-rendering-architecture.md)

### Related Sessions
- [2026-06-17-river-hotedit-root-cause-confirmation.md](./2026-06-17-river-hotedit-root-cause-confirmation.md)
- [2026-06-17-river-debug1-post-bottom-validation.md](./2026-06-17-river-debug1-post-bottom-validation.md)

### External Resources
- `C:\Users\Redwa\Desktop\debug1.rdc`
- `C:\Users\Redwa\Desktop\ck3-river.rdc`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_albedo_only_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_directdiffuse_only_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_diffuseibl_only_clean.hlsl`
- `C:\Users\Redwa\Desktop\renderdoc-mcp-export\riverbottom_ibl_only_clean.hlsl`

### Code References
- bottom lighting: `Terrain.Editor/Effects/RiverBottom.sdsl`
- bottom 参数绑定: `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- bottom 默认强度: `Terrain.Editor/Rendering/River/RiverRenderObject.cs`

---

## Notes & Observations

- 本轮最重要的结论不是“某条分支更像 CK3”，而是 current bottom 的灰色可以被数值上分解成“暖色直射 + 冷色 IBL”。
- 如果用户继续问“为什么明明一样还差这么多”，最短答案就是：pass 输出由 capture 时的 cubemap / sun / shadow / normal 共同决定，不是只有底图资源和函数体。

---
