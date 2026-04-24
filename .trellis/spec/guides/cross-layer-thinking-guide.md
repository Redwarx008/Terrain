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
