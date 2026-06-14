# Game Root Resource Bootstrap And Scaffold
**Date**: 2026-06-14
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 修正虚拟资源系统把二进制输出目录误当资源根的问题，改为固定定位工作区 `game/` 目录。

**Secondary Objectives:**
- 在仓库 `game/` 目录补齐最小可用的文本资源脚手架。
- 让 Editor 在缺少 `.terrain` 和 `biome_mask.png` 时仍可启动并编辑。

**Success Criteria:**
- Editor/Runtime 都从工作区 `game/` 根读取 `LaunchSetting.json` 和 `map_data/`。
- `game/LaunchSetting.json`、`game/map_data/default.toml`、`game/map_data/biome_settings.toml`、`game/map_data/materials/descriptor.toml` 存在。
- Editor 不因缺失 `.terrain` 或 `biome_mask.png` 而在 bootstrap 阶段失败。

---

## What We Did

### 1. 资源根从二进制目录改为工作区 `game/`
**Files Changed:** `Terrain/Resources/GameResourceRootLocator.cs`, `Terrain/Resources/GameResourceResolver.cs`, `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`, `Terrain/Core/TerrainProcessor.cs`

**Implementation:**
- 新增 `GameResourceRootLocator.FindFrom()`，从 `AppContext.BaseDirectory` 向上查找工作区 `game/` 根。
- Editor bootstrap 改为从 `game/LaunchSetting.json` 构建层级。
- Runtime bootstrap 也改为先定位 `game/`，再解析 LaunchSettings 和资源层。
- 新增 `GameResourceResolver.ResolveWritableTarget()`，用于“文件可不存在，但要先拿到最终写回目标”的场景。

**Rationale:**
- 资源组织应跟工作区 `game/` 目录绑定，而不是跟 `Bin/...` 里的二进制输出目录绑定。

### 2. Editor 对 `.terrain` 和 `biome_mask.png` 改为可缺省
**Files Changed:** `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`

**Implementation:**
- `terrain.terrain` 不再要求在 Editor 打开时已存在；会话只记录它的最终写回目标，供 Export Terrain 使用。
- `biome_mask.png` 不再要求在 Editor 打开时已存在；`TerrainManager.LoadFromResourceSession()` 会在缺失时保留内存中的默认空 biome mask。

**Rationale:**
- Editor 本来就是从 `heightmap.png` 建立编辑态地形，不应被运行时 `.terrain` 文件反向卡住。
- 缺失 `biome_mask.png` 时，首次 Save 再落盘即可，符合既有行为。

### 3. 补齐 `game/` 文本资源脚手架并导入 CK3 贴图
**Files Changed:** `game/LaunchSetting.json`, `game/map_data/default.toml`, `game/map_data/biome_settings.toml`, `game/map_data/materials/descriptor.toml`, `game/map_data/materials/*.dds`

**Implementation:**
- 新增空 mod 列表的 `LaunchSetting.json`。
- 新增 `default.toml`，声明：
  - `heightmap.png`
  - `terrain.terrain`
  - `rivers.png`
  - `height_scale = 200.0`
- 新增最小 `biome_settings.toml` 和 `materials/descriptor.toml`。
- 从 `E:\SteamLibrary\steamapps\common\Crusader Kings III\game\gfx\map\terrain` 复制：
  - `plains_01_diffuse.dds`
  - `plains_01_normal.dds`
  - `plains_01_properties.dds`

**Rationale:**
- 当前仓库原本只有 `heightmap.png` 和 `rivers.png`，缺少可被新虚拟资源系统直接消费的最小文本描述层。

---

## Decisions Made

### Decision 1: `LaunchSetting.json` 放在 `game/` 根，不放在 `Bin/...`
**Context:** 用户明确指出资源路径应在 `Terrain\\game`，而不是输出目录。
**Decision:** 统一把 `game/` 视为作者态资源根，二进制只负责向上定位它。
**Rationale:** 这与工作区资源管理、手工编辑和版本控制都更一致。

### Decision 2: Editor 不要求 `.terrain` 和 `biome_mask.png` 预先存在
**Context:** 用户明确说明这是 Editor 的原始语义。
**Decision:** `terrain.terrain` 仅作为 Export 目标；`biome_mask.png` 缺失时不加载，保留默认空 mask。
**Rationale:** 避免把运行时产物反向升级成 Editor 打开的前置条件。

---

## Verification

- `dotnet run --project Terrain.Editor.Tests\\Terrain.Editor.Tests.csproj` 通过。
- `dotnet build Terrain.sln` 通过。
- 新增测试覆盖：
  - `game` 资源根定位
  - 缺失文件时的可写目标解析
  - 仓库 `game/` 脚手架文件存在性

---

## Next Session

### Immediate Next Steps
1. 做一次真实 Editor 冒烟验证，确认从 `Bin/Editor/...` 启动时能正确反查到工作区 `game/`。
2. 如需支持更多材质，扩展 `materials/descriptor.toml` 和 `biome_settings.toml`。
3. 若后续需要 Runtime 直接可跑，再补 `.terrain` 生成或现成导出步骤。

### Gotchas
- 不要把资源根重新绑回 `AppContext.BaseDirectory` 本身。
- 不要让 Editor 因为缺失 `.terrain` 或 `biome_mask.png` 而启动失败。
- `materials/descriptor.toml` 里的贴图路径仍必须是平铺短文件名。

---

## Session Statistics

**Commits:** 0

---

*Template Version: 1.0 - Based on Archon-Engine template*
