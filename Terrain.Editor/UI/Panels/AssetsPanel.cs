#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 资源类型
/// </summary>
public enum AssetType
{
    Folder,
    Texture,
    Material,
    Mesh,
    Scene,
    Script,
    Prefab,
    Audio,
    Animation,
    Unknown
}

/// <summary>
/// 资源条目
/// </summary>
public class AssetEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public AssetType Type { get; set; } = AssetType.Unknown;
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public bool IsSelected { get; set; }
    public object? Tag { get; set; }
}

/// <summary>
/// 资源视图面板 - 显示项目资源
/// </summary>
public class AssetsPanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 当前路径
    /// </summary>
    public string CurrentPath { get; set; } = "";

    /// <summary>
    /// 资源条目列表
    /// </summary>
    public List<AssetEntry> Assets { get; } = new();

    /// <summary>
    /// 选中的资源
    /// </summary>
    public List<AssetEntry> SelectedAssets { get; } = new();

    /// <summary>
    /// 视图模式
    /// </summary>
    public AssetViewMode ViewMode { get; set; } = AssetViewMode.List;

    /// <summary>
    /// 图标大小
    /// </summary>
    public float IconSize { get; set; } = 64.0f;

    /// <summary>
    /// 搜索过滤
    /// </summary>
    public string SearchFilter { get; set; } = "";

    #endregion

    #region 事件

    /// <summary>
    /// 资源选择改变事件
    /// </summary>
    public event EventHandler<AssetSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// 资源双击事件
    /// </summary>
    public event EventHandler<AssetEventArgs>? AssetDoubleClicked;

    /// <summary>
    /// 路径改变事件
    /// </summary>
    public event EventHandler<AssetPathChangedEventArgs>? PathChanged;

    #endregion

    #region 私有字段

    private string searchBuffer = "";
    private List<string> pathHistory = new();
    private int historyIndex = -1;

    #endregion

    #region 构造函数

    public AssetsPanel()
    {
        Title = "Assets";
        Icon = Icons.Folder;
        ShowTitleBar = true;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        // 渲染工具栏
        RenderToolbar();

        // 渲染面包屑导航
        RenderBreadcrumb();

        // 渲染资源列表
        RenderAssetList();
    }

    private void RenderToolbar()
    {
        float toolbarHeight = 28;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 toolbarPos = new Vector2(ContentRect.X, ContentRect.Y);
        Vector2 toolbarEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        // 导航按钮
        ImGui.SetCursorScreenPos(new Vector2(toolbarPos.X + 8, toolbarPos.Y + 4));

        if (ImGui.Button(Icons.ArrowLeft, new Vector2(24, 20)))
        {
            NavigateBack();
        }

        ImGui.SameLine();

        if (ImGui.Button(Icons.ArrowRight, new Vector2(24, 20)))
        {
            NavigateForward();
        }

        ImGui.SameLine();

        if (ImGui.Button(Icons.ArrowUp, new Vector2(24, 20)))
        {
            NavigateUp();
        }

        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();

        // 视图模式切换
        PushViewModeButtonStyle(ViewMode == AssetViewMode.List);
        if (ImGui.Button("List", new Vector2(40, 20)))
            ViewMode = AssetViewMode.List;
        PopViewModeButtonStyle();

        ImGui.SameLine();

        PushViewModeButtonStyle(ViewMode == AssetViewMode.Grid);
        if (ImGui.Button("Grid", new Vector2(40, 20)))
            ViewMode = AssetViewMode.Grid;
        PopViewModeButtonStyle();

        ImGui.SameLine();

        // 搜索框
        ImGui.SetCursorScreenPos(new Vector2(toolbarEnd.X - 150, toolbarPos.Y + 4));
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputTextWithHint($"##search_{Id}", "Search...", ref searchBuffer, 256))
        {
            SearchFilter = searchBuffer;
        }

        // 底部边框
        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void RenderBreadcrumb()
    {
        float toolbarHeight = 28;
        float breadcrumbHeight = 24;
        float y = ContentRect.Y + toolbarHeight;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 crumbPos = new Vector2(ContentRect.X, y);
        Vector2 crumbEnd = new Vector2(ContentRect.X + ContentRect.Width, y + breadcrumbHeight);
        drawList.AddRectFilled(crumbPos, crumbEnd, ColorPalette.Background.ToUint());

        // 渲染路径
        ImGui.SetCursorScreenPos(new Vector2(crumbPos.X + 8, crumbPos.Y + 4));

        var parts = CurrentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string currentPath = "";

        for (int i = 0; i < parts.Length; i++)
        {
            currentPath += "/" + parts[i];

            if (i > 0)
            {
                ImGui.TextDisabled("/");
                ImGui.SameLine();
            }

            if (ImGui.Button(parts[i]))
            {
                NavigateTo(currentPath);
            }

            if (i < parts.Length - 1)
            {
                ImGui.SameLine();
            }
        }

        // 底部边框
        drawList.AddLine(
            new Vector2(ContentRect.X, y + breadcrumbHeight),
            new Vector2(ContentRect.X + ContentRect.Width, y + breadcrumbHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void RenderAssetList()
    {
        float toolbarHeight = 28;
        float breadcrumbHeight = 24;
        float listY = ContentRect.Y + toolbarHeight + breadcrumbHeight;
        float listHeight = ContentRect.Height - toolbarHeight - breadcrumbHeight;

        Vector2 listPos = new Vector2(ContentRect.X, listY);

        // 开始滚动区域
        ImGui.SetCursorScreenPos(listPos);
        ImGui.BeginChild($"##asset_list_{Id}", new Vector2(ContentRect.Width, listHeight), ImGuiChildFlags.None);

        if (ViewMode == AssetViewMode.List)
        {
            RenderListView();
        }
        else
        {
            RenderGridView();
        }

        // 右键菜单（空白处）
        if (ImGui.BeginPopupContextWindow($"##context_empty_{Id}", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            RenderContextMenu(null);
            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }

    private void RenderListView()
    {
        // 表头
        float nameWidth = ImGui.GetWindowWidth() * 0.4f;
        float typeWidth = 100;
        float dateWidth = 120;
        float sizeWidth = 80;

        ImGui.Columns(4, $"##columns_{Id}", true);
        ImGui.SetColumnWidth(0, nameWidth);
        ImGui.SetColumnWidth(1, typeWidth);
        ImGui.SetColumnWidth(2, dateWidth);
        ImGui.SetColumnWidth(3, sizeWidth);

        ImGui.Text("Name");
        ImGui.NextColumn();
        ImGui.Text("Type");
        ImGui.NextColumn();
        ImGui.Text("Date Modified");
        ImGui.NextColumn();
        ImGui.Text("Size");
        ImGui.NextColumn();

        ImGui.Separator();

        // 资源条目
        foreach (var asset in Assets)
        {
            // 搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter) &&
                !asset.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            // 选中状态
            if (asset.IsSelected)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColorPalette.Accent.ToVector4());
            }

            // 名称（带图标）
            string icon = GetAssetIcon(asset.Type);
            if (ImGui.Selectable($"{icon} {asset.Name}", asset.IsSelected, ImGuiSelectableFlags.SpanAllColumns))
            {
                HandleAssetClick(asset);
            }

            if (asset.IsSelected)
            {
                ImGui.PopStyleColor();
            }

            // 双击
            if (ImGui.IsItemHovered() && ImGuiP.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                AssetDoubleClicked?.Invoke(this, new AssetEventArgs { Asset = asset });
            }

            // 右键菜单
            if (ImGui.BeginPopupContextItem($"##context_{asset.Id}"))
            {
                RenderContextMenu(asset);
                ImGui.EndPopup();
            }

            ImGui.NextColumn();

            // 类型
            ImGui.Text(asset.Type.ToString());
            ImGui.NextColumn();

            // 修改日期
            ImGui.Text(asset.LastModified.ToString("yyyy-MM-dd HH:mm"));
            ImGui.NextColumn();

            // 大小
            ImGui.Text(FormatFileSize(asset.Size));
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }

    private void RenderGridView()
    {
        float windowWidth = ImGui.GetWindowWidth();
        int columns = Math.Max(1, (int)(windowWidth / (IconSize + 16)));

        int index = 0;
        foreach (var asset in Assets)
        {
            // 搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter) &&
                !asset.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index > 0 && index % columns != 0)
            {
                ImGui.SameLine();
            }

            RenderGridItem(asset);

            index++;
        }
    }

    private void RenderGridItem(AssetEntry asset)
    {
        Vector2 itemSize = new Vector2(IconSize + 16, IconSize + 32);
        Vector2 cursorPos = ImGui.GetCursorScreenPos();

        // 背景
        uint bgColor = asset.IsSelected ? ColorPalette.Selection.ToUint() : 0;
        if (bgColor != 0)
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                cursorPos,
                new Vector2(cursorPos.X + itemSize.X, cursorPos.Y + itemSize.Y),
                bgColor,
                4
            );
        }

        // 开始组
        ImGui.BeginGroup();

        // 图标
        string icon = GetAssetIcon(asset.Type);
        var iconSize = ImGui.CalcTextSize(icon);
        ImGui.SetCursorPosX((itemSize.X - iconSize.X) * 0.5f);
        ImGui.Text(icon);

        // 名称
        var textSize = ImGui.CalcTextSize(asset.Name);
        float maxTextWidth = itemSize.X - 8;
        string displayName = textSize.X > maxTextWidth ?
            asset.Name[..Math.Min(asset.Name.Length, 10)] + "..." :
            asset.Name;

        ImGui.SetCursorPosX((itemSize.X - ImGui.CalcTextSize(displayName).X) * 0.5f);
        ImGui.Text(displayName);

        ImGui.EndGroup();

        // 检测点击
        if (ImGui.IsItemClicked())
        {
            HandleAssetClick(asset);
        }

        // 双击
        if (ImGui.IsItemHovered() && ImGuiP.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            AssetDoubleClicked?.Invoke(this, new AssetEventArgs { Asset = asset });
        }

        // 右键菜单
        if (ImGui.BeginPopupContextItem($"##context_{asset.Id}"))
        {
            RenderContextMenu(asset);
            ImGui.EndPopup();
        }

        // 设置下一个项目的位置
        ImGui.Dummy(new Vector2(0, 0));
    }

    private void RenderContextMenu(AssetEntry? asset)
    {
        if (asset == null)
        {
            if (ImGui.MenuItem("Create Folder"))
            {
                // TODO: 创建文件夹
            }

            if (ImGui.BeginMenu("Import"))
            {
                if (ImGui.MenuItem("Texture..."))
                {
                    // TODO: 导入纹理
                }
                if (ImGui.MenuItem("Model..."))
                {
                    // TODO: 导入模型
                }
                ImGui.EndMenu();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Show in Explorer"))
            {
                // TODO: 在资源管理器中打开
            }
        }
        else
        {
            ImGui.TextDisabled(asset.Name);
            ImGui.Separator();

            if (ImGui.MenuItem("Open"))
            {
                AssetDoubleClicked?.Invoke(this, new AssetEventArgs { Asset = asset });
            }

            if (ImGui.MenuItem("Rename"))
            {
                // TODO: 重命名
            }

            if (ImGui.MenuItem("Duplicate"))
            {
                // TODO: 复制
            }

            if (ImGui.MenuItem("Delete"))
            {
                // TODO: 删除
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Show in Explorer"))
            {
                // TODO: 在资源管理器中打开
            }

            if (ImGui.MenuItem("Copy Path"))
            {
                ImGui.SetClipboardText(asset.Path);
            }
        }
    }

    #endregion

    #region 导航

    private void NavigateBack()
    {
        if (historyIndex > 0)
        {
            historyIndex--;
            CurrentPath = pathHistory[historyIndex];
            PathChanged?.Invoke(this, new AssetPathChangedEventArgs { Path = CurrentPath });
        }
    }

    private void NavigateForward()
    {
        if (historyIndex < pathHistory.Count - 1)
        {
            historyIndex++;
            CurrentPath = pathHistory[historyIndex];
            PathChanged?.Invoke(this, new AssetPathChangedEventArgs { Path = CurrentPath });
        }
    }

    private void NavigateUp()
    {
        var parent = System.IO.Directory.GetParent(CurrentPath);
        if (parent != null)
        {
            NavigateTo(parent.FullName);
        }
    }

    private void NavigateTo(string path)
    {
        // 移除当前位置之后的历史记录
        if (historyIndex < pathHistory.Count - 1)
        {
            pathHistory.RemoveRange(historyIndex + 1, pathHistory.Count - historyIndex - 1);
        }

        pathHistory.Add(path);
        historyIndex = pathHistory.Count - 1;
        CurrentPath = path;

        PathChanged?.Invoke(this, new AssetPathChangedEventArgs { Path = CurrentPath });
    }

    #endregion

    #region 辅助方法

    private void HandleAssetClick(AssetEntry asset)
    {
        // 清除其他选择
        foreach (var a in Assets)
        {
            a.IsSelected = false;
        }

        asset.IsSelected = true;
        SelectedAssets.Clear();
        SelectedAssets.Add(asset);

        SelectionChanged?.Invoke(this, new AssetSelectionChangedEventArgs
        {
            SelectedAssets = new List<AssetEntry>(SelectedAssets)
        });
    }

    private string GetAssetIcon(AssetType type)
    {
        return type switch
        {
            AssetType.Folder => Icons.Folder,
            AssetType.Texture => Icons.FileImage,
            AssetType.Material => Icons.Paint,
            AssetType.Mesh => Icons.Cube,
            AssetType.Scene => Icons.File,
            AssetType.Script => Icons.File,
            AssetType.Prefab => Icons.Cube,
            AssetType.Audio => Icons.File,
            AssetType.Animation => Icons.File,
            _ => Icons.File
        };
    }

    private string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }

    private void PushViewModeButtonStyle(bool isActive)
    {
        if (isActive)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());
        }
    }

    private void PopViewModeButtonStyle()
    {
        ImGui.PopStyleColor(3);
    }

    #endregion
}

/// <summary>
/// 资源视图模式
/// </summary>
public enum AssetViewMode
{
    List,
    Grid
}

/// <summary>
/// 资源选择改变事件参数
/// </summary>
public class AssetSelectionChangedEventArgs : EventArgs
{
    public List<AssetEntry> SelectedAssets { get; set; } = new();
}

/// <summary>
/// 资源事件参数
/// </summary>
public class AssetEventArgs : EventArgs
{
    public AssetEntry Asset { get; set; } = null!;
}

/// <summary>
/// 资源路径改变事件参数
/// </summary>
public class AssetPathChangedEventArgs : EventArgs
{
    public string Path { get; set; } = "";
}
