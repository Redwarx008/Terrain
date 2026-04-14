# Index Map 数据格式升级
**Date**: 2026-04-07
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 调查 E:\reference\IndexMapTerrain 项目，借鉴关键技术
- 升级材质索引图从 R8 到 RGBA 格式
- 添加权重、旋转、3D投影支持

**Success Criteria:**
- 材质索引图支持 RGBA 四通道
- 着色器支持旋转和 3D 投影
- UI 有相应的控制面板
- 构建成功

---

## What We Did

### 1. IndexMapTerrain 调查

**关键技术发现：**
- Index Map 使用 R8G8B8A8 格式存储更多材质信息
- R: 材质索引, G: 权重, B: 投影方向, A: 旋转角度
- 3D 投影编码 (4:4 格式) 解决悬崖纹理拉伸
- 随机旋转打破平铺重复

### 2. 数据格式升级

**Files Changed:**

| 文件 | 修改内容 |
|------|----------|
| `Terrain.Editor/Services/MaterialIndexMap.cs` | 重构为 RGBA 格式，添加 `MaterialPixel` 结构体 |
| `Terrain.Editor/Rendering/EditorTerrainEntity.cs` | 纹理格式改为 `R8G8B8A8_UNorm` |
| `Terrain.Editor/Services/TerrainManager.cs` | 保存/加载 RGBA 格式 |

### 3. 着色器增强

**EditorTerrainDiffuse.sdsl 改动：**
- 添加 `MaterialPixel` 结构体和解码函数
- 添加 `DecodeProjectionDirection()` 投影方向解码
- 添加 `RotateUV()` 旋转 UV 计算
- 添加 `GetProjectedUV()` 3D 投影 UV 计算
- 支持根据投影方向自动切换 2D/3D 采样

### 4. 绘制工具增强

**Files Changed:**

| 文件 | 修改内容 |
|------|----------|
| `Terrain.Editor/Services/IPaintTool.cs` | 扩展 `PaintEditContext` 添加新参数 |
| `Terrain.Editor/Services/PaintTool.cs` | 支持权重、旋转、投影参数 |
| `Terrain.Editor/Services/EraseTool.cs` | 使用 `MaterialPixel` 结构体 |
| `Terrain.Editor/Services/PaintEditor.cs` | 传递新参数到上下文 |
| `Terrain.Editor/Services/BrushParameters.cs` | 添加 Weight, RandomRotation, FixedRotationDegrees, Use3DProjection |

### 5. UI 控制面板

**RightPanel.cs 改动：**
- `BrushParamsPanel` 添加 `CurrentMode` 属性
- 新增 `RenderMaterialPaintParams()` 方法
- 添加 Weight 滑块
- 添加 Random Rotation 开关
- 添加 Fixed Angle 滑块
- 添加 3D Projection 开关

---

## Decisions Made

### Decision 1: 不处理旧格式迁移

**Context:** 用户明确表示不用管旧格式迁移

**Decision:** 移除迁移逻辑，简化代码

### Decision 2: 使用 `MaterialPixel` 结构体

**Context:** 需要管理 RGBA 四通道数据

**Decision:** 创建 `MaterialPixel` 结构体封装四通道数据

**Rationale:**
- 代码更清晰
- 类型安全
- 便于扩展

---

## What Worked ✅

1. **参考成熟项目设计**
   - What: 参考 IndexMapTerrain 的技术方案
   - Impact: 快速理解关键技术，避免重复探索

2. **渐进式实施**
   - What: 按步骤依次完成数据结构、着色器、工具、UI
   - Impact: 每步可验证，减少错误

---

## What Didn't Work ❌

1. **尝试使用 `EditorState.Instance.CurrentMode`**
   - What we tried: 在 BrushParamsPanel 中使用 EditorState
   - Why it failed: EditorState 没有 CurrentMode 属性
   - Fix: 使用 BrushParamsPanel 的属性，由 RightPanel 同步

---

## Architecture Impact

### New Patterns

**Pattern:** `MaterialPixel` 四通道数据结构
- When to use: 管理材质索引图的 RGBA 数据
- Benefits: 类型安全，代码清晰

---

## Next Session

### Immediate Next Steps
1. 测试编辑器绘制功能
2. 验证旋转效果
3. 验证 3D 投影效果

### Pending
- Step 2: 运行时材质混合支持
- 从高度图采样法线计算投影方向

---

## Session Statistics

**Files Changed:** 9
**Build:** ✅ 成功

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `MaterialIndexMap.cs` - RGBA 数据结构
- Shader: `EditorTerrainDiffuse.sdsl` - 旋转和投影解码
- UI: `RightPanel.cs` - BrushParamsPanel 材质设置
- New parameters: Weight, RandomRotation, FixedRotationDegrees, Use3DProjection

**Gotchas for Next Session:**
- Watch out for: `BrushParamsPanel.CurrentMode` 需要由 `RightPanel` 同步
- Remember: 投影方向编码是 4:4 格式 (0-15, 0-15)

---

## Links & References

### External Resources
- IndexMapTerrain: `E:\reference\IndexMapTerrain`
  - 关键文件: `IndexedTerrainShader.shader` - 投影编码解码

### Code References
- RGBA 数据结构: `MaterialIndexMap.cs:13-35`
- 投影方向编码: `MaterialIndexMap.cs:142-160`
- 着色器解码: `EditorTerrainDiffuse.sdsl:45-60`
- UI 控制: `RightPanel.cs:211-268`
