# Logging Guidelines

> Log levels, format, what to log.

---

## Overview

本项目使用 `System.Diagnostics.Debug.WriteLine` 进行日志输出，结合诊断编译器符号。

---

## Log Levels

### Debug (诊断信息)

仅在调试时输出，不进入生产日志：

```csharp
System.Diagnostics.Debug.WriteLine($"Viewport size: {size}");
System.Diagnostics.Debug.WriteLine($"Loaded scene: {sceneSourceStatus}");
```

### Trace (详细流程)

用于追踪代码执行路径：

```csharp
Debug.WriteLine($"Entering Update frame {frameCount}");
Debug.WriteLine($"Brush stroke at ({x}, {y}) with strength {strength}");
```

---

## What to Log

### 应当记录

- **启动/初始化阶段**：关键组件创建成功
- **资源加载**：成功或失败（使用 `exception.Message`）
- **状态转换**：模式切换、工具选择
- **错误恢复**：降级行为

### 不应记录

- 每帧更新的高频数据
- 敏感信息
- 用户输入细节
- 堆栈跟踪（用于外部暴露）

---

## Format

```
{Context}: {Message}
{Context}: {Status} ({ExceptionType})
```

示例：
```csharp
Debug.WriteLine($"Failed to load scene: {exception.Message}");
Debug.WriteLine($"Scene: fallback ({exception.GetType().Name})");
```

---

## Diagnostics Class

不引入额外的日志框架。使用 `Debug` 类本身：

```csharp
using System.Diagnostics;
Debug.WriteLine("message");
```

---

## Anti-patterns

1. **不要**使用 `Console.WriteLine`
2. **不要**在热路径中记录详细日志
3. **不要**使用字符串插值构建过长的日志消息
4. **不要**记录敏感或用户隐私数据