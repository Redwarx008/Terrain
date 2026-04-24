# Error Handling

> How errors are caught, logged, and returned.

---

## Overview

本项目遵循 Stride 引擎的错误处理模式，结合 C# 标准异常处理。

---

## Exception Patterns

### 1. 静默失败 (用于可恢复情况)

当操作可以优雅失败时，捕获异常并记录：

```csharp
try
{
    var scene = Content.Load<Scene>("MainScene");
}
catch (Exception exception)
{
    // 静默失败，返回降级值
    System.Diagnostics.Debug.WriteLine($"Failed to load scene: {exception.Message}");
    return null;
}
```

### 2. 配置验证 (启动时检查)

```csharp
if (string.IsNullOrEmpty(terrainPath))
{
    throw new InvalidOperationException("Terrain path must be set before initialization.");
}
```

### 3. 参数验证 (公共 API)

```csharp
if (x < 0 || x >= Width)
    throw new ArgumentOutOfRangeException(nameof(x));
```

### 4. 断言式检查 (内部不变量)

```csharp
Debug.Assert(chunks != null, "Chunks must be initialized before call.");
```

---

## Logging

使用 `System.Diagnostics.Debug.WriteLine` 进行诊断输出：

```csharp
System.Diagnostics.Debug.WriteLine($"Failed to load asset: {exception.Message}");
```

关键点：
- 调试信息使用 `Debug.WriteLine`
- 生产环境中这些仅在调试器附加时输出
- 不要使用 `Console.WriteLine`

---

## Error Types

| 场景 | 异常类型 |
|------|----------|
| 配置错误 | `InvalidOperationException` |
| 参数越界 | `ArgumentOutOfRangeException` |
| 文件未找到 | `FileNotFoundException` |
| 资源加载失败 | `Exception` (捕获后返回 null) |
| 内部不变量 | `Debug.Assert` |

---

## Examples

### 资源加载失败

```csharp
try
{
    defaultTerrainTexture = Content.Load<Texture>("Grid Gray 128x128");
}
catch (Exception exception)
{
    System.Diagnostics.Debug.WriteLine($"Failed to load default terrain texture asset: {exception.Message}");
}
```

### 配置验证

参见 [EditorGame.cs](Terrain.Editor/EditorGame.cs) 中的窗口初始化。

---

## Anti-patterns

1. **不要**在正常流程中抛出异常用于控制流
2. **不要**使用 `catch` 而不处理
3. **不要**在 `catch` 中重新抛出相同异常后丢失堆栈
4. **不要**在编辑器外部存储敏感错误信息