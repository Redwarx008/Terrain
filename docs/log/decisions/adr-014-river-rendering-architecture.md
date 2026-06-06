# ADR-014: 河流渲染架构

**Date**: 2026-06-06
**Status**: ✅ Accepted
**Decision ID**: ADR-014

---

## Context

Terrain 当前已经具备路径编辑、道路渲染和河流 mesh 生成能力，但河流渲染仍面临两个结构性问题：

- editor 层 service 已经能拿到 `RiverSegment` 并生成 `RiverMeshData`，但没有正式接入 Stride 的自定义渲染生命周期。
- 河流需要独立的河底/水面多 pass、折射缓冲和调试模式，这些能力不适合继续塞进 `RiverRenderingService` 或临时 `ModelComponent` 预览路径。

同时，本次排障也证明：当 shader 不走 Stride 标准 transformation 链路时，河流会出现屏幕空间闪烁、全屏大三角等严重错误。因此河流架构不仅要能“画出来”，还必须遵守 Stride 的标准位置流约定。

---

## Decision

采用以下河流渲染架构：

- `RiverRenderingService` 保留为 **editor façade**
  - 接收编辑器侧 `RiverSegment`
  - 调用 `RiverMeshService` 生成 `RiverMeshData`
  - 同步显隐状态
  - 不直接承担正式渲染 pass 逻辑
- `RiverComponent` 持有快照化 `RiverMeshData` 和 `Version`
- `RiverProcessor` 监听组件版本变化并重建 `RiverRenderObject`
- `RiverRenderObject` 管理河流的 GPU 顶点/索引缓冲、bounds 和 draw 数据
- `RiverRenderFeature` 负责正式渲染：
  - 河底 pass
  - 水面 pass
  - 中间折射缓冲
  - 线框/单 pass 调试模式
- 河流 shader (`RiverBottom.sdsl`, `RiverSurface.sdsl`) 必须使用 Stride 标准 `TransformationWAndVP`，依赖其生成 `PositionWS / PositionH / DepthVS`

---

## Options Considered

### Option 1: 继续使用 service-only 渲染
**Description:**
让 `RiverRenderingService` 继续直接创建/更新 GPU 资源，并在 editor 代码里驱动 draw。

**Pros:**
- 初期改动少
- 上手快

**Cons:**
- 渲染生命周期和编辑器逻辑耦合
- 不适合多 pass、render stage、visibility group
- 后续难以维护和扩展

### Option 2: 使用 `ModelComponent` / 普通材质系统挂载河流
**Description:**
把河流 mesh 做成普通模型或材质路径，复用更现成的渲染接入点。

**Pros:**
- 可以更快看到结果
- 利用现成实体/材质流程

**Cons:**
- 不适合河底/水面双 pass 与折射缓冲
- 调试模式和连接段控制边界不清晰
- 容易退化为临时方案

### Option 3: 建立 `Component -> Processor -> RenderObject -> RenderFeature` 子系统（选中）
**Description:**
按 Stride 自定义渲染扩展方式，为河流建立独立渲染子系统。

**Pros:**
- 生命周期清晰，符合 Stride 渲染架构
- 天然支持 render stage、visibility group、多 pass
- editor façade 与渲染实现职责分离
- 便于后续扩展更多河流视觉与调试能力

**Cons:**
- 基础设施代码更多
- 首次落地成本高于临时方案

---

## Rationale

河流不是普通静态模型，也不是单 pass 的简单材质对象。它同时具备：

- 编辑器侧动态生成 mesh
- 运行期频繁更新 GPU buffer
- 河底/水面分离渲染
- 依赖折射中间缓冲
- 需要显隐控制与线框调试模式

这组需求与 Stride 的 `Component -> Processor -> RenderObject -> RenderFeature` 分层高度契合。把 `RiverRenderingService` 保留为 editor façade，则可以让编辑器继续以熟悉的 service 方式调用，同时不再污染正式渲染路径。

另一方面，`TransformationWAndVP` 的采用不是实现细节，而是该架构的一部分约束：河流 shader 必须遵守 Stride 的标准位置流语义，不能再用输入布局特殊映射之类的临时补丁兜底。

---

## Trade-offs

**What we gain:**
- 河流拥有正式、可维护的渲染生命周期
- editor façade 与渲染实现解耦
- 更容易承载多 pass、调试模式和后续视觉扩展
- 回到 Stride 标准 transformation 语义，降低空间链路错误风险

**What we give up:**
- 实现复杂度比 service-only 或 `ModelComponent` 更高
- 需要维护自定义 RenderFeature、RenderObject 和 shader 约束

---

## Consequences

### Positive
- 河流渲染已能稳定接入编辑器原生视口和 Stride 渲染管线。
- `RiverRenderingService` 职责更窄，更接近“编辑器桥接服务”。
- 本次已验证：在该架构下修复 `TransformationWAndVP` 后，河流显示恢复正常。

### Negative
- 后续任何绕过 `RiverRenderFeature` 的“快速渲染捷径”都会更明显地违背当前架构。
- 需要为河流维护独立的 render group / render stage 接入代码。

### Neutral
- 现有道路渲染仍可保留原路径渲染实现，不必强制与河流完全同构。

---

## Implementation Notes

- `Terrain.Editor/Rendering/River/RiverComponent.cs`：持有 mesh 快照与版本号。
- `Terrain.Editor/Rendering/River/RiverProcessor.cs`：根据 `Version` 同步 `RiverRenderObject`。
- `Terrain.Editor/Rendering/River/RiverRenderObject.cs`：封装顶点/索引缓冲与 draw 数据。
- `Terrain.Editor/Rendering/River/RiverRenderFeature.cs`：
  - 河底 pass 写入折射中间缓冲
  - 水面 pass 采样折射纹理
  - 支持 `Wireframe / BottomOnly / SurfaceOnly` 调试模式
- `Terrain.Editor/Services/RiverRenderingService.cs`：editor façade，仅负责 mesh 更新与显隐。
- `Terrain.Editor/Rendering/NativeViewport/EmbeddedStrideViewportGame.cs`：创建 `RiverSystem`、注册 `RiverRenderFeature`。
- `Terrain.Editor/Effects/RiverBottom.sdsl` / `RiverSurface.sdsl`：必须混入 `TransformationWAndVP`。

**显式反模式：**
- 不要再在 `RiverRenderFeature` 中加入 `POSITION -> POSITION_WS` 之类的输入语义特殊映射来弥补 shader 变换缺失。
- 不要把正式渲染 pass 回塞进 `RiverRenderingService`。

---

## Related Decisions

- [adr-013-vic3-path-rendering.md](./adr-013-vic3-path-rendering.md)

---

## References

- [2026-06-06 河流渲染架构落地与顶点变换修复](../2026/06/06/2026-06-06-1-river-rendering-architecture-and-transform-fix.md)
- [2026-06-05 River Mesh 生成修复](../2026/06/05/2026-06-05-1-river-mesh-generation-fix.md)
- [2026-05-16 Vic3 河流 RenderDoc 调研归档](../2026/05/16/2026-05-16-1-vic3-river-renderdoc-research.md)

---

*ADR Template Version: 1.0*
