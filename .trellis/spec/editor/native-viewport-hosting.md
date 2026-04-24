# Native Viewport Hosting

> Avalonia 编辑器中嵌入 Stride SDL 视口的可执行约定。

---

## Scenario: Avalonia NativeControlHost + SDL Viewport

### 1. Scope / Trigger

- Trigger: 在 `Terrain.Editor` 中通过 Avalonia `NativeControlHost` 承载 Stride 3D 视口。
- 这是 UI、Win32 宿主、SDL 和 Stride `GameContextSDL` 的交叉边界，属于高风险基础设施集成。

### 2. Signatures

- `NativeStrideViewportControl.CreateNativeControlCore(IPlatformHandle parent)`
- `NativeStrideViewportHost.Attach(IntPtr childHwnd, int width, int height)`
- `NativeStrideViewportHost.Resize(int width, int height)`
- `EmbeddedStrideViewportGame.SetViewportSize(int width, int height)`

### 3. Contracts

- Avalonia 控件必须先创建自己的原生子 `HWND`，再把该句柄交给 `NativeStrideViewportHost.Attach(...)`。
- 传入 SDL/Stride 的 `width`、`height` 必须是**物理像素**，不是 Avalonia 逻辑尺寸；使用 `TopLevel.RenderScaling` 做换算。
- SDL 宿主必须使用 `GameFormSDL` **自建窗口**，再通过 Win32 `SetParent` / `SetWindowLongPtrW` / `SetWindowPos` 重挂接到 Avalonia 子 `HWND`。
- 不要使用 `Stride.Graphics.SDL.Window(parentHwnd)` 或 SDL `CreateWindowFrom(existing HWND)` 直接绑定已有宿主窗口。
- `EmbeddedStrideViewportGame` 必须关闭“失焦等价最小化”：
  - `TreatNotFocusedLikeMinimized = false`
  - `DrawWhileMinimized = true`
- resize 时必须同时更新：
  - 原生 SDL 窗口 `ClientSize`
  - Win32 子窗口位置和尺寸
  - `GraphicsDeviceManager.PreferredBackBufferWidth/Height`

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| `childHwnd == IntPtr.Zero` | `Attach(...)` 直接抛 `ArgumentException` |
| 把 Avalonia 逻辑尺寸直接传给 SDL/Win32 | 高 DPI 下视口只覆盖左上角，宿主底色漏出 |
| 使用 `CreateWindowFrom(existing HWND)` | 可能出现 `ClientBounds=1x1`，首帧虽触发但画面黑屏 |
| 未关闭 `TreatNotFocusedLikeMinimized` | 嵌入式视口失焦后停绘，首帧或后续帧保持黑屏 |
| resize 只改宿主窗口，不改 backbuffer | presenter/backbuffer 与可见区域错位，出现黑边或裁切 |

### 5. Good / Base / Bad Cases

- Good: `GameFormSDL` 自建窗口后重挂接到 Avalonia 子 `HWND`，`ClientBounds`、`BackBuffer` 和可见区域一致。
- Base: 视口先以清屏色出图，再接入 `GraphicsCompositor`，用于验证 presenter/present 链路。
- Bad: 直接把现有 `HWND` 喂给 SDL，运行状态显示有首帧，但 `ClientBounds=1x1` 且画面持续黑屏。

### 6. Tests Required

- 启动冒烟：
  - 断言 Avalonia 主窗口可启动
  - 断言 SDL 视口进入 `runtime ready` / `ready` 状态
- 尺寸同步：
  - 在 100% 和高 DPI 缩放下验证视口铺满宿主区域
  - resize 后确认 `ClientBounds` 与 presenter/backbuffer 尺寸一致
- 失焦行为：
  - 切走焦点再切回，断言视口不黑屏、不停止首帧/后续帧
- 回归验证：
  - 保留一条最小 presenter-only 清屏诊断路径，必要时可快速确认宿主链是否通畅

### 7. Wrong vs Correct

#### Wrong

```csharp
Window window = new(parentHwnd);
GameContextSDL context = new(window, width, height, isUserManagingRun: true);
```

- 问题：看起来省事，但在当前工程里会出现 `ClientBounds=1x1` 和黑屏。

#### Correct

```csharp
_window = new GameFormSDL("Terrain Editor Viewport")
{
    Visible = true,
    FormBorderStyle = FormBorderStyle.None,
};

SetParent(_window.Handle, _childHwnd);
SetWindowPos(_window.Handle, IntPtr.Zero, 0, 0, width, height, flags);

_context = new GameContextSDL(_window, width, height, isUserManagingRun: true);
```

- 原因：SDL 先拥有自己的窗口生命周期，再由 Win32 负责重挂接，Stride `Window.ClientBounds` 和 presenter 尺寸才能稳定同步。

---

## Common Mistake: 把调试状态栏当成长期 UI

**Symptom**: 视口标题栏塞满 `BackBuffer=...; Depth=...; ClientBounds=...` 一长串文本，挤压正常布局。

**Cause**: 排障时把底层诊断直接暴露到 UI 状态栏，问题修完后没有收回。

**Fix**: UI 只保留简洁状态，例如 `SDL viewport ready 1620x1066; mode Shaded.`；详细宿主诊断使用 `Debug.WriteLine(...)`。

**Prevention**: 视口问题排查完成后，检查是否仍有调试专用文案、颜色清屏或强制诊断开关暴露在正常运行路径上。
