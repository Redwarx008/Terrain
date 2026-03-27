#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 按钮样式
/// </summary>
public enum ButtonStyle
{
    Default,    // 默认样式
    Primary,    // 主要按钮（强调色）
    Ghost,      // 幽灵按钮（透明背景）
    Danger,     // 危险按钮（红色）
    Tool        // 工具栏按钮（紧凑、图标为主）
}

/// <summary>
/// 按钮控件
/// </summary>
public class Button : ControlBase
{
    #region 属性

    /// <summary>
    /// 按钮文本
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 图标（Unicode字符）
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 按钮样式
    /// </summary>
    public ButtonStyle Style { get; set; } = ButtonStyle.Default;

    /// <summary>
    /// 是否紧凑模式
    /// </summary>
    public bool IsCompact { get; set; } = false;

    /// <summary>
    /// 是否占满可用宽度
    /// </summary>
    public bool IsFullWidth { get; set; } = false;

    /// <summary>
    /// 按钮尺寸（覆盖默认尺寸）
    /// </summary>
    public Vector2? ButtonSize { get; set; }

    /// <summary>
    /// 是否已按下
    /// </summary>
    public bool IsPressed { get; private set; }

    #endregion

    #region 事件

    /// <summary>
    /// 点击事件
    /// </summary>
    public new event EventHandler? Click;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        // 计算按钮尺寸
        Vector2 size = ButtonSize ?? CalculateSize();

        // 准备按钮文本
        string label = GetDisplayText();

        // 推入自定义样式
        PushButtonStyle();

        bool clicked;
        if (IsFullWidth)
        {
            clicked = ImGui.Button(label, new Vector2(-1, size.Y));
        }
        else
        {
            clicked = ImGui.Button(label, size);
        }

        // 弹出样式
        PopButtonStyle();

        // 更新状态
        IsPressed = ImGui.IsItemActive();
        State = ImGui.IsItemHovered() ? ControlState.Hovered :
                IsPressed ? ControlState.Pressed : ControlState.Normal;

        // 显示工具提示
        ShowTooltip();

        // 触发点击事件
        if (clicked)
        {
            RaiseClick(ImGui.GetIO().MousePos);
            Click?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        if (ButtonSize.HasValue)
            return ButtonSize.Value;

        return CalculateSize();
    }

    private Vector2 CalculateSize()
    {
        var io = ImGui.GetIO();
        float height = IsCompact ? 22 : EditorStyle.ButtonHeight;

        if (string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(Icon))
        {
            // 纯图标按钮
            return new Vector2(height, height);
        }

        // 计算文本宽度
        var textSize = ImGui.CalcTextSize(GetDisplayText());
        float width = textSize.X + (IsCompact ? 12 : 24);

        // 考虑图标
        if (!string.IsNullOrEmpty(Icon))
        {
            width += EditorStyle.IconSize + 4;
        }

        return new Vector2(width, height);
    }

    private string GetDisplayText()
    {
        if (!string.IsNullOrEmpty(Icon) && !string.IsNullOrEmpty(Text))
        {
            return $"{Icon} {Text}";
        }
        if (!string.IsNullOrEmpty(Icon))
        {
            return Icon;
        }
        return Text;
    }

    #endregion

    #region 样式

    private void PushButtonStyle()
    {
        var colors = ImGui.GetStyle().Colors;

        switch (Style)
        {
            case ButtonStyle.Primary:
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.Text, ColorPalette.TextHighlight.ToVector4());
                break;

            case ButtonStyle.Ghost:
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());
                break;

            case ButtonStyle.Danger:
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Error.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(
                    ColorPalette.Error.R * 1.2f,
                    ColorPalette.Error.G * 1.2f,
                    ColorPalette.Error.B * 1.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(
                    ColorPalette.Error.R * 0.8f,
                    ColorPalette.Error.G * 0.8f,
                    ColorPalette.Error.B * 0.8f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.Text, ColorPalette.TextHighlight.ToVector4());
                break;

            case ButtonStyle.Tool:
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());
                break;
        }

        if (IsCompact && Style != ButtonStyle.Tool)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 2));
        }
    }

    private void PopButtonStyle()
    {
        switch (Style)
        {
            case ButtonStyle.Primary:
            case ButtonStyle.Danger:
                ImGui.PopStyleColor(4);
                break;

            case ButtonStyle.Ghost:
                ImGui.PopStyleColor(3);
                break;

            case ButtonStyle.Tool:
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
                break;
        }

        if (IsCompact && Style != ButtonStyle.Tool)
        {
            ImGui.PopStyleVar();
        }
    }

    #endregion
}
