# Quality Guidelines

> Code review standards, testing requirements.

---

## Overview

本项目使用 C# 标准实践，结合 Stride 引擎约定。

---

## Code Style

### Nullable 引用类型

始终启用 `#nullable enable`，所有公共 API 必须标注可空性：

```csharp
#nullable enable

public string? TerrainDataPath { get; set; }
public Texture? DefaultDiffuseTexture { get; set; }
```

### 文件头部

每个 `.cs` 文件以 `#nullable enable` 开头。

### 访问修饰符

- `public` - 公共 API
- `internal` - 项目内部使用
- `private` - 仅类内部使用
- 不使用 `protected`

### 密封类

不继承的类使用 `sealed`：

```csharp
public sealed class TerrainComponent : ActivableEntityComponent
```

---

## DataContract 属性

用于 Stride 序列化的属性：

```csharp
[DataContract("TerrainComponent")]
public sealed class TerrainComponent
{
    [DataMember(10)]
    public string? TerrainDataPath { get; set; }

    [DataMemberIgnore]
    internal TerrainChunkNode[] ChunkNodeData = Array.Empty<TerrainChunkNode>();
}
```

属性编号按功能区域分组，留有间隔便于扩展。

---

## Code Review Checklist

- [ ] `#nullable enable` 在文件顶部
- [ ] 所有公共 API 有可空性标注
- [ ] 字段使用 `_camelCase` 前缀
- [ ] 密封不继承的类
- [ ] XML 文档注释用于公共 API
- [ ] 没有 `Console.WriteLine`
- [ ] 没有 `TODO` 未完成代码

---

## Anti-patterns

1. **不要**留下 `// TODO` 注释
2. **不要**使用 `var` 声明字段（必须显式类型）
3. **不要**在核心库中引用编辑器代码
4. **不要**忽略 nullable 警告

---

## Tests

项目当前没有自动化测试基础设施。如果添加测试：

- 使用 xUnit/NUnit
- 测试组件初始化
- 测试资源加载失败路径
- Mock Stride 服务依赖