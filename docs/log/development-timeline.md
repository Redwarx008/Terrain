# 开发时间线 (2026-04 ~ 2026-05)

**来源：** Trellis 归档任务记录 + Git 提交历史
**注意：** 2026-04-15 之前的会话日志记录在 `docs/log/2026/04/`，之后无会话日志，时间线基于 Trellis 任务归档补充。

---

## 阶段一：Avalonia 迁移 (2026-04-23 ~ 2026-04-30)

| 日期 | 任务 | 描述 |
|------|------|------|
| 04-23 | editor-avalonia-migration | 编辑器从 ImGui 迁移到 Avalonia MVVM 架构 |
| 04-25 | editor-gui-metro-light | 重新设计编辑器 GUI Metro Light 主题 |
| 04-26 | avalonia-migration-wiring | 修复 Avalonia 迁移后编辑器功能接线问题 |
| 04-26 | migrate-imgui-features-to-avalonia | 将 ImGui 功能迁移到 Avalonia |
| 04-27 | viewport-out-of-bounds | 修复视口越界问题 |
| 04-28 | restore-asset-panel | 恢复资源面板图标与功能 |
| 04-28 | fix-texture-thumbnails | 修复资源面板纹理缩略图 |
| 04-28 | remove-asset-panel-placeholders | 移除资源面板占位符 |
| 04-29 | fix-brush-projection | 修复 Avalonia 迁移后笔刷投射问题（屏幕空间 Decal） |
| 04-29 | terrain-lighting-regression | 修复缩略图改动后地形光影丢失回归 |
| 04-30 | fix-material-index-map-overflow | 修复大地形 MaterialIndexMap 数组溢出（半分辨率 SplatMap） |
| 04-30 | fix-undo-redo-after-avalonia-migration | 修复 Avalonia 迁移后撤销重做功能 |

**关键架构决策：**
- 放弃共享纹理路线，选择 Avalonia `NativeControlHost` + SDL `GameFormSDL`
- SDL 视口黑屏根因：`CreateWindowFrom(existing HWND)` 不可靠，必须用 `GameFormSDL + SetParent`
- SplatMap 从 1:1 改为 1/2 分辨率，文件格式 v2→v3

---

## 阶段二：Biome 规则系统 (2026-04-30 ~ 2026-05-02)

| 日期 | 任务 | 描述 |
|------|------|------|
| 04-30 | biome-rulelayer | Biome map + RuleLayer 地形纹理化 |
| 05-01 | review-f1b6323-biome-rule-layer | 审查 biome rule layer 迁移提交 |
| 05-02 | align-modifier-falloff-semantics-to-reference | 对齐 modifier falloff 语义到 Unity 参考 |
| 05-02 | biome-mask-brush-fix | 修复 biome mask 笔刷未应用选中 biome |
| 05-02 | fix-material-slot-sidebar | 修复材质槽侧栏缩略图 |

**关键架构决策：**
- Climate→Biome 全面重命名
- 移除手动画笔（PaintEditor/PaintMaterialTool），纹理分布完全由规则驱动
- 6 种 BiomeModifier，5 种混合模式
- Falloff 向范围外延伸（Unity 语义），不再向范围内收缩

**已知缺陷（来自审查）：**
- H1: Noise 修改器 FBM 被替换为单八度，Octaves 参数无效
- H2: Modifier Opacity 在 UI 中无编辑入口，锁死 100%
- H3: LayerHeatmap 调试图 >3 层时始终错误
- H4: TextureMask 修改器暴露在 UI 但功能链路未闭合
- M2: 修改器迭代方向与参考实现相反

---

## 阶段三：编辑器打磨 (2026-05-03 ~ 2026-05-04)

| 日期 | 任务 | 描述 |
|------|------|------|
| 05-03 | editor-save-pipeline | 修复 Editor 保存链路 |
| 05-03 | editor-settings-height | Editor Settings Mode — 地形高度设置 |
| 05-03 | export-pipeline-material-descriptor | 导出管线材质描述符（→ 重命名为 BiomeConfig） |
| 05-03 | fix-file-menu-missing | 迁移后 File 菜单项丢失修复 |
| 05-03 | fix-undo-redo-shortcut | 修复撤销/重做快捷键激活 |
| 05-03 | remove-sculpt-advance | 删除 Sculpt 编辑器 Advance 栏 |
| 05-03 | review-terrain-editor-runtime-persistence-check | 审查运行时持久化 |
| 05-03 | toolbar-view-hover-style | 修复 ToolBar View 按钮 hover 样式 |
| 05-04 | toolbar-bg-color | 工具栏背景色改为 #F8FAFC |

---

## 阶段四：道路河流系统 (2026-05-06 ~ 2026-05-14)

| 日期 | 任务 | 描述 |
|------|------|------|
| 05-06 | editor-road-river-drawing | 编辑器道路和河流绘制 |
| 05-12 | vic3-road-river-rework | Vic3 风格道路河流渲染重做 |

**关键架构决策：**
- 放弃高度图写回塑形，改为贴图 + alpha + depth bias + 贴地渲染
- 道路样式用专用枚举（Dirt/Paved），不再复用 MaterialSlotIndex
- DepthBias = -2000 / SlopeScaleDepthBias = -10 解决 z-fighting
- 河流：余弦深度曲线、流动动画、边缘淡出
- 首版只做道路（土路 + 铺砌路），河流后续补齐

---

*时间线版本: 1.0 — 基于 Trellis 归档任务记录*