# Cross-Layer Thinking Guide

> **Purpose**: Think through data flow across layers before implementing.

---

## The Problem

**Most bugs happen at layer boundaries**, not within layers.

Common cross-layer bugs:
- API returns format A, frontend expects format B
- Database stores X, service transforms to Y, but loses data
- Multiple layers implement the same logic differently

---

## Before Implementing Cross-Layer Features

### Step 1: Map the Data Flow

Draw out how data moves:

```
Source → Transform → Store → Retrieve → Transform → Display
```

For each arrow, ask:
- What format is the data in?
- What could go wrong?
- Who is responsible for validation?

### Step 2: Identify Boundaries

| Boundary | Common Issues |
|----------|---------------|
| API ↔ Service | Type mismatches, missing fields |
| Service ↔ Database | Format conversions, null handling |
| Backend ↔ Frontend | Serialization, date formats |
| Component ↔ Component | Props shape changes |

### Step 3: Define Contracts

For each boundary:
- What is the exact input format?
- What is the exact output format?
- What errors can occur?

---

## Common Cross-Layer Mistakes

### Mistake 1: Implicit Format Assumptions

**Bad**: Assuming date format without checking

**Good**: Explicit format conversion at boundaries

### Mistake 1.5: 把原生宿主问题误判成渲染内容问题

**Symptom**: 视口已经进入 `Draw()`，甚至首帧事件都触发了，但窗口仍然黑屏。

**Cause**: 在 Avalonia / Win32 / SDL / Stride 这种多层宿主链里，`Draw()` 发生不等于最终 `Present` 已经正确到达屏幕。窗口创建方式、重挂接方式、`ClientBounds` 和 presenter/backbuffer 同步都可能先出错。

**Fix**:
- 先做 presenter-only 对照实验，例如只清屏成明显颜色
- 确认 `ClientBounds`、backbuffer、depth buffer、可见区域是否一致
- 只有宿主链确认通了，再继续排查 scene/compositor

**Prevention**:
- 原生宿主问题按“宿主链 → presenter 链 → scene/compositor 链”顺序二分
- 不要一开始就把问题归到 camera、feature 或 shader

### Mistake 1.6: shader 声明和 C# 参数 key 类型不一致

**Symptom**: 编译能过，但地形法线、光照、切片采样或材质混合突然异常，表现看起来像渲染逻辑坏了。

**Cause**: `.sdsl` 里声明的是一种常量缓冲类型（例如 `int4`），而生成的 `.sdsl.cs` key 或调用侧却改成了另一种类型（例如 `Vector4`）。这样数据会以错误的位模式写进 shader 参数。

**Fix**:
- 先回到 `.sdsl` 原始声明核对参数类型
- 让 `.sdsl.cs` 的 `ParameterKey<T>` 类型与 shader 声明完全一致
- 检查所有 `parameters.Set(...)` 调用是否也使用同一类型

**Prevention**:
- 修改自动生成 shader key 文件时，把 `.sdsl` 视为唯一真源
- 出现“光照坏了但编译正常”的回归时，优先排查 shader 参数声明与 C# 绑定是否漂移

### Mistake 1.7: 多分辨率空间坐标在跨层传递时遗漏缩放

**Symptom**: 编辑器画笔偏移、splatmap 采样位置错误，或运行时材质边界出现马赛克/锯齿。

**Cause**: SplatMap 使用半分辨率（1/2 of heightmap），但某一层的坐标仍按 1:1 计算。任何一层遗漏 `/2` 或 `*2` 转换都会导致全部对不齐。

**Fix**:
- 明确每个空间：heightmap space（1:1）vs splatmap space（1/2）
- 在层边界处显式标注坐标空间转换：
  - Editor C# → BiomeMask / GPU splat rebuild trigger: `heightmap / 2`
  - Editor Shader → LoadIndexMapAtGlobal: `coord / 2`
  - Editor Shader → BuildSplatMap 输出: `splatCoord * 2` 回到 heightmap 采样高度
  - Paint brush: `pixel / 2, radius / 2`
  - Undo/redo: 在 splatmap 空间捕获，`* 2` 转回 heightmap 标记脏
  - Runtime C# → SplatInfo: 从 ratio 计算偏移/步幅
  - Runtime Shader → splatPageLocalPos: 使用 float 步幅

**Prevention**:
- 跨分辨率空间传递时，在层边界画表格列出 src space → dst space 转换
- 每次新增坐标计算时先问："这是 heightmap 空间还是 splatmap 空间？"

### Mistake 2: Scattered Validation

**Bad**: Validating the same thing in multiple layers

**Good**: Validate once at the entry point

### Mistake 3: Leaky Abstractions

**Bad**: Component knows about database schema

**Good**: Each layer only knows its neighbors

---

## Checklist for Cross-Layer Features

Before implementation:
- [ ] Mapped the complete data flow
- [ ] Identified all layer boundaries
- [ ] Defined format at each boundary
- [ ] Decided where validation happens

After implementation:
- [ ] Tested with edge cases (null, empty, invalid)
- [ ] Verified error handling at each boundary
- [ ] Checked data survives round-trip

---

## When to Create Flow Documentation

Create detailed flow docs when:
- Feature spans 3+ layers
- Multiple teams are involved
- Data format is complex
- Feature has caused bugs before
