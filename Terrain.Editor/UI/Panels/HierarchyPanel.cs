#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 层级视图面板 - 显示场景对象树
/// </summary>
public class HierarchyPanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 场景对象树根节点
    /// </summary>
    public List<HierarchyNode> Nodes { get; } = new();

    /// <summary>
    /// 选中的节点
    /// </summary>
    public List<HierarchyNode> SelectedNodes { get; } = new();

    /// <summary>
    /// 是否允许多选
    /// </summary>
    public bool AllowMultiSelect { get; set; } = false;

    /// <summary>
    /// 搜索过滤文本
    /// </summary>
    public string SearchFilter { get; set; } = "";

    #endregion

    #region 事件

    /// <summary>
    /// 节点选择改变事件
    /// </summary>
    public event EventHandler<HierarchySelectionChangedEventArgs>? SelectionChanged;

    /// <summary>
    /// 节点双击事件
    /// </summary>
    public event EventHandler<HierarchyNodeEventArgs>? NodeDoubleClicked;

    /// <summary>
    /// 节点右键菜单事件
    /// </summary>
    public event EventHandler<HierarchyNodeEventArgs>? NodeContextMenu;

    #endregion

    #region 私有字段

    private string searchBuffer = "";
    private HierarchyNode? contextMenuNode;

    #endregion

    #region 构造函数

    public HierarchyPanel()
    {
        Title = "Hierarchy";
        Icon = Icons.Folder;
        ShowTitleBar = true;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        // 渲染搜索框
        RenderSearchBox();

        // 渲染树形视图
        RenderTreeView();
    }

    private void RenderSearchBox()
    {
        float searchHeight = 28;

        // 搜索框背景
        var drawList = ImGui.GetWindowDrawList();
        Vector2 searchPos = new Vector2(ContentRect.X, ContentRect.Y);
        Vector2 searchEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + searchHeight);
        drawList.AddRectFilled(searchPos, searchEnd, ColorPalette.DarkBackground.ToUint());

        // 搜索图标
        var iconPos = new Vector2(searchPos.X + 8, searchPos.Y + (searchHeight - ImGui.CalcTextSize(Icons.Search).Y) * 0.5f);
        drawList.AddText(iconPos, ColorPalette.TextSecondary.ToUint(), Icons.Search);

        // 搜索输入框
        ImGui.SetCursorScreenPos(new Vector2(searchPos.X + 28, searchPos.Y + 4));
        ImGui.PushItemWidth(ContentRect.Width - 36);

        if (ImGui.InputTextWithHint($"##search_{Id}", "Search...", ref searchBuffer, 256))
        {
            SearchFilter = searchBuffer;
        }

        ImGui.PopItemWidth();

        // 底部边框
        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + searchHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + searchHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void RenderTreeView()
    {
        float searchHeight = 28;
        float treeY = ContentRect.Y + searchHeight;
        float treeHeight = ContentRect.Height - searchHeight;

        // 开始滚动区域
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, treeY));
        ImGui.BeginChild($"##tree_{Id}", new Vector2(ContentRect.Width, treeHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        // 渲染所有根节点
        foreach (var node in Nodes)
        {
            RenderNode(node, 0);
        }

        // 右键菜单（在空白处）
        if (ImGui.BeginPopupContextWindow($"##context_empty_{Id}", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            RenderContextMenu(null);
            ImGui.EndPopup();
        }

        ImGui.EndChild();
    }

    private void RenderNode(HierarchyNode node, int depth)
    {
        // 过滤检查
        if (!string.IsNullOrEmpty(SearchFilter) && !NodeMatchesSearch(node, SearchFilter))
        {
            return;
        }

        // 计算缩进
        float indent = depth * 20 + 4;

        // 节点ID
        string nodeId = $"{Id}_{node.Id}";

        // 树节点标志
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;

        // 如果没有子节点，添加叶节点标志
        if (node.Children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        // 选中状态
        if (SelectedNodes.Contains(node))
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        // 设置缩进
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);

        // 渲染节点
        bool isOpen = false;

        // 推入样式
        if (SelectedNodes.Contains(node))
        {
            ImGui.PushStyleColor(ImGuiCol.Header, ColorPalette.Selection.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ColorPalette.Selection.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColorPalette.Pressed.ToVector4());
        }

        // 构建显示文本（带图标）
        string displayText = $"{node.Icon ?? Icons.Cube} {node.Name}";

        isOpen = ImGui.TreeNodeEx(nodeId, flags, displayText);

        if (SelectedNodes.Contains(node))
        {
            ImGui.PopStyleColor(3);
        }

        // 处理点击
        if (ImGui.IsItemClicked())
        {
            HandleNodeClick(node);
        }

        // 处理双击
        if (ImGui.IsItemHovered() && ImGuiP.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            NodeDoubleClicked?.Invoke(this, new HierarchyNodeEventArgs { Node = node });
        }

        // 右键菜单
        if (ImGui.BeginPopupContextItem($"##context_{nodeId}", ImGuiPopupFlags.MouseButtonRight))
        {
            contextMenuNode = node;
            RenderContextMenu(node);
            ImGui.EndPopup();
        }

        // 渲染子节点
        if (isOpen && node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                RenderNode(child, depth + 1);
            }

            ImGui.TreePop();
        }
    }

    private void RenderContextMenu(HierarchyNode? node)
    {
        if (node == null)
        {
            ImGui.TextDisabled("Scene");
            ImGui.Separator();

            if (ImGui.MenuItem("Create Empty"))
            {
                // TODO: 创建空对象
            }

            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Terrain"))
                {
                    // TODO: 创建地形
                }
                if (ImGui.MenuItem("Camera"))
                {
                    // TODO: 创建相机
                }
                if (ImGui.MenuItem("Light"))
                {
                    // TODO: 创建光源
                }
                ImGui.EndMenu();
            }
        }
        else
        {
            ImGui.TextDisabled(node.Name);
            ImGui.Separator();

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

            if (ImGui.MenuItem(node.IsVisible ? "Hide" : "Show"))
            {
                node.IsVisible = !node.IsVisible;
            }

            if (ImGui.MenuItem(node.IsLocked ? "Unlock" : "Lock"))
            {
                node.IsLocked = !node.IsLocked;
            }
        }
    }

    #endregion

    #region 输入处理

    private void HandleNodeClick(HierarchyNode node)
    {
        var io = ImGui.GetIO();

        if (AllowMultiSelect && (io.KeyCtrl || io.KeyShift))
        {
            // 多选模式
            if (io.KeyCtrl)
            {
                // Ctrl+点击：切换选择
                if (SelectedNodes.Contains(node))
                {
                    SelectedNodes.Remove(node);
                }
                else
                {
                    SelectedNodes.Add(node);
                }
            }
            else if (io.KeyShift && SelectedNodes.Count > 0)
            {
                // Shift+点击：范围选择
                // TODO: 实现范围选择
            }
        }
        else
        {
            // 单选模式
            SelectedNodes.Clear();
            SelectedNodes.Add(node);
        }

        SelectionChanged?.Invoke(this, new HierarchySelectionChangedEventArgs
        {
            SelectedNodes = new List<HierarchyNode>(SelectedNodes)
        });
    }

    #endregion

    #region 辅助方法

    private bool NodeMatchesSearch(HierarchyNode node, string filter)
    {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;

        // 递归检查子节点
        foreach (var child in node.Children)
        {
            if (NodeMatchesSearch(child, filter))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 选择指定节点
    /// </summary>
    public void SelectNode(HierarchyNode node)
    {
        SelectedNodes.Clear();
        SelectedNodes.Add(node);

        SelectionChanged?.Invoke(this, new HierarchySelectionChangedEventArgs
        {
            SelectedNodes = new List<HierarchyNode>(SelectedNodes)
        });
    }

    /// <summary>
    /// 清除选择
    /// </summary>
    public void ClearSelection()
    {
        SelectedNodes.Clear();

        SelectionChanged?.Invoke(this, new HierarchySelectionChangedEventArgs
        {
            SelectedNodes = new List<HierarchyNode>()
        });
    }

    /// <summary>
    /// 添加节点
    /// </summary>
    public void AddNode(HierarchyNode node, HierarchyNode? parent = null)
    {
        if (parent == null)
        {
            Nodes.Add(node);
        }
        else
        {
            parent.Children.Add(node);
            node.Parent = parent;
        }
    }

    /// <summary>
    /// 移除节点
    /// </summary>
    public void RemoveNode(HierarchyNode node)
    {
        if (node.Parent != null)
        {
            node.Parent.Children.Remove(node);
        }
        else
        {
            Nodes.Remove(node);
        }

        SelectedNodes.Remove(node);
    }

    #endregion
}

/// <summary>
/// 层级节点
/// </summary>
public class HierarchyNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New Object";
    public string? Icon { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    public bool IsExpanded { get; set; } = true;
    public HierarchyNode? Parent { get; set; }
    public List<HierarchyNode> Children { get; } = new();
    public object? Tag { get; set; }
}

/// <summary>
/// 选择改变事件参数
/// </summary>
public class HierarchySelectionChangedEventArgs : EventArgs
{
    public List<HierarchyNode> SelectedNodes { get; set; } = new();
}

/// <summary>
/// 节点事件参数
/// </summary>
public class HierarchyNodeEventArgs : EventArgs
{
    public HierarchyNode Node { get; set; } = null!;
}
