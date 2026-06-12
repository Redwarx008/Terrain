## Why

当前河流系统已能生成 ribbon mesh，但渲染仍是普通透明材质占位，无法呈现参考捕获中的河底、折射、水面流动、泡沫和连接处淡出效果。河流 mesh 已完成并经过修复，现在需要补齐正式渲染链路，才能让河流从调试几何进入可用视觉功能。

## What Changes

**River Rendering Architecture**
- From: `RiverRenderingService` 直接创建 `ModelComponent`、`MaterialDescriptor` 和 `VertexPositionNormalTexture` mesh。
- To: 新增 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature` 体系，`RiverRenderingService` 仅作为 Editor façade。
- Reason: 河流渲染需要专用 multi-pass、offscreen RT、dual-source blending 和自定义 vertex attributes。
- Impact: 非破坏性保留现有 Generate / Show Rivers 外部 API，但内部渲染路径会替换。

**River Vertex Contract**
- From: 河流 mesh 使用 `VertexPositionNormalTexture`，只能承载 position、normal、uv。
- To: 使用 `RiverVertex`，包含 Position、Transparency、UV、Tangent、Normal、Width、DistanceToMain，并绑定 `POSITION + TEXCOORD0..5`。
- Reason: 参考 river shader 依赖这些属性计算宽度、flow、fade 和连接处透明度。
- Impact: `RiverMeshService` 输出格式与相关测试需要迁移。

**River Multi-Pass Rendering**
- From: 单 pass 透明材质直接绘制到主场景。
- To: `RiverRenderFeature` 先执行 half-res bottom/refraction pass，再执行 full-res surface pass 采样 bottom/refraction RT。
- Reason: 参考渲染中 surface draw 依赖 bottom pass 输出。
- Impact: 新增 offscreen texture/depth 资源、effect/pipeline state 管理和 RenderDoc 验证要求。

**Shader and Resources**
- From: `RiverBottom.sdsl` / `RiverSurface.sdsl` 是简化占位 shader。
- To: 迁移为完整 river bottom/surface shader 结构，绑定中性命名的 water/bottom/environment 纹理资源与 `RiverRenderSettings`。
- Reason: 需要实现 bottom parallax、flow normal、water color、foam、reflection、edge fade 和 distance-to-main fade。
- Impact: 需要复制资源到项目目录、更新 shader asset generator 配置并运行 Stride asset workflow。

## Capabilities

### New Capabilities
- `river-component-rendering`: Defines river scene component, processor, render object lifecycle, visibility, clear, and editor service integration.
- `river-vertex-mesh`: Defines the river vertex contract and mesh output needed by the renderer.
- `river-multipass-rendering`: Defines half-resolution bottom/refraction pass, full-resolution surface pass, resource chain, blend/depth behavior, and RenderDoc expectations.
- `river-shader-resources`: Defines river shader parameter binding, neutral fallback inputs, copied texture resources, and asset compilation requirements.

### Modified Capabilities
- None.

## Impact

Affected systems:
- `Terrain.Editor/Services/RiverRenderingService.cs`
- `Terrain.Editor/Services/RiverMeshService.cs`
- `Terrain.Editor/Effects/RiverBottom.sdsl`
- `Terrain.Editor/Effects/RiverSurface.sdsl`
- `Terrain.Editor/Effects/RiverEffect.sdfx`
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- `Terrain.Editor/Rendering/RiverWireframeModeController.cs`
- `Terrain.Editor.Tests/Program.cs`
- `Terrain.Editor/Terrain.Editor.csproj`
- `Terrain.Editor/Terrain.Editor.sdpkg` if new asset folders are required

New code areas:
- `Terrain.Editor/Rendering/River/` for `RiverComponent`, `RiverProcessor`, `RiverRenderObject`, `RiverRenderFeature`, `RiverRenderResources`, `RiverRenderSettings`, `RiverVertex`, and resource loading.
- `Terrain.Editor/Assets/River/` for water, bottom, and environment textures with neutral names.

Validation impact:
- Existing river mesh tests must remain green after vertex format migration.
- Shader generation and asset compilation must be run explicitly.
- Manual RenderDoc verification becomes part of acceptance for pass structure, RT sizes, vertex inputs, blend states, and texture bindings.
