# River Bottom Light Binding Design

## Goal

基于 `C:\Users\Redwa\Desktop\debug-latest_frame1887.rdc` 的 RenderDoc 热替换证据，优先修正 current river bottom 的光照输入链，而不是继续调整 `RiverBottom.sdsl` 的 UV 或 parallax 语义。

## Evidence

- current bottom 代表像素约为 `0.032 ~ 0.046`。
- current `direct sun only` 代表像素仅约 `0.004 ~ 0.007`。
- current `IBL only` 代表像素约 `0.028 ~ 0.039`。
- 把整体 lighting 乘 `3x` 后，代表像素可抬到 `0.097 ~ 0.136`，已经进入 CK3 bottom 的量级。
- 把 `IBL` 单独乘 `3x` 后，也能抬到 `0.088 ~ 0.123`。
- current `RiverRenderFeature` 没有把 `_BottomSunDirection / _BottomSunColor / _BottomSunIntensity / _BottomEnvironmentIntensity / _BottomSpecularIntensity / _BottomNormalStrength` 绑定到 bottom pass。
- current bottom 的环境图绑定来源是 `River/Environment/reflection-specular`，而 editor 场景本身另有独立的 `Skybox texture` / `LightSkybox`。

## Decision

本轮只做 C# 侧输入链修正，不改 `SDSL`：

1. `RiverBottom` 的环境 cubemap 改为优先绑定 editor 场景的 `Skybox texture`。
2. `RiverSurface` 继续保留现有 `reflection-specular` 资源作为水面反射/高光变化贴图。
3. 把 bottom lighting 控制量显式纳入
   - `RiverRenderSettings`
   - `RiverRenderObject`
   - `RiverRenderFeature.ApplyBottomParameters`
4. editor 场景初始化时，把 key light 的基础参数同步到 `RiverComponent.Settings`，避免 bottom pass 永远只吃 shader 默认值。

## Scope

- 修改 `Terrain.Editor/Rendering/River/RiverResourceLoader.cs`
- 修改 `Terrain.Editor/Rendering/River/RiverRenderSettings.cs`
- 修改 `Terrain.Editor/Rendering/River/RiverRenderObject.cs`
- 修改 `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`
- 必要时修改 `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`
- 增加/更新 `Terrain.Editor.Tests/RiverShaderTextTests.cs`

## Non-Goals

- 本轮不回退到 CK3 non-advanced world-UV bottom 路径。
- 本轮不修改 `RiverBottom.sdsl` / `RiverSurface.sdsl`。
- 本轮不接 shadow texture 或完整 CK3 `GetRiverBottomSunLightingProperties(...)`。

## Verification

- 文本测试必须先失败，确认 bottom lighting 参数与 skybox 环境图绑定尚未落地。
- 修改后运行 `Terrain.Editor.Tests`，确认新增测试转绿且既有 river tests 不回归。
- 如时间允许，后续再用新 capture 验证 bottom RT 是否从“近黑”抬升到更接近 CK3 的暖亮量级。
