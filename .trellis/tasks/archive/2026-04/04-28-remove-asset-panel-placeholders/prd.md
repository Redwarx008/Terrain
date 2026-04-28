# 消除Editor资源面板占位符

## Goal

将 Editor 资源面板从硬编码假数据驱动替换为真实资产数据，参考旧 IMGUI 实现的模式：Paint 模式用 MaterialSlotManager 驱动纹理槽位，Sculpt 模式显示高度层，Foliage 模式显示植被项。

## What I already know

* 资产浏览器 UI 已在 commit 69e3874 中恢复（图标、搜索、分类标签）
* 整个资产列表由 `EditorShellViewModel.CreateAssetItemsForCategory()` 硬编码生成，4个分类各5个虚构资产名
* 当前4个分类 (Textures, Meshes, Foliage, Prefabs) 是通用文件浏览器模式，与项目实际需求不匹配

### 旧 IMGUI 实现的核心设计（commit 5616c1c 之前）

旧实现采用**模式驱动**方式，资产面板内容随当前编辑模式切换：

| 模式 | 数据来源 | 显示内容 |
|------|----------|----------|
| Sculpt | 硬编码 HeightLayer 列表 | 高度层（可见性/锁定切换） |
| Paint | `MaterialSlotManager.Instance` | 活跃材质槽位 + "添加纹理" 瓷贴 |
| Foliage | 硬编码 FoliageItem 列表 | 植被项网格 |

Paint 模式是最完整的实现：
- 从 `MaterialSlotManager.GetActiveSlots()` 获取非空槽位
- 每个槽位显示 AlbedoTexture 预览或占位符
- "+" 瓷贴触发文件对话框导入纹理
- 右键菜单可删除槽位
- 通过 TextureImporter 导入 + 自动发现法线贴图

### 仍存在的后端服务

* `MaterialSlotManager` — 256槽位材质管理单例，仍活跃使用中
* `MaterialSlot` — 槽位数据模型（Albedo/Normal/Properties 纹理路径和 GPU 纹理）
* `TextureImporter` — 文件到 GPU 纹理的导入器
* `EditorMode` — 编辑模式枚举（Sculpt, Paint, Foliage, Water, Landscape）

## Open Questions

* (无剩余阻塞问题 — 方向已明确：复用旧 IMGUI 的模式驱动设计)

## Requirements

* 资产面板应切换为模式驱动，内容随当前 EditorMode 变化
* Paint 模式：显示 MaterialSlotManager 的活跃槽位（有纹理的显示真实缩略图），末尾有 "+" 瓷贴用于添加新纹理
* 右键菜单支持删除已有槽位的纹理
* 点击空槽位（非 "+" 瓷贴）不触发文件选择器
* Sculpt 模式：显示高度层列表
* Foliage 模式：显示植被项
* 搜索过滤应在当前模式的内容中工作
* 去除与项目无关的通用分类 (Meshes, Prefabs)

## Acceptance Criteria

* [ ] 资产面板内容随 EditorMode 切换
* [ ] Paint 模式显示 MaterialSlotManager 活跃槽位（真实数据）
* [ ] "+" 瓷贴可触发文件选择器导入纹理
* [ ] 右键菜单支持删除槽位
* [ ] 无硬编码假数据（虚构资产名）
* [ ] 搜索在当前模式内容中过滤
* [ ] Sculpt/Foliage 模式显示相应内容（至少基础版本）

## Definition of Done

* Lint / typecheck / CI green
* 无残留硬编码假数据
* 占位符注释已清除

## Out of Scope

* Inspector 面板硬编码滑块/ComboBox（独立任务）
* 视口统计绑定（独立任务）
* Settings/Help 面板实现（独立任务）
* 文件系统级别的 Stride .sdpkg 资产扫描（暂不需要，Paint 模式已通过 MaterialSlotManager 管理纹理）

## Technical Approach

**模式驱动的资产面板** — 复用旧 IMGUI 的核心设计，但用 Avalonia 数据绑定实现：

1. 资产面板内容由 `CurrentEditorMode` 驱动，而非固定的通用分类标签
2. Paint 模式：显示 MaterialSlotManager 的活跃槽位（有纹理的显示缩略图），末尾有 "+" 瓷贴触发文件对话框添加新纹理
3. 纹理导入：点击 "+" 瓷贴 → 文件对话框 → TextureImporter 导入
4. 搜索：对当前模式的项目名称进行 `Contains` 过滤（与旧实现一致）
5. 预览：有 GPU 纹理的槽位显示真实缩略图，空槽位显示类型图标占位符

## Decision (ADR-lite)

**Context**: 旧 IMGUI 实现采用模式驱动而非通用文件浏览器；MaterialSlotManager 已是完善的纹理管理后端
**Decision**: 采用模式驱动设计，Paint 模式绑定 MaterialSlotManager，而非引入 Stride PackageSession 做通用资产扫描
**Consequences**: 与编辑器工作流一致；避免引入重型 Stride.Core.Assets 依赖；暂不支持浏览项目所有 .sdpkg 资产（可后续扩展）

## Technical Notes

* 核心文件: `EditorShellViewModel.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs`, `AssetBrowserItemViewModel.cs`
* 后端服务: `MaterialSlotManager.cs`, `MaterialSlot.cs`, `TextureImporter.cs`, `EditorMode.cs`
* 旧实现参考: commit 5616c1c 之前的 `AssetsPanel.cs`, `GridTileRenderer.cs`, `ImGuiExtension.cs`