# 学习文档索引

本目录存储从开发过程中提炼的技术学习文档。

---

## 什么是学习文档？

学习文档用于记录：
- 解决特定问题的模式
- 常见陷阱和避免方法
- 性能优化技巧
- 框架特定的最佳实践
- 第三方参考项目的分析

---

## 如何创建新的学习文档

1. 复制 `TEMPLATE.md` 到 `[topic].md`
2. 填写各个部分
3. 在本文件底部添加索引条目

---

## 学习文档索引

### 项目特定经验

| 文档 | 主题 | 最后更新 |
|------|------|----------|
| [chunk-based-undo-redo](chunk-based-undo-redo.md) | 地形笔刷的 Chunk 化 Undo/Redo 事务模式 | 2026-04-07 |
| [index-map-terrain](index-map-terrain.md) | Index Map 技术借鉴自 Unity IndexMapTerrain | 2026-04-07 |
| [tommy-toml-library](tommy-toml-library.md) | Tommy 3.1.2 TOML 库 API 模式和陷阱 | 2026-04-08 |
| [biome-rule-layer-review-findings](biome-rule-layer-review-findings.md) | Biome 规则系统代码审查发现的已知缺陷 | 2026-05-01 |
| [stride-river-rendering-patterns](stride-river-rendering-patterns.md) | Stride 河流渲染分层、标准变换链与常见反模式 | 2026-06-06 |
| [native-viewport-airspace-overlays](native-viewport-airspace-overlays.md) | Avalonia 覆盖层与 NativeControlHost/SDL child HWND 的 airspace 限制 | 2026-06-15 |

### 引擎与框架调研

| 文档 | 主题 | 最后更新 |
|------|------|----------|
| [vic3-road-river-rendering](vic3-road-river-rendering.md) | Victoria 3 道路河流渲染完整逆向分析 | 2026-05-06 |
| [unity-rulelayer-reference](unity-rulelayer-reference.md) | Unity ProceduralTerrainPainter RuleLayer 系统分析 | 2026-04-30 |

---

*最后更新: 2026-06-15*
