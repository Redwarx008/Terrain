# RenderDoc 河流热修改优先验证最终可见事件

**Topic**: RenderDoc river hot-edit verification target
**Date**: 2026-06-19
**Related Sessions**: [2026-06-19 river ck3 capture pass diff and hotedit](../2026/06/19/2026-06-19-river-ck3-capture-pass-diff-and-hotedit.md)

---

## Problem / Context

- 河流 shader 的中间 RT 常常是 HDR、half-res、或仅供后续 refraction/surface pass 消费。
- 直接在这些中间 RT 上看 replacement，很容易误判“shader_replace 没生效”或“当前颜色就是最终可见结果”。

---

## Solution / Pattern

- 先用一次极端 replacement（例如纯绿色）确认当前 capture 里哪个事件是最终可见验证面。
- 之后所有定向 hot-edit 都在这个最终 composite 事件上看结果，而不是一直盯中间 RT。

---

## Key Insights

### 1. 中间 RT 适合做归因，不适合做最终可见判断
- 它适合看“这个 pass 写了什么数据”。
- 但如果用户关心“屏幕上为什么是这个效果”，最终验证面必须落在后续真正被显示的事件上。

### 2. 先打通 replacement 可见链路，再做细分实验
- 纯色 replacement 能快速证明热修改是否真的影响最终画面。
- 一旦验证面找对，后续 `refraction-only`、`mask-only` 实验才可信。

---

## When to Use

- 河流、水体、体积云、SSR、Bloom 这类多 pass / 中间缓冲重的渲染链。
- 中间 RT 的导出结果和最终屏幕观感明显不一致时。

---

## Common Mistakes

### ❌ Mistake 1: 一直盯着中间 HDR RT 判断 replacement 是否生效
**What to avoid:**
- 在 surface draw 的中间 RT 上做热改后，直接凭导出 PNG 下结论。

**Why it's bad:**
- 很可能只是验证面选错，而不是 replacement 真没挂上。

**Correct approach:**
- 先找最终 composite 事件，再把 replacement 结果映射到用户真正看到的画面。

---
