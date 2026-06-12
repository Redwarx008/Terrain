## Context

当前河流系统已经能够从 river map 解析 segment 并生成 ribbon mesh，但渲染仍是占位实现：`RiverRenderingService` 使用 `VertexPositionNormalTexture`、普通 `MaterialDescriptor` 和透明材质直接把 mesh 挂到 Scene 中。这无法实现参考捕获中的河流渲染结构。

RenderDoc 捕获显示河流渲染是两阶段链路：第一组 river bottom draw 写入 half-res bottom/refraction render target，第二组 river surface draw 在 full-res 主 render target 上采样前者。参考 shader 使用专用 river vertex 输入、dual-source blending、bottom/refraction buffer、flow normal、water color、foam、reflection、edge fade 与 distance-to-main fade。

项目已有 `TerrainRenderFeature`，证明本仓库接受专用 render feature 管理底层 draw 的架构。河流渲染也应从普通 `ModelComponent` 材质路径升级为专用 render pipeline。

约束：
- 目标后端不考虑 OpenGL；以 Windows/D3D 路径为主。
- 代码、目录、shader、资源命名不得包含外部产品或相关字样；使用中性项目命名。
- 外部 shader/纹理资源可作为参考和来源，但复制到项目后使用中性名称，并在 README 中记录来源路径与用途。
- 必须遵守 Stride shader asset workflow，新增/修改 SDSL 后运行 generated files、clean asset、compile asset 与 solution build。

## Goals / Non-Goals

**Goals:**
- 实现完整河流渲染链路：`RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`。
- 使用专用 `RiverVertex`，对齐参考 river vertex 输入契约：`POSITION + TEXCOORD0..5`。
- 实现 river bottom half-res pass：写入 half-res bottom/refraction RT，使用 dual-source blending，支持 bottom diffuse/normal/properties、parallax、depth/fade、compressed world/refraction data。
- 实现 river surface full-res pass：采样 bottom/refraction RT，支持 flow normal、water color、ambient normal、reflection、foam、transparency、edge fade、distance-to-main fade。
- 保留现有 Editor 外部交互：Generate、Show/Hide Rivers、Clear、Dispose、项目持久化语义不破坏。
- 复制并加载必要河流纹理资源，使用中性项目目录和文件名。
- 用 RenderDoc 验证 bottom/surface pass 顺序、RT 尺寸、vertex layout、shader resources、blend/depth/raster state。

**Non-Goals:**
- 不支持 OpenGL 后端保真路径。
- 不在本 change 中实现完整全局雾战、云阴影、完整地形阴影系统；这些输入保留参数/函数边界并使用 neutral fallback。
- 不把河流继续塞进普通 `MaterialDescriptor` / `ModelComponent` 主渲染路径。
- 不在第一版做 draw call 合批优化；每段河流独立 draw，便于对照 RenderDoc。
- 不重写 river map 导入、segment 提取和 mesh 拓扑算法的既有语义，除非为 `RiverVertex` 输出必要调整。

## Decisions

### D1：采用专用 RiverRenderFeature，而不是普通材质路径
- **选择**：新增专用 `RiverRenderFeature`，使用 `DynamicEffectInstance` 和手动 multi-pass draw。
- **理由**：参考实现的核心是 bottom pass 写 half-res RT、surface pass 采样该 RT，并使用 dual-source blending；普通 `MaterialDescriptor` 难以表达 pass 间资源链路、RT ownership 和特殊 blend state。
- **已考虑 alternative**：保留 `ModelComponent/MeshRenderFeature` 并用 RenderStage/Selector 编排。该方案与默认 mesh 管线兼容性好，但 effect 参数、half-res RT、dual-source 输出和 pass ownership 会分散，维护成本更高。

### D2：使用 RiverComponent 体系作为场景数据入口
- **选择**：采用 `RiverComponent -> RiverProcessor -> RiverRenderObject -> RiverRenderFeature`。
- **理由**：该结构与现有 terrain 渲染架构一致，生命周期、启用/禁用、runtime 复用和 scene 状态管理更清晰。
- **已考虑 alternative**：`RiverRenderingService -> RiverRenderData -> RiverRenderFeature`。该方案改动较少，但数据游离于 Scene/component 体系，不利于长期维护和 runtime 复用。

### D3：RiverRenderingService 保留为 Editor façade
- **选择**：`RiverRenderingService` 保留现有 `UpdateMeshes`、`SetVisible`、`ClearMeshes`、`Dispose` 外部 API，但内部改为操作 `RiverComponent`。
- **理由**：避免大范围改动 `RiverViewModel`、`EditorShellViewModel` 和 viewport host 的调用链，同时把正式渲染状态迁入 component 体系。
- **已考虑 alternative**：移除 service，让 ViewModel 直接操作 component。拒绝原因是会把 UI 层与 scene/component 生命周期耦合得过紧。

### D4：RiverVertex 严格对齐参考输入契约
- **选择**：新增 `RiverVertex`，字段为 Position、Transparency、UV、Tangent、Normal、Width、DistanceToMain，layout 为 `POSITION + TEXCOORD0..5`。
- **理由**：参考 shader 明确依赖这些属性；现有 `VertexPositionNormalTexture` 无法承载 transparency、tangent、width、distance-to-main。
- **已考虑 alternative**：把额外数据挤进现有 UV 或 normal 字段。拒绝原因是语义混乱、shader 难维护且难以用 RenderDoc 对照。

### D5：bottom pass 使用 dual-source blending 作为主线
- **选择**：不考虑 OpenGL，按参考 bottom pass 输出 `Color : SV_Target0` 与 `Blend : SV_Target0_SRC1`，使用 src1 alpha blending。
- **理由**：目标后端以 Windows/D3D 为主，dual-source 更贴近参考 RenderDoc 与 shader 行为。
- **已考虑 alternative**：MRT fallback 作为跨平台基线。由于用户明确不考虑 OpenGL，MRT 仅作为实现受阻时的临时调试方案，不作为设计主线。

### D6：保留完整 shader 结构，对缺失全局系统使用 neutral fallback
- **选择**：移植 river bottom/surface/water 相关 shader 结构；项目缺失的 fog/cloud/shadow/flatmap 输入使用 neutral fallback。
- **理由**：这样既能保持完整 shader 架构，又不会被本项目暂缺的全局系统阻塞。未来有对应系统时可直接接入。
- **已考虑 alternative**：删除缺失系统相关逻辑，做简化 shader。拒绝原因是会偏离完整参考实现目标，并增加后续补齐成本。

### D7：资源复制到项目中并使用中性命名
- **选择**：复制必要纹理到 `Assets/River/Water`、`Assets/River/Bottom`、`Assets/River/Environment`，使用 `water_color.dds`、`bottom_diffuse.dds` 等中性名称。
- **理由**：项目不依赖外部安装路径；命名符合用户要求；README 记录来源路径和用途。
- **已考虑 alternative**：运行时从外部安装目录读取。拒绝原因是不可移植且路径依赖强。

### D8：每段河流初期独立 draw
- **选择**：一个 segment 对应一个 draw item / render object。
- **理由**：参考捕获中河段以独立 draw 出现；当前 mesh 生成也按 segment 输出；独立 draw 便于 RenderDoc 对照和调试。
- **已考虑 alternative**：合批所有 segment。拒绝原因是会增加 draw range、segment 参数和调试复杂度，且不利于对齐参考 draw。

### D9：wireframe/debug 迁移到 RiverRenderFeature
- **选择**：最终由 `RiverRenderFeature` 提供 wireframe/debug pipeline，而不是依赖当前 `MeshRenderFeature` selector 路径。
- **理由**：方案 B 不再以 `ModelComponent` 作为主渲染路径，继续依赖 mesh selector 会造成双路径和状态不一致。
- **已考虑 alternative**：保留 debug-only `ModelComponent`。可作为短期过渡，但不是最终设计。

## Risks / Trade-offs

- [Risk] SDSL 可能不支持或不稳定支持 `SV_Target0_SRC1` → Mitigation: 先验证 SDSL dual-source output；若受阻，临时用 MRT 验证 bottom/surface 资源链路，同时记录阻塞并继续寻找 Stride 支持写法。
- [Risk] `RiverRenderingService`、`RiverProcessor`、`RiverRenderFeature` 之间资源所有权混乱 → Mitigation: 规定 CPU mesh/settings 归 component/service，GPU buffers 归 processor/render object，RT/effect/pipeline 归 render feature。
- [Risk] `UpdateMeshes` 与 render draw 之间出现同步问题 → Mitigation: 使用 `Version` 检测和明确更新点；必要时引入 pending CPU data 与 GPU data swap。
- [Risk] Compositor draw 时机不正确导致河流覆盖关系或透明排序错误 → Mitigation: 先实现并 RenderDoc 验证，再调整到 terrain/opaque 后、普通 transparent 前的顺序。
- [Risk] 复制的 DDS/cubemap 资源无法通过 Stride asset pipeline 直接加载 → Mitigation: 优先尝试 asset pipeline；如不顺，集中在 `RiverResourceLoader` 中使用文件加载并统一 dispose。
- [Risk] 完整 shader 依赖的全局系统本项目没有等价输入 → Mitigation: neutral fallback 参数化，保留接口，后续可接入真实系统。
- [Trade-off] 每段独立 draw 增加 draw call → 接受理由：第一阶段以正确性、可验证性和参考对齐为优先，后续可合批优化。
- [Trade-off] 专用 RenderFeature 比材质路径复杂 → 接受理由：多 pass、half-res RT、dual-source 和 resource chain 是本功能核心，专用路径能集中管理复杂度。

## Migration Plan

1. 新增 `RiverComponent`、`RiverRenderSettings`、`RiverMeshData`，让 `RiverRenderingService` 创建/更新 component。
2. 新增 `RiverVertex` 并迁移 `RiverMeshService` 输出，更新测试覆盖 vertex layout、tangent/normal、distance-to-main。
3. 新增 `RiverProcessor` 与 `RiverRenderObject`，实现 component version 到 GPU buffers 的同步。
4. 新增 `RiverRenderResources` 管理 half-res RT/depth 与 resize/dispose。
5. 新增/修改 shader common files：`RiverVertexStreams.sdsl`、`RiverCommon.sdsl`、`RiverWaterCommon.sdsl`、`RiverBottom.sdsl`、`RiverSurface.sdsl`、`RiverEffect.sdfx`。
6. 复制河流纹理资源到中性项目目录，新增 README 记录来源路径与用途。
7. 实现 `RiverRenderFeature`：bottom dual-source pass、surface transparent pass、参数绑定、texture binding、draw loop。
8. 在 `EmbeddedStrideViewportGame` 中注册/确保 river render feature，并保持 terrain、decal、camera、viewport focus 不受影响。
9. 迁移 wireframe/debug 模式到 river render feature 或提供过渡方案。
10. 运行 shader asset workflow、测试、solution build。
11. 手动启动 Editor，Generate 河流，用 RenderDoc 验证 bottom/surface pass、RT、vertex input、blend/depth state、纹理绑定。
12. 更新 `docs/ARCHITECTURE_OVERVIEW.md`、`docs/CURRENT_FEATURES.md`、会话日志；如架构稳定，新增 ADR。

Rollback strategy:
- 保留 `RiverRenderingService` 外部 API 可降低回滚范围。
- 如果 `RiverRenderFeature` 不稳定，可在 feature flag/debug setting 下临时禁用新路径，并恢复/保留旧简单材质路径作为短期可视化 fallback；最终完成后移除 fallback。

## Open Questions

- Stride SDSL 中 dual-source output 的准确语义写法是什么？是否支持 `SV_Target0_SRC1` 或需要其他声明？
- `CompressRiverWorldSpace` 应完整移植参考实现，还是使用本项目等价 encode/decode？优先继续定位参考函数。
- 纹理资源应全部走 Stride asset pipeline，还是 DDS/cubemap 首期由 `RiverResourceLoader` 文件加载？实现时按 Stride DDS/cubemap 支持情况决定。
- `RiverComponent` 第一阶段放在 `Terrain.Editor` 还是下沉到 `Terrain` runtime 项目？长期更适合 runtime，但首期可能为降低依赖改动先放 Editor。
- River pass 的 compositor 插入点需要在实现中实测：terrain/opaque 后、transparent 前是否能直接通过现有 compositor 增强点完成。
