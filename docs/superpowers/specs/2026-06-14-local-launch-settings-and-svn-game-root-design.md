# 本地 LaunchSetting 与 SVN Game 根目录设计

**Date**: 2026-06-14  
**Status**: Approved Draft  
**Author**: Codex

---

## 1. 背景

当前 Terrain 的资源入口仍然默认围绕仓库内 `game/LaunchSetting.json` 展开：

- `GameResourceRootLocator` 把“完整合法的 `game/` 根”定义为同时拥有 `LaunchSetting.json` 和 `map_data/`
- Editor 与 Runtime 都从 `gameRoot` 下读取 `LaunchSetting.json`
- `Terrain.Editor.Tests` 里仍有“仓库自带 game scaffold”前提

这与新的资源管理约束冲突：

- `game/` 目录未来由 SVN 管理，不再由 Git 跟踪
- `LaunchSetting.json` 应位于 `exe` 目录，而不是 `game/`
- `game/` 仍然是 base 资源根，但不应该由 `LaunchSetting.json` 指定
- `mods[*].Root` 已有语义是绝对路径，不应因 `LaunchSetting.json` 搬迁而改变

本设计的目标是把“base game 发现”和“mod 配置入口”彻底拆开，同时保持 Editor / Runtime 共用同一套资源层模型。

---

## 2. 目标

1. `game/` 整目录退出 Git 管理，后续由 SVN 或本地工作区负责。
2. `LaunchSetting.json` 固定位于 `AppContext.BaseDirectory`。
3. `game/` 继续作为隐式 base 层，由扫描逻辑发现，不写进 `LaunchSetting.json`。
4. Editor 与 Runtime 统一采用“扫描 game + 读取本地 LaunchSetting + 组装层栈”的入口。
5. `LaunchSetting.json` 缺失时自动生成默认文件。
6. `mods[*].Root` 保持绝对路径语义，不做相对路径推导。
7. 启动失败模式保持明确，不用静默修复去掩盖错误配置。

---

## 3. 非目标

- 不让 `LaunchSetting.json` 显式声明 `gameRoot`
- 不把 `mods[*].Root` 改成相对 `game/` 或相对 `exe` 的路径语义
- 不在本轮引入新的资源打包/部署模型
- 不改变 Editor 对缺失 `.terrain` / `biome_mask.png` 的容忍边界
- 不改变 Runtime 对缺失 `.terrain` / `biome_mask.png` 的严格边界
- 不在本轮实现 SVN 自动同步、SVN 状态探测或工作副本修复

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| `LaunchSetting.json` 位置 | 固定在 `AppContext.BaseDirectory` |
| `gameRoot` 来源 | 继续由扫描逻辑发现 `bin/` 同级 `game/` |
| base 层声明 | 隐式存在，不出现在 `LaunchSetting.json` |
| `mods[*].Root` 语义 | 必须是绝对路径 |
| 缺失 `LaunchSetting.json` | 自动生成默认文件 |
| 损坏 `LaunchSetting.json` | 报错并停止，不自动覆盖 |
| Git 管理范围 | `game/` 整目录不再由 Git 跟踪 |
| SVN 管理范围 | `game/` 目录及其内容 |

---

## 5. 目录与入口模型

推荐目录关系如下：

```text
workspace/
  game/                      # SVN 管理的 base 资源根
    map_data/
      default.toml
      heightmap.png
      terrain.terrain
      biome_mask.png
      biome_settings.toml
      rivers.png
      materials/
        descriptor.toml
        *.dds
  bin/
    Debug/
      net8.0/
        Terrain.Editor.exe
        LaunchSetting.json   # 本地配置入口，缺失时自动生成
```

约束如下：

- `game/` 与 `bin/` 是同级目录
- `LaunchSetting.json` 与 `exe` 同目录
- `game/` 不再要求包含 `LaunchSetting.json`
- `mods` 目录不要求位于 `game/` 下
- 启用 mod 的根目录由 `LaunchSetting.json` 里的绝对路径指定

---

## 6. `LaunchSetting.json` 职责

### 6.1 文件职责

`LaunchSetting.json` 只负责描述 mod 层：

- 哪些 mod 启用
- 这些 mod 的稳定标识
- 这些 mod 的绝对根路径

它不负责：

- 指定 `gameRoot`
- 指定 base 层顺序
- 显式映射具体资源文件
- 修复缺失的 game 工作区

### 6.2 文件结构

```json
{
  "version": 1,
  "mods": [
    {
      "id": "mod_a",
      "root": "E:/mods/mod_a",
      "enabled": true
    }
  ]
}
```

如果文件缺失，生成的默认内容为：

```json
{
  "version": 1,
  "mods": []
}
```

### 6.3 校验规则

对 `enabled = true` 的 mod，必须满足：

- `id` 非空
- `root` 非空
- `root` 是绝对路径
- `root` 指向的目录存在

对 `enabled = false` 的 mod：

- 继续保持宽松，不阻塞启动

---

## 7. `gameRoot` 发现规则

`gameRoot` 的发现继续独立于 `LaunchSetting.json`。

### 7.1 定位原则

- 从 `AppContext.BaseDirectory` 出发向上扫描
- 命中工作区后，优先寻找其同级 `game/`
- `gameRoot` 的合法性不再依赖 `game/LaunchSetting.json`

### 7.2 最低合法条件

定位器只需要确认：

- `game/` 目录存在
- `game/map_data/` 目录存在

更细的资源完整性由后续 bootstrap 校验：

- `map_data/default.toml`
- `heightmap.png`
- `biome_settings.toml`
- `materials/descriptor.toml`
- Runtime 必需的 `.terrain` / `biome_mask.png`

### 7.3 失败行为

如果扫描不到合法的 `gameRoot`：

- Editor 启动失败
- Runtime 启动失败
- 错误信息应明确指出扫描起点和未找到 `game/map_data/`

---

## 8. 统一启动顺序

Editor 与 Runtime 都应采用同一套前置入口：

1. 从 `AppContext.BaseDirectory` 扫描 `gameRoot`
2. 确保 `Path.Combine(AppContext.BaseDirectory, "LaunchSetting.json")` 存在
3. 如果配置文件不存在，则生成默认 `{"version":1,"mods":[]}`
4. 读取并校验 `LaunchSetting.json`
5. 构建资源层列表：
   - `base = gameRoot`
   - 追加所有启用 mod 的绝对路径层
6. 用同一套 `GameResourceResolver` 解析虚拟资源
7. 分流到 Editor bootstrap 或 Runtime bootstrap

这样可以保证：

- `game/` 的来源不依赖本地配置文件
- `LaunchSetting.json` 只负责 overlay/mod
- Editor 与 Runtime 看到同一条资源层栈

---

## 9. Editor 与 Runtime 边界

### 9.1 Editor

Editor 保持当前作者态容忍边界：

- 必须加载：
  - `map_data/default.toml`
  - `heightmap`
  - `map_data/biome_settings.toml`
  - `map_data/materials/descriptor.toml`
- 可以缺失：
  - `terrain.terrain`
  - `biome_mask.png`
  - `rivers.png`

缺失时行为不变：

- `terrain.terrain` 只保留导出目标
- `biome_mask.png` 只保留写回目标，并在内存中使用默认空 mask

### 9.2 Runtime

Runtime 保持当前运行时严格边界：

- 必须加载：
  - `map_data/default.toml`
  - `terrain.terrain`
  - `map_data/biome_mask.png`
  - `map_data/biome_settings.toml`
  - `map_data/materials/descriptor.toml`
- 可选：
  - `rivers.png`

缺失关键资源时：

- 记录错误日志
- terrain 保持未初始化
- 同配置下不逐帧重复重试

---

## 10. 自动生成与失败处理

### 10.1 自动生成只覆盖“缺失”场景

仅当 `exe/LaunchSetting.json` 不存在时：

- 自动创建默认配置文件
- 继续启动流程

### 10.2 已存在但损坏时不得覆盖

以下情况都属于配置错误：

- JSON 无法解析
- `version` 不受支持
- 启用 mod 的 `id` 为空
- 启用 mod 的 `root` 为空
- 启用 mod 的 `root` 不是绝对路径
- 启用 mod 的 `root` 指向不存在目录

这些情况下：

- 直接失败
- 保留原文件
- 不做猜测性修复

### 10.3 写入失败

如果默认 `LaunchSetting.json` 无法写入：

- 直接失败
- 错误信息必须包含目标路径

---

## 11. 代码职责调整

### 11.1 `GameResourceRootLocator`

职责调整为：

- 只负责定位 `gameRoot`
- 不再把 `game/LaunchSetting.json` 作为合法根判定条件

### 11.2 `LaunchSettingsService`

需要新增统一入口：

- 在 `AppContext.BaseDirectory` 加载 `LaunchSetting.json`
- 文件缺失时生成默认配置
- 对启用 mod 执行严格校验

### 11.3 共享资源层构建

建议把“`gameRoot + LaunchSettings -> GameResourceLayer[]`”提炼为共享入口，供：

- `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`
- `Terrain/Core/TerrainProcessor.cs`

共同复用。

### 11.4 Bootstrap 入口

Editor 与 Runtime 的前置入口都改成：

- 扫描 `gameRoot`
- 读取或生成 `exe/LaunchSetting.json`
- 组装 `base + enabled mods`

而不是：

- 从 `gameRoot/LaunchSetting.json` 进入

---

## 12. 测试策略调整

### 12.1 删除旧前提

以下前提需要退出：

- “仓库自带 `game/` scaffold”
- “`game/LaunchSetting.json` 是 base 根合法性的一部分”

### 12.2 新测试基线

测试应改成临时工作区模式，按需创建：

- `bin/`
- 同级 `game/`
- 绝对路径 mod 目录
- `exe` 目录旁的 `LaunchSetting.json`

### 12.3 必要回归测试

至少需要覆盖：

1. `gameRoot` 定位不依赖 `game/LaunchSetting.json`
2. `exe/LaunchSetting.json` 缺失时自动生成默认文件
3. 已存在但损坏的 `LaunchSetting.json` 会失败且不被覆盖
4. 启用 mod 的 `root` 不是绝对路径时启动失败
5. 启用 mod 的 `root` 不存在时启动失败
6. Editor 仍允许缺失 `.terrain` / `biome_mask.png`
7. Runtime 仍严格要求 `.terrain` / `biome_mask.png`
8. `git ls-files game` 结果为空

---

## 13. Git / SVN 迁移

### 13.1 Git

Git 侧需要收口为：

- 根 `.gitignore` 新增 `/game/`
- 当前已跟踪的 `game/**` 全部从 Git 索引移除
- `game/` 后续不再作为 Git scaffold 或测试前提

### 13.2 SVN

`game/` 目录的后续职责：

- 由 SVN 管理 base 资源
- 允许本地作者态与运行时二进制共存
- 不再受 Git 忽略规则之外的额外约束

### 13.3 `LaunchSetting.json`

`LaunchSetting.json` 位于 `bin/` 下，因此天然落在现有 Git 忽略规则内，不需要额外新增忽略项。

---

## 14. 实施顺序

推荐按以下顺序实施：

1. 修改 `GameResourceRootLocator`，去掉对 `game/LaunchSetting.json` 的依赖
2. 在 `LaunchSettingsService` 中增加“读取或创建 exe 旁配置”的入口
3. 收紧启用 mod 的绝对路径与目录存在性校验
4. 抽共享的资源层构建入口
5. 让 Editor bootstrap 切到新入口
6. 让 Runtime bootstrap 切到新入口
7. 重写相关虚拟资源测试
8. 把 `game/**` 从 Git 索引移除，并更新 `.gitignore`
9. 更新架构总览与功能清单中的资源入口描述

---

## 15. 验收标准

完成后应满足：

1. 仓库执行 `git ls-files game` 没有输出
2. `game/` 不包含 `LaunchSetting.json` 也不会破坏 `gameRoot` 发现
3. 首次启动时，如果 exe 目录没有 `LaunchSetting.json`，会自动生成默认文件
4. 默认生成后的配置可让 Editor / Runtime 在无 mod 情况下继续运行
5. 已存在但损坏的 `LaunchSetting.json` 会明确失败，不被自动覆盖
6. 启用 mod 的 `root` 必须是存在的绝对路径，否则失败
7. Editor 仍允许缺失 `.terrain` / `biome_mask.png`
8. Runtime 仍严格要求 `.terrain` / `biome_mask.png`
9. Editor 与 Runtime 继续共享同一套 `base + enabled mods` 资源层解析顺序

---

## 16. 对现有文档的影响

以下口径需要更新：

- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/log/decisions/adr-015-workspace-game-root-and-runtime-requirements.md`

其中最关键的变化是：

- 不再表述为“从 `game/LaunchSetting.json` 启动”
- 改为“扫描 `game/` 作为 base，再从 exe 目录读取或生成 `LaunchSetting.json`”

---

## 17. 备注

- 这次调整的核心不是改资源层模型，而是拆开 base 发现与本地配置入口。
- `LaunchSetting.json` 的移动不应改变 mod 路径语义。
- `game/` 退出 Git 后，所有依赖仓库内 scaffold 的测试都必须切到临时工作区模型。
