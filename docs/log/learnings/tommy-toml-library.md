---
name: Tommy TOML Library Usage
description: Tommy 3.1.2 NuGet 包的 API 用法和陷阱，用于 .toml 配置文件读写
type: reference
---

# Tommy TOML 库用法参考

## 基本信息
- **包**: Tommy 3.1.2 (NuGet)
- **目标框架**: netstandard2.0
- **许可证**: MIT

## 读取 TOML

```csharp
using Tommy;

using var reader = File.OpenText("config.toml");
TomlNode root = TOML.Parse(reader);
```

### 类型判断（不要用 Type 属性，没有 TomlType 枚举）
```csharp
// 正确 ✅ — 用 IsXxx 属性
node.IsString   // bool
node.IsInteger  // bool
node.IsFloat    // bool
node.IsBoolean  // bool
node.IsArray    // bool
node.IsTable    // bool

// 错误 ❌ — 不存在 TomlType 枚举
// node.Type == TomlType.String  // 编译错误！
```

### 安全访问字段
```csharp
// 先检查 key 存在，再检查类型
if (root.HasKey("name") && root["name"].IsString)
{
    string name = root["name"].AsString.Value;
}

// 类型转换
int version = (int)root["version"].AsInteger;  // AsInteger 返回 long
double scale = root["scale"].AsFloat;            // AsFloat 返回 double
bool enabled = root["enabled"].AsBoolean;
```

### 数组遍历
```csharp
if (root.HasKey("items") && root["items"].IsArray)
{
    foreach (TomlNode item in root["items"].AsArray)
    {
        // item 就是一个 TomlNode，可以用同样的 IsXxx 检查
    }
}
```

## 写入 TOML

```csharp
var root = new TomlTable();
root["version"] = 1;
root["name"] = "MyProject";

// 嵌套表
var terrain = new TomlTable();
terrain["heightmap"] = "path/to/map.png";
root["terrain"] = terrain;

// 数组表（对应 TOML 的 [[items]]）
var items = new TomlArray();
var item1 = new TomlTable();
item1["id"] = 0;
item1["label"] = "first";
items.Add(item1);
root["items"] = items;

// 写入文件
using var writer = File.CreateText("config.toml");
root.WriteTo(writer);
```

## 陷阱

1. **`AsInteger` 返回 `long`**，赋值给 `int` 需要 `(int)` 强转
2. **`AsString` 返回 `TomlString`**，取值要用 `.AsString.Value`
3. **没有 `TomlType` 枚举**，类型判断用 `IsString`/`IsInteger` 等属性
4. **不存在的 key 不会返回 null**，`root["missing"]` 返回一个空的 `TomlNode`，访问 `.AsString` 会抛异常
5. **数字赋值给 TomlNode** 支持 `int`/`long`/`double`/`float` 隐式转换
6. **字符串赋值** 支持 `string` 隐式转换到 `TomlString`

## 相关文件
- `Terrain.Editor/Services/TomlProjectConfig.cs` — 项目中的使用示例
- `Terrain.Editor/Terrain.Editor.csproj` — 包引用
