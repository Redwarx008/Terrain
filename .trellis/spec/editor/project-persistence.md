# Project Persistence

> Editor 项目保存链路的可执行合同。适用于修改 `EditorShellViewModel`、`TerrainManager`、`ProjectManager`、`TomlProjectConfig` 或导出真源时。

## Scenario: Editable Terrain Snapshot Persistence

### 1. Scope / Trigger

- Trigger: 修改 `Save` / `Save As` / `Open` / 导出真源，且链路跨越 `ViewModel -> Service -> Config -> File IO`。
- 目标：防止再次出现“内存中的高度/蒙版已修改，但 `.toml`、PNG 快照、重开结果不一致”的漂移。

### 2. Signatures

- `EditorShellViewModel.SaveProject()`
- `EditorShellViewModel.SaveProjectAs()`
- `TerrainManager.SaveProject()`
- `TerrainManager.SaveProjectAs(string projectFilePath, string projectName)`
- `ProjectManager.SaveConfigAs(string projectFilePath, TomlProjectConfig config)`
- `TomlProjectConfig.WriteTo(string tomlFilePath)`

### 3. Contracts

- `Save`:
  - 只在 `ProjectManager.IsProjectOpen == true` 时执行。
  - 保持当前 `HeightmapPath` / `BiomeMaskPath` 的引用语义。
  - 但必须把当前内存中的 `HeightDataCache` 与 `BiomeMask` 写回这些路径。
  - 如果存在道路/河流等可重建的路径塑形层，项目保存必须写入路径塑形的**基准高度**，并把路径网络/参数写入 `.toml`；重开项目后再由路径数据重建可见路床/河槽，避免保存重开后重复压平或重复挖深。
  - 路径几何表现相关但会影响重建结果的样式参数（例如 `width`、`depth`、`side_slope`、`corner_span`）必须作为 `path_features` 的持久化字段保存；不要把它们留成仅存在于运行期算法常量或 UI 临时状态。
  - 普通高度编辑命令（Raise/Lower/Smooth/Flatten）的提交、撤销、重做必须同步更新路径塑形基准高度，再从基准高度重建路径层；不能只修改当前可见 `HeightDataCache`，否则下一次移动/删除路径会用旧基准覆盖用户后续雕刻。
  - 如果路径缺失，必须落到当前项目目录内的默认快照：
    - 高度图：`<project>/heightmaps/<source-name-or-terrain_heightmap.png>`
    - 蒙版：`<project>/splatmaps/<source-name-or-terrain_biome_mask.png>`

- `Save As`:
  - 必须生成新项目目录内的高度图与蒙版快照。
  - 新 `.toml` 必须指向这些新项目内资源，而不是旧项目目录或旧外部路径。
  - 成功后，`ProjectManager.ProjectFilePath`、`TerrainManager.CurrentTerrainPath`、`TerrainManager.CurrentBiomeMaskPath` 都必须切到新路径。

- `Open` / `Reopen`:
  - 切换项目或移除当前地形时，必须清空待加载的暂存状态（例如延迟加载的 biome mask 路径）。
  - 打开失败时，不能保留旧的 `ProjectFilePath` / `ProjectName` 继续伪装成“项目仍然打开”。
  - 如果 biome mask 依赖 `TerrainLoaded -> TryLoadPendingBiomeMask()` 这类延迟消费链，暂存路径必须在启动高度图加载之前就准备好，不能在 `LoadTerrainAsync(...)` 之后再补写。
  - 如果 `LoadProject()` 通过 `pendingBiomeMaskPath` 把蒙版路径交给 `LoadTerrainAsync(...)`，则 `LoadTerrainAsync(...)` 内部的 `RemoveCurrentTerrain()` 不能把这份“当前重开流程正在使用的暂存路径”提前清掉；要么显式保留，要么改成别的传参方式。
  - 重新打开项目之前，必须先清空 `MaterialSlotManager` 的旧路径和旧 GPU 纹理缓存，不能把上一项目残留的槽位状态混进新项目。
  - 材质纹理恢复如果依赖 `MaterialTexturesLoadRequired` 之类的事件，不能假设事件触发当下就一定有可用 `CommandList`；必须支持挂起并在首个可渲染帧补执行，否则会出现 biome mask 已恢复但实际地表材质未重新绑定的假象。
  - 恢复 `biome_layers` / `biome_modifiers` 后，必须把后续 layer/modifier ID 分配器 rebase 到已恢复最大 ID 之后；保存后重开再添加 modifier 时不能复用 TOML 中已有的 modifier ID。
  - 恢复完成后必须把 `EditorState.SelectedRuleIndex` clamp 到有效 layer 范围，并同步 `CurrentBiomeId` 到选中 layer；不能让 UI 选中旧项目中已经不存在的 layer 后继续执行 Add/Remove modifier 命令。

- 导出真源:
  - `.terrain v6` 只允许持久化：
    - heightmap VT
    - biome mask VT
    - MinMaxErrorMap / header 元数据
  - 导出器**不能**把 `MaterialIndexMap` / detail index / detail weight 当成导出真源写入 `.terrain`。
  - Runtime 必须从 `.terrain` 的 `heightmap + biome mask`，再结合 `BiomeConfigPath` 指向的 TOML biome 规则重新生成 detail maps。
  - 如果 Runtime 仍依赖 detail index / weight，必须在加载期重建，而不是要求 Editor 预烘焙并持久化这些派生图。
  - 与项目保存不同，当前 Runtime 尚无道路/河流路径重建系统时，`.terrain` 导出应使用编辑器当前可见高度（包含路径塑形结果），而不是仅导出路径基准高度。

### 4. Validation & Error Matrix

| 条件 | 处理 |
|---|---|
| `Save` 时项目未打开 | ViewModel 转调 `SaveProjectAs()` |
| `Save` / `Save As` 时 `heightDataCache == null` | `TerrainManager` 直接返回，不写 `.toml` 快照 |
| `Save As` 时有已打开项目但当前无地形 | 允许 `ProjectManager.SaveProjectAs(...)` 复制当前 TOML 配置 |
| `Save As` 时既无项目也无地形 | UI 显示 `Nothing to save.` |
| 保存目标目录不存在 | `ProjectManager.SaveConfigAs` / `TomlProjectConfig.WriteTo` 负责创建目录 |
| 当前资源路径为空 | 生成项目内默认快照路径，不能把空路径写回 `.toml` |
| 项目含路径塑形层 | 保存 heightmap 基准高度 + TOML 路径数据；重开后重建塑形 |
| 调整路径拐角展开参数 | `path_features[*].corner_span` 必须写入并在重开后恢复，避免同一路径重开后转角视觉变化 |
| 已有路径后继续普通高度雕刻 | 高度编辑命令把 chunk delta 同步进路径基准高度，然后重建路径层 |
| 普通高度编辑撤销/重做 | 同步对路径基准高度应用 before/after delta，避免 undo/redo 后基准与可见高度漂移 |
| 导出 `.terrain` 且 Runtime 没有路径重建 | 导出当前可见高度，包含路径塑形 |

### 5. Good / Base / Bad Cases

- Good:
  - 打开引用外部高度图的项目，编辑高度后点击 `Save`，外部高度图文件被覆盖，重开项目后高度保持一致。
  - 打开项目后点击 `Save As` 到新目录，新目录出现 `heightmaps/` 和 `splatmaps/` 快照，删掉旧项目目录后新项目仍可独立重开。

- Base:
  - 仅修改 biome 规则和材质槽位，`Save` 仍会刷新 `.toml`，并在已有蒙版路径存在时同步写回蒙版。
  - 重开项目时，即使材质纹理数组需要等到后续渲染帧才能上传，最终也必须自动补齐，不允许用户靠再次编辑 biome 才“碰巧刷新出来”。
  - 重开包含已有 modifier stack 的项目后，点击 Add Modifier 会追加新 modifier 并分配新 ID，不会覆盖或复用已恢复 modifier。
  - 保存包含道路/河流路径的项目，heightmap 文件保持未重复烘焙的基准高度；重开项目后路径 mesh 与路床/河槽由 TOML 路径数据恢复。
  - 调整路径 `Corner Span` 后保存并重开，转角展开程度保持一致，不会回退到默认值。
  - 创建道路/河流后继续用 Raise/Lower/Smooth/Flatten 雕地形，再移动路径节点；普通雕刻结果仍保留，只重新叠加当前路径塑形。

- Bad:
  - `Save As` 先把旧 `cachedConfig` 写到新路径，再单独写新快照。这会产生“新 `.toml` 指向旧资源”的窗口期，也容易让后续维护者复制出错误流程。
  - `LoadProject()` 先设置好了 `pendingBiomeMaskPath`，但 `LoadTerrainAsync()` 开头又调用 `RemoveCurrentTerrain()` 把它清空。表现会变成：compute 确实 dispatch 了，但拿到的仍是默认全 0 mask。

### 6. Tests Required

- Build:
  - `dotnet build Terrain.sln`

- Manual regression:
  - `Save` 外部引用场景：
    - 编辑高度与 biome mask。
    - 点击 `Save`。
    - 重新打开同一项目，断言高度与蒙版都保留。
  - `Save As` 快照场景：
    - 从已有项目另存为到空目录。
    - 断言新目录生成 `heightmaps/*.png` 与 `splatmaps/*.png`。
    - 打开新 `.toml`，断言 `terrain.heightmap` / `terrain.biome_mask` 指向项目内相对路径。
- 导出一致性场景：
    - 修改高度后分别执行“直接导出”和“保存 -> 重开 -> 导出”。
    - 断言导出的 `.terrain` 使用同一组高度与 biome mask 真源。
    - 断言 Runtime 打开这两个 `.terrain` 后，基于同一份 TOML 规则重建出一致的材质结果。
- 路径塑形保存/重开：
    - 创建道路或河流路径并保存项目。
    - 重开项目后断言路径数据恢复，且路床/河槽没有比保存前进一步压低或挖深。
    - 移动或删除路径节点后断言旧路床/河槽痕迹不残留。
    - 已有路径后执行普通高度编辑，提交、撤销、重做、再移动路径节点，断言路径基准高度与可见高度不会漂移。

### 7. Wrong vs Correct

#### Wrong

```csharp
terrainExporter.Write(detailIndexData, detailWeightData); // 把派生图当真源
```

#### Correct

```csharp
terrainExporter.Write(heightData, biomeMaskData); // Runtime 再读 TOML 规则重建 detail maps
```

#### Wrong

```csharp
// 普通地形编辑只改可见高度，路径基准仍停在旧快照。
heightData[index] = editedHeight;
```

#### Correct

```csharp
// HeightEditCommand 提交/撤销/重做时，把相同 chunk delta 并入路径基准。
pathFeatureService.ApplyExternalHeightEditDeltas(changedChunks, applyAfterState: true);
```
