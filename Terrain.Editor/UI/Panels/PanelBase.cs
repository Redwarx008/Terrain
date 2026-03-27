#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 面板基类
/// </summary>
public abstract class PanelBase : ControlBase
{
    #region 属性

    /// <summary>
    /// 面板标题
    /// </summary>
    public string Title { get; set; } = "Panel";

    /// <summary>
    /// 是否显示标题栏
    /// </summary>
    public bool ShowTitleBar { get; set; } = true;

    /// <summary>
    /// 是否可折叠
    /// </summary>
    public bool IsCollapsible { get; set; } = false;

    /// <summary>
    /// 是否已折叠
    /// </summary>
    public bool IsCollapsed { get; set; } = false;

    /// <summary>
    /// 是否可关闭
    /// </summary>
    public bool IsClosable { get; set; } = false;

    /// <summary>
    /// 是否已关闭
    /// </summary>
    public bool IsClosed { get; set; } = false;

    /// <summary>
    /// 标题栏高度
    /// </summary>
    public float TitleBarHeight { get; set; } = 26.0f;

    /// <summary>
    /// 图标
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 是否自动滚动
    /// </summary>
    public bool AutoScroll { get; set; } = false;

    /// <summary>
    /// 内容区域
    /// </summary>
    public Rect ContentRect { get; protected set; }

    #endregion

    #region 事件

    /// <summary>
    /// 面板关闭事件
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// 面板折叠状态改变事件
    /// </summary>
    public event EventHandler<bool>? CollapseChanged;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        if (IsClosed || !IsVisible)
            return;

        // 绘制面板背景
        RenderBackground();

        // 绘制标题栏
        if (ShowTitleBar)
        {
            RenderTitleBar();
        }

        // 绘制内容区域
        if (!IsCollapsed)
        {
            RenderContent();
        }
    }

    /// <summary>
    /// 绘制背景
    /// </summary>
    protected virtual void RenderBackground()
    {
        var drawList = ImGui.GetWindowDrawList();

        // 绘制面板背景
        drawList.AddRectFilled(
            Position,
            new Vector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.PanelBackground.ToUint()
        );

        // 绘制边框
        drawList.AddRect(
            Position,
            new Vector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.Border.ToUint(),
            0,
            ImDrawFlags.None,
            1.0f
        );
    }

    /// <summary>
    /// 绘制标题栏
    /// </summary>
    protected virtual void RenderTitleBar()
    {
        var drawList = ImGui.GetWindowDrawList();

        // 标题栏背景
        Vector2 titleBarEnd = new Vector2(Position.X + Size.X, Position.Y + TitleBarHeight);
        drawList.AddRectFilled(
            Position,
            titleBarEnd,
            ColorPalette.TitleBar.ToUint()
        );

        // 标题栏底部边框
        drawList.AddLine(
            new Vector2(Position.X, Position.Y + TitleBarHeight),
            new Vector2(Position.X + Size.X, Position.Y + TitleBarHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );

        // 图标
        float textX = Position.X + 8;
        if (!string.IsNullOrEmpty(Icon))
        {
            Vector2 iconSize = GetIconTextSize(Icon);
            var iconPos = new Vector2(textX, Position.Y + (TitleBarHeight - iconSize.Y) * 0.5f);
            DrawIconText(drawList, iconPos, ColorPalette.TextSecondary.ToUint(), Icon);
            textX += iconSize.X + 6;
        }

        // 标题文本
        var titlePos = new Vector2(textX, Position.Y + (TitleBarHeight - ImGui.CalcTextSize(Title).Y) * 0.5f);
        drawList.AddText(titlePos, ColorPalette.TextPrimary.ToUint(), Title);

        // 折叠按钮
        if (IsCollapsible)
        {
            float buttonSize = 16;
            float buttonX = Position.X + Size.X - buttonSize - 8;
            float buttonY = Position.Y + (TitleBarHeight - buttonSize) * 0.5f;

            string collapseIcon = IsCollapsed ? Icons.ChevronDown : Icons.ChevronUp;
            var iconSize = GetIconTextSize(collapseIcon);
            var iconPos = new Vector2(
                buttonX + (buttonSize - iconSize.X) * 0.5f,
                buttonY + (buttonSize - iconSize.Y) * 0.5f
            );

            // 检测点击
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (ImGui.InvisibleButton($"##collapse_{Id}", new Vector2(buttonSize, buttonSize)))
            {
                IsCollapsed = !IsCollapsed;
                CollapseChanged?.Invoke(this, IsCollapsed);
            }

            // 绘制图标
            uint iconColor = ImGui.IsItemHovered() ? ColorPalette.Accent.ToUint() : ColorPalette.TextSecondary.ToUint();
            DrawIconText(drawList, iconPos, iconColor, collapseIcon);
        }

        // 关闭按钮
        if (IsClosable)
        {
            float buttonSize = 16;
            float offset = IsCollapsible ? 24 : 8;
            float buttonX = Position.X + Size.X - buttonSize - offset;
            float buttonY = Position.Y + (TitleBarHeight - buttonSize) * 0.5f;

            var iconSize = GetIconTextSize(Icons.Times);
            var iconPos = new Vector2(
                buttonX + (buttonSize - iconSize.X) * 0.5f,
                buttonY + (buttonSize - iconSize.Y) * 0.5f
            );

            // 检测点击
            ImGui.SetCursorScreenPos(new Vector2(buttonX, buttonY));
            if (ImGui.InvisibleButton($"##close_{Id}", new Vector2(buttonSize, buttonSize)))
            {
                IsClosed = true;
                Closed?.Invoke(this, EventArgs.Empty);
            }

            // 绘制图标
            uint iconColor = ImGui.IsItemHovered() ? ColorPalette.Error.ToUint() : ColorPalette.TextSecondary.ToUint();
            DrawIconText(drawList, iconPos, iconColor, Icons.Times);
        }
    }

    /// <summary>
    /// 绘制内容（子类重写）
    /// </summary>
    protected abstract void RenderContent();

    #endregion

    #region 布局

    protected override void OnArrange(Vector2 position, Vector2 size)
    {
        base.OnArrange(position, size);

        // 计算内容区域
        float contentY = position.Y;
        float contentHeight = size.Y;

        if (ShowTitleBar)
        {
            contentY += TitleBarHeight;
            contentHeight -= TitleBarHeight;
        }

        ContentRect = new Rect(
            position.X + Padding.Left,
            contentY + Padding.Top,
            size.X - Padding.Horizontal,
            contentHeight - Padding.Vertical
        );
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 开始内容区域裁剪
    /// </summary>
    protected void BeginContentClip()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));
        ImGuiChildFlags childFlags = ImGuiChildFlags.None;
        ImGuiWindowFlags windowFlags = AutoScroll ? ImGuiWindowFlags.AlwaysVerticalScrollbar : ImGuiWindowFlags.None;
        ImGui.BeginChild($"##content_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), childFlags, windowFlags);
    }

    /// <summary>
    /// 结束内容区域裁剪
    /// </summary>
    protected void EndContentClip()
    {
        if (AutoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }

    private static Vector2 GetIconTextSize(string icon)
    {
        // Measure with the icon font so title-bar buttons stay centered even when
        // the text font and icon font have different metrics.
        FontManager.PushIcons();
        Vector2 size = ImGui.CalcTextSize(icon);
        FontManager.PopFont();
        return size;
    }

    private static void DrawIconText(ImDrawListPtr drawList, Vector2 position, uint color, string icon)
    {
        // Draw directly with the icon face because draw-list text bypasses the
        // current ImGui font stack.
        ImGui.AddText(drawList, FontManager.Icons, FontManager.IconSize, position, color, icon);
    }

    #endregion
}
