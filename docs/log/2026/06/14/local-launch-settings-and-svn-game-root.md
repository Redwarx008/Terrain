# 本地 LaunchSetting 与 SVN Game 根目录实现
**Date**: 2026-06-14
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- 把 `LaunchSetting.json` 迁到 exe 目录，并让 `game/` 整目录退出 Git 管理

**Secondary Objectives:**
- 统一 Editor/Runtime 的资源入口文档口径
- 补齐 ADR 与会话日志，便于后续会话直接续接

**Success Criteria:**
- 总览文档、功能清单、ADR-015 都不再引用 `game/LaunchSetting.json`
- 会话日志明确记录 `gameRoot` 扫描与本地 LaunchSetting 的分工
- 自动验证完成并记录无法执行的手工检查项

---

## Context & Background

**Previous Work:**
- See: [virtual-resource-review-followup.md](./virtual-resource-review-followup.md)
- Related: [adr-015-workspace-game-root-and-runtime-requirements.md](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md)

**Current State:**
- `GameResourceRootLocator` 已不再依赖 `game/LaunchSetting.json` 判定 `gameRoot`
- 如果起点本身已位于目录名为 `game` 且包含 `map_data/` 的合法根，locator 仍会直接接受该根
- `LaunchSetting.json` 已固定在 exe 目录旁，缺失时自动生成
- `Editor` / `Runtime` 已共用 `GameResourceResolverBootstrap`
- `mods[*].Root` 仍保持存在的绝对路径语义

**Why Now:**
- 代码行为已经落地，文档仍残留旧口径；如果不收口，后续会继续误导实现与验证

---

## What We Did

### 1. 把 `LaunchSetting.json` 迁到 exe 目录旁，并保留自动生成
**Files Changed:** `Terrain/Resources/LaunchSettingsService.cs`, `Terrain.Editor.Tests/VirtualResources/LaunchSettingsResolverTests.cs`, `Terrain.Editor.Tests/VirtualResources/LocalLaunchSettingsBootstrapTests.cs`, `Terrain.Editor.Tests/Program.cs`

**Implementation:**
- `LaunchSetting.json` 固定放在 `AppContext.BaseDirectory`
- 缺失配置时自动生成默认文件
- `mods[*].Root` 继续保持存在的绝对路径语义，不因配置文件搬迁改语义

**Rationale:**
- 把本地运行配置从作者态 `game/` 根中拆开，避免再把工作区资源根和本地 mod 配置混成一个概念

### 2. 调整 `GameResourceRootLocator` 的根判定边界
**Files Changed:** `Terrain/Resources/GameResourceRootLocator.cs`, `Terrain.Editor.Tests/VirtualResources/*`

**Implementation:**
- 不再依赖 `game/LaunchSetting.json` 判定 `gameRoot`
- 优先扫描工作区同级 `game/`
- 如果起点本身已位于目录名为 `game` 且包含 `map_data/` 的合法根，仍会 direct-hit 接受该根

**Rationale:**
- 避免把 locator 错写成“只能扫描工作区同级 `game/`”或“仍依赖 `LaunchSetting.json`”

### 3. 统一 Editor / Runtime 的资源 bootstrap
**Files Changed:** `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`, `Terrain/Resources/GameRuntimeResourceBootstrap.cs`, `Terrain/Resources/GameResourceResolverBootstrap.cs`, `Terrain.Editor.Tests/VirtualResources/*`

**Implementation:**
- `Editor` 与 `Runtime` 都先通过 `GameResourceResolverBootstrap` 构建 `base(gameRoot) + enabled absolute-path mods`
- 然后分别进入各自的 Editor / Runtime bootstrap
- 保留 Editor 允许缺失 `.terrain` / `biome_mask.png`、Runtime 严格要求它们的边界

**Rationale:**
- 共享 resolver 装配链，避免再次出现两套入口规则漂移

### 4. 让 `game/` 退出 Git 管理，但保留必要跟踪文件
**Files Changed:** `.gitignore`, `Terrain.Editor.Tests/VirtualResources/GameResourceGitIgnoreTextTests.cs`, `Terrain.Editor.Tests/RiverWorkspaceDiagnosticsTests.cs`

**Implementation:**
- 根 `.gitignore` 忽略 `/game/`
- `git ls-files game` 为空
- `Terrain.Editor/app.manifest` 继续通过例外规则被 Git 跟踪

**Rationale:**
- 让 SVN 管理的作者态资源目录退出 Git，同时不误伤仍需版本控制的编辑器清单文件

### 5. 收口总览、ADR 与会话日志
**Files Changed:** `docs/ARCHITECTURE_OVERVIEW.md`, `docs/CURRENT_FEATURES.md`, `docs/log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md`, `docs/log/2026/06/14/local-launch-settings-and-svn-game-root.md`

**Implementation:**
- 把资源入口改写为“扫描 `game/` 作为 base + 从 exe 目录旁读取或自动生成 `LaunchSetting.json`”
- 把 direct-hit 合法 `game/` 根行为补到总览、功能清单和 ADR
- 记录本次会话的实现事实、验证结果与无法在当前环境完成的手工检查

**Rationale:**
- 让系统总览、功能清单、ADR 和会话日志与整次会话完成的真实行为保持一致

---

## Decisions Made

### Decision 1: `gameRoot` 与 `LaunchSetting` 解耦
- `gameRoot` 继续扫描
- 起点本身若已位于目录名为 `game` 且包含 `map_data/` 的合法根，locator 也会直接接受该根
- `LaunchSetting.json` 只描述 mod

### Decision 2: `mods[*].Root` 继续保持绝对路径
- 不因配置文件搬迁改语义

### Decision 3: `Editor` / `Runtime` 共用 resolver 装配链
- 两端都先经过 `GameResourceResolverBootstrap`
- 只在进入各自 bootstrap 后再分化必需资源规则

---

## What Worked ✅

1. **文档收口围绕实现事实**
   - What: 直接用当前已完成的代码行为统一总览、功能清单和 ADR
   - Why it worked: 避免把历史口径误当成现状继续复制

2. **把 base 与 mod 分层写清楚**
   - What: base 永远来自扫描到的 `gameRoot`，mod 永远来自 `LaunchSetting.json`
   - Impact: 后续讨论资源入口时不再混淆“根定位”和“层叠配置”

---

## What Didn't Work ❌

1. **尝试把 `LaunchSetting.json` 继续当作 `game/` 合法性条件**
   - What we tried: 旧文档持续把 `LaunchSetting.json` 写成 `gameRoot` 判定条件
   - Why it failed: 这与 SVN 管理 `game/`、本地 exe 配置分离的实现方向冲突
   - Lesson learned: 资源根合法性和本地 mod 配置必须分开描述
   - Don't try this again because: 会再次把 Git/SVN 边界和资源入口语义搅混

---

## Architecture Impact

### Documentation Updates Required
- [x] Update ARCHITECTURE_OVERVIEW.md - 资源入口改为“扫描 `game/` + exe 本地 LaunchSetting”，并补 direct-hit 合法 `game/` 根口径
- [x] Update CURRENT_FEATURES.md - 虚拟资源会话 / Bootstrap 口径同步，并补 direct-hit 合法 `game/` 根口径
- [x] Update ADR-015 - Decision 段与后文统一到当前 locator 行为

### Architectural Decisions That Changed
- **Changed:** 资源入口文档口径
- **From:** `game/LaunchSetting.json` 既参与根定位又承载 mod 配置
- **To:** `gameRoot` 扫描负责 base；起点本身若已位于合法 `game/` 根可 direct-hit 接受；exe 目录旁 `LaunchSetting.json` 只负责 mod
- **Scope:** `GameResourceRootLocator`、共享 resolver bootstrap、Git/SVN 资源边界及其文档
- **Reason:** 对齐 SVN 管理的 `game/` 与本地运行配置的职责边界

---

## Code Quality Notes

### Testing
- **Tests Written:** 新增/扩展了本地 LaunchSetting、game root direct-hit、shared bootstrap、git ignore、temporary river diagnostics 等行为测试
- **Coverage:** 覆盖了 exe 目录旁 `LaunchSetting.json` 自动生成、enabled mod 绝对路径校验、direct-hit 合法 `game/` 根、Editor/Runtime 共用 bootstrap、`game/` 退跟踪与 `app.manifest` 保留跟踪
- **Manual Tests:** 见下文记录的手工检查项

---

## Verification

- `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`
  - 结果：通过，退出码 `0`
  - 备注：全部测试 `PASS`；输出包含既有 NuGet 漏洞告警 `NU1901` / `NU1903`
- `dotnet build Terrain.sln`
  - 结果：通过，退出码 `0`
  - 备注：`Build succeeded.`；输出包含 28 条既有 NuGet 漏洞告警，无编译错误
- `git ls-files game`
  - 结果：无输出，退出码 `0`

### Manual Checks

当前环境无法实际启动 GUI，因此以下手工项未执行，只能保留为待补检查：

1. 启动 `Terrain.Editor`，删除 exe 目录旁已有的 `LaunchSetting.json`，确认重启后会自动生成
2. 确认 `game/` 目录里不再需要 `LaunchSetting.json`
3. 确认未导出的 `terrain.terrain` / `biome_mask.png` 不阻塞 Editor 启动
4. 让 Runtime 指向缺失的 `terrain.terrain` 或 `biome_mask.png`，确认只记录错误并保持未初始化

---

## Next Session

### Immediate Next Steps (Priority Order)
1. 在可启动 GUI 的环境里补做 Editor/Runtime 手工检查
2. 如果资源入口规则再次变化，优先同步更新 ADR 与总览文档

### Questions to Resolve
1. 是否需要为 exe 目录旁 `LaunchSetting.json` 的默认内容单独建 ADR

### Docs to Read Before Next Session
- [ARCHITECTURE_OVERVIEW.md](../../../../ARCHITECTURE_OVERVIEW.md) - 资源入口与运行时边界
- [adr-015-workspace-game-root-and-runtime-requirements.md](../../../decisions/adr-015-workspace-game-root-and-runtime-requirements.md) - 根定位与必需资源规则

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `gameRoot` 不再依赖 `LaunchSetting.json`
- 起点本身若已位于目录名为 `game` 且包含 `map_data/` 的合法根，locator 仍会直接接受该根
- `LaunchSetting.json` 固定在 exe 目录旁，缺失时自动生成
- `Editor` / `Runtime` 共用 `GameResourceResolverBootstrap`
- `mods[*].Root` 继续是绝对路径且必须存在

**What Changed Since Last Doc Read:**
- Architecture: 资源入口已切换到“扫描工作区 `game/` 为主、direct-hit 合法 `game/` 根为辅 + exe 本地 LaunchSetting + 共享 resolver bootstrap”
- Implementation: `LaunchSetting.json` 已迁到 exe 目录旁，`Editor` / `Runtime` 共用 `GameResourceResolverBootstrap`
- Constraints: `game/` 不再由 Git 跟踪，`git ls-files game` 为空，但 `Terrain.Editor/app.manifest` 仍需继续被跟踪

**Gotchas for Next Session:**
- 不要再把 `game/LaunchSetting.json` 写回总览、ADR 或功能清单
- 不要把 locator 再误写成“只能扫描工作区同级 `game/`”
- GUI 手工检查尚需在可视环境补做

---

## Notes & Observations

- 本日志记录的是整次会话收口，不只是最后一次文档提交
- 本次最后的文档修订不改代码，但整次会话已完成资源入口、测试与 Git 跟踪边界变更
- `git ls-files game` 预期为空，但仍需作为自动验证步骤重新执行并记录

---

*Template Version: 1.0 - Based on Archon-Engine template*
