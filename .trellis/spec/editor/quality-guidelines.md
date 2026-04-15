# 编辑器质量规范

> Terrain Editor 的 UI 质量、事件卫生和构建标准

---

## DPI 缩放

所有像素尺寸通过 `EditorStyle.ScaleValue()` 缩放，支持不同 DPI 设置：

```csharp
float width = EditorStyle.ScaleValue(200);  // 200 逻辑像素 → 实际像素
```

---

## 事件订阅卫生

### 规则：构造函数订阅，Dispose 取消订阅

```csharp
// Terrain.Editor/UI/Panels/ToolsPanel.cs
public ToolsPanel()
{
    EditorState.Instance.HeightToolChanged += OnHeightToolChanged;
    EditorState.Instance.PaintToolChanged += OnPaintToolChanged;
}

public override void Dispose()
{
    EditorState.Instance.HeightToolChanged -= OnHeightToolChanged;
    EditorState.Instance.PaintToolChanged -= OnPaintToolChanged;
    base.Dispose();
}
```

遗漏取消订阅会导致：
- 面板销毁后回调仍被触发（NullReferenceException）
- 面板无法被垃圾回收（内存泄漏）

---

## 样式 Push/Pop 平衡

ImGui 样式修改必须用 try/finally 确保配对，防止样式泄漏到其他控件：

```csharp
ImGui.PushStyleColor(ImGuiCol.Button, color);
try
{
    // 绘制按钮
}
finally
{
    ImGui.PopStyleColor();
}
```

---

## 导出回滚

`ExportManager` 在导出失败时自动删除不完整的输出文件，防止用户误用损坏数据：

```csharp
// Terrain.Editor/Services/Export/ExportManager.cs
try
{
    await exporter.ExportAsync(path, progress, ct);
}
catch
{
    // 回滚：删除不完整文件
    if (File.Exists(path))
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
    throw;
}
```

---

## 画笔参数一致性

画笔参数通过 `BrushParameters` 单例集中管理，所有工具共享同一套参数实例，确保 UI 控件和编辑逻辑的参数始终同步。

---

## 构建验证

```bash
dotnet build -c Debug
```

编辑器项目构建为 win-x64 自包含应用，输出到 `Bin/Editor/Debug/`。

---

## 反模式

- **不要硬编码像素尺寸** — 使用 `EditorStyle.ScaleValue()` 支持 DPI 缩放
- **不要泄漏事件订阅** — 每个订阅必须在 Dispose 中对应取消
- **不要在主线程执行同步 I/O** — 使用 `async/await` 或后台线程
- **不要在渲染循环中分配对象** — 预分配缓冲区，复用对象
- **不要忽略导出失败** — 总是清理不完整的输出文件
