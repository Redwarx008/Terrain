# 恢复资源面板图标与功能

## Goal

将资源面板从当前占位符状态恢复到完整功能——包括实际纹理预览、类型图标、固定标签页分类、搜索过滤、资源选中交互、添加/导入操作等。

## Requirements

### 标签页分类（固定，4 个）
- **Textures** — 纹理资源，显示实际纹理图片预览
- **Meshes** — 模型资源，显示类型图标
- **Foliage** — 植被资源，显示类型图标
- **Prefabs** — 预制体，显示类型图标
- 移除 Materials 和 Brushes 分类

### 图标与预览系统
- Texture 类型：从磁盘加载纹理文件的实际缩略图预览（BitmapImage）
- Mesh/Tree/Shrub/Grass/Prefab 等类型：使用 Segoe MDL2 Assets 图标字体显示对应类型图标
- Create/空槽位：显示 "+" 图标
- 预览区域使用 PreviewBackground 颜色作为背景（不再硬编码 #3A3A3A）
- 选中状态有视觉反馈（高亮边框 + 图标/边框颜色变化）

### 搜索与过滤
- 搜索框绑定 ViewModel，实时过滤资源项
- 搜索范围包括 Name 字段

### 交互功能
- 资源项选中事件（通知 ViewModel 当前选中的资源）
- "Add Asset" / "+" 项提供添加资源的入口（打开文件对话框导入）
- 右键上下文菜单（Import Texture / Clear / Delete）
- Texture 的空/非空槽位区分显示

### 列表视图
- 实现列表视图作为网格视图的替代（ViewModel 已有 IsGridView 切换）
- 列表视图：小图标 + 名称 + 类型，水平排列

## Acceptance Criteria

- [ ] 4 个标签页（Textures, Meshes, Foliage, Prefabs）可切换
- [ ] Texture 类型显示实际纹理预览图
- [ ] 非纹理类型显示对应的 Segoe MDL2 图标
- [ ] 预览区域使用 PreviewBackground 颜色
- [ ] 搜索框实际过滤资源项
- [ ] 选中资源项有视觉反馈并触发 ViewModel 事件
- [ ] "Add Asset" 项可点击（打开导入对话框或触发命令）
- [ ] 网格/列表视图切换可用
- [ ] 右键上下文菜单可用

## Definition of Done

- 代码通过 typecheck
- 资源面板各标签页功能可用
- 无回归（其他面板不受影响）
- ViewModel 数据结构为后续接入真实 Stride 资产数据库预留扩展性

## Technical Approach

### ViewModel 层改动
1. `AssetBrowserItemViewModel` 扩展：
   - 添加 `PreviewImage` 属性（BitmapImage? 类型，用于纹理预览）
   - 添加 `IconGlyph` 属性（string，Segoe MDL2 图标码，用于非纹理类型）
   - 添加 `IsEmpty` 属性（bool，区分空槽位）
   - 保留 `PreviewBackground`/`PreviewForeground` 用于背景色和图标色

2. `EditorShellViewModel` 改动：
   - `AssetCategories` 改为 4 项：Textures, Meshes, Foliage, Prefabs
   - 添加 `AssetSearchText` 属性 + 过滤逻辑
   - 添加 `SelectedAssetItem` 属性 + 选中事件
   - `CreateAssetItemsForCategory()` 为每种 Kind 分配对应图标 glyph
   - Texture 类型尝试加载实际预览图（从项目纹理目录）

### View 层改动
1. Tab 栏：绑定 `AssetCategories` 集合，动态生成标签页按钮
2. ItemTemplate：使用 DataTemplate Selector 或绑定转换区分纹理预览 vs 图标显示
   - 纹理：`<Image Source="{Binding PreviewImage}" />`
   - 图标：`<TextBlock FontFamily="Segoe MDL2 Assets" Text="{Binding IconGlyph}" />`
3. 搜索框绑定 `AssetSearchText`
4. 列表视图模板实现
5. 右键菜单添加

### 图标映射 (Segoe MDL2 Assets)
| Kind | Glyph | Unicode |
|------|-------|---------|
| Texture | Image | &#xE71B; |
| Mesh | Cube/Capability | &#xE80A; |
| Tree | MapLeaf | &#xEC7A; |
| Shrub | MapLeaf | &#xEC7A; |
| Grass | MapLeaf | &#xEC7A; |
| Prefab | Package | &#xE7B8; |
| Create | Add | &#xE710; |

## Out of Scope (explicit)

- 完整的 Stride 缩略图编译管线（GPU 渲染 3D 预览）
- 从 Stride 资产数据库加载真实资源元数据
- 拖拽操作
- 资源属性编辑面板
- Height Layers 面板（Sculpt 模式专用）

## Technical Notes

- IMGUI 旧代码参考：`git show ad8da9c:Terrain.Editor/UI/Panels/AssetsPanel.cs`
- IMGUI GridTileRenderer：`git show ad8da9c:Terrain.Editor/UI/Panels/GridTileRenderer.cs`
- 当前 Avalonia 面板：`Terrain.Editor/Views/MainWindow.axaml` 行 178-239
- 当前 ViewModel：`Terrain.Editor/ViewModels/EditorShellViewModel.cs` 行 959-1178
- 当前 Item VM：`Terrain.Editor/ViewModels/AssetBrowserItemViewModel.cs`
- Stride 缩略图系统参考：`E:\WorkSpace\stride\sources\editor\Stride.Editor\Thumbnails\`
