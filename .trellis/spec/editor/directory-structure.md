# 编辑器目录结构

> Terrain.Editor 独立编辑器的文件组织规范

---

## 项目信息

| 项目 | 目标框架 | 输出类型 | 作用 |
|------|----------|----------|------|
| Terrain.Editor | net10.0-windows (win-x64) | WinExe | 基于 Stride + ImGui 的独立地形编辑器 |

引用：Terrain + TerrainPreProcessor

---

## 目录布局

```
Terrain.Editor/
  EditorGame.cs                           -- Game 子类（场景设置、渲染循环）
  Program.cs                              -- 入口

  Effects/                                -- 编辑器专用着色器（Editor* 前缀）
    EditorTerrainDiffuse.sdsl + .sdsl.cs
    EditorTerrainDisplacement.sdsl + .sdsl.cs
    EditorTerrainForwardShadingEffect.sdfx + .sdfx.cs
    EditorTerrainHeightParameters.sdsl + .sdsl.cs
    EditorTerrainHeightStream.sdsl + .sdsl.cs
    ImGuiShader.sdsl + ImGuiShaderKeys.cs

  Input/
    HybridCameraController.cs             -- 编辑器相机控制

  Models/
    TerrainFileFormat.cs                  -- .terrain 文件格式模型

  Platform/
    FileDialog.cs                         -- OS 文件对话框
    WindowInterop.cs                      -- Win32 互操作

  Rendering/                              -- 编辑器端渲染（Editor* 前缀）
    EditorGlobalLodMap.cs
    EditorTerrainEntity.cs                -- 编辑器地形数据持有者
    EditorTerrainModeController.cs
    EditorTerrainProcessor.cs             -- EntityProcessor + EditorTerrainComponent
    EditorTerrainQuadTree.cs
    EditorTerrainRenderFeature.cs         -- Editor RootEffectRenderFeature
    Materials/
      MaterialEditorTerrainDiffuseFeature.cs
      MaterialEditorTerrainDisplacementFeature.cs
    SceneRenderTargetManager.cs
    ViewportRenderTextureSceneRenderer.cs

  Services/                               -- 核心业务逻辑
    BrushParameters.cs                    -- 单例：画笔参数
    ClimateEditor.cs                      -- 单例：气候蒙版笔刷服务
    ClimateMask.cs                        -- 气候蒙版数据（R8, 1/4 高度图）
    ClimateRuleService.cs                 -- 单例：气候定义和规则栈管理
    EditorPreferences.cs                  -- 用户偏好设置
    EditorState.cs                        -- 单例：当前工具状态（含 ActiveSeason）
    EraseTool.cs                          -- IPaintTool 实现
    HeightEditor.cs                       -- 单例：高度编辑逻辑
    HeightmapLoader.cs                    -- 高度图加载/校验
    IHeightTool.cs / IPaintTool.cs        -- 工具接口
    MaterialIndexMap.cs                   -- CPU 侧 splatmap 数据
    MaterialSlot.cs / MaterialSlotManager.cs  -- 单例：256 材质槽 + GPU 数组
    PaintBrushCore.cs                     -- 画笔数学共享
    PaintEditor.cs                        -- 单例：绘制编辑逻辑
    PaintTool.cs                          -- IPaintTool 实现
    ProjectManager.cs                     -- 单例：TOML 项目文件 I/O
    SplitTerrainConfig.cs
    TerrainManager.cs                     -- 中央地形生命周期管理
    TerrainRaycast.cs
    TerrainSplitter.cs
    TextureImporter.cs
    TomlProjectConfig.cs                  -- TOML 配置序列化

    Commands/                             -- 撤销/重做系统
      ICommand.cs                         -- 基础接口
      TerrainEditCommand.cs               -- 抽象基类（分块状态捕获）
      HeightEditCommand.cs                -- 高度编辑命令
      PaintEditCommand.cs                 -- 绘制编辑命令
      StrokeChunkTracker.cs              -- 笔画分块追踪
      HistoryManager.cs                   -- 单例：撤销/重做栈

    Export/                               -- 导出系统
      ExportManager.cs                    -- 单例：IExporter 注册表
      ExportProgress.cs
      IExporter.cs                        -- 导出接口
      Exporters/
        MaterialDescriptorExporter.cs
        TerrainExporter.cs                -- 导出 .terrain 运行时文件

  UI/                                     -- 自定义 ImGui UI
    Controls/                             -- 可复用控件
      Button.cs, CheckBox.cs, ControlBase.cs, Label.cs,
      NumericField.cs, Separator.cs, Slider.cs, TabController.cs,
      TextBox.cs, Toggle.cs
    Dialogs/                              -- 模态对话框
      ExportProgressDialog.cs, NewProjectWizard.cs
    Layout/
      LayoutManager.cs
    Panels/                               -- 面板（组合 UI 单元）
      AssetsPanel.cs, ClimateManagerPanel.cs, ConsolePanel.cs, GridTileRenderer.cs,
      InputsDataPanel.cs, PanelBase.cs, RightPanel.cs, RuleManagerPanel.cs,
      SceneViewPanel.cs, SculptModePanel.cs, ToolbarPanel.cs
    Styling/
      EditorStyle.cs, ColorPalette.cs, FontManager.cs, ...
    EditorUIRenderer.cs
    ImGuiExtension.cs
    MainWindow.cs
```

---

## 命名前缀规则

运行时与编辑器使用严格的命名前缀区分：

| 运行时（Terrain 项目） | 编辑器（Terrain.Editor 项目） |
|------------------------|-------------------------------|
| `TerrainComponent` | `EditorTerrainComponent` |
| `TerrainProcessor` | `EditorTerrainProcessor` |
| `TerrainRenderFeature` | `EditorTerrainRenderFeature` |
| `TerrainQuadTree` | `EditorTerrainQuadTree` |
| `MaterialTerrainDiffuseFeature` | `MaterialEditorTerrainDiffuseFeature` |
| `TerrainBuildLodLookup`（着色器） | 复用运行时的 Key 类 |

**规则**：编辑器中新增与运行时对应的类时，统一加 `Editor` 前缀。

---

## 命名空间规则

编辑器使用 **层级命名空间**，匹配目录结构：

```csharp
namespace Terrain.Editor;                             // 根（含 Effects/ 下的着色器 Key 类）
namespace Terrain.Editor.Input;                       // Input/
namespace Terrain.Editor.Models;                      // Models/
namespace Terrain.Editor.Platform;                    // Platform/
namespace Terrain.Editor.Rendering;                   // Rendering/
namespace Terrain.Editor.Rendering.Materials;          // Rendering/Materials/
namespace Terrain.Editor.Services;                    // Services/
namespace Terrain.Editor.Services.Commands;           // Services/Commands/
namespace Terrain.Editor.Services.Export;             // Services/Export/
namespace Terrain.Editor.Services.Export.Exporters;    // Services/Export/Exporters/
namespace Terrain.Editor.UI;                          // UI/
namespace Terrain.Editor.UI.Controls;                 // UI/Controls/
namespace Terrain.Editor.UI.Dialogs;                  // UI/Dialogs/
namespace Terrain.Editor.UI.Layout;                   // UI/Layout/
namespace Terrain.Editor.UI.Panels;                   // UI/Panels/
namespace Terrain.Editor.UI.Styling;                  // UI/Styling/
```

**例外**：`Effects/` 目录下的所有文件（包括手写的 `ImGuiShaderKeys.cs` 和自动生成的 `.sdsl.cs`/`.sdfx.cs`）使用根命名空间 `Terrain.Editor`，而非 `Terrain.Editor.Effects`。这是 Stride 着色器代码生成器的固定行为，手动 Key 文件也保持一致。

---

## 反模式

- 不要在 UI 面板中放业务逻辑——面板只负责渲染和调用 Service 方法
- 不要在 Services/ 外直接调用 ImGui——ImGui 调用只属于 UI/ 目录
- 不要混用编辑器和运行时的渲染代码——使用 Editor* 前缀隔离
- 不要在 UI/Controls/ 中放特定业务控件——通用控件放 Controls/，业务面板放 Panels/
