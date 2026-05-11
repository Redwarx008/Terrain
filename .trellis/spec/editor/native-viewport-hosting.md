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

## Scenario: Embedded Viewport Tool Gizmos

### 1. Scope / Trigger

- Trigger: 在 `EmbeddedStrideViewportGame` 承载的自研 Stride 视口里显示编辑工具提示，例如路径节点、选中点、吸附点或连接点。
- 这是 Stride scene、工具状态、资源生命周期和编辑器模式切换的交叉边界。

### 2. Signatures

- `PathFeatureService.SetGizmosVisible(bool visible)`
- `PathFeatureService.RefreshNodeGizmos()`
- `GeometricPrimitive.Sphere.New(GraphicsDevice graphicsDevice)`
- `ModelComponent.RenderGroup = IEntityGizmo.PickingRenderGroup`

### 3. Contracts

- 嵌入式 viewport 内的工具 gizmo 必须作为 Stride scene entity / `ModelComponent` 管理，不要用 Avalonia overlay 叠到 SDL backbuffer 上。
- 可以复用 Stride 自带 gizmo 约定和 primitive：`Stride.Engine.Gizmos.IEntityGizmo.PickingRenderGroup`、`GeometricPrimitive.Sphere/Cube/...`。
- 不要假设 Game Studio 的 `GizmoComponentAttribute` 自动扫描服务在本编辑器中存在；本编辑器没有接入 `EditorGameComponentGizmoService` 时，需要由本地服务显式创建、更新、隐藏和释放 gizmo entity。
- gizmo 可见性必须跟编辑模式同步；切出对应工具模式时隐藏，删除/重开/清理场景时移除实体并释放 `GeometricPrimitive` 等 GPU 资源。
- 选中、共享连接、普通控制点等状态变化必须刷新材质或模型状态，不能只更新路径 mesh。

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| 使用 Avalonia overlay 表示 3D 节点 | 提示与地形/相机深度脱节，无法正确随世界位置变化 |
| 只添加 `[GizmoComponent]` 但没有接入 Game Studio gizmo service | gizmo 不会自动出现 |
| 切出工具模式未隐藏 gizmo | 非编辑模式下残留工具提示，干扰视口 |
| 删除路径节点未移除对应 gizmo entity | 场景中留下孤立提示点 |
| 未释放 `GeometricPrimitive` | 长时间编辑或频繁重开后存在 GPU 资源泄漏风险 |

### 5. Good / Base / Bad Cases

- Good: 工具服务维护 gizmo handle 字典；创建时使用 `GeometricPrimitive.Sphere.New(...)` + `ModelComponent`，更新时同步位置/材质/可见性，清理时移除 entity 并释放 primitive。
- Base: 先显示固定颜色球体确认 gizmo 在嵌入式视口可见，再加入选中/连接状态色。
- Bad: 仅依赖 `GizmoComponentAttribute`，或用 Avalonia Canvas 在原生视口上方画 2D 点。

### 6. Tests Required

- 构建验证：
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj` 必须通过
- 运行时验证：
  - 进入对应编辑模式后可见 gizmo
  - 切出模式、右键相机控制或清理地形后 gizmo 不残留
  - 创建、删除、撤销/重做、保存重开后 gizmo 数量与控制点一致
- 生命周期验证：
  - 反复创建/删除路径节点时无 GPU 资源释放异常

### 7. Wrong vs Correct

#### Wrong

```csharp
[GizmoComponent(typeof(PathNodeComponent), true)]
public sealed class PathNodeGizmo : IEntityGizmo
{
}
```

- 问题：自研嵌入式 viewport 没有自动扫描并实例化 Game Studio gizmo 服务时，这个类型不会自己显示。

#### Correct

```csharp
GeometricPrimitive sphere = GeometricPrimitive.Sphere.New(graphicsDevice);
var component = new ModelComponent
{
    Model = model,
    RenderGroup = IEntityGizmo.PickingRenderGroup,
};
scene.Entities.Add(new Entity("PathNodeGizmo") { component });
```

- 原因：在当前编辑器里显式把 Stride gizmo primitive 放入 editor scene，生命周期由工具服务控制。

---

## Scenario: Path Feature Curve Sampling

### 1. Scope / Trigger

- Trigger: 道路、河流或类似路径工具需要从控制点生成可见 mesh、拾取几何或地形塑形区域。
- 这是编辑器路径拓扑、视口交互、保存格式和地形高度缓存的交叉边界。

### 2. Signatures

- `PathFeature.NodeIds`
- `PathFeatureService.BuildCurvePoints(PathFeature feature)`
- `PathFeatureService.EnumerateCurveSegments(PathFeature feature)`
- `PathFeatureService.EvaluateCentripetalCatmullRom(...)`

### 3. Contracts

- 路径拓扑、显式连接、撤销/重做和 TOML 保存必须继续基于控制点节点图；不要把可编辑拓扑改成只保存采样点。
- 几何表现层使用 centripetal Catmull-Rom 采样曲线；道路/河流 mesh、曲线段拾取、地形压平/挖河道都应基于采样曲线段。
- 两个有效控制点时必须退回控制点折线，保证最小路径也可见可编辑。
- 三个及以上控制点时才进行 Catmull-Rom 平滑；端点可以通过重复端控制点形成稳定首尾段。
- 插入节点仍要映射回原控制点段的插入索引，不能插入到采样点列表。
- 采样密度必须有上限/间距常量，避免长路径生成过密 mesh 或过慢高度塑形。
- 采样密度不能只按控制点段长固定分段；当控制点间距很短但转角很急时，仍必须按曲线弦偏差或等价曲率指标继续细分，否则中心线虽然声明为样条，转角视觉仍会退化成折角。
- 路径 mesh 的左右边界不能简单使用每个采样点的瞬时法线直接等宽外扩；在拐角处需要基于相邻切线计算 join 方向，并限制 miter 长度，避免外边界出现尖刺、内边界塌陷或看起来“不平滑”的折线感。

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| 保存采样点而不是控制点 | 共享节点、断开/重连和显式连接语义丢失 |
| mesh 使用样条但地形塑形仍用折线 | 可见道路/河流与路床/河槽不一致 |
| 拾取仍用折线 | 用户点击曲线中段时插入位置不符合视觉 |
| 两点路径强行跑样条 | 首条路径可能不可见或端点切线不稳定 |
| 采样过密且无上限思考 | 大地形长路网编辑时出现明显卡顿 |
| 只按控制点段长做固定采样 | 短距离急转角被欠采样，mesh 转角看起来仍然发折 |
| mesh 只按单点法线做左右外扩 | 拐角外侧容易拉尖，内侧容易挤压，视觉上不像平滑 join |

### 5. Good / Base / Bad Cases

- Good: `PathFeature.NodeIds` 存控制点；`BuildCurvePoints` 生成采样点；mesh、拾取、地形塑形共享 `EnumerateCurveSegments`。
- Base: 两点路径显示为直线；三点路径开始平滑，但节点 gizmo 仍显示控制点而不是所有采样点。
- Bad: 草绘时把每个采样点都保存成永久节点，或只让 mesh 平滑但地形修改仍走原折线。

### 6. Tests Required

- 构建验证：
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj`
- 运行时验证：
  - 两点路径可创建、可见、可保存重开。
  - 三点以上路径 mesh 平滑，路床/河槽沿同一平滑曲线。
  - 创建短距离急转角路径时，转角处不应退化成明显折角或尖刺。
  - 点击曲线段插入节点时，新节点插入到正确的控制点区间。
  - 共享节点和断开/重连操作仍作用于控制点节点图。

### 7. Wrong vs Correct

#### Wrong

```csharp
feature.NodeIds.Clear();
feature.NodeIds.AddRange(sampledPointIds);
```

- 问题：采样点污染拓扑，连接关系和用户控制点语义都会漂移。

#### Correct

```csharp
IReadOnlyList<PathCurvePoint> points = BuildCurvePoints(feature);
foreach (PathCurveSegment segment in EnumerateCurveSegments(feature))
{
    ApplyTerrainAlong(segment);
}
```

- 原因：控制点仍是真源，采样点只服务几何表现和地形塑形。

#### Wrong

```csharp
int sampleCount = Math.Max(1, (int)MathF.Ceiling(segmentLength / CurveSampleSpacing));
for (int sample = 1; sample <= sampleCount; sample++)
{
    result.Add(EvaluateCurve(sample / (float)sampleCount));
}
```

- 问题：这种“只按段长固定采样”的做法在短距离急转角上会欠采样，视觉上仍像折线。

#### Correct

```csharp
SubdivideCurveSegmentByDeviation(
    startPoint,
    endPoint,
    maxChordLength: CurveSampleSpacing,
    maxDeviation: CurveSubdivisionTolerance);
```

- 原因：同时约束弦长和曲线偏差，才能让短而急的拐角也保持平滑。

---

## Scenario: Path Mesh Wireframe Toggle

### 1. Scope / Trigger

- Trigger: 需要在编辑器 viewport 中为 `path mesh` 提供独立于地形的 wireframe 显示开关。
- 这是 Avalonia Inspector、ViewModel 状态、原生 viewport host 与 Stride 渲染 stage selector 的跨层集成。

### 2. Signatures

- `EditorShellViewModel.IsPathWireframeEnabled`
- `NativeStrideViewportHost.SetPathWireframeEnabled(bool enabled)`
- `EmbeddedStrideViewportGame.SetPathWireframeEnabled(bool enabled)`
- `EditorTerrainModeController.Apply(SceneViewMode mode, bool isPathWireframeEnabled, GraphicsCompositor graphicsCompositor)`

### 3. Contracts

- 地形 wireframe 仍由 `SceneViewMode.Wireframe` 控制；不要把 path mesh wireframe 再次绑定到 `SceneViewMode`。
- path mesh wireframe 必须通过独立布尔状态控制，并允许与 terrain shaded / wireframe 任意组合。
- path mesh 使用单独的 render group（当前为 `RenderGroup.Group1`）时，默认 `MeshTransparentRenderStageSelector` 必须排除这个 group，避免默认 opaque/transparent 路由覆盖自定义 wireframe selector。
- `MeshRenderFeature` 上必须挂 path 自己的 `WireframePipelineProcessor`，不能只依赖 terrain render feature 的 processor。
- 只要 terrain 或 path 任一方启用 wireframe，共用的 `SingleStageRenderer.RenderStage` 就必须指向 wireframe stage；两者都关闭时才置空。
- 右侧 Inspector 的 path wireframe UI 必须放在 Path 模式面板内，而不是复用全局 View 菜单或 terrain 的视图模式下拉。

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| path wireframe 继续复用 `SceneViewMode.Wireframe` | 用户无法单独查看 path 线框 |
| 默认 mesh selector 未排除 `Group1` | path 仍可能走默认 stage，勾选框表现失真 |
| 未给 `MeshRenderFeature` 添加 path 专属 wireframe processor | path 进入 wireframe stage 但栅格化状态不正确 |
| wireframe stage renderer 只看 terrain 状态 | terrain shaded + path wireframe 时没有任何线框输出 |
| 把开关放到 Path 模式之外 | 状态语义分散，用户难以理解当前控制对象 |

### 5. Good / Base / Bad Cases

- Good: terrain 用视图模式下拉切换，path 用右侧 `Path Wireframe` 勾选框切换，四种组合都可稳定显示。
- Base: terrain shaded + path wireframe 能看到仅道路/河流的线框覆盖。
- Bad: 切换 path wireframe 后，地形也一起切线框，或 path 根本不进入 wireframe stage。

### 6. Tests Required

- 构建验证：
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj`
- 运行时验证：
  - `SceneViewMode=Perspective` + `Path Wireframe=true` 时，只显示 path 线框。
  - `SceneViewMode=Wireframe` + `Path Wireframe=false` 时，只显示 terrain 线框。
  - 两者都开启时 terrain/path 都显示 wireframe。
  - 两者都关闭时恢复正常 shaded。

### 7. Wrong vs Correct

#### Wrong

```csharp
bool enableWireframe = mode == EditorTerrainViewMode.Wireframe;
ApplySelectorState(terrainRenderFeature, opaqueSelector, wireframeSelector, enableWireframe);
ApplySelectorState(pathMeshRenderFeature, pathOpaqueSelector, pathWireframeSelector, enableWireframe);
```

- 问题：terrain 和 path 被同一个布尔状态硬绑在一起，UI 无法做独立控制。

#### Correct

```csharp
bool enableTerrainWireframe = mode == EditorTerrainViewMode.Wireframe;
ApplySelectorState(terrainRenderFeature, opaqueSelector, wireframeSelector, enableTerrainWireframe);
ApplySelectorState(pathMeshRenderFeature, pathOpaqueSelector, pathWireframeSelector, isPathWireframeEnabled);
wireframeStageRenderer.RenderStage = enableTerrainWireframe || isPathWireframeEnabled ? wireframeStage : null;
```

- 原因：terrain/path 各自维护 selector 状态，但共享同一个 wireframe render stage 输出。

---

## Scenario: Path Sketch Simplification

### 1. Scope / Trigger

- Trigger: Path 工具支持拖拽草绘时，需要在收笔后自动把高密度草绘采样简化为少量控制点。
- 这是 viewport 输入、Path 参数面板、服务内拓扑维护、撤销重做与路网连接语义的跨层集成。

### 2. Signatures

- `PathFeatureParameters.IsSketchModeEnabled`
- `EmbeddedStrideViewportGame.UpdatePathEditing()`
- `PathFeatureService.BeginPointerEdit(Vector3 worldPosition, PathFeatureKind kind, bool sketchMode)`
- `PathFeatureService.ContinuePointerEdit(Vector3 worldPosition)`
- `PathFeatureService.EndPointerEdit(bool commit)`

### 3. Contracts

- 草绘必须有显式入口；除了 `Shift` 临时触发外，还要允许通过 Path 面板中的开关进入持续草绘模式。
- 草绘过程中可以按固定间距持续加点，但收笔时必须自动简化为控制点集合，不能把全部草绘采样永久写入 `NodeIds`。
- 简化必须保留首尾控制点。
- 如果草绘过程中某个节点已经与其他 feature 共享，简化时必须保留该共享节点，不能为了降点数破坏显式连接语义。
- 简化只应替换本次草绘连续追加出来的节点片段，不应误改 feature 上更早存在的控制点。
- 草绘简化必须走现有 `PathFeatureEditCommand`，保证撤销/重做看到的是“草绘完成后的最终控制点集”。

### 4. Validation & Error Matrix

| Condition | Result |
|---|---|
| 只有隐藏的 `Shift` 入口，没有显式开关 | 用户难以发现草绘功能 |
| 收笔后不做简化 | `NodeIds` 被高密度草绘点污染，后续编辑困难 |
| 简化不保留首尾点 | 路径起终点漂移 |
| 简化删除共享节点 | 现有路网连接被悄悄断开 |
| 简化替换了错误的 node 片段 | 老控制点丢失或路径拓扑错乱 |

### 5. Good / Base / Bad Cases

- Good: 打开 `Sketch Mode` 后拖拽画一段弯路，松手后路径保留少量控制点，但视觉曲线基本不变。
- Base: 使用 `Shift + 拖拽` 也能进入同一套草绘逻辑。
- Bad: 拖一小段之后 feature 上堆满密点，或者经过共享节点后路网连接被简化掉。

### 6. Tests Required

- 构建验证：
  - `dotnet build Terrain.Editor/Terrain.Editor.csproj`
- 运行时验证：
  - Path 面板勾选 `Sketch Mode` 后可直接拖拽画线。
  - 不勾选时，按住 `Shift` 仍可临时草绘。
  - 收笔后控制点数量明显少于草绘采样点数量。
  - 草绘经过共享连接节点时，收笔后共享连接关系仍存在。
  - 撤销/重做能正确恢复草绘简化前后的最终结果。

### 7. Wrong vs Correct

#### Wrong

```csharp
if (distance >= SketchPointSpacing)
{
    feature.NodeIds.Add(CreateNode(worldPosition));
}
```

- 问题：这只是在草绘期间不断堆控制点，没有收笔简化步骤。

#### Correct

```csharp
if (commit && operation.SketchMode)
{
    FinalizeSketch(operation);
}
```

- 原因：草绘采样与控制点落盘是两件事，必须在提交阶段把采样点收敛为可编辑控制点。

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
