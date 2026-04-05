# 地形纹理刷功能规划
**Date**: 2026-04-06
**Session**: 2
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- 规划地形纹理刷功能的完整实现方案

**Secondary Objectives:**
- 确定数据结构设计
- 确定纹理导入流程
- 确定项目目录结构

**Success Criteria:**
- 完成详细的实现计划文档
- 计划获得用户批准

---

## Context & Background

**Previous Work:**
- See: [2026-04-06-1-ai-workflow-setup.md](./2026-04-06-1-ai-workflow-setup.md) - AI 协作规范化流程引入
- Related: [terrain-editor-design-phase-2.md](../../design/terrain-editor-design-phase-2.md) - 原始材质绘制系统设计

**Current State:**
- 核心层 ✅ 全部完成（地形组件、高度数据、流式加载、LOD 系统）
- 渲染层 ✅ 全部完成
- 编辑器层 🚧 植被编辑进行中
- 纹理刷功能 📋 待实现

**Why Now:**
- 用户要求实现刷地形纹理的功能
- 需要先规划再实现

---

## What We Did

### 1. 需求收集与方案探索

**用户需求:**
- 使用单张 R8 Uint 纹理存储材质索引 (0-255)
- 着色时采用双线性过滤实现材质过渡
- 支持更多材质（使用 Texture2DArray）
- 纹理尺寸可选（512/1024/2048）
- 用户选择项目目录，不固定名称

### 2. 创建实现计划

**计划文件:** `C:\Users\Redwa\.claude\plans\cheerful-singing-stardust.md`

**架构概览:**
```
CPU Layer:
  MaterialIndexMap (byte[]) → 单张 R8_UInt 纹理
  MaterialSlotManager → 256 材质槽位配置

GPU Layer:
  Texture2D<uint> MaterialIndexMap → 索引采样
  Texture2DArray MaterialAlbedoArray → 材质纹理数组

Shader:
  双线性采样 4 个相邻索引 → 混合 4 种材质颜色
```

### 3. 创建核心数据结构（部分）

**Files Created:** `Terrain.Editor/Services/MaterialIndexMap.cs`

```csharp
public sealed class MaterialIndexMap
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] indices;

    public byte GetIndex(int x, int z);
    public void SetIndex(int x, int z, byte materialIndex);
    public byte[] GetRawData();
}
```

---

## Decisions Made

### Decision 1: 纹理存储方案

**Context:** 如何存储材质索引

**Options Considered:**
1. SplatMap 方案（64 层 RGBA8）- 内存大，支持任意位置多材质混合
2. R8 Uint 方案（单张纹理）- 内存小，仅边缘过渡

**Decision:** R8 Uint 方案
**Rationale:** 用户明确要求，简单高效
**Trade-offs:** 只能实现边缘过渡，不能在同一像素混合多种材质

### Decision 2: 纹理尺寸

**Context:** 材质纹理统一尺寸

**Options Considered:**
1. 固定 512×512
2. 可选 512/1024/2048

**Decision:** 可选尺寸
**Rationale:** 用户要求支持不同精度需求

### Decision 3: 纹理数组管理

**Context:** 导入新材质时如何更新纹理数组

**Options Considered:**
1. 每次重建整个数组
2. 按需扩容

**Decision:** 按需扩容
**Rationale:** 避免每次导入都重建，性能更好
**策略:** 初始 4 个槽位，每次翻倍，最大 256

### Decision 4: 图像处理库

**Context:** 纹理加载和缩放

**Options Considered:**
1. System.Drawing (Windows 原生)
2. ImageSharp (跨平台)

**Decision:** ImageSharp
**Rationale:** 项目已依赖 SixLabors.ImageSharp

### Decision 5: 项目目录结构

**Context:** Editor 数据保存位置

**Options Considered:**
1. 固定 EditorData 目录名
2. 用户选择目录

**Decision:** 用户选择目录
**Rationale:** 用户明确要求，更灵活

---

## What Worked ✅

1. **Plan Mode 流程**
   - 逐步收集用户需求
   - 迭代调整方案
   - 最终获得批准

2. **Explore Agent**
   - 快速了解现有代码结构
   - 找到参考实现模式

---

## What Didn't Work ❌

1. **遗漏纹理导入设计**
   - 问题: 初版计划缺少纹理导入流程
   - 解决: 用户指出后补充完整

2. **遗漏会话日志**
   - 问题: 会话结束时忘记创建日志
   - 解决: 用户提醒后补上

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Phase 2.1** - 完成核心数据结构（MaterialSlot, MaterialSlotManager, PaintEditContext, IPaintTool）
2. **Phase 2.2** - 实现绘制工具（PaintTool, EraseTool, PaintEditor）
3. **Phase 2.3** - GPU 资源管理扩展

### Blocked Items
- 无阻塞

### Questions to Resolve
- 无

### Docs to Read Before Next Session
- [HeightEditor.cs](../../../Terrain.Editor/Services/HeightEditor.cs) - 三阶段笔触模式参考
- [EditorTerrainDiffuse.sdsl](../../../Terrain.Editor/Effects/EditorTerrainDiffuse.sdsl) - 着色器扩展基础

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- 计划文件: `C:\Users\Redwa\.claude\plans\cheerful-singing-stardust.md`
- 已创建: `MaterialIndexMap.cs`
- 方案: R8 Uint + Texture2DArray + 双线性过滤
- 扩容策略: 初始 4，翻倍扩容，最大 256

**What Changed Since Last Doc Read:**
- 确定了纹理刷功能的技术方案
- 创建了 MaterialIndexMap 数据结构

**Gotchas for Next Session:**
- 使用 ImageSharp 而非 System.Drawing
- 纹理数组按需扩容，不要每次重建
- 项目目录由用户选择，不固定名称

---

## Links & References

### Related Documentation
- [实现计划](file:///C:/Users/Redwa/.claude/plans/cheerful-singing-stardust.md)
- [原始设计文档](../../design/terrain-editor-design-phase-2.md)
- [架构概览](../../ARCHITECTURE_OVERVIEW.md)

### Code References
- 已创建: `Terrain.Editor/Services/MaterialIndexMap.cs`
- 参考: `Terrain.Editor/Services/HeightEditor.cs` - 三阶段笔触模式
- 参考: `Terrain/Effects/Stream/TerrainHeightParameters.sdsl` - Texture2DArray 用法

---

*Session Duration: Planning phase complete, implementation pending*
