# 纹理自动导入法线贴图 + Inspector 属性面板

## 需求概述

1. **自动导入法线贴图**：导入 Diffuse/Albedo 纹理时，自动查找并导入相似名称的法线贴图
2. **Inspector 属性面板**：点击纹理槽位时，在右侧面板显示纹理属性

## 功能设计

### 1. 自动法线贴图检测

#### 名称匹配规则

常见法线贴图命名模式（优先级从高到低）：

| Albedo 文件名 | 法线贴图候选名 |
|--------------|---------------|
| `texture_diffuse.png` | `texture_normal.png`, `texture_n.png`, `texture_normalmap.png` |
| `texture_albedo.png` | `texture_normal.png`, `texture_n.png` |
| `texture_basecolor.png` | `texture_normal.png`, `texture_n.png` |
| `texture.png` | `texture_normal.png`, `texture_n.png`, `Normal/texture.png` |

#### 实现策略

```csharp
// TextureImporter.cs 新增方法
public static string? FindMatchingNormalMap(string albedoPath)
{
    string directory = Path.GetDirectoryName(albedoPath)!;
    string nameWithoutExt = Path.GetFileNameWithoutExtension(albedoPath);
    string ext = Path.GetExtension(albedoPath);

    // 候选名称列表
    string[] normalSuffixes = { "_normal", "_Normal", "_n", "_N", "_normalmap", "_NormalMap" };
    string[] normalPrefixes = { "Normal_", "normal_" };

    // 1. 尝试后缀替换
    string[] diffuseSuffixes = { "_diffuse", "_Diffuse", "_albedo", "_Albedo",
                                  "_basecolor", "_BaseColor", "_color", "_Color", "_D", "_d" };

    foreach (var ds in diffuseSuffixes)
    {
        if (nameWithoutExt.EndsWith(ds, StringComparison.OrdinalIgnoreCase))
        {
            string baseName = nameWithoutExt[..^ds.Length];
            foreach (var ns in normalSuffixes)
            {
                string candidate = Path.Combine(directory, baseName + ns + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
    }

    // 2. 尝试添加后缀
    foreach (var ns in normalSuffixes)
    {
        string candidate = Path.Combine(directory, nameWithoutExt + ns + ext);
        if (File.Exists(candidate)) return candidate;
    }

    // 3. 尝试 Normal 子目录
    string normalSubdir = Path.Combine(directory, "Normal");
    if (Directory.Exists(normalSubdir))
    {
        string candidate = Path.Combine(normalSubdir, nameWithoutExt + ext);
        if (File.Exists(candidate)) return candidate;
    }

    return null;
}
```

### 2. Inspector 属性面板

#### UI 结构

在 `RightPanel` 中新增一个标签页 "Texture"（仅在 Paint 模式且选中纹理时显示）：

```
┌─────────────────────────────┐
│ [Params] [Brushes] [Texture]│  ← 新增 Texture 标签
├─────────────────────────────┤
│ Name: Grass_01              │
│ Index: 0                    │
├─────────────────────────────┤
│ Diffuse                     │
│ ┌─────────┐                 │
│ │ [预览]  │ Path: ...       │
│ │         │ Size: 512x512   │
│ └─────────┘                 │
├─────────────────────────────┤
│ Normal                      │
│ ┌─────────┐                 │
│ │ [预览]  │ Path: ...       │
│ │         │ Size: 512x512   │
│ └─────────┘                 │
│ [Import Normal]             │  ← 如果没有法线贴图
├─────────────────────────────┤
│ Tiling Scale: [1.0]         │
└─────────────────────────────┘
```

#### 实现文件

新建 `TextureInspectorPanel.cs`：

```csharp
namespace Terrain.Editor.UI.Panels;

internal class TextureInspectorPanel
{
    public int SelectedSlotIndex { get; set; } = -1;

    public event EventHandler<TextureImportEventArgs>? ImportNormalRequested;

    public void Render()
    {
        if (SelectedSlotIndex < 0) return;

        var slot = MaterialSlotManager.Instance[SelectedSlotIndex];
        if (slot.IsEmpty) return;

        // 渲染属性...
    }
}
```

#### RightPanel 修改

```csharp
public class RightPanel : PanelBase
{
    private readonly BrushParamsPanel brushParamsPanel;
    private readonly BrushesPanel brushesPanel;
    private readonly TextureInspectorPanel textureInspectorPanel;  // 新增

    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;
    public int SelectedTextureSlot { get; set; } = -1;

    protected override void RenderContent()
    {
        // Paint 模式下显示三个标签，其他模式只显示两个
        // ...
    }
}
```

## 实现计划

### Phase 1: 自动法线贴图检测

1. 在 `TextureImporter.cs` 添加 `FindMatchingNormalMap` 方法
2. 修改 `MainWindow.OnTextureImportRequested`：
   - 导入 Albedo 后自动查找法线贴图
   - 找到则自动导入并设置到同槽位

### Phase 2: Inspector 属性面板

1. 创建 `TextureInspectorPanel.cs`
2. 修改 `RightPanel.cs`：
   - 添加 TextureInspectorPanel 实例
   - Paint 模式下显示 Texture 标签
3. 连接事件：
   - AssetsPanel.TextureSlotSelected → 更新 RightPanel.SelectedTextureSlot
   - TextureInspectorPanel.ImportNormalRequested → 触发导入流程

## 文件清单

| 文件 | 操作 |
|------|------|
| `Services/TextureImporter.cs` | 添加法线贴图检测方法 |
| `UI/MainWindow.cs` | 修改导入逻辑，添加自动检测 |
| `UI/Panels/TextureInspectorPanel.cs` | **新建** |
| `UI/Panels/RightPanel.cs` | 集成 TextureInspectorPanel |
| `UI/Panels/AssetsPanel.cs` | 可能需要调整事件 |

## 关键细节

### 纹理预览渲染

使用现有的 ImGui DrawList 方式：

```csharp
if (slot.AlbedoTexture != null)
{
    var texRef = ImGuiExtension.GetTextureKey(slot.AlbedoTexture);
    drawList.AddImage(texRef, minPos, maxPos);
}
```

### 文件信息显示

```csharp
var fileInfo = new FileInfo(path);
string sizeInfo = $"{texture.Width}x{texture.Height}";
string fileSize = FormatFileSize(fileInfo.Length);  // KB/MB
```
