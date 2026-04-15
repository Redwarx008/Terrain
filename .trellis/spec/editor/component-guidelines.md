# 编辑器组件指南

> Terrain Editor 的 UI 控件层次与 ImGui 渲染模式

---

## 控件层次

```
ControlBase (抽象)          -- 基础控件：位置、大小、事件、生命周期、布局
  ├─ PanelBase (抽象)       -- 面板基类：标题栏、折叠/关闭、内容区域
  │    ├─ SceneViewPanel    -- 3D 视口
  │    ├─ ToolsPanel        -- 工具选择
  │    ├─ RightPanel        -- 属性/设置
  │    ├─ AssetsPanel       -- 资源浏览器
  │    ├─ ToolbarPanel      -- 顶部工具栏
  │    ├─ ConsolePanel      -- 日志输出
  │    └─ TextureInspectorPanel -- 纹理预览
  ├─ Button                 -- 按钮
  ├─ CheckBox               -- 复选框
  ├─ Slider                 -- 滑动条
  ├─ TextBox                -- 文本框
  ├─ NumericField           -- 数值输入
  ├─ Toggle                 -- 开关
  ├─ Label                  -- 文本标签
  └─ Separator              -- 分隔线
```

---

## ControlBase 生命周期

```csharp
Initialize()   -- 首次创建时调用一次
Update(dt)     -- 每帧调用，处理逻辑更新
Render()       -- 每帧调用，绘制 UI
Dispose()      -- 销毁时调用，释放资源
```

布局使用 Measure/Arrange 两遍模式：

```csharp
Measure(availableSize)   -- 计算期望大小
Arrange(finalRect)       -- 安排最终位置和大小
```

---

## 即时模式渲染

编辑器使用 **ImGui 即时模式** 渲染 UI，不是保留模式控件：

### OnRender 模式

面板通过重写 `RenderContent()` 方法直接调用 ImGui 绘制命令：

```csharp
// Terrain.Editor/UI/Panels/PanelBase.cs
protected override void RenderContent()
{
    // 子类重写，调用 ImGui API
}
```

### DrawList 直接绘制

自定义视觉元素使用 `ImGui.GetWindowDrawList()` 进行底层绘制：

```csharp
var drawList = ImGui.GetWindowDrawList();
drawList.AddRectFilled(min, max, color);
drawList.AddText(pos, color, text);
```

### 命中测试

使用 `ImGui.InvisibleButton()` 进行不可见的点击区域检测：

```csharp
ImGui.InvisibleButton($"##{Id}_hit", size);
if (ImGui.IsItemHovered()) { /* 鼠标悬停 */ }
if (ImGui.IsItemActive())  { /* 鼠标按下 */ }
```

---

## 控件标识

每个控件拥有唯一的 `Id`（8 字符十六进制字符串），用于 ImGui 控件标识：

```csharp
// ImGui 控件 ID 格式
$"{Id}##{suffix}"
```

`##` 后的 suffix 确保同一面板内多个同类控件的唯一性。

---

## 样式 Push/Pop

样式修改必须使用 try/finally 确保配对：

```csharp
ImGui.PushStyleColor(ImGuiCol.Button, color);
try
{
    // 绘制内容
}
finally
{
    ImGui.PopStyleColor();
}
```

---

## 模态对话框

对话框遵循创建→显示→处理结果→销毁模式：

- `NewProjectWizard` — 新建项目向导
- `ExportProgressDialog` — 导出进度对话框

---

## 反模式

- **不要在 `RenderContent()` 中放业务逻辑** — 渲染方法只负责绘制，逻辑放在 Service 层
- **不要每帧创建新的字体/纹理** — 资源在 `Initialize()` 中创建，`Dispose()` 中释放
- **不要硬编码像素尺寸** — 使用 `EditorStyle.ScaleValue()` 支持 DPI 缩放
- **不要在控件中直接修改 Service 状态** — 通过 Service 的公共方法间接操作
