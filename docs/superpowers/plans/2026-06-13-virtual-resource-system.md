# Virtual Resource System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用固定入口和共享 resolver 替换 Terrain Runtime / Editor 的旧路径与旧 TOML 工作流，让 Editor 启动即加载 `LaunchSetting.json` + `map_data/default.toml`，并只把作者态资源写回当前命中的实体文件。

**Architecture:** 先在 `Terrain` 项目中建立可测试的资源层栈、虚拟路径解析器、以及 `default.toml` / `descriptor.toml` / `biome_settings.toml` 读取器，再用一个纯加载编排器把 Runtime 所需的 `.terrain`、biome mask、biome 规则和材质描述拼成稳定的资源包。`heightmap` 仅保留在 Editor 作者态链路中。Editor 侧不再维护平行规则，而是通过新的 `EditorResourceSession + EditorBootstrapService` 直接消费 Runtime 的 resolver 与读取器，负责自动加载、作者态写回和固定目标 `.terrain` 导出。

**Tech Stack:** C# / .NET 10 / Stride / Avalonia / Tommy / SixLabors.ImageSharp

> **Status Update (2026-06-14):** 主实现与关键自动验证已完成；后续收口补齐了工作区 `game/` 根定位、Editor 缺失 `.terrain`/`biome_mask.png` 的启动容错、Runtime 缺失必需资源时的错误日志，以及对应集成测试。最终实现相对原计划的一个明确调整是：`heightmap` 继续保留在 `default.toml` 和 Editor 作者态链路中，但 Runtime 不再解析或校验它，也不会把它放进 runtime bundle。手工 Editor 冒烟仍未执行；所有“提交”步骤均按用户要求未执行。

> **Superseded Snippet Notice (2026-06-14):** 下文部分 task-by-task 红/绿阶段代码片段保留为实现轨迹，不再保证与最终源码逐行一致。若片段与当前实现冲突，以本状态更新、`docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md` 和仓库当前代码为准。当前最终口径特别包括：1) `TerrainRuntimeResourceBundle` 不再暴露 `HeightmapPath`；2) Runtime bootstrap 不再解析/校验 `heightmap`；3) `EditorBootstrapService` 对 `terrain.terrain` / `biome_mask.png` 使用可写目标解析并允许缺失；4) 作者态保存通过事务化写回保证失败回滚。

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `Terrain/Properties/AssemblyInfo.cs` | 向 `Terrain.Editor.Tests` 暴露必要 internal 类型 |
| Create | `Terrain/Resources/LaunchSettings.cs` | `LaunchSetting.json` DTO |
| Create | `Terrain/Resources/LaunchSettingsService.cs` | 读取与校验 `LaunchSetting.json` |
| Create | `Terrain/Resources/GameResourceLayer.cs` | 资源层描述 |
| Create | `Terrain/Resources/ResolvedGameResource.cs` | 解析结果与来源信息 |
| Create | `Terrain/Resources/GameResourceResolver.cs` | `base + mods` 虚拟资源解析器 |
| Create | `Terrain/Resources/RuntimeMapDefinition.cs` | `map_data/default.toml` 运行时模型 |
| Create | `Terrain/Resources/RuntimeMapDefinitionReader.cs` | 读取并校验 `default.toml` |
| Create | `Terrain/Resources/RuntimeMaterialDescriptor.cs` | `map_data/materials/descriptor.toml` 运行时模型 |
| Create | `Terrain/Resources/RuntimeMaterialDescriptorReader.cs` | 读取材质描述文件 |
| Create | `Terrain/Resources/RuntimeBiomeSettings.cs` | `map_data/biome_settings.toml` 运行时模型 |
| Create | `Terrain/Resources/RuntimeBiomeSettingsReader.cs` | 读取 biome 规则文件 |
| Create | `Terrain/Resources/TerrainRuntimeResourceBundle.cs` | Runtime 资源加载结果聚合 |
| Create | `Terrain/Resources/TerrainRuntimeBootstrap.cs` | 负责把 resolver + 读取器拼成 Runtime 资源包 |
| Modify | `Terrain/Core/TerrainComponent.cs` | 移除旧路径字段，保留运行时配置字段 |
| Modify | `Terrain/Core/TerrainProcessor.cs` | 通过固定入口与 `TerrainRuntimeBootstrap` 加载资源 |
| Modify | `Terrain/Materials/RuntimeMaterialManager.cs` | 从新材质描述模型初始化 |
| Modify | `Terrain/Materials/RuntimeDetailMapBuilder.cs` | 从新 biome 规则模型生成 detail map |
| Delete | `Terrain/Materials/RuntimeBiomeConfig.cs` | 移除旧 `biome_config.toml` 读取器 |
| Modify | `Terrain/Assets/MainScene.sdscene` | 删除 `TerrainDataPath` / `BiomeConfigPath` 序列化字段 |
| Create | `Terrain.Editor/Services/Resources/EditorResourceSession.cs` | 保存当前解析到的资源句柄、写回目标和 dirty 状态 |
| Create | `Terrain.Editor/Services/Resources/EditorBootstrapService.cs` | Editor 启动自动加载固定入口 |
| Create | `Terrain.Editor/Services/Resources/HeightmapWriter.cs` | 写回 `map_data/heightmap.png` |
| Create | `Terrain.Editor/Services/Resources/BiomeMaskWriter.cs` | 写回 `map_data/biome_mask.png` |
| Create | `Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs` | 写回 `map_data/biome_settings.toml` |
| Create | `Terrain.Editor/Services/Resources/MaterialDescriptorWriter.cs` | 写回 `map_data/materials/descriptor.toml` |
| Modify | `Terrain.Editor/Services/TerrainManager.cs` | 去掉旧项目保存/加载，改为消费 `EditorResourceSession` |
| Modify | `Terrain.Editor/Services/MaterialSlotManager.cs` | 从新材质 descriptor 应用槽位、保留相对路径 |
| Modify | `Terrain.Editor/Services/BiomeRuleService.cs` | 从新 biome settings 应用规则，并写回时输出 `material_id` |
| Modify | `Terrain.Editor/Services/TextureThumbnailProvider.cs` | 使用 `EditorResourceSession` 解析短相对贴图路径 |
| Modify | `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs` | 固定导出目标为当前命中的 `map_data/terrain.terrain` |
| Delete | `Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs` | 删除旧导出器 |
| Delete | `Terrain.Editor/Services/ProjectManager.cs` | 删除旧项目工作流类 |
| Delete | `Terrain.Editor/Services/TomlProjectConfig.cs` | 删除旧项目 TOML 模型 |
| Modify | `Terrain.Editor/App.axaml.cs` | 启动时构建 `EditorShellViewModel` 并触发 bootstrap |
| Modify | `Terrain.Editor/ViewModels/EditorShellViewModel.cs` | 移除 Open/SaveAs 工作流，绑定 Save 与固定目标 Export |
| Modify | `Terrain.Editor/Views/MainWindow.axaml` | 去掉 Open / Save As 按钮、菜单和快捷键 |
| Create | `Terrain.Editor.Tests/TestHarness.cs` | 手写控制台测试公共断言与运行器 |
| Create | `Terrain.Editor.Tests/VirtualResources/LaunchSettingsResolverTests.cs` | resolver 单元测试 |
| Create | `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs` | TOML 读取器单元测试 |
| Create | `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs` | Runtime 资源包编排测试 |
| Create | `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs` | 文本级断言旧字段已移除 |
| Create | `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs` | Editor 写回规则测试 |
| Create | `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs` | UI 命令与菜单退场文本断言 |
| Modify | `Terrain.Editor.Tests/Program.cs` | 调用新的测试分组 |
| Modify | `docs/ARCHITECTURE_OVERVIEW.md` | 反映资源系统架构变化 |
| Modify | `docs/CURRENT_FEATURES.md` | 反映固定入口与虚拟资源系统状态 |

---

### Task 1: 建立共享 resolver 与测试骨架

**Files:**
- Create: `Terrain/Properties/AssemblyInfo.cs`
- Create: `Terrain/Resources/LaunchSettings.cs`
- Create: `Terrain/Resources/LaunchSettingsService.cs`
- Create: `Terrain/Resources/GameResourceLayer.cs`
- Create: `Terrain/Resources/ResolvedGameResource.cs`
- Create: `Terrain/Resources/GameResourceResolver.cs`
- Create: `Terrain.Editor.Tests/TestHarness.cs`
- Create: `Terrain.Editor.Tests/VirtualResources/LaunchSettingsResolverTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 先把测试 harness 和 resolver 失败用例写出来**

创建 `Terrain.Editor.Tests/TestHarness.cs`：

```csharp
namespace Terrain.Editor.Tests;

internal static class TestHarness
{
    public static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    public static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void AssertEqual<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}. Expected: {expected}. Actual: {actual}.");
    }
}
```

创建 `Terrain.Editor.Tests/VirtualResources/LaunchSettingsResolverTests.cs`：

```csharp
using System.Text.Json;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class LaunchSettingsResolverTests
{
    public static void RunAll()
    {
        TestHarness.Run("launch settings loads enabled mods in declared order", LoadsEnabledModsInDeclaredOrder);
        TestHarness.Run("resolver prefers highest priority layer", ResolverPrefersHighestPriorityLayer);
        TestHarness.Run("resolver reports writable hit and fallback state", ResolverReportsWritableHitAndFallbackState);
    }

    private static void LoadsEnabledModsInDeclaredOrder()
    {
        string root = CreateWorkspace();
        File.WriteAllText(Path.Combine(root, "LaunchSetting.json"), JsonSerializer.Serialize(new
        {
            version = 1,
            mods = new object[]
            {
                new { id = "mod_a", root = Path.Combine(root, "mod_a"), enabled = true },
                new { id = "mod_b", root = Path.Combine(root, "mod_b"), enabled = false },
                new { id = "mod_c", root = Path.Combine(root, "mod_c"), enabled = true },
            }
        }));

        LaunchSettings settings = LaunchSettingsService.Load(Path.Combine(root, "LaunchSetting.json"));

        TestHarness.AssertEqual(1, settings.Version, "version");
        TestHarness.AssertEqual(3, settings.Mods.Count, "mods count");
        TestHarness.AssertEqual("mod_a", settings.Mods[0].Id, "first mod id");
        TestHarness.AssertEqual("mod_c", settings.Mods[2].Id, "third mod id");
    }

    private static void ResolverPrefersHighestPriorityLayer()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_a", "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_b", "map_data"));

        File.WriteAllText(Path.Combine(root, "map_data", "default.toml"), "base");
        File.WriteAllText(Path.Combine(root, "mod_a", "map_data", "default.toml"), "mod_a");
        File.WriteAllText(Path.Combine(root, "mod_b", "map_data", "default.toml"), "mod_b");

        var layers = new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
            new GameResourceLayer("mod_a", Path.Combine(root, "mod_a"), isBaseLayer: false),
            new GameResourceLayer("mod_b", Path.Combine(root, "mod_b"), isBaseLayer: false),
        };

        var resolver = new GameResourceResolver(layers);
        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data/default.toml");

        TestHarness.AssertEqual("mod_b", resolved.SourceLayerId, "highest priority layer");
        TestHarness.AssertEqual("mod_b", File.ReadAllText(resolved.ResolvedPath), "resolved file contents");
    }

    private static void ResolverReportsWritableHitAndFallbackState()
    {
        string root = CreateWorkspace();
        Directory.CreateDirectory(Path.Combine(root, "map_data"));
        Directory.CreateDirectory(Path.Combine(root, "mod_a", "map_data"));

        string baseFile = Path.Combine(root, "map_data", "heightmap.png");
        string modFile = Path.Combine(root, "mod_a", "map_data", "heightmap.png");
        File.WriteAllText(baseFile, "base");
        File.WriteAllText(modFile, "mod");

        var resolver = new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
            new GameResourceLayer("mod_a", Path.Combine(root, "mod_a"), isBaseLayer: false),
        });

        ResolvedGameResource resolved = resolver.ResolveRequiredFile("map_data/heightmap.png");

        TestHarness.Assert(resolved.IsWritable, "resolved hit should be writable in temp workspace");
        TestHarness.Assert(resolved.HasLowerPriorityFallback, "resolved mod file should report covered base fallback");
    }

    private static string CreateWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-virtual-resource-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
```

- [x] **Step 2: 运行测试，确认当前必然失败**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected: `BUILD FAILED`，并包含 `CS0246` 或 `CS0103`，提示缺少 `Terrain.Resources`、`LaunchSettings`、`LaunchSettingsService`、`GameResourceResolver`

- [x] **Step 3: 写 resolver 最小实现**

创建 `Terrain/Properties/AssemblyInfo.cs`：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Terrain.Editor.Tests")]
```

创建 `Terrain/Resources/LaunchSettings.cs`：

```csharp
namespace Terrain.Resources;

public sealed class LaunchSettings
{
    public int Version { get; set; } = 1;
    public List<LaunchSettingsMod> Mods { get; set; } = new();
}

public sealed class LaunchSettingsMod
{
    public string Id { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
```

创建 `Terrain/Resources/GameResourceLayer.cs`：

```csharp
namespace Terrain.Resources;

public readonly record struct GameResourceLayer(string Id, string RootPath, bool IsBaseLayer);
```

创建 `Terrain/Resources/ResolvedGameResource.cs`：

```csharp
namespace Terrain.Resources;

public readonly record struct ResolvedGameResource(
    string VirtualPath,
    string ResolvedPath,
    string SourceLayerId,
    bool IsWritable,
    bool HasLowerPriorityFallback);
```

创建 `Terrain/Resources/LaunchSettingsService.cs`：

```csharp
using System.Text.Json;

namespace Terrain.Resources;

public static class LaunchSettingsService
{
    public static LaunchSettings Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("LaunchSetting.json was not found.", filePath);

        LaunchSettings? settings = JsonSerializer.Deserialize<LaunchSettings>(File.ReadAllText(filePath));
        if (settings == null)
            throw new InvalidDataException("LaunchSetting.json could not be parsed.");
        if (settings.Version != 1)
            throw new InvalidDataException($"Unsupported LaunchSetting.json version: {settings.Version}.");

        return settings;
    }
}
```

创建 `Terrain/Resources/GameResourceResolver.cs`：

```csharp
namespace Terrain.Resources;

public sealed class GameResourceResolver
{
    private readonly IReadOnlyList<GameResourceLayer> layers;

    public GameResourceResolver(IReadOnlyList<GameResourceLayer> layers)
    {
        this.layers = layers;
    }

    public ResolvedGameResource ResolveRequiredFile(string virtualPath)
    {
        string normalized = NormalizeVirtualPath(virtualPath);
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            GameResourceLayer layer = layers[i];
            string candidate = Path.Combine(layer.RootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(candidate))
                continue;

            bool hasLowerPriorityFallback = false;
            for (int lower = i - 1; lower >= 0; lower--)
            {
                string lowerCandidate = Path.Combine(layers[lower].RootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(lowerCandidate))
                {
                    hasLowerPriorityFallback = true;
                    break;
                }
            }

            return new ResolvedGameResource(
                normalized,
                Path.GetFullPath(candidate),
                layer.Id,
                IsWritable(candidate),
                hasLowerPriorityFallback);
        }

        throw new FileNotFoundException($"Virtual resource was not found: {normalized}", normalized);
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        if (string.IsNullOrWhiteSpace(virtualPath))
            throw new InvalidDataException("Virtual path is empty.");

        string normalized = virtualPath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Parent traversal is not allowed: {virtualPath}");
        return normalized;
    }

    private static bool IsWritable(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReadOnly) == 0;
    }
}
```

- [x] **Step 4: 运行测试，确认 resolver 骨架通过**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS launch settings loads enabled mods in declared order
PASS resolver prefers highest priority layer
PASS resolver reports writable hit and fallback state
```

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain/Properties/AssemblyInfo.cs Terrain/Resources/LaunchSettings.cs Terrain/Resources/LaunchSettingsService.cs Terrain/Resources/GameResourceLayer.cs Terrain/Resources/ResolvedGameResource.cs Terrain/Resources/GameResourceResolver.cs Terrain.Editor.Tests/TestHarness.cs Terrain.Editor.Tests/VirtualResources/LaunchSettingsResolverTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add shared virtual resource resolver core"
```

---

### Task 2: 添加 `default.toml`、材质 descriptor、biome settings 读取器

**Files:**
- Create: `Terrain/Resources/RuntimeMapDefinition.cs`
- Create: `Terrain/Resources/RuntimeMapDefinitionReader.cs`
- Create: `Terrain/Resources/RuntimeMaterialDescriptor.cs`
- Create: `Terrain/Resources/RuntimeMaterialDescriptorReader.cs`
- Create: `Terrain/Resources/RuntimeBiomeSettings.cs`
- Create: `Terrain/Resources/RuntimeBiomeSettingsReader.cs`
- Create: `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 写 descriptor 读取器失败用例**

创建 `Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs`：

```csharp
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class DescriptorReaderTests
{
    public static void RunAll()
    {
        TestHarness.Run("map definition reads required and optional paths", MapDefinitionReadsRequiredAndOptionalPaths);
        TestHarness.Run("material descriptor preserves short relative texture paths", MaterialDescriptorPreservesShortRelativePaths);
        TestHarness.Run("biome settings keep material_id references", BiomeSettingsKeepMaterialIdReferences);
    }

    private static void MapDefinitionReadsRequiredAndOptionalPaths()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "default.toml"), """
version = 1

[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
rivers = "rivers.png"

[settings]
height_scale = 200.0
""");

        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(Path.Combine(root, "default.toml"));

        TestHarness.AssertEqual("heightmap.png", map.HeightmapPath, "heightmap path");
        TestHarness.AssertEqual("terrain.terrain", map.TerrainDataPath, "terrain data path");
        TestHarness.AssertEqual("rivers.png", map.RiversPath, "rivers path");
        TestHarness.AssertEqual(null, map.ProvincesPath, "provinces path");
        TestHarness.AssertEqual(200.0f, map.HeightScale, "height scale");
    }

    private static void MaterialDescriptorPreservesShortRelativePaths()
    {
        string root = Path.Combine(CreateMapData(), "materials");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "descriptor.toml"), """
version = 1

[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
normal = "grass_n.png"
properties = "grass_p.png"
""");

        RuntimeMaterialDescriptor descriptor = RuntimeMaterialDescriptorReader.ReadFrom(Path.Combine(root, "descriptor.toml"));

        TestHarness.AssertEqual(1, descriptor.Materials.Count, "material count");
        TestHarness.AssertEqual("grassland", descriptor.Materials[0].Id, "material id");
        TestHarness.AssertEqual("grass_a.png", descriptor.Materials[0].AlbedoPath, "albedo path");
    }

    private static void BiomeSettingsKeepMaterialIdReferences()
    {
        string root = CreateMapData();
        File.WriteAllText(Path.Combine(root, "biome_settings.toml"), """
version = 1

[[biomes]]
id = 1
name = "Default Biome"

[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true

[[modifiers]]
id = 1
layer_id = 1
type = "HeightRange"
blend_mode = "Multiply"
min = 0
max = 1
enabled = true
visible = true
opacity = 1
min_falloff = 0.1
max_falloff = 0.1
""");

        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(Path.Combine(root, "biome_settings.toml"));

        TestHarness.AssertEqual(1, settings.Layers.Count, "layer count");
        TestHarness.AssertEqual("grassland", settings.Layers[0].MaterialId, "material id binding");
        TestHarness.AssertEqual(1, settings.Modifiers.Count, "modifier count");
    }

    private static string CreateMapData()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-map-definition-tests", Guid.NewGuid().ToString("N"), "map_data");
        Directory.CreateDirectory(root);
        return root;
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
DescriptorReaderTests.RunAll();
```

- [x] **Step 2: 运行测试，确认读取器类型尚未实现**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected: `BUILD FAILED`，并包含 `RuntimeMapDefinition`、`RuntimeMaterialDescriptor`、`RuntimeBiomeSettings` 相关缺失类型错误

- [x] **Step 3: 写读取器与模型最小实现**

创建 `Terrain/Resources/RuntimeMapDefinition.cs`：

```csharp
namespace Terrain.Resources;

public sealed class RuntimeMapDefinition
{
    public string HeightmapPath { get; init; } = string.Empty;
    public string TerrainDataPath { get; init; } = string.Empty;
    public string? RiversPath { get; init; }
    public string? ProvincesPath { get; init; }
    public float HeightScale { get; init; }
}
```

创建 `Terrain/Resources/RuntimeMaterialDescriptor.cs`：

```csharp
namespace Terrain.Resources;

public sealed class RuntimeMaterialDescriptor
{
    public List<RuntimeMaterialEntry> Materials { get; } = new();
}

public sealed class RuntimeMaterialEntry
{
    public string Id { get; init; } = string.Empty;
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AlbedoPath { get; init; }
    public string? NormalPath { get; init; }
    public string? PropertiesPath { get; init; }
}
```

创建 `Terrain/Resources/RuntimeBiomeSettings.cs`：

```csharp
namespace Terrain.Resources;

public sealed class RuntimeBiomeSettings
{
    public List<RuntimeBiomeEntry> Biomes { get; } = new();
    public List<RuntimeBiomeLayerEntry> Layers { get; } = new();
    public List<RuntimeBiomeModifierEntry> Modifiers { get; } = new();
}

public sealed class RuntimeBiomeEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class RuntimeBiomeLayerEntry
{
    public int Id { get; init; }
    public int BiomeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MaterialId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public bool Enabled { get; init; }
    public bool Visible { get; init; }
}

public sealed class RuntimeBiomeModifierEntry
{
    public int Id { get; init; }
    public int LayerId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string BlendMode { get; init; } = string.Empty;
    public float Min { get; init; }
    public float Max { get; init; }
    public float MinFalloff { get; init; }
    public float MaxFalloff { get; init; }
    public float Opacity { get; init; }
    public bool Enabled { get; init; }
    public bool Visible { get; init; }
}
```

创建 `Terrain/Resources/RuntimeMapDefinitionReader.cs`、`RuntimeMaterialDescriptorReader.cs`、`RuntimeBiomeSettingsReader.cs`，都遵循同一个模式：

```csharp
using Tommy;

namespace Terrain.Resources;

public static class RuntimeMapDefinitionReader
{
    public static RuntimeMapDefinition ReadFrom(string filePath)
    {
        using var reader = File.OpenText(filePath);
        TomlTable root = TOML.Parse(reader);

        TomlNode terrain = root["terrain"];
        TomlNode settings = root["settings"];

        string heightmap = terrain["heightmap"].AsString.Value;
        string terrainData = terrain["terrain_data"].AsString.Value;
        float heightScale = settings["height_scale"].IsFloat
            ? (float)settings["height_scale"].AsFloat.Value
            : (float)settings["height_scale"].AsInteger.Value;

        if (string.IsNullOrWhiteSpace(heightmap) || string.IsNullOrWhiteSpace(terrainData))
            throw new InvalidDataException("default.toml requires heightmap and terrain_data.");
        if (heightScale <= 0)
            throw new InvalidDataException("height_scale must be greater than zero.");

        return new RuntimeMapDefinition
        {
            HeightmapPath = heightmap,
            TerrainDataPath = terrainData,
            RiversPath = terrain.HasKey("rivers") ? terrain["rivers"].AsString.Value : null,
            ProvincesPath = terrain.HasKey("provinces") ? terrain["provinces"].AsString.Value : null,
            HeightScale = heightScale,
        };
    }
}
```

`RuntimeMaterialDescriptorReader` 和 `RuntimeBiomeSettingsReader` 用同样的 Tommy 读取模式，直接把 descriptor / settings 内容转成上面的模型，不做路径推断，不做自动命名。

- [x] **Step 4: 运行测试，确认新读取器通过**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS map definition reads required and optional paths
PASS material descriptor preserves short relative texture paths
PASS biome settings keep material_id references
```

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain/Resources/RuntimeMapDefinition.cs Terrain/Resources/RuntimeMapDefinitionReader.cs Terrain/Resources/RuntimeMaterialDescriptor.cs Terrain/Resources/RuntimeMaterialDescriptorReader.cs Terrain/Resources/RuntimeBiomeSettings.cs Terrain/Resources/RuntimeBiomeSettingsReader.cs Terrain.Editor.Tests/VirtualResources/DescriptorReaderTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add runtime map and descriptor readers"
```

---

### Task 3: 创建 `TerrainRuntimeBootstrap` 纯编排器

**Files:**
- Create: `Terrain/Resources/TerrainRuntimeResourceBundle.cs`
- Create: `Terrain/Resources/TerrainRuntimeBootstrap.cs`
- Create: `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 写运行时资源包编排失败用例**

> **Final-state note:** 本步骤下面的首版测试片段是历史红灯草稿。最终测试口径已更新为：Runtime bundle 不包含 `HeightmapPath`，并额外验证 Runtime 不要求 `heightmap` 资源或声明、缺少 `terrain.terrain` / `biome_mask.png` 会失败、缺少 `rivers.png` 仍保持 optional。

创建 `Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs`：

```csharp
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class TerrainRuntimeBootstrapTests
{
    public static void RunAll()
    {
        TestHarness.Run("bootstrap loads fixed companion resources", BootstrapLoadsFixedCompanionResources);
        TestHarness.Run("bootstrap keeps rivers optional", BootstrapKeepsRiversOptional);
        TestHarness.Run("bootstrap reports provinces as declared but not implemented", BootstrapReportsProvincesAsNotImplemented);
    }

    private static void BootstrapLoadsFixedCompanionResources()
    {
        string root = CreateWorkspace(withRivers: false, withProvinces: false);
        var bootstrap = new TerrainRuntimeBootstrap(new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        }));

        TerrainRuntimeResourceBundle bundle = bootstrap.Load();

        TestHarness.Assert(bundle.HeightmapPath.EndsWith(Path.Combine("map_data", "heightmap.png")), "heightmap path");
        TestHarness.Assert(bundle.TerrainDataPath.EndsWith(Path.Combine("map_data", "terrain.terrain")), "terrain path");
        TestHarness.Assert(bundle.BiomeMaskPath.EndsWith(Path.Combine("map_data", "biome_mask.png")), "biome mask path");
        TestHarness.Assert(bundle.BiomeSettingsPath.EndsWith(Path.Combine("map_data", "biome_settings.toml")), "biome settings path");
        TestHarness.Assert(bundle.MaterialDescriptorPath.EndsWith(Path.Combine("map_data", "materials", "descriptor.toml")), "materials descriptor path");
    }

    private static void BootstrapKeepsRiversOptional()
    {
        string root = CreateWorkspace(withRivers: false, withProvinces: false);
        var bootstrap = new TerrainRuntimeBootstrap(new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        }));

        TerrainRuntimeResourceBundle bundle = bootstrap.Load();

        TestHarness.AssertEqual(null, bundle.RiversPath, "rivers path");
        TestHarness.AssertEqual(false, bundle.HasDeclaredProvinces, "provinces declared flag");
    }

    private static void BootstrapReportsProvincesAsNotImplemented()
    {
        string root = CreateWorkspace(withRivers: false, withProvinces: true);
        var bootstrap = new TerrainRuntimeBootstrap(new GameResourceResolver(new[]
        {
            new GameResourceLayer("base", root, isBaseLayer: true),
        }));

        TerrainRuntimeResourceBundle bundle = bootstrap.Load();

        TestHarness.Assert(bundle.HasDeclaredProvinces, "provinces declared");
        TestHarness.Assert(bundle.Diagnostics.Any(d => d.Contains("not implemented", StringComparison.OrdinalIgnoreCase)), "diagnostic should mention provinces are not implemented");
    }

    private static string CreateWorkspace(bool withRivers, bool withProvinces)
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-bootstrap-tests", Guid.NewGuid().ToString("N"));
        string mapData = Path.Combine(root, "map_data");
        string materials = Path.Combine(mapData, "materials");
        Directory.CreateDirectory(materials);

        File.WriteAllText(Path.Combine(root, "LaunchSetting.json"), """{"version":1,"mods":[]}""");
        File.WriteAllText(Path.Combine(mapData, "default.toml"), withProvinces
            ? """
version = 1
[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
provinces = "provinces.png"
[settings]
height_scale = 100
"""
            : """
version = 1
[terrain]
heightmap = "heightmap.png"
terrain_data = "terrain.terrain"
[settings]
height_scale = 100
""");
        File.WriteAllText(Path.Combine(mapData, "heightmap.png"), "height");
        File.WriteAllText(Path.Combine(mapData, "terrain.terrain"), "terrain");
        File.WriteAllText(Path.Combine(mapData, "biome_mask.png"), "mask");
        File.WriteAllText(Path.Combine(mapData, "biome_settings.toml"), """
version = 1
[[biomes]]
id = 1
name = "Default Biome"
[[layers]]
id = 1
biome_id = 1
name = "Default Base"
material_id = "grassland"
priority = 0
enabled = true
visible = true
[[modifiers]]
id = 1
layer_id = 1
type = "HeightRange"
blend_mode = "Multiply"
min = 0
max = 1
enabled = true
visible = true
opacity = 1
min_falloff = 0.1
max_falloff = 0.1
""");
        File.WriteAllText(Path.Combine(materials, "descriptor.toml"), """
version = 1
[[materials]]
id = "grassland"
index = 0
name = "Grassland"
albedo = "grass_a.png"
""");
        File.WriteAllText(Path.Combine(materials, "grass_a.png"), "albedo");
        if (withRivers)
            File.WriteAllText(Path.Combine(mapData, "rivers.png"), "rivers");
        if (withProvinces)
            File.WriteAllText(Path.Combine(mapData, "provinces.png"), "provinces");

        return root;
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
DescriptorReaderTests.RunAll();
TerrainRuntimeBootstrapTests.RunAll();
```

- [x] **Step 2: 运行测试，确认缺少 bootstrap 类型**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected: `BUILD FAILED`，并包含 `TerrainRuntimeBootstrap`、`TerrainRuntimeResourceBundle` 缺失错误

- [x] **Step 3: 写编排器最小实现**

> **Final-state note:** 本步骤下面的首版 bootstrap 片段已被最终实现替换。当前代码使用 `RuntimeMapDefinitionReader.ReadFrom(..., requireHeightmap: false)`，只把 `.terrain`、`biome_mask.png`、`biome_settings.toml`、`materials/descriptor.toml` 和可选 `rivers.png` 组装进 runtime bundle，并通过 `material_id` 校验 biome 引用。

创建 `Terrain/Resources/TerrainRuntimeResourceBundle.cs`：

```csharp
using Terrain.Shared;

namespace Terrain.Resources;

public sealed class TerrainRuntimeResourceBundle
{
    public string HeightmapPath { get; init; } = string.Empty;
    public string TerrainDataPath { get; init; } = string.Empty;
    public string BiomeMaskPath { get; init; } = string.Empty;
    public string BiomeSettingsPath { get; init; } = string.Empty;
    public string MaterialDescriptorPath { get; init; } = string.Empty;
    public string? RiversPath { get; init; }
    public bool HasDeclaredProvinces { get; init; }
    public float HeightScale { get; init; }
    public List<RuntimeMaterialEntry> Materials { get; init; } = new();
    public List<TerrainBiomeRuleLayer> RuntimeLayers { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
}
```

创建 `Terrain/Resources/TerrainRuntimeBootstrap.cs`：

```csharp
using Terrain.Shared;

namespace Terrain.Resources;

public sealed class TerrainRuntimeBootstrap
{
    private const string DefaultMapPath = "map_data/default.toml";
    private const string BiomeMaskVirtualPath = "map_data/biome_mask.png";
    private const string BiomeSettingsVirtualPath = "map_data/biome_settings.toml";
    private const string MaterialDescriptorVirtualPath = "map_data/materials/descriptor.toml";

    private readonly GameResourceResolver resolver;

    public TerrainRuntimeBootstrap(GameResourceResolver resolver)
    {
        this.resolver = resolver;
    }

    public TerrainRuntimeResourceBundle Load()
    {
        ResolvedGameResource mapFile = resolver.ResolveRequiredFile(DefaultMapPath);
        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(mapFile.ResolvedPath);
        ResolvedGameResource heightmap = resolver.ResolveRequiredFile($"map_data/{map.HeightmapPath}");
        ResolvedGameResource terrainData = resolver.ResolveRequiredFile($"map_data/{map.TerrainDataPath}");
        ResolvedGameResource biomeMask = resolver.ResolveRequiredFile(BiomeMaskVirtualPath);
        ResolvedGameResource biomeSettings = resolver.ResolveRequiredFile(BiomeSettingsVirtualPath);
        ResolvedGameResource materialDescriptor = resolver.ResolveRequiredFile(MaterialDescriptorVirtualPath);

        RuntimeMaterialDescriptor materials = RuntimeMaterialDescriptorReader.ReadFrom(materialDescriptor.ResolvedPath);
        RuntimeBiomeSettings settings = RuntimeBiomeSettingsReader.ReadFrom(biomeSettings.ResolvedPath);

        var indexByMaterialId = materials.Materials.ToDictionary(m => m.Id, m => m.Index, StringComparer.OrdinalIgnoreCase);
        var runtimeLayers = BuildRuntimeLayers(settings, indexByMaterialId);
        var diagnostics = new List<string>();
        string? riversPath = null;
        bool hasDeclaredProvinces = !string.IsNullOrWhiteSpace(map.ProvincesPath);

        if (!string.IsNullOrWhiteSpace(map.RiversPath))
            riversPath = resolver.ResolveRequiredFile($"map_data/{map.RiversPath}").ResolvedPath;

        if (hasDeclaredProvinces)
            diagnostics.Add("map_data/provinces.png is declared but the provinces pipeline is not implemented in v1.");

        return new TerrainRuntimeResourceBundle
        {
            HeightmapPath = heightmap.ResolvedPath,
            TerrainDataPath = terrainData.ResolvedPath,
            BiomeMaskPath = biomeMask.ResolvedPath,
            BiomeSettingsPath = biomeSettings.ResolvedPath,
            MaterialDescriptorPath = materialDescriptor.ResolvedPath,
            RiversPath = riversPath,
            HasDeclaredProvinces = hasDeclaredProvinces,
            HeightScale = map.HeightScale,
            Materials = materials.Materials,
            RuntimeLayers = runtimeLayers,
            Diagnostics = diagnostics,
        };
    }

    private static List<TerrainBiomeRuleLayer> BuildRuntimeLayers(RuntimeBiomeSettings settings, IReadOnlyDictionary<string, int> indexByMaterialId)
    {
        var layers = new List<TerrainBiomeRuleLayer>();
        var modifiersByLayer = settings.Modifiers.GroupBy(m => m.LayerId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (RuntimeBiomeLayerEntry layer in settings.Layers.OrderBy(l => l.Priority))
        {
            if (!indexByMaterialId.TryGetValue(layer.MaterialId, out int materialIndex))
                throw new InvalidDataException($"Unknown material_id '{layer.MaterialId}' in biome settings.");

            var runtimeLayer = new TerrainBiomeRuleLayer
            {
                Name = layer.Name,
                BiomeId = layer.BiomeId,
                Enabled = layer.Enabled,
                Visible = layer.Visible,
                MaterialSlotIndex = materialIndex,
                PriorityOrder = layer.Priority,
            };

            foreach (RuntimeBiomeModifierEntry modifier in modifiersByLayer.GetValueOrDefault(layer.Id) ?? new List<RuntimeBiomeModifierEntry>())
            {
                runtimeLayer.Modifiers.Add(new TerrainBiomeModifier
                {
                    Name = modifier.Id.ToString(),
                    Type = Enum.Parse<BiomeModifierType>(modifier.Type, ignoreCase: true),
                    BlendMode = Enum.Parse<BiomeModifierBlendMode>(modifier.BlendMode, ignoreCase: true),
                    Enabled = modifier.Enabled,
                    Visible = modifier.Visible,
                    Opacity = modifier.Opacity,
                    Min = modifier.Min,
                    Max = modifier.Max,
                    MinFalloff = modifier.MinFalloff,
                    MaxFalloff = modifier.MaxFalloff,
                });
            }

            layers.Add(runtimeLayer);
        }

        return layers;
    }
}
```

- [x] **Step 4: 运行测试，确认编排器通过**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS bootstrap loads fixed companion resources
PASS bootstrap keeps rivers optional
PASS bootstrap reports provinces as declared but not implemented
```

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain/Resources/TerrainRuntimeResourceBundle.cs Terrain/Resources/TerrainRuntimeBootstrap.cs Terrain.Editor.Tests/VirtualResources/TerrainRuntimeBootstrapTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add runtime resource bootstrap pipeline"
```

---

### Task 4: 将 Runtime 切到固定入口并移除旧路径字段

**Files:**
- Modify: `Terrain/Core/TerrainComponent.cs`
- Modify: `Terrain/Core/TerrainProcessor.cs`
- Modify: `Terrain/Materials/RuntimeMaterialManager.cs`
- Modify: `Terrain/Materials/RuntimeDetailMapBuilder.cs`
- Delete: `Terrain/Materials/RuntimeBiomeConfig.cs`
- Modify: `Terrain/Assets/MainScene.sdscene`
- Create: `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 先写“旧路径字段和旧入口已退场”的文本测试**

创建 `Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs`：

```csharp
namespace Terrain.Editor.Tests.VirtualResources;

internal static class RuntimeMigrationTextTests
{
    public static void RunAll()
    {
        TestHarness.Run("terrain component no longer exposes old resource path fields", TerrainComponentNoLongerExposesOldResourcePathFields);
        TestHarness.Run("main scene no longer serializes old terrain resource paths", MainSceneNoLongerSerializesOldTerrainResourcePaths);
    }

    private static void TerrainComponentNoLongerExposesOldResourcePathFields()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Terrain", "Core", "TerrainComponent.cs"));
        TestHarness.Assert(!source.Contains("TerrainDataPath", StringComparison.Ordinal), "TerrainDataPath should be removed from TerrainComponent");
        TestHarness.Assert(!source.Contains("BiomeConfigPath", StringComparison.Ordinal), "BiomeConfigPath should be removed from TerrainComponent");
    }

    private static void MainSceneNoLongerSerializesOldTerrainResourcePaths()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Terrain", "Assets", "MainScene.sdscene"));
        TestHarness.Assert(!source.Contains("TerrainDataPath:", StringComparison.Ordinal), "MainScene should no longer serialize TerrainDataPath");
        TestHarness.Assert(!source.Contains("BiomeConfigPath:", StringComparison.Ordinal), "MainScene should no longer serialize BiomeConfigPath");
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
DescriptorReaderTests.RunAll();
TerrainRuntimeBootstrapTests.RunAll();
RuntimeMigrationTextTests.RunAll();
```

- [x] **Step 2: 运行测试，确认当前文本断言失败**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
FAIL terrain component no longer exposes old resource path fields: TerrainDataPath should be removed from TerrainComponent
FAIL main scene no longer serializes old terrain resource paths: MainScene should no longer serialize TerrainDataPath
```

- [x] **Step 3: 最小改造 Runtime 主链路**

在 `Terrain/Core/TerrainComponent.cs` 中删除：

```csharp
[DataMember(10)]
public string? TerrainDataPath { get; set; }

[DataMember(15)]
public string? BiomeConfigPath { get; set; }
```

并把 `TerrainConfig` 收成：

```csharp
internal struct TerrainConfig : IEquatable<TerrainConfig>
{
    public int MaxVisibleChunkInstances;
    public int MaxResidentChunks;

    public static TerrainConfig Capture(TerrainComponent component)
    {
        return new TerrainConfig
        {
            MaxVisibleChunkInstances = component.MaxVisibleChunkInstances,
            MaxResidentChunks = component.MaxResidentChunks,
        };
    }

    public bool Equals(TerrainConfig other)
        => MaxVisibleChunkInstances == other.MaxVisibleChunkInstances
        && MaxResidentChunks == other.MaxResidentChunks;

    public override int GetHashCode()
        => HashCode.Combine(MaxVisibleChunkInstances, MaxResidentChunks);
}
```

把 `Terrain/Materials/RuntimeDetailMapBuilder.cs` 签名改成：

```csharp
public static RuntimeDetailMapData Generate(
    ushort[] heightData,
    int heightWidth,
    int heightHeight,
    byte[] biomeMaskData,
    int biomeMaskWidth,
    int biomeMaskHeight,
    IReadOnlyList<TerrainBiomeRuleLayer> biomeLayers,
    float heightScale,
    int biomeMaskResolutionRatio)
```

把 `Terrain/Materials/RuntimeMaterialManager.cs` 改成直接吃 descriptor 结果：

```csharp
public static List<(int index, string albedoPath, string? normalPath, string? propertiesPath)> ReadMaterialSlots(
    string materialsDirectory,
    IReadOnlyList<RuntimeMaterialEntry> materials)
{
    return materials
        .Select(static material => (
            material.Index,
            Path.Combine(materialsDirectory, material.AlbedoPath ?? string.Empty),
            string.IsNullOrWhiteSpace(material.NormalPath) ? null : Path.Combine(materialsDirectory, material.NormalPath),
            string.IsNullOrWhiteSpace(material.PropertiesPath) ? null : Path.Combine(materialsDirectory, material.PropertiesPath)))
        .ToList();
}
```

把 `Terrain/Core/TerrainProcessor.cs` 的资源加载入口改成：

```csharp
private bool TryLoadTerrainData(TerrainComponent component, out LoadedTerrainData loadedData)
{
    loadedData = default;
    try
    {
        string appRoot = AppContext.BaseDirectory;
        LaunchSettings launchSettings = LaunchSettingsService.Load(Path.Combine(appRoot, "LaunchSetting.json"));
        var layers = new List<GameResourceLayer> { new("base", appRoot, isBaseLayer: true) };
        layers.AddRange(launchSettings.Mods
            .Where(static mod => mod.Enabled)
            .Select(static mod => new GameResourceLayer(mod.Id, mod.Root, isBaseLayer: false)));

        var bootstrap = new TerrainRuntimeBootstrap(new GameResourceResolver(layers));
        TerrainRuntimeResourceBundle bundle = bootstrap.Load();

        var fileReader = new TerrainFileReader(bundle.TerrainDataPath);
        var minMaxErrorMaps = fileReader.ReadAllMinMaxErrorMaps();
        ushort[] heightData = fileReader.ReadAllHeightData();
        byte[] biomeMaskData = fileReader.ReadAllBiomeMaskData();
        RuntimeDetailMapData generatedDetailMaps = RuntimeDetailMapBuilder.Generate(
            heightData,
            fileReader.Header.Width,
            fileReader.Header.Height,
            biomeMaskData,
            fileReader.SplatMapHeader.Width,
            fileReader.SplatMapHeader.Height,
            bundle.RuntimeLayers,
            bundle.HeightScale,
            fileReader.SplatMapResolutionRatio);

        component.HeightScale = bundle.HeightScale;
        loadedData = new LoadedTerrainData(
            bundle,
            fileReader,
            generatedDetailMaps,
            fileReader.Header.Width,
            fileReader.Header.Height,
            minMaxErrorMaps);
        return true;
    }
    catch (Exception ex)
    {
        Log.Warning($"Terrain data could not be read: {ex.Message}");
        return false;
    }
}
```

把 `Terrain/Assets/MainScene.sdscene` 中 TerrainComponent 片段改成只保留：

```yaml
2ba1961aff3ebcce0bb304a3bbc609ea: !TerrainComponent
    Id: ac8e605b-05c2-4bc6-9829-5ccb824040d0
    HeightScale: 200.0
    MaxScreenSpaceErrorPixels: 8.0
    DefaultDiffuseTexture: 4ecb1370-7ee3-41d3-a06f-17ca7336ae72:Grid Gray 128x128
    BaseColor: {R: 1.0, G: 1.0, B: 1.0, A: 1.0}
```

删除 `Terrain/Materials/RuntimeBiomeConfig.cs` 文件。

- [x] **Step 4: 运行测试并做一次完整编译**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS terrain component no longer exposes old resource path fields
PASS main scene no longer serializes old terrain resource paths
```

Run: `dotnet build Terrain.sln`

Expected: `Build succeeded.`

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain/Core/TerrainComponent.cs Terrain/Core/TerrainProcessor.cs Terrain/Materials/RuntimeMaterialManager.cs Terrain/Materials/RuntimeDetailMapBuilder.cs Terrain/Assets/MainScene.sdscene Terrain/Properties/AssemblyInfo.cs Terrain.Editor.Tests/VirtualResources/RuntimeMigrationTextTests.cs Terrain.Editor.Tests/Program.cs
git rm Terrain/Materials/RuntimeBiomeConfig.cs
git commit -m "refactor: switch runtime terrain loading to fixed virtual resources"
```

---

### Task 5: 创建 Editor 资源会话与作者态写回器

**Files:**
- Create: `Terrain.Editor/Services/Resources/EditorResourceSession.cs`
- Create: `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`
- Create: `Terrain.Editor/Services/Resources/HeightmapWriter.cs`
- Create: `Terrain.Editor/Services/Resources/BiomeMaskWriter.cs`
- Create: `Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs`
- Create: `Terrain.Editor/Services/Resources/MaterialDescriptorWriter.cs`
- Create: `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 先写作者态写回规则失败用例**

创建 `Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs`：

```csharp
using Terrain.Editor.Services.Resources;
using Terrain.Resources;

namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorResourceWriterTests
{
    public static void RunAll()
    {
        TestHarness.Run("material descriptor writer keeps short relative texture paths", MaterialDescriptorWriterKeepsShortRelativeTexturePaths);
        TestHarness.Run("biome settings writer persists material_id references", BiomeSettingsWriterPersistsMaterialIdReferences);
        TestHarness.Run("heightmap writer saves directly to resolved target", HeightmapWriterSavesDirectlyToResolvedTarget);
    }

    private static void MaterialDescriptorWriterKeepsShortRelativeTexturePaths()
    {
        string root = CreateEditorWorkspace();
        var session = new EditorResourceSession(
            new ResolvedGameResource("map_data/materials/descriptor.toml", Path.Combine(root, "map_data", "materials", "descriptor.toml"), "base", true, false));
        var slots = new[]
        {
            new EditorMaterialDescriptorSlot("grassland", 0, "Grassland", "grass_a.png", "grass_n.png", "grass_p.png"),
        };

        new MaterialDescriptorWriter().Write(session, slots);

        string toml = File.ReadAllText(session.MaterialDescriptor.ResolvedPath);
        TestHarness.Assert(toml.Contains("albedo = \"grass_a.png\"", StringComparison.Ordinal), "albedo path should stay short and relative");
        TestHarness.Assert(!toml.Contains("map_data/materials", StringComparison.Ordinal), "writer should not expand to rooted or virtual-root path");
    }

    private static void BiomeSettingsWriterPersistsMaterialIdReferences()
    {
        string root = CreateEditorWorkspace();
        var session = new EditorResourceSession(
            materialDescriptor: new ResolvedGameResource("map_data/materials/descriptor.toml", Path.Combine(root, "map_data", "materials", "descriptor.toml"), "base", true, false),
            biomeSettings: new ResolvedGameResource("map_data/biome_settings.toml", Path.Combine(root, "map_data", "biome_settings.toml"), "base", true, false));

        var layers = new[]
        {
            new EditorBiomeLayerDefinition(1, 1, "Default Base", "grassland", 0, true, true),
        };

        new BiomeSettingsWriter().Write(session, new[] { new EditorBiomeDefinition(1, "Default Biome") }, layers, Array.Empty<EditorBiomeModifierDefinition>());

        string toml = File.ReadAllText(session.BiomeSettings.ResolvedPath);
        TestHarness.Assert(toml.Contains("material_id = \"grassland\"", StringComparison.Ordinal), "biome settings should persist material_id");
        TestHarness.Assert(!toml.Contains("material_slot", StringComparison.Ordinal), "old material_slot integer field must not reappear");
    }

    private static void HeightmapWriterSavesDirectlyToResolvedTarget()
    {
        string root = CreateEditorWorkspace();
        string output = Path.Combine(root, "map_data", "heightmap.png");
        var session = new EditorResourceSession(heightmap: new ResolvedGameResource("map_data/heightmap.png", output, "base", true, false));

        ushort[] data = { 0, ushort.MaxValue, ushort.MaxValue, 0 };
        new HeightmapWriter().Write(session, data, 2, 2);

        TestHarness.Assert(File.Exists(output), "heightmap writer should write directly to resolved target file");
    }

    private static string CreateEditorWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "terrain-editor-resource-writer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "map_data", "materials"));
        return root;
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
DescriptorReaderTests.RunAll();
TerrainRuntimeBootstrapTests.RunAll();
RuntimeMigrationTextTests.RunAll();
EditorResourceWriterTests.RunAll();
```

- [x] **Step 2: 运行测试，确认 Editor 写回服务尚未存在**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected: `BUILD FAILED`，并包含 `Terrain.Editor.Services.Resources`、`EditorResourceSession`、`MaterialDescriptorWriter` 缺失错误

- [x] **Step 3: 写 session 与作者态写回服务**

> **Final-state note:** 本步骤下面的首版写回片段只展示了单文件 writer。最终实现已额外抽出 `EditorResourceSaveService` 与 `AtomicResourceWriteTransaction`，`TerrainManager.SaveAuthoringResources()` 不再顺序直写目标文件，而是先写 staging 再统一提交，确保任一后续 writer 失败时整组作者态资源回滚。

创建 `Terrain.Editor/Services/Resources/EditorResourceSession.cs`：

```csharp
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorResourceSession
{
    public EditorResourceSession(
        ResolvedGameResource? heightmap = null,
        ResolvedGameResource? terrainData = null,
        ResolvedGameResource? biomeMask = null,
        ResolvedGameResource? biomeSettings = null,
        ResolvedGameResource? materialDescriptor = null,
        ResolvedGameResource? rivers = null)
    {
        Heightmap = heightmap;
        TerrainData = terrainData;
        BiomeMask = biomeMask;
        BiomeSettings = biomeSettings;
        MaterialDescriptor = materialDescriptor;
        Rivers = rivers;
    }

    public ResolvedGameResource? Heightmap { get; }
    public ResolvedGameResource? TerrainData { get; }
    public ResolvedGameResource? BiomeMask { get; }
    public ResolvedGameResource? BiomeSettings { get; }
    public ResolvedGameResource? MaterialDescriptor { get; }
    public ResolvedGameResource? Rivers { get; }
    public bool IsDirty { get; private set; }

    public void MarkDirty() => IsDirty = true;
    public void ClearDirty() => IsDirty = false;
}
```

创建 `Terrain.Editor/Services/Resources/HeightmapWriter.cs` 与 `BiomeMaskWriter.cs`：

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Terrain.Editor.Services.Resources;

public sealed class HeightmapWriter
{
    public void Write(EditorResourceSession session, ushort[] heightData, int width, int height)
    {
        string output = session.Heightmap?.ResolvedPath ?? throw new InvalidOperationException("Heightmap target is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        using var image = new Image<L16>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<L16> row = accessor.GetRowSpan(y);
                int offset = y * width;
                for (int x = 0; x < row.Length; x++)
                    row[x] = new L16(heightData[offset + x]);
            }
        });
        image.SaveAsPng(output);
    }
}
```

创建 `Terrain.Editor/Services/Resources/MaterialDescriptorWriter.cs`：

```csharp
using Tommy;

namespace Terrain.Editor.Services.Resources;

public readonly record struct EditorMaterialDescriptorSlot(
    string Id,
    int Index,
    string Name,
    string? Albedo,
    string? Normal,
    string? Properties);

public sealed class MaterialDescriptorWriter
{
    public void Write(EditorResourceSession session, IReadOnlyList<EditorMaterialDescriptorSlot> slots)
    {
        string output = session.MaterialDescriptor?.ResolvedPath ?? throw new InvalidOperationException("Material descriptor target is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var root = new TomlTable();
        root["version"] = 1;
        var materials = new TomlArray();
        foreach (EditorMaterialDescriptorSlot slot in slots)
        {
            var table = new TomlTable
            {
                ["id"] = slot.Id,
                ["index"] = slot.Index,
                ["name"] = slot.Name,
            };
            if (!string.IsNullOrWhiteSpace(slot.Albedo)) table["albedo"] = slot.Albedo;
            if (!string.IsNullOrWhiteSpace(slot.Normal)) table["normal"] = slot.Normal;
            if (!string.IsNullOrWhiteSpace(slot.Properties)) table["properties"] = slot.Properties;
            materials.Add(table);
        }
        root["materials"] = materials;

        using var writer = File.CreateText(output);
        root.WriteTo(writer);
    }
}
```

创建 `Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs`：

```csharp
using Tommy;

namespace Terrain.Editor.Services.Resources;

public readonly record struct EditorBiomeDefinition(int Id, string Name);
public readonly record struct EditorBiomeLayerDefinition(int Id, int BiomeId, string Name, string MaterialId, int Priority, bool Enabled, bool Visible);
public readonly record struct EditorBiomeModifierDefinition(int Id, int LayerId, string Type, string BlendMode, float Min, float Max, float MinFalloff, float MaxFalloff, float Opacity, bool Enabled, bool Visible);

public sealed class BiomeSettingsWriter
{
    public void Write(
        EditorResourceSession session,
        IReadOnlyList<EditorBiomeDefinition> biomes,
        IReadOnlyList<EditorBiomeLayerDefinition> layers,
        IReadOnlyList<EditorBiomeModifierDefinition> modifiers)
    {
        string output = session.BiomeSettings?.ResolvedPath ?? throw new InvalidOperationException("Biome settings target is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var root = new TomlTable();
        root["version"] = 1;

        var biomesArray = new TomlArray();
        foreach (EditorBiomeDefinition biome in biomes)
        {
            biomesArray.Add(new TomlTable
            {
                ["id"] = biome.Id,
                ["name"] = biome.Name,
            });
        }
        root["biomes"] = biomesArray;

        var layersArray = new TomlArray();
        foreach (EditorBiomeLayerDefinition layer in layers)
        {
            layersArray.Add(new TomlTable
            {
                ["id"] = layer.Id,
                ["biome_id"] = layer.BiomeId,
                ["name"] = layer.Name,
                ["material_id"] = layer.MaterialId,
                ["priority"] = layer.Priority,
                ["enabled"] = layer.Enabled,
                ["visible"] = layer.Visible,
            });
        }
        root["layers"] = layersArray;

        var modifiersArray = new TomlArray();
        foreach (EditorBiomeModifierDefinition modifier in modifiers)
        {
            modifiersArray.Add(new TomlTable
            {
                ["id"] = modifier.Id,
                ["layer_id"] = modifier.LayerId,
                ["type"] = modifier.Type,
                ["blend_mode"] = modifier.BlendMode,
                ["min"] = modifier.Min,
                ["max"] = modifier.Max,
                ["min_falloff"] = modifier.MinFalloff,
                ["max_falloff"] = modifier.MaxFalloff,
                ["opacity"] = modifier.Opacity,
                ["enabled"] = modifier.Enabled,
                ["visible"] = modifier.Visible,
            });
        }
        root["modifiers"] = modifiersArray;

        using var writer = File.CreateText(output);
        root.WriteTo(writer);
    }
}
```

本阶段不实现河流写回器；`rivers.png` 仅保持可选解析/读取，缺失时不生成。

- [x] **Step 4: 运行测试，确认写回器通过**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS material descriptor writer keeps short relative texture paths
PASS biome settings writer persists material_id references
PASS heightmap writer saves directly to resolved target
```

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain.Editor/Services/Resources/EditorResourceSession.cs Terrain.Editor/Services/Resources/HeightmapWriter.cs Terrain.Editor/Services/Resources/BiomeMaskWriter.cs Terrain.Editor/Services/Resources/BiomeSettingsWriter.cs Terrain.Editor/Services/Resources/MaterialDescriptorWriter.cs Terrain.Editor.Tests/VirtualResources/EditorResourceWriterTests.cs Terrain.Editor.Tests/Program.cs
git commit -m "feat: add editor authoring resource writers"
```

---

### Task 6: 接入 Editor 自动 bootstrap、删除旧工作流、固定 `.terrain` 导出目标

**Files:**
- Create: `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`
- Modify: `Terrain.Editor/Services/TerrainManager.cs`
- Modify: `Terrain.Editor/Services/MaterialSlotManager.cs`
- Modify: `Terrain.Editor/Services/BiomeRuleService.cs`
- Modify: `Terrain.Editor/Services/TextureThumbnailProvider.cs`
- Modify: `Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs`
- Delete: `Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs`
- Delete: `Terrain.Editor/Services/ProjectManager.cs`
- Delete: `Terrain.Editor/Services/TomlProjectConfig.cs`
- Modify: `Terrain.Editor/App.axaml.cs`
- Modify: `Terrain.Editor/ViewModels/EditorShellViewModel.cs`
- Modify: `Terrain.Editor/Views/MainWindow.axaml`
- Create: `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`
- Modify: `Terrain.Editor.Tests/Program.cs`

- [x] **Step 1: 先写 UI / 命令退场文本测试**

创建 `Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs`：

```csharp
namespace Terrain.Editor.Tests.VirtualResources;

internal static class EditorWorkflowTextTests
{
    public static void RunAll()
    {
        TestHarness.Run("main window no longer binds open or save-as commands", MainWindowNoLongerBindsOpenOrSaveAsCommands);
        TestHarness.Run("editor shell no longer uses project file pickers for save or export", EditorShellNoLongerUsesProjectFilePickersForSaveOrExport);
    }

    private static void MainWindowNoLongerBindsOpenOrSaveAsCommands()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Terrain.Editor", "Views", "MainWindow.axaml"));
        TestHarness.Assert(!source.Contains("OpenProjectCommand", StringComparison.Ordinal), "OpenProjectCommand binding should be removed");
        TestHarness.Assert(!source.Contains("SaveProjectAsCommand", StringComparison.Ordinal), "SaveProjectAsCommand binding should be removed");
    }

    private static void EditorShellNoLongerUsesProjectFilePickersForSaveOrExport()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Terrain.Editor", "ViewModels", "EditorShellViewModel.cs"));
        TestHarness.Assert(!source.Contains("Title = \"Open Terrain Project\"", StringComparison.Ordinal), "open project file picker should be removed");
        TestHarness.Assert(!source.Contains("Title = \"Save Project As\"", StringComparison.Ordinal), "save as file picker should be removed");
        TestHarness.Assert(!source.Contains("SuggestedFileName = \"terrain\"", StringComparison.Ordinal), "terrain export should stop prompting for arbitrary target file");
    }
}
```

把 `Terrain.Editor.Tests/Program.cs` 改成：

```csharp
using Terrain.Editor.Tests.VirtualResources;

LaunchSettingsResolverTests.RunAll();
DescriptorReaderTests.RunAll();
TerrainRuntimeBootstrapTests.RunAll();
RuntimeMigrationTextTests.RunAll();
EditorResourceWriterTests.RunAll();
EditorWorkflowTextTests.RunAll();
```

- [x] **Step 2: 运行测试，确认旧 UI 命令仍然存在**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
FAIL main window no longer binds open or save-as commands: OpenProjectCommand binding should be removed
FAIL editor shell no longer uses project file pickers for save or export: open project file picker should be removed
```

- [x] **Step 3: 用新 bootstrap 和新 session 接管 Editor**

> **Final-state note:** 本步骤下面的首版 `EditorBootstrapService` 片段已被后续收口调整覆盖。当前实现通过 `GameResourceRootLocator.FindFrom(AppContext.BaseDirectory)` 锁定工作区 `game/` 根目录；`heightmap` 仍为 required 作者态输入；`terrain.terrain` 与 `biome_mask.png` 使用 `ResolveWritableTarget(...)` 解析，因此 Editor 在两者缺失时仍可启动；`rivers.png` 继续保持 optional。

创建 `Terrain.Editor/Services/Resources/EditorBootstrapService.cs`：

```csharp
using Terrain.Resources;

namespace Terrain.Editor.Services.Resources;

public sealed class EditorBootstrapService
{
    public EditorResourceSession LoadCurrentSession()
    {
        string appRoot = AppContext.BaseDirectory;
        LaunchSettings launchSettings = LaunchSettingsService.Load(Path.Combine(appRoot, "LaunchSetting.json"));
        var layers = new List<GameResourceLayer> { new("base", appRoot, isBaseLayer: true) };
        layers.AddRange(launchSettings.Mods
            .Where(static mod => mod.Enabled)
            .Select(static mod => new GameResourceLayer(mod.Id, mod.Root, isBaseLayer: false)));

        var resolver = new GameResourceResolver(layers);
        RuntimeMapDefinition map = RuntimeMapDefinitionReader.ReadFrom(resolver.ResolveRequiredFile("map_data/default.toml").ResolvedPath);

        return new EditorResourceSession(
            heightmap: resolver.ResolveRequiredFile($"map_data/{map.HeightmapPath}"),
            terrainData: resolver.ResolveRequiredFile($"map_data/{map.TerrainDataPath}"),
            biomeMask: resolver.ResolveRequiredFile("map_data/biome_mask.png"),
            biomeSettings: resolver.ResolveRequiredFile("map_data/biome_settings.toml"),
            materialDescriptor: resolver.ResolveRequiredFile("map_data/materials/descriptor.toml"),
            rivers: string.IsNullOrWhiteSpace(map.RiversPath) ? null : resolver.ResolveRequiredFile($"map_data/{map.RiversPath}"));
    }
}
```

在 `Terrain.Editor/App.axaml.cs` 中把 DataContext 改成显式 bootstrap：

```csharp
desktop.MainWindow = new MainWindow
{
    DataContext = new EditorShellViewModel(new EditorBootstrapService()),
};
```

在 `Terrain.Editor/ViewModels/EditorShellViewModel.cs` 中：

```csharp
private readonly EditorBootstrapService _bootstrapService;
private readonly EditorResourceSession _resourceSession;

public EditorShellViewModel(EditorBootstrapService bootstrapService)
{
    _bootstrapService = bootstrapService;
    _resourceSession = _bootstrapService.LoadCurrentSession();
    _viewportHost = new NativeStrideViewportHost();
    Viewport = new NativeStrideViewportViewModel(_viewportHost);
    BrushParams = new BrushParametersViewModel();
    Biome = new BiomeViewModel();
    Settings = new SettingsViewModel();
    _terrainExporter.TerrainManager = _viewportHost.TerrainManager;

    if (_viewportHost.TerrainManager != null)
        _viewportHost.TerrainManager.LoadFromResourceSession(_resourceSession);
}
```

把 `OpenProject()`、`SaveProjectAs()` 整个删除，把 `SaveProject()` 改成：

```csharp
[RelayCommand]
private void SaveProject()
{
    if (!TryGetTerrainManager(out var terrainManager))
        return;

    terrainManager.SaveAuthoringResources(_resourceSession);
    AddConsole("Info", "Saved authoring resources.");
}
```

把 `ExportTerrain()` 改成固定目标，不再弹文件对话框：

```csharp
[RelayCommand]
private async Task ExportTerrain()
{
    if (!TryGetTerrainManager(out var terrainManager))
        return;
    if (!terrainManager.HasTerrainLoaded)
    {
        AddConsole("Warning", "No terrain loaded to export.");
        return;
    }

    string outputPath = _resourceSession.TerrainData?.ResolvedPath
        ?? throw new InvalidOperationException("Terrain export target is missing.");

    _terrainExporter.TerrainManager = terrainManager;
    await ExportManager.Instance.ExecuteAsync("Terrain", outputPath, new Progress<ExportProgress>(report =>
    {
        if (!string.IsNullOrWhiteSpace(report.Message))
            AddConsole(report.ErrorMessage == null ? "Info" : "Error", report.ErrorMessage ?? report.Message);
    }), CancellationToken.None);
    AddConsole("Info", $"Terrain exported to {outputPath}.");
}
```

在 `Terrain.Editor/Views/MainWindow.axaml` 中删除：

```xml
<KeyBinding Gesture="Ctrl+O" Command="{Binding OpenProjectCommand}" />
<KeyBinding Gesture="Ctrl+Shift+S" Command="{Binding SaveProjectAsCommand}" />
```

并删除所有绑定到 `OpenProjectCommand` / `SaveProjectAsCommand` 的按钮和菜单项，只保留：

```xml
<KeyBinding Gesture="Ctrl+S" Command="{Binding SaveProjectCommand}" />
```

同时：

- 删除 `Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs`
- 删除 `Terrain.Editor/Services/ProjectManager.cs`
- 删除 `Terrain.Editor/Services/TomlProjectConfig.cs`
- 把 `TerrainManager.LoadProject()` / `SaveProject()` / `SaveProjectAs()` 重写为 `LoadFromResourceSession()` / `SaveAuthoringResources()`
- 把 `TextureThumbnailProvider.ResolveTextureThumbnailPath()` 改为先尝试 `session.MaterialDescriptor` 所在目录，而不是 `ProjectManager.MaterialsPath`

- [x] **Step 4: 运行测试并编译 Editor**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected:

```text
PASS main window no longer binds open or save-as commands
PASS editor shell no longer uses project file pickers for save or export
```

Run: `dotnet build Terrain.sln`

Expected: `Build succeeded.`

- [x] **Step 5: 提交（按用户要求未执行提交）**

```bash
git add Terrain.Editor/App.axaml.cs Terrain.Editor/ViewModels/EditorShellViewModel.cs Terrain.Editor/Views/MainWindow.axaml Terrain.Editor/Services/Resources/EditorBootstrapService.cs Terrain.Editor/Services/TerrainManager.cs Terrain.Editor/Services/MaterialSlotManager.cs Terrain.Editor/Services/BiomeRuleService.cs Terrain.Editor/Services/TextureThumbnailProvider.cs Terrain.Editor/Services/Export/Exporters/TerrainExporter.cs Terrain.Editor.Tests/VirtualResources/EditorWorkflowTextTests.cs Terrain.Editor.Tests/Program.cs
git rm Terrain.Editor/Services/Export/Exporters/BiomeConfigExporter.cs Terrain.Editor/Services/ProjectManager.cs Terrain.Editor/Services/TomlProjectConfig.cs
git commit -m "refactor: switch editor to automatic virtual resource bootstrap"
```

---

### Task 7: 文档回写与全量验证

**Files:**
- Modify: `docs/ARCHITECTURE_OVERVIEW.md`
- Modify: `docs/CURRENT_FEATURES.md`
- Modify: `docs/log/2026/06/13/virtual-resource-system-design-finalization.md`

- [x] **Step 1: 更新架构与功能总览**

在 `docs/ARCHITECTURE_OVERVIEW.md` 中把 Editor 持久化相关描述替换为：

```md
| **资源解析系统** | ✅ 已实现 | Runtime / Editor 共用 `LaunchSetting.json` + 固定入口 `map_data/default.toml` |
| **项目持久化（旧 TOML）** | ❌ 已移除 | 被虚拟资源系统取代 |
```

在 `docs/CURRENT_FEATURES.md` 中把以下内容更新为：

```md
| 虚拟资源系统 | ✅ | `Terrain/Resources/`, `Terrain.Editor/Services/Resources/` | `docs/superpowers/specs/2026-06-13-editor-virtual-resource-system-design.md` |
| 旧项目 TOML 工作流 | ❌ 已移除 | - | 被固定入口和 resolver 取代 |
```

- [x] **Step 2: 运行全量验证（自动验证已完成；手工 Editor 冒烟未执行）**

Run: `dotnet run --project Terrain.Editor.Tests/Terrain.Editor.Tests.csproj`

Expected: 所有 `PASS`，退出码为 `0`

Run: `dotnet build Terrain.sln`

Expected: `Build succeeded.`

手工验证：

```text
1. 启动 Terrain.Editor，确认不再出现 Open/New/Load Workspace。
2. 确认启动后直接按 LaunchSetting.json 加载地形、biome mask、biome settings、materials descriptor。
3. 修改 heightmap 或 biome settings 后执行 Save，确认只写回当前命中的实体文件。
4. 执行 Export Terrain，确认输出固定落到当前命中的 map_data/terrain.terrain。
5. 将命中的目标文件改成只读，确认 Save / Export 直接失败且不 fallback。
```

- [x] **Step 3: 提交（按用户要求未执行提交）**

```bash
git add docs/ARCHITECTURE_OVERVIEW.md docs/CURRENT_FEATURES.md docs/log/2026/06/13/virtual-resource-system-design-finalization.md
git commit -m "docs: update architecture docs for virtual resource system"
```
