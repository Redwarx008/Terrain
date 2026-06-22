# Native Viewport Airspace Overlays（Avalonia + SDL 视口）

**Topic**: Avalonia 覆盖层与 NativeControlHost/SDL child HWND 的 airspace 限制
**Date**: 2026-06-15
**Updated**: 2026-06-22
**Related Sessions**: [2026-06-15-editor-save-progress-native-airspace-fix](../2026/06/15/2026-06-15-editor-save-progress-native-airspace-fix.md), [2026-06-22-export-terrain-progress-window](../2026/06/22/2026-06-22-export-terrain-progress-window.md)

---

## Problem / Context

- Terrain Editor 的 Stride viewport 通过 Avalonia `NativeControlHost` 嵌入 Win32/SDL child HWND。
- Save 时 Avalonia inline overlay 的半透明背景能让 viewport 外区域变暗，但居中的 progress card 被 native child HWND 覆盖。
- 初次尝试隐藏部分 native HWND 后，用户截图仍显示白色 `Terrain Editor Viewport` native host 区域压在 overlay 上。
- 2026-06-22 Export Terrain 迁移到 baked detail 导出后，入口只把 `ExportProgress` 写入 Console，不再打开可见模态进度窗口；用户看到的是点击后进度条消失。

---

## Solution / Pattern

当 UI 必须显示在嵌入式 native viewport 上方时，使用 owned top-level window 承载关键内容：

```csharp
_saveProgressWindow = new SaveProgressWindow
{
    DataContext = _observedViewModel,
};

_saveProgressWindow.Show(this);

await Task.Yield();
```

Avalonia inline overlay 可继续用于禁用交互和让非 native 区域变暗，但不要把它作为 native viewport 上方关键 UI 的唯一承载层。Save 与 Export 这类长操作都应暴露显式 busy state（例如 `IsSaving` / `IsExporting`），由 `MainWindow` 监听并打开对应 owned top-level progress window。

---

## Key Insights

### 1. Avalonia ZIndex 只解决 Avalonia visual tree 内部排序
- `Panel.ZIndex`、XAML 顺序和 sibling/parent 调整不能让 Avalonia visual 稳定盖住 Win32 child HWND。

### 2. “viewport 外变暗、viewport 区域不变”是 airspace 信号
- 这种症状通常说明 binding 和 overlay visibility 已经生效，问题在 native child HWND 覆盖。

### 3. 隐藏部分 HWND 不是可靠 modal 策略
- SDL/NativeControlHost 可能存在多层 host/window 关系；隐藏一层可能暴露白底 host 或窗口 chrome，仍不能保证 overlay 可见。

### 4. 打开窗口后要给 UI loop 一次绘制机会
- 如果 `IsSaving=true` 后立刻执行同步 snapshot 捕获，owned window 可能已经创建但还没绘制首帧。
- 在同步重工作前 `await Task.Yield()`，让 UI 调度先处理窗口显示。

### 5. 进度报告不能只写 Console
- Console 适合保留操作日志，但不是 modal progress 的可见承载层。
- 对 Export 这类用户主动触发的长操作，`IProgress<T>` 应同时更新 ViewModel 的进度文本/百分比，让 owned window 有稳定绑定源。

---

## When to Use

- 保存、导入、导出等 modal progress 必须显示在 Stride viewport 之上。
- 任意 Avalonia 弹层需要跨过 `NativeControlHost` / SDL child HWND 的 airspace。

---

## When NOT to Use

- UI 只覆盖普通 Avalonia 控件，不跨 native child HWND。
- 弹层不需要跨 viewport，可限定在 inspector、asset browser 等 Avalonia 区域。

---

## Common Mistakes

### Mistake 1: 只调高 Avalonia ZIndex
**What to avoid:**
- 依赖 `Panel.ZIndex` 或把 overlay 放到更后面的 XAML 节点。

**Why it's bad:**
- native child HWND 不属于 Avalonia compositor 的正常绘制层级。

**Correct approach:**
- 关键 modal 内容使用 owned top-level window，或先用真实窗口截图验证 Popup 是否能跨过 native child HWND。

### Mistake 2: 用“XAML 中存在 ProgressBar”证明可见
**What to avoid:**
- 只写字符串测试检查 `ProgressBar` / 文案是否存在。

**Why it's bad:**
- 这无法证明 progress card 在 native viewport 上方可见。

**Correct approach:**
- 回归约束要体现 airspace 策略，例如检查 owned top-level window 生命周期或经验证的 native overlay coordinator。

---

## Related Patterns

- [Stride River Rendering Patterns](stride-river-rendering-patterns.md)

---

## References

- [Session: 2026-06-15 editor save progress native airspace fix](../2026/06/15/2026-06-15-editor-save-progress-native-airspace-fix.md)
