# Editor 作者态缺失材质降级加载设计

**Date**: 2026-06-15  
**Status**: Approved Draft  
**Author**: Codex

---

## 1. 背景

当前 Editor 作者态在加载 `map_data/materials/descriptor.toml` 与 `map_data/biome_settings.toml` 时仍采用“材质配置必须完全一致”的硬失败口径：

- `descriptor.toml` 缺失时会被 scaffold 重建为空 `materials` 数组
- 但若现有 `biome_settings.toml` 仍引用旧的 `material_id`
- `RuntimeBiomeSettingsReader` 与 `BiomeRuleService.ApplyRuntimeSettings(...)`
- 会因未知 `material_id` 直接抛异常，导致整个地形加载失败

此外，现有材质纹理回退能力只覆盖“完全没有材质数组”的场景：

- `EditorTerrainDiffuse` 仅在 `MaterialArraySize == 0` 时使用全局 `DefaultDiffuseTexture`
- 若只是某个槽位缺失 `albedo` 贴图
- 当前链路不会为该槽位自动写入默认切片

这与新的作者态容错目标不一致。用户要求：

1. 缺失 `descriptor.toml` 时 Editor 继续启动
2. 缺失哪个材质，就明确打印哪个材质的错误
3. 对缺失材质或缺失贴图的槽位，降级使用默认纹理
4. 不把运行时补位偷偷写回真实资源文件

本设计的核心是把“作者态容错显示”与“作者态资源真相”拆开：

- **文件内容仍由 `descriptor.toml` / `biome_settings.toml` 定义**
- **Editor 只在运行时做逐槽降级补位**

---

## 2. 目标

1. `descriptor.toml` 缺失后即使被重建为空文件，Editor 仍能继续启动并加载地形。
2. `biome_settings.toml` 中引用缺失 `material_id` 时，不再让加载链路崩溃。
3. 每个缺失 `material_id` 都映射到独立的运行时临时默认材质，而不是把所有问题压成一个全局槽位。
4. 已存在的材质条目若缺失贴图文件，只让缺失通道回退默认表现，不影响其它材质槽位。
5. 日志必须精确指出缺失的 `material_id`、贴图通道类型与绝对路径。
6. 仅缺贴图文件时允许 `Save` / `Export .terrain`；缺失 `material_id` 这种结构性不一致时禁止 `Save` / `Export .terrain`。
7. 运行时补位永不自动写回 `descriptor.toml` 或 `biome_settings.toml`。

---

## 3. 非目标

- 不自动往 `descriptor.toml` 写入伪造材质条目
- 不自动修改 `biome_settings.toml` 中缺失的 `material_id`
- 不把多个缺失材质合并成同一个匿名默认槽位
- 不在本轮放宽 Runtime 对资源一致性的要求
- 不在本轮生成真实缺失贴图文件
- 不在本轮引入“保存时自动修复 descriptor”能力
- 不在本轮改变用户显式导入材质贴图的现有工作流

---

## 4. 核心决策

| 主题 | 决策 |
|------|------|
| 真相源 | `descriptor.toml` 与 `biome_settings.toml` 继续是唯一作者态真相源 |
| 容错层级 | 仅 Editor 作者态运行时补位，不写回文件 |
| 缺失 `material_id` | 为每个缺失 id 合成独立运行时临时槽位 `Missing:<material_id>` |
| 缺失贴图文件 | 保留原槽位，仅缺失通道回退默认表现 |
| `albedo` 缺失 | 明确写入默认 diffuse 切片 |
| `normal` 缺失 | 继续使用 flat normal 默认切片 |
| `properties` 缺失 | 记录错误但不阻塞；当前 Editor shader 未消费该通道，不额外生成默认切片 |
| `Save` / `Export` | 仅缺贴图文件时允许；存在缺失 `material_id` 时禁止 |
| UI 暴露 | 临时缺失材质槽位可见，用于诊断与定位 |
| 诊断粒度 | 每个缺失项单独记录，不做聚合吞并 |

---

## 5. 缺失问题分类

启动时需要区分两类问题。

### 5.1 资源文件缺失，但结构仍一致

指 `descriptor.toml` 中材质条目存在，且 `biome_settings.toml` 引用的 `material_id` 也存在，只是某个贴图文件缺失：

- `albedo`
- `normal`
- `properties`

这类问题视为**可降级资源缺失**：

- Editor 允许继续启动
- 地形允许继续加载
- `Save` 允许
- `Export .terrain` 允许
- 保存时保留原始文件名，不把默认纹理路径写回 descriptor

### 5.2 结构性不一致

指 `biome_settings.toml` 引用了 `descriptor.toml` 中不存在的 `material_id`。

常见入口包括：

- `descriptor.toml` 被删除后重建为空文件
- 用户手工删掉了某个材质条目
- `biome_settings.toml` 手工写入了未声明的 `material_id`

这类问题视为**可加载但不可保存的结构性降级**：

- Editor 允许继续启动
- 地形允许继续加载
- 缺失 `material_id` 会被映射到临时默认槽位
- `Save` 禁止
- `Export .terrain` 禁止

---

## 6. 运行时数据模型

需要在作者态加载链路中显式表达材质降级结果，而不是继续靠异常控制流程。

### 6.1 新的恢复结果对象

引入一个仅供 Editor 使用的恢复结果对象，至少包含：

- 真实 descriptor 材质槽位
- 运行时临时缺失材质槽位
- 贴图缺失诊断列表
- 缺失 `material_id` 诊断列表
- `material_id -> slotIndex` 的最终映射
- 是否存在阻断 `Save` / `Export` 的结构性问题

该对象负责把“原始资源事实”和“运行时补位结果”分开。

### 6.2 `EditorResourceSession` 扩展

`EditorResourceSession` 需要新增只读诊断状态，用于 Shell 与后续命令判断：

- 是否存在材质结构性问题
- 是否仅存在可降级贴图缺失
- 材质降级诊断集合
- `CanSaveAuthoringResources`
- `CanExportTerrainData`

新的保存/导出可用性规则：

- `HasPendingHeightmap == true` -> 禁止保存/导出
- 存在缺失 `material_id` -> 禁止保存/导出
- 仅缺贴图文件 -> 允许保存/导出

---

## 7. 启动链路

`EditorBootstrapService` 与 `TerrainManager.LoadFromResourceSession(...)` 的职责保持分层。

### 7.1 `EditorBootstrapService`

仍只负责：

- 解析工作区根目录
- 确保 `default.toml` / `biome_settings.toml` / `descriptor.toml` 存在
- 返回 `EditorResourceSession`

它**不**负责：

- 推断缺失 `material_id`
- 合成临时材质槽位
- 判定单个材质贴图是否存在

### 7.2 `TerrainManager.LoadFromResourceSession(...)`

改为以下四段式流程：

1. 读取 `descriptor.toml`
2. 基于 descriptor 构建基础材质槽位，并扫描贴图缺失
3. 读取 `biome_settings.toml`，对未知 `material_id` 执行运行时补位
4. 将最终槽位映射应用到 `MaterialSlotManager` 与 `BiomeRuleService`

关键结果：

- 不再让 `RuntimeBiomeSettingsReader` / `BiomeRuleService.ApplyRuntimeSettings(...)` 因未知 `material_id` 直接终止启动
- 地形几何加载与材质资源一致性解耦

---

## 8. 材质槽位补位规则

### 8.1 缺失 `material_id`

如果某个 `material_id` 在 `biome_settings.toml` 中存在，但 descriptor 中不存在：

- 为该 `material_id` 创建单独的运行时临时槽位
- 槽位名称固定为 `Missing:<material_id>`
- 槽位 `MaterialId` 保持原始缺失 id，便于日志与 UI 定位
- 槽位使用默认 diffuse 表现
- 槽位不写回 descriptor

如果有多个缺失 `material_id`：

- 每个 id 都获得自己的槽位
- 不共享单一默认槽位

这样可以确保 layer 仍然保留原始语义归属：

- `plains` 缺失就落到 `Missing:plains`
- `rock` 缺失就落到 `Missing:rock`

而不是全部混成同一个“默认材质”。

### 8.2 缺失 `albedo`

若材质条目存在但 `albedo` 文件缺失：

- 保留该槽位原始 `MaterialId`
- 为该槽位显式注入默认 diffuse 切片
- 其它通道仍按实际存在情况加载

原因：

- 当前 shader 只有在 `MaterialArraySize == 0` 时才使用全局 `DefaultDiffuseTexture`
- 单槽缺失不能依赖现有 shader 自动回退
- 必须由 `MaterialSlotManager` 在材质数组构建阶段补齐默认切片

### 8.3 缺失 `normal`

若 `normal` 文件缺失：

- 保留该槽位
- 继续使用现有 flat normal 默认切片
- 不影响该槽位的 diffuse 表现

### 8.4 缺失 `properties`

若 `properties` 文件缺失：

- 保留该槽位
- 记录错误日志
- 不阻塞加载
- 不额外生成默认 properties 切片

理由：

- 当前 `EditorTerrainDiffuse.sdsl` 声明了 `MaterialPropertiesArray`
- 但当前最终着色路径并未实际采样该通道
- 因此本轮不引入未被消费的伪默认贴图语义

若未来 Editor shader 开始消费 `properties`：

- 需要单独提出变更，为其定义中性默认值与数组补位策略

---

## 9. `BiomeRuleService` 映射规则

当前 `BiomeRuleService.ApplyRuntimeSettings(...)` 在遇到未知 `material_id` 时会直接抛：

```csharp
throw new InvalidDataException($"Unknown material_id '{sourceLayer.MaterialId}' in biome settings.");
```

本轮调整后，该层不再承担“拒绝未知材质”的职责，而改为消费前置恢复结果提供的最终映射：

- 若 `material_id` 真实存在 -> 使用真实槽位索引
- 若 `material_id` 缺失 -> 使用对应临时槽位索引

`BiomeRuleService` 的目标从“校验并拒绝”变为“应用已经恢复好的层到槽位映射”。

这能保证：

- 现有 layer 结构保留
- 现有 modifier 结构保留
- 仅材质视觉表现降级

---

## 10. 日志与 UI

### 10.1 资源层日志

每个问题都必须单独打印 `Log.Error`。

建议消息格式：

- 缺失 `material_id`：
  - `Terrain material id 'plains' is referenced by biome settings but missing from descriptor: <descriptor-path>`
- 缺失 `albedo`：
  - `Terrain material 'plains' is missing albedo texture. Falling back to default diffuse: <absolute-path>`
- 缺失 `normal`：
  - `Terrain material 'plains' is missing normal texture. Falling back to flat normal: <absolute-path>`
- 缺失 `properties`：
  - `Terrain material 'plains' is missing properties texture: <absolute-path>`

### 10.2 Shell 控制台

Shell 控制台需要把诊断同步给用户：

- 每个具体问题单独 `Error`
- 若至少存在一个问题，再补一条汇总 `Warning`

汇总警告分两类：

- 仅缺贴图文件：
  - `Terrain workspace loaded with degraded materials. Missing texture files are using default fallback visuals.`
- 存在缺失 `material_id`：
  - `Terrain workspace loaded with degraded materials. Fix descriptor material ids before save/export.`

### 10.3 材质 UI 可见性

材质列表中应能看到临时槽位：

- 名称显示 `Missing:<material_id>`
- 便于用户确认到底是哪一个材质引用断裂

已有真实槽位若发生贴图回退：

- 详情文案应明确指出当前通道正在使用默认表现
- 但该槽位仍被视为真实作者态槽位

---

## 11. `Save` / `Export` 规则

### 11.1 允许的情况

如果仅存在贴图文件缺失：

- `Save` 允许
- `Export .terrain` 允许

保存时行为：

- descriptor 继续写回原始相对文件名
- 不把默认纹理路径写回
- 不自动删除缺失字段

### 11.2 禁止的情况

如果存在缺失 `material_id`：

- `Save` 禁止
- `Export .terrain` 禁止

原因：

- 临时槽位不是作者态真相
- 若允许保存，会把运行时补位与真实资源边界混淆
- 若允许导出，会把结构性不一致的工程误判为健康工程

Shell 行为：

- `Save` 时打印 `Warning`
- `Export` 时打印 `Warning`
- 明确说明需要先修复 `descriptor.toml` 中缺失的材质声明

---

## 12. 对现有组件的影响

### 12.1 `MaterialSlotManager`

需要支持：

- 基于恢复结果应用真实槽位与临时槽位
- 为缺失 `albedo` 的槽位生成默认 diffuse 切片
- 继续为缺失 `normal` 的槽位提供 flat normal
- 保持已有槽位索引稳定

### 12.2 `TerrainManager`

需要支持：

- 启动时执行材质恢复
- 保存最近一次材质降级诊断
- 把诊断传给 Shell

### 12.3 `BiomeRuleService`

需要支持：

- 接收恢复后的最终 `material_id -> slotIndex` 映射
- 不再直接把未知 `material_id` 当作致命异常处理

### 12.4 `EditorShellViewModel`

需要支持：

- 在加载完成后打印逐项材质错误
- 根据 session 中的材质结构性问题决定是否阻止 `Save` / `Export`
- 在控制台显示汇总 Warning

---

## 13. 测试计划

至少补以下回归覆盖。

### 13.1 缺失 `descriptor.toml` 被重建为空

1. 删除 `map_data/materials/descriptor.toml`
2. 保留现有 `biome_settings.toml`，其中继续引用多个已有 `material_id`
3. 再次启动 Editor

断言：

- 启动成功
- 地形继续加载
- 每个缺失 `material_id` 都生成对应的临时槽位
- 每个缺失 `material_id` 都打印独立错误
- `Save` / `Export` 被阻止

### 13.2 仅缺失 `albedo`

断言：

- 只有该槽位使用默认 diffuse
- 其它槽位不受影响
- `Save` / `Export` 允许
- 保存后 descriptor 中仍保留原始文件名

### 13.3 仅缺失 `normal`

断言：

- 使用 flat normal
- diffuse 继续正常显示
- `Save` / `Export` 允许

### 13.4 仅缺失 `properties`

断言：

- 打印错误
- 不阻塞启动
- `Save` / `Export` 允许

### 13.5 混合场景

场景：

- 一个材质缺 `albedo`
- 一个材质缺 `normal`
- 一个 layer 引用了 descriptor 中不存在的 `material_id`

断言：

- 只降级出问题的槽位
- 真实存在的材质正常加载
- 结构性问题仍会阻止 `Save` / `Export`

### 13.6 行为钉死测试

需要新增行为测试，防止未来回归到“遇到未知 `material_id` 直接崩”：

- `RuntimeBiomeSettingsReader` 不再把 Editor 作者态缺失 `material_id` 视为整个启动失败
- `BiomeRuleService.ApplyRuntimeSettings(...)` 不再作为最终致命拒绝点
- `MaterialSlotManager` 对单槽缺 `albedo` 的回退不能退化成“仅全空时才回退”

---

## 14. 风险与取舍

### 14.1 为什么不把所有缺失材质都映射到一个默认槽

这样虽然实现更简单，但会丢失诊断精度：

- 用户无法知道到底是 `plains` 丢了还是 `rock` 丢了
- 多个 layer 会在 UI 中混成一个匿名材质

因此不采用。

### 14.2 为什么不自动把临时槽位写回 descriptor

这样会把运行时补位伪装成真实作者态数据：

- 生成出来的条目并非用户明确声明
- 还会把默认纹理语义污染到作者态资源文件

因此不采用。

### 14.3 为什么只对缺失 `material_id` 禁止保存/导出

贴图文件缺失属于外部资源暂时不可达：

- descriptor 结构仍然完整
- biome settings 结构仍然完整

但缺失 `material_id` 属于结构性断裂：

- descriptor 与 biome settings 已经失配
- 这时继续保存/导出会放大不一致

因此二者必须区分。

---

## 15. 成功标准

满足以下条件即可认为本设计落地成功：

1. 删除 `descriptor.toml` 后再次启动 Editor，不再因未知 `material_id` 导致地形加载失败。
2. 缺失哪个材质，就能在日志与材质 UI 中明确定位到哪个材质。
3. 缺失 `albedo` / `normal` 时，只有对应槽位退化到默认表现。
4. 仅缺贴图文件时可以继续保存与导出。
5. 存在缺失 `material_id` 时，Editor 会明确禁止保存与导出。
6. 不会把运行时临时缺失材质写回到 `descriptor.toml`。

