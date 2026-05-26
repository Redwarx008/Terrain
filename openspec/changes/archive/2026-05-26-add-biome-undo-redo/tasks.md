## 1. 枚举扩展

- [x] 1.1 在 `TerrainDataChannel` 枚举中添加 `Biome` 成员

## 2. BiomeEditCommand 实现

- [x] 2.1 新建 `Terrain.Editor/Services/Commands/BiomeEditCommand.cs`，继承 `TerrainEditCommand`
- [x] 2.2 实现 `CaptureBeforeChunk` — 从 `BiomeMask.GetRawData()` 行拷贝 byte[] chunk
- [x] 2.3 实现 `CaptureAfterStateAndFilter` — 抓取 after-state，过滤 unchanged chunk
- [x] 2.4 实现 `Execute` / `Undo` — 逐 chunk 回放 byte[] 数据，调用 `MarkBiomeMaskDirty()` + `RegenerateMaterialIndices()`
- [x] 2.5 实现 `GetDataWidth` / `GetDataHeight`、`AffectedChannel`、`Description`、`EstimatedSizeBytes`

## 3. BiomeEditor 生命周期重构

- [x] 3.1 添加 `BeginStroke(TerrainManager, biomeId)` — 创建 `BiomeEditCommand`，调用 `HistoryManager.BeginCommand()`
- [x] 3.2 在 `ApplyStroke` 开头添加 `HistoryManager.MarkCommandChunks()` 调用（先标记后写入）
- [x] 3.3 添加 `EndStroke()` — 调用 `HistoryManager.CommitCommand()`
- [x] 3.4 添加 `CancelStroke()` — 调用 `HistoryManager.CancelCommand()`

## 4. Viewport 笔触调度接入

- [x] 4.1 修改 `BeginBrushStroke` 中 `case EditorMode.Paint` — 调用 `BiomeEditor.Instance.BeginStroke()`
- [x] 4.2 修改 `EndBrushStrokeIfNeeded` 中 `case EditorMode.Paint` — 调用 `BiomeEditor.Instance.EndStroke()`
- [x] 4.3 修改 `CancelBrushStrokeIfNeeded` 中 `case EditorMode.Paint` — 调用 `BiomeEditor.Instance.CancelStroke()`

## 5. 构建验证

- [x] 5.1 `dotnet build` 通过，无编译错误
- [x] 5.2 启动编辑器，验证 biome 笔刷 Ctrl+Z/Ctrl+Y 功能正常
