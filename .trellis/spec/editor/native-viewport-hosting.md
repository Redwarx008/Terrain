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
- Avalonia `NativeControlHost` 返回的外层宿主 `HWND` 尺寸由 Avalonia 自己布局；不要再用 `RenderScaling` 后的物理像素手动放大这个外层窗口。
- 传入 SDL/Stride 的 `width`、`height` 必须是**物理像素**，不是 Avalonia 逻辑尺寸；使用 `TopLevel.RenderScaling` 做换算。
- 当宿主已经交给 Avalonia `NativeControlHost` 自动定位/缩放时，SDL 最终使用的物理像素尺寸应优先从真实子窗口 `GetClientRect(...)` 读取，而不是再次手算。
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
| 把 `Bounds * RenderScaling` 同时用于 SDL 和外层 `NativeControlHost` 子窗口 | 高 DPI 下外层宿主二次放大，视口越界覆盖 Asset Browser / Inspector |
| 在 Avalonia 已完成原生宿主布局前就提前用估算值驱动 SDL resize | SDL/backbuffer 与真实宿主 client rect 不一致，表现为持续越界或裁切 |
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
PixelSize pixelSize = GetPixelSize();
childWindow.Resize(pixelSize.Width, pixelSize.Height);
viewportHost.Attach(childWindow.Handle, pixelSize.Width, pixelSize.Height);
```

- 问题：把 SDL 的物理像素尺寸直接拿去改外层 `NativeControlHost` 子窗口，会让宿主在高 DPI 下被二次放大，越界压住兄弟 UI。

#### Correct

```csharp
PixelSize pixelSize = GetPixelSize();
NativeChildWindow childWindow = new(parent.Handle, 1, 1);
viewportHost.Attach(childWindow.Handle, pixelSize.Width, pixelSize.Height);
```

- 原因：虽然拆开了外层宿主和 SDL 尺寸职责，但这里仍然依赖手算值，时序上可能早于 Avalonia 对原生宿主的最终布局。

#### Correct

```csharp
NativeChildWindow childWindow = new(parent.Handle, 1, 1);
viewportHost.Attach(childWindow.Handle, 1, 1);

Dispatcher.UIThread.Post(() =>
{
    TryUpdateNativeControlPosition();
    PixelSize pixelSize = childWindow.GetClientSize();
    viewportHost.Resize(pixelSize.Width, pixelSize.Height);
}, DispatcherPriority.Render);
```

- 原因：先让 Avalonia 完成 `NativeControlHost` 的实际摆放，再从真实 `HWND client rect` 回读物理像素尺寸给 SDL，可避免 DPI 和布局时序带来的错位。

---

## Scenario: Embedded Viewport Brush / Decal Overlay

### 1. Scope / Trigger

- Trigger: 需要在 Avalonia 承载的 Stride 原生 viewport 内显示笔刷投影、decal 或其他贴地世界空间覆盖层。
- 这是 Avalonia 宿主、Stride 渲染管线、深度缓冲读取和编辑器工具状态的交叉边界，属于高风险渲染集成。

### 2. Signatures

- `EmbeddedStrideViewportGame.EnsureBrushDecalRenderFeature(GraphicsCompositor graphicsCompositor)`
- `EmbeddedStrideViewportGame.CreateBrushDecalEntity()`
- `BrushDecalProcessor.Draw(RenderContext context)`
- `BrushDecalRootRenderFeature.Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)`

### 3. Contracts

- 嵌入式 viewport 内的世界空间覆盖层必须走 Stride 渲染链：`Component -> Processor -> RenderObject -> RootRenderFeature`。
- 不要尝试在 Avalonia 层直接给 Stride backbuffer 叠画笔刷 UI；Avalonia 无法直接绘制到这个原生 backbuffer 上。
- `RootRenderFeature` 里读取深度时，必须从 `graphicsDevice.Presenter.DepthStencilBuffer` 走 `ResolveDepthStencil(...)`，不要改成其他离屏 depth 来源。
- decal/brush 的可见性必须同步到 `RenderObject.Enabled`，不能只停留在组件布尔值上。
- 动态创建的 `GeometricPrimitive`、`DynamicEffectInstance` 等 GPU 资源必须有明确释放路径。

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| 尝试用 Avalonia overlay 直接覆盖 Stride 视口 | 画不到 backbuffer，或只得到与世界空间脱节的 2D 假象 |
| `RootRenderFeature` 使用当前 API 不存在的 pipeline processor 接法 | 直接编译失败 |
| `component.Enabled` 改变但未同步 `renderObject.Enabled` | 关闭投影后仍可能残留渲染 |
| 未释放 `GeometricPrimitive` / effect | 长时间运行或反复创建销毁后出现 GPU 资源泄漏风险 |
| 未使用 presenter depth 做深度重建 | embedded viewport 下 decal 可能完全不显示或投影位置错误 |

### 5. Good / Base / Bad Cases

- Good: 笔刷投影通过 `BrushDecalRootRenderFeature` 读取 presenter depth，在地形表面贴地显示，并受编辑器模式与右键相机控制共同约束。
- Base: 先只打通 `Component -> RenderObject -> RootRenderFeature` 链并确认单色 decal 能随鼠标移动，再逐步加入 falloff 和颜色逻辑。
- Bad: 把笔刷投影当成 Avalonia 层的普通 overlay，或直接照搬其他版本 Stride/Xenko 的过时 API。

### 6. Tests Required

- 构建验证：
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj` 必须通过
- 运行时验证：
  - Sculpt / Paint / Landscape 模式下悬停地形时可见贴地投影
  - 右键相机控制时投影隐藏
  - 无选中工具或非笔刷模式时投影隐藏
- 生命周期验证：
  - 反复打开/关闭 viewport 或销毁场景时无资源泄漏异常

### 7. Wrong vs Correct

#### Wrong

```csharp
// 试图在 Avalonia 层直接叠画 viewport 笔刷
overlayCanvas.Children.Add(brushPreview);
```

- 问题：这只能生成宿主 UI 覆盖，不会得到基于地形深度的贴地投影。

#### Correct

```csharp
var depthStencil = context.Resolver.ResolveDepthStencil(graphicsDevice.Presenter.DepthStencilBuffer);
_decalShader.Parameters.Set(DepthBaseKeys.DepthStencil, depthStencil);
```

- 原因：嵌入式 viewport 下，贴花需要直接读取 presenter 的深度缓冲来重建世界位置。

---

## Common Mistake: 外层宿主窗口和 SDL 共用一套物理像素尺寸

**Symptom**: viewport 本体越过底部 Asset Browser 或右侧 Inspector，像一整块原生窗口压在 Avalonia 布局之上。

**Cause**: `Bounds * RenderScaling` 生成的物理像素尺寸本来只该传给 SDL/Stride，但又被拿去 `Resize` 外层 `NativeControlHost` 子窗口，导致高 DPI 下放大两次。

**Fix**: 外层原生子窗口让 Avalonia 自己布局；仅对 `NativeStrideViewportHost.Attach/Resize` 传递物理像素宽高，并以真实 `HWND client rect` 为准。

**Prevention**: 以后看到 `RenderScaling` 时先区分“这是给 SDL/backbuffer 的物理像素”还是“这是给 Avalonia 宿主布局的逻辑区域”，不要混用。

## Common Mistake: 给嵌入式 viewport 宿主设置过大的 `MinHeight`

**Symptom**: 编辑器启动第一帧开始，viewport 同时压到上方工具栏和下方 Asset Browser；检查控件 `Bounds` 时可见类似 `Y=-15` 这种负偏移。

**Cause**: `NativeStrideViewportControl` 放在 `Grid RowDefinitions=\"Auto,*\"` 一类布局里时，如果给它设置了超过实际可用高度的 `MinHeight`，Avalonia 会按约束摆放控件并产生负向溢出。

**Fix**: 对嵌入式 viewport 宿主优先使用 `Stretch` 对齐，让它消费父布局给出的实际区域；不要给它设置会超过常见启动窗口可用高度的硬编码 `MinHeight`。

**Prevention**: viewport 宿主尺寸由父级 `Grid`/`Border` 决定，最小可用高度应通过窗口整体 `MinHeight` 或可调整面板设计保证，不要在 `NativeControlHost` 本体上额外强塞更大的最小高度。

## Common Mistake: 子窗口主动 Resize 破坏 Avalonia 布局所有权

**Symptom**: 高 DPI 下视口越界覆盖兄弟 UI，或在 resize 时出现短暂闪烁/尺寸跳动。

**Cause**: `NativeChildWindow` 自带 `Resize(w, h)` 方法并用 `MoveWindow` 强制设定尺寸，导致原生子窗口与 Avalonia `NativeControlHost` 的布局系统产生竞争。Avalonia 认为子窗口应该是一块区域，但 Win32 层面子窗口被拉到了物理像素尺寸。

**Fix**: 移除 `NativeChildWindow.Resize()`，子窗口尺寸完全交给 Avalonia `NativeControlHost.TryUpdateNativeControlPosition()` 摆放；需要物理像素尺寸时从 `GetClientRect` 回读。

**Prevention**: `NativeChildWindow` 只负责创建和销毁 HWND，不负责尺寸管理。尺寸唯一来源是 Avalonia 布局 → `TryUpdateNativeControlPosition()` → `GetClientRect` 回读。

## Common Mistake: 把调试状态栏当成长期 UI

**Symptom**: 视口标题栏塞满 `BackBuffer=...; Depth=...; ClientBounds=...` 一长串文本，挤压正常布局。

**Cause**: 排障时把底层诊断直接暴露到 UI 状态栏，问题修完后没有收回。

**Fix**: UI 只保留简洁状态，例如 `SDL viewport ready 1620x1066; mode Shaded.`；详细宿主诊断使用 `Debug.WriteLine(...)`。

**Prevention**: 视口问题排查完成后，检查是否仍有调试专用文案、颜色清屏或强制诊断开关暴露在正常运行路径上。

## Common Mistake: 只在 Avalonia Window.KeyBindings 里补全局快捷键

**Symptom**: `Ctrl+Z` / `Ctrl+Y` 等编辑器快捷键已经写在 `Window.KeyBindings`，但启动后或点击 3D 视口后不生效；只有先点击 toolbar/menu 等 Avalonia 控件后才恢复。

**Cause**: 嵌入式 SDL/Stride 视口拥有独立原生子 `HWND`，并且视口聚焦时会通过 Win32 `SetFocus` 把键盘焦点交给 SDL 窗口。此时 Avalonia 的 `Window.KeyBindings` 收不到键盘消息，单纯补 XAML 绑定只能覆盖 Avalonia 焦点路径。

**Fix**: 保留 `Window.KeyBindings` 处理普通 Avalonia 焦点路径；对于必须在视口聚焦时也可用的编辑器级快捷键，在 SDL/Stride 窗口消息边界做轻量桥接，再回调 ViewModel 现有命令。不要把这类问题修成系统级 `RegisterHotKey`，否则会抢占编辑器进程外的全局快捷键。

**Prevention**: 新增“编辑器级快捷键”时先判断它是否需要在 3D 视口持有焦点时生效。若需要，必须同时验证 Avalonia 控件焦点路径和 SDL 视口焦点路径；PR/任务验收里写明“不点击 toolbar，视口聚焦后快捷键仍可用”。如果通过 SDL 窗口 WndProc 做桥接，必须在 Stride game/window dispose 之前恢复原 WndProc，避免重启 viewport runtime 后桥接安装状态失真。
