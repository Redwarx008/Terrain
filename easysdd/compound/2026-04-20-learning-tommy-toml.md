---
doc_type: learning
feature: terrain-editor
status: current
tags: [toml, config, persistence, tommy, api]
created: 2026-04-20
source: docs/log/learnings/tommy-toml-library.md
---

# Tommy TOML 库 API 陷阱

## 问题 / 背景

项目使用 Tommy 3.1.2 库读写 `.toml` 配置文件。Tommy 的 API 有若干与直觉不符的行为，容易踩坑。

## 解决方案 / 模式

### 类型判断：用 IsXxx 属性，不用 Type

```csharp
// ❌ 错误：TomlNode 没有 Type 属性（无 TomlType 枚举）
if (node.Type == TomlType.String) { ... }

// ✅ 正确：用 IsString / IsInteger 等布尔属性
if (node.IsString) { ... }
if (node.IsInteger) { ... }
```

### 数值类型：AsInteger 返回 long

```csharp
// ❌ 错误：隐式当 int 用
int value = node.AsInteger;

// ✅ 正确：需要显式强转
int value = (int)node.AsInteger;
```

### 字符串：AsString 返回 TomlString

```csharp
// ❌ 错误：直接当 string 用
string name = node.AsString;

// ✅ 正确：取 .Value 属性
string name = node.AsString.Value;
```

### 缺失键：不抛异常，返回空节点

```csharp
// ❌ 错误：直接访问可能缺失的键的属性
string val = root["missing"].AsString.Value; // NullReferenceException

// ✅ 正确：先判断节点类型
var child = root["missing"];
string val = child.IsString ? child.AsString.Value : defaultValue;
```

### 数组 + 表格：自动生成 [[section]] 语法

```csharp
// Tommy 的 TomlArray + TomlTable 组合自动输出为：
// [[material_slots]]
// index = 0
// albedo = "textures/grass.png"
var table = new TomlTable();
table["index"] = 0;
var array = new TomlArray { table };
root["material_slots"] = array;
```

## 关键要点

1. **IsString / IsInteger** 是判断类型的唯一正确方式
2. **AsInteger → long**，需要 `(int)` 强转
3. **AsString → TomlString**，需要 `.Value` 取字符串
4. **缺失键返回空 TomlNode**，不是 null，访问属性会抛异常
5. **TomlArray + TomlTable** 自动输出 `[[section]]` 语法

## 参考

- Tommy NuGet 包：`Tommy`（版本 3.1.2）
- 项目使用位置：`TomlProjectConfig.cs`