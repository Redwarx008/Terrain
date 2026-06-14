# Editor 作者态缺失 MapData 资源补齐设计

**Date**: 2026-06-15  
**Status**: Approved Draft  
**Author**: Codex

---

## 1. 背景

当前 Editor 作者态启动链路对 `map_data/` 下的关键文件采用“严格必需”口径：

- `map_data/default.toml`
- `map_data/heightmap.png`
- `map_data/biome_settings.toml`
- `map_data/materials/descriptor.toml`

其中只有：

- `terrain.terrain`
- `biome_mask.png`

允许缺失并作为可写目标保留。

这与新的作者态约束不一致：

- `game/` 未来由 SVN 管理，资源不一定在首次进入仓库时完整
- `default.toml`
- `materials/descriptor.toml`
- `biome_settings.toml`

应当在缺失时自动生成最小合法骨架，避免作者态因为纯配置文件缺失而无法进入。

同时，用户明确要求：

- **不自动生成 `heightmap.png`**
- 如果 `default.toml` 已生成或已存在，但其中声明的 `heightmap.png` 缺失：
  - **继续启动 Editor**
  - **把它视为“待补资源”**
  - **打印日志报错**

本设计的核心是把“工程配置可进入”与“地形数据已就绪”拆开，而不是继续把二者绑定成同一个成功条件。

---

## 2. 目标

1. Editor 启动时自动补齐缺失的 `default.toml`、`descriptor.toml`、`biome_settings.toml`。
2. 自动生成的文件必须是当前 reader 可读取的最小合法内容。
3. `heightmap.png` 缺失时不生成、不阻塞 Editor 主界面启动。
4. `heightmap.png` 缺失时将当前工程标记为“待补资源”状态。
5. 日志必须明确指出缺失文件路径与当前受限行为。
6. `Save` / `Export .terrain` 在缺失 `heightmap` 时继续禁止。
7. Runtime 启动口径不变，不因为作者态容错而放宽运行时资源要求。

---

## 3. 非目标

- 不自动生成 `heightmap.png`
- 不自动生成 `terrain.terrain`
- 不自动生成 `biome_mask.png`
- 不在本轮改变 Runtime 的资源严格性
- 不在本轮引入“占位高度图”或内存平坦地形
- 不自动覆盖已存在但内容损坏的 TOML 文件
- 不自动修复 `default.toml` 中的无效相对路径

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| 自动补齐范围 | 仅 `default.toml`、`biome_settings.toml`、`materials/descriptor.toml` |
| 触发条件 | 仅文件缺失时自动生成 |
| 已存在但损坏 | 报错并停止，不自动覆盖 |
| `heightmap.png` 缺失 | 不生成，进入“待补资源”状态 |
| Editor 启动成功判定 | 不再等同于“地形实体已创建” |
| 待补资源日志级别 | 资源层 `Error`，Shell 控制台 `Error + Warning` |
| `Save` / `Export` | 缺失 `heightmap` 时禁止 |
| Runtime | 保持现有严格边界，不复用该容错语义 |

---

## 5. 资源分类

作者态启动后，资源分为三类：

### 5.1 自动补齐资源

这些文件缺失时由 Editor 自动创建：

- `map_data/default.toml`
- `map_data/biome_settings.toml`
- `map_data/materials/descriptor.toml`

### 5.2 待补资源

这些文件缺失时 **不** 自动创建，但不阻塞 Editor 主界面进入：

- `map_data/heightmap.png`

### 5.3 现有可缺失写回目标

这些文件继续维持现有“可缺失但保留写回目标”语义：

- `map_data/terrain.terrain`
- `map_data/biome_mask.png`

---

## 6. 自动生成骨架内容

### 6.1 `map_data/default.toml`

缺失时生成以下最小合法内容：

```toml
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"

[settings]
height_scale = 100.0
```

约束如下：

- 固定把 `heightmap` 指向 `heightmap.png`
- 固定把 `terrain_data` 指向 `terrain.terrain`
- 不自动写入 `rivers`
- 不自动写入 `provinces`
- `height_scale` 取当前系统默认值 `100.0`

### 6.2 `map_data/materials/descriptor.toml`

缺失时生成以下最小合法内容：

```toml
version = 1
materials = []
```

理由：

- 当前 reader 允许空数组
- 不强行注入伪造材质槽
- 让后续材质导入继续成为显式作者操作

### 6.3 `map_data/biome_settings.toml`

缺失时生成以下最小合法内容：

```toml
version = 1
biomes = []
layers = []
modifiers = []
```

理由：

- 当前 reader 允许空数组
- 不伪造默认 biome / layer / modifier
- 避免生成后立刻引入假的领域语义

---

## 7. 启动链路调整

### 7.1 资源 bootstrap

`EditorBootstrapService` 的前置流程调整为：

1. 解析 `gameRoot` 与资源层
2. 确保 `map_data/default.toml` 存在，不存在则生成
3. 读取 `default.toml`
4. 确保 `map_data/biome_settings.toml` 存在，不存在则生成
5. 确保 `map_data/materials/descriptor.toml` 存在，不存在则生成
6. 解析 `heightmap` 虚拟路径
7. 若 `heightmap` 文件存在，按正常链路加载
8. 若 `heightmap` 文件缺失，构造“待补资源”会话并继续返回

关键变化是：

- `default.toml` 不再必须先物理存在
- `heightmap` 不再作为 bootstrap 的致命缺失项
- `biome_settings.toml` 与 `descriptor.toml` 不再要求用户手工先建文件

### 7.2 会话模型

`EditorResourceSession` 需要显式表达“地形源是否就绪”，不能继续把 `Heightmap` 当成无条件可用字段。

推荐新增的会话语义：

- `Heightmap`：可为空
- `PendingResources`：待补资源列表
- `HasPendingHeightmap`：快速判定标志

最低要求是让上层明确区分两种状态：

1. 工程配置已加载，且地形源已就绪
2. 工程配置已加载，但地形源缺失，当前处于待补资源状态

---

## 8. `heightmap` 缺失时的行为

### 8.1 启动行为

如果 `default.toml` 中声明的 `heightmap` 路径解析后不存在：

- Editor 主窗口继续启动
- `_resourceSession` 仍然建立成功
- 不创建 terrain entity
- 不加载 height cache
- 不创建基于高度图尺寸的 runtime biome mask

### 8.2 日志行为

必须输出两类日志：

1. 资源层错误日志
   - 由 `Stride` logger 输出 `Error`
   - 内容必须包含缺失的 `heightmap` 绝对路径
2. Shell 控制台日志
   - `Error`: 明确指出 `heightmap` 缺失
   - `Warning`: 明确指出当前工程以“待补资源”模式启动，部分功能受限

推荐口径：

- `Error`: `Terrain workspace heightmap is missing: <path>`
- `Warning`: `Terrain workspace loaded with pending resources. Add the missing heightmap before save/export.`

### 8.3 UI / 状态语义

当前项目状态不再是“加载失败”，而是：

- 工程已加载
- 地形未加载
- 存在待补资源

这意味着：

- 设置面板和纯配置型 UI 可继续进入
- 控制台必须保留明显错误提示
- 依赖真实地形的工具不能假装可用

---

## 9. `TerrainManager` 边界

`TerrainManager.LoadFromResourceSession` 需要从“全有或全无”调整为“两段式加载”：

1. 先加载与地形实体无关的配置资源：
   - `descriptor.toml`
   - `biome_settings.toml`
   - 可选 `rivers`
2. 再根据 `heightmap` 是否存在决定是否创建 terrain entity

这样可以保证：

- 配置资源不会因为 `heightmap` 缺失而完全失去可见性
- “工程可进入”和“地形可编辑”被明确拆开

若 `heightmap` 缺失：

- 返回“无地形实体”的成功结果，而不是异常失败
- 保存最后一次加载错误到专门的待补资源诊断字段，而不是复用“加载崩溃”语义

---

## 10. Shell 与命令边界

### 10.1 `LoadEditorResourceSessionAsync`

Shell 不应再把 `entities.Count == 0` 直接判定为“Terrain workspace 加载失败”。

新的判定应为：

- 会话创建失败：启动失败
- 会话创建成功但 `heightmap` 缺失：启动成功，进入待补资源状态
- 会话创建成功且地形实体已创建：正常作者态

### 10.2 `Save`

缺失 `heightmap` 时：

- `Save` 继续禁止
- 日志明确提示原因是“高度真相源缺失”

原因：

- 当前保存链路会真实写回 `heightmap.png`
- 没有源高度图时，不应凭空生成或导出一个作者态真相文件

### 10.3 `Export .terrain`

缺失 `heightmap` 时：

- `Export .terrain` 继续禁止

原因：

- 当前 `.terrain` 导出依赖已加载的高度缓存
- 没有高度缓存时导出结果没有可信来源

### 10.4 地形相关工具

依赖真实高度缓存的功能应保持禁用或直接短路：

- Sculpt
- 基于高度采样的查询
- 需要 terrain bounds 的交互

本轮不要求重构所有工具状态栏，但至少不得出现误导性的“看起来可编辑、实际无底层数据”的状态。

---

## 11. 错误处理原则

### 11.1 仅修复“缺失”，不修复“损坏”

以下情况允许自动生成：

- 文件不存在

以下情况不得自动覆盖：

- TOML 无法解析
- TOML 缺失必需字段
- TOML 字段值非法
- `default.toml` 指向非法相对路径

这些都属于显式配置错误，应当：

- 报错
- 停止本次会话 bootstrap
- 保留原文件供用户修正

### 11.2 生成失败

如果自动生成目标文件时写入失败：

- 直接失败
- 错误信息必须包含目标路径

---

## 12. 测试策略

至少补充以下测试：

1. 缺失 `map_data/default.toml` 时自动生成最小合法文件
2. 缺失 `map_data/materials/descriptor.toml` 时自动生成空 `materials` 数组文件
3. 缺失 `map_data/biome_settings.toml` 时自动生成空数组骨架文件
4. 生成后的三个文件都能被当前 runtime reader 直接读过
5. `default.toml` 生成后若 `heightmap.png` 不存在，bootstrap 不抛异常，返回待补资源状态
6. Shell 在待补资源状态下打印错误与警告日志，而不是直接判定整个工程加载失败
7. 待补资源状态下 `Save` 被拒绝
8. 待补资源状态下 `Export .terrain` 被拒绝
9. Runtime 仍然不会因为缺失作者态 `heightmap` 而改变既有行为

---

## 13. 实施顺序

推荐按以下顺序落地：

1. 为三个 TOML 增加作者态骨架生成入口
2. 调整 `EditorBootstrapService`，先补齐文件再读
3. 扩展 `EditorResourceSession`，显式表达待补资源状态
4. 调整 `TerrainManager.LoadFromResourceSession` 为“两段式加载”
5. 调整 `EditorShellViewModel` 的成功/失败判定与控制台日志
6. 收紧 `Save` / `Export` 的待补资源拦截
7. 补齐自动生成与待补资源回归测试

---

## 14. 验收标准

完成后应满足：

1. 删除 `map_data/default.toml` 后再次启动 Editor，会自动生成该文件
2. 删除 `map_data/materials/descriptor.toml` 后再次启动 Editor，会自动生成该文件
3. 删除 `map_data/biome_settings.toml` 后再次启动 Editor，会自动生成该文件
4. 若仅缺失 `heightmap.png`，Editor 主窗口仍能启动
5. 若仅缺失 `heightmap.png`，控制台会明确显示缺失路径与“待补资源”提示
6. 若仅缺失 `heightmap.png`，不会创建 terrain entity
7. 若仅缺失 `heightmap.png`，`Save` 与 `Export .terrain` 都会被明确阻止
8. 已存在但损坏的 TOML 文件不会被自动覆盖，而是明确报错
9. Runtime 对资源严格性的现有行为保持不变

---

## 15. 对现有文档的影响

实现完成后需要同步更新：

- `docs/ARCHITECTURE_OVERVIEW.md`
- `docs/CURRENT_FEATURES.md`
- `docs/design/map-data-toml-formats.md`

其中应新增或修正的口径包括：

- Editor 会自动补齐三个 TOML 骨架
- Editor 允许 `heightmap.png` 缺失并以“待补资源”模式进入
- `heightmap.png` 仍然是作者态真相源，不会被系统默认生成

---

## 16. 备注

- 本设计刻意不引入“内存占位高度图”，因为那会把“可启动”误扩展成“可编辑”。
- 用空数组生成 `descriptor.toml` 与 `biome_settings.toml`，比伪造默认材质和默认 biome 更干净。
- 这次调整的本质不是放宽所有资源要求，而是把“配置骨架缺失”和“作者数据缺失”分开处理。
