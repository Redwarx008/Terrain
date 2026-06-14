# 工作区 `game/` 优先定位与 Runtime 必需资源边界

**Date**: 2026-06-14
**Status**: ✅ Accepted
**Decision ID**: ADR-015

---

## Context

- Terrain 的作者态资源位于工作区 `game/`，而 Editor/Runtime 二进制位于 `Bin/...`
- 仅按“最近的 `map_data/` 或最近的 `game/` 目录”定位资源根，会让路径上更近但不完整的目录干扰真实资源根选择
- Editor 需要在尚未导出 `.terrain`、尚未生成 `biome_mask.png` 时仍可启动并继续作者态工作
- Runtime 直接消费 `.terrain` 与 `biome_mask.png`，缺失这两个文件时不能静默降级

---

## Decision

- 资源根继续由 `GameResourceRootLocator` 从 `AppContext.BaseDirectory` 向上查找工作区同级 `game/`
- 如果起点本身已经位于目录名为 `game` 且包含 `map_data/` 的合法根，locator 也会直接接受该根
- `game/` 的合法性只要求目录名为 `game` 且包含 `map_data/`
- `LaunchSetting.json` 固定放在 `AppContext.BaseDirectory`，缺失时自动生成默认配置
- `LaunchSetting.json` 只描述 mod 层；base 层永远由扫描得到的 `gameRoot` 隐式注入
- 启用 mod 的 `root` 必须是存在的绝对路径
- Editor 启动时：
  - 必须加载 `default.toml`、`heightmap`、`biome_settings.toml`、`materials/descriptor.toml`
  - 不要求 `.terrain` 已存在
  - 不要求 `biome_mask.png` 已存在
  - 对缺失的 `.terrain` 与 `biome_mask.png` 仅保留固定写回目标
- Runtime 启动时：
  - 必须加载 `.terrain` 与 `biome_mask.png`
  - 会忽略 `default.toml` 中的 `heightmap` 声明
  - 缺失任一必需资源时记录错误日志，并保持 terrain 未初始化

---

## Options Considered

### Option 1: 直接使用 `AppContext.BaseDirectory`
**Description:**
- 把当前二进制目录当作 base 根目录

**Pros:**
- 实现最简单
- 不需要额外定位逻辑

**Cons:**
- 会把 `Bin/...` 当成资源根
- 与作者态 `game/` 目录分离
- 不符合当前项目资源布局

### Option 2: 选择最近的 `game/` 或 `map_data/`
**Description:**
- 从当前路径向上找最近能对上的资源目录

**Pros:**
- 比直接使用 `AppContext.BaseDirectory` 稍稳一些
- 对局部目录结构容忍度高

**Cons:**
- 容易误选路径上更近但不完整的目录
- 无法表达“工作区 `game/` 应优先”
- 仍然会让 Editor/Runtime 命中错误层

### Option 3: 工作区 `game/` 优先，并区分 Editor / Runtime 必需资源
**Description:**
- 上探工作区根并优先绑定其 `game/`
- 同时允许直接命中的合法 `game/` 根继续工作
- 统一解析链，但按消费端区分资源必需性

**Pros:**
- 对当前工作区结构最稳定
- 不会被路径上更近但不完整的目录干扰
- Editor 作者态工作流与 Runtime 消费边界清晰
- 与 SVN 管理的工作区 `game/` 和当前资源布局对齐

**Cons:**
- 仍然保留对“起点本身已位于合法 `game/` 根”时的直接接受行为
- 不面向任意部署目录，需要工作区结构保持明确

---

## Rationale

- 用户明确要求资源路径优先指向 `Terrain/game`，而不是默认把 `Bin` 输出目录当 base 根
- Editor 和 Runtime 共享 resolver 是目标，但两者对 `.terrain` / `biome_mask.png` 的依赖强度不同，必须拆开定义
- 把“工作区 `game/` 优先、起点本身若已位于合法 `game/` 根也会直接接受”写清楚，能避免后续文档和实现继续偏离

---

## Trade-offs

**What we gain:**
- 与当前实现一致的资源根定位口径
- 明确的 Editor / Runtime 生命周期边界
- 更可测的启动与失败行为

**What we give up:**
- 如果进程起点本身已落在一个目录名为 `game` 且包含 `map_data/` 的合法根内，定位器仍会接受它
- 部署目录如果脱离工作区约定，需要单独设计新的根定位方案

---

## Consequences

### Positive
- Editor 可以在未导出 `.terrain` 的早期作者态阶段直接工作
- Runtime 对缺失关键二进制资源不再静默失败
- 资源覆盖与写回目标优先围绕工作区 `game/` 根展开

### Negative
- 依赖 `GameResourceRootLocator` 正确识别工作区根
- 文档必须明确说明“起点本身若位于合法 `game/` 根也会被直接接受”
- 如果未来支持打包部署，当前定位策略需要扩展

### Neutral
- `rivers.png` 仍然只读接入，不随本决策改变
- `provinces.png` 仍保留资源位但未实现

---

## Implementation Notes

- `Terrain/Resources/GameResourceRootLocator.cs`
  - 优先命中工作区根下的 `game/`
  - 也接受起点本身已处于目录名为 `game` 且包含 `map_data/` 的合法根
- `Terrain/Core/TerrainProcessor.cs`
  - Runtime bootstrap 失败时使用错误日志，并阻止同配置下的逐帧重复重试
- `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`
  - 启动时对缺失 `.terrain` / `biome_mask.png` 仅保留写回目标
- `Terrain.Editor.Tests/VirtualResources/*`
  - 覆盖 Bin 误命中、Editor 缺失运行时资源仍可启动、Runtime 严格要求关键资源等行为

---

## Related Decisions

- [adr-012-biome-rule-layer-system.md](./adr-012-biome-rule-layer-system.md)
- [adr-014-river-rendering-architecture.md](./adr-014-river-rendering-architecture.md)

---

## References

- [Editor / Runtime 共用虚拟资源系统设计](../../superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md)
- [2026-06-14 会话日志](../2026/06/14/runtime-game-root-and-required-resource-alignment.md)

---

*ADR Template Version: 1.0*
