## ADDED Requirements

### Requirement: Biome stroke undo
系统 SHALL 支持撤销 biome 笔刷的单次笔触操作，将受影响的 BiomeMask 区域恢复到笔触开始前的状态。

#### Scenario: 单次笔触撤销
- **WHEN** 用户在 Paint 模式下完成一次 biome 笔触后按 Ctrl+Z
- **THEN** 笔触覆盖区域的 BiomeMask 数据恢复到笔触前的 biome ID
- **AND** SplatMap 重新生成以反映恢复后的 biome 分布
- **AND** 3D 视图中地形纹理回到笔触前的状态

#### Scenario: 多次笔触依次撤销
- **WHEN** 用户连续执行 3 次 biome 笔触，然后依次按 Ctrl+Z 3 次
- **THEN** 每次撤销恢复对应笔触的 BiomeMask 数据
- **AND** 撤销顺序为后进先出（最后一次笔触最先被撤销）

### Requirement: Biome stroke redo
系统 SHALL 支持重做已撤销的 biome 笔触操作。

#### Scenario: 撤销后重做
- **WHEN** 用户撤销一次 biome 笔触后按 Ctrl+Y
- **THEN** BiomeMask 数据恢复到撤销前的状态（重做该笔触）
- **AND** SplatMap 重新生成

#### Scenario: 新操作清空重做栈
- **WHEN** 用户撤销一次笔触后，执行新的 biome 笔触
- **THEN** 重做栈被清空，之前的撤销不可重做

### Requirement: No-op stroke filtering
系统 SHALL 过滤无实际变更的 biome 笔触，不将其加入历史栈。

#### Scenario: 在已覆盖区域重复绘制
- **WHEN** 用户在已被 biome ID=3 覆盖的区域再次用 biome ID=3 绘制
- **THEN** 该笔触不被加入 undo 栈
- **AND** 按 Ctrl+Z 撤销的是上一次有效笔触，而非本次空操作

### Requirement: Undo/Redo UI feedback
系统 SHALL 通过菜单项状态反映 undo/redo 的可用性。

#### Scenario: 无历史时菜单禁用
- **WHEN** 编辑器启动后未执行任何 biome 笔触
- **THEN** Edit > Undo 菜单项为禁用状态
- **AND** Edit > Redo 菜单项为禁用状态

#### Scenario: 有历史时菜单启用
- **WHEN** 用户执行一次 biome 笔触后
- **THEN** Edit > Undo 菜单项为可用状态，显示 "Undo Biome Paint"
