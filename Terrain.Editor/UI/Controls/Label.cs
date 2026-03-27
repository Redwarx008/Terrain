#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 文本对齐方式
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>
/// 标签控件
/// </summary>
public class Label : ControlBase
{
    #region 属性

    /// <summary>
    /// 显示文本
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 文本对齐方式
    /// </summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>
    /// 是否自动换行
    /// </summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>
    /// 是否使用粗体
    /// </summary>
    public bool IsBold { get; set; } = false;

    /// <summary>
    /// 是否禁用
    /// </summary>
    public bool IsDisabled { get; set; } = false;

    /// <summary>
    /// 是否截断溢出文本
    /// </summary>
    public bool Truncate { get; set; } = false;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        if (string.IsNullOrEmpty(Text))
            return;

        // 推入字体样式
        if (IsBold)
        {
            FontManager.PushBold();
        }

        // 设置文本颜色
        var textColor = GetTextColor();
        ImGui.PushStyleColor(ImGuiCol.Text, textColor);

        // 设置位置
        Vector2 position = CalculateTextPosition();
        ImGui.SetCursorScreenPos(position);

        // 渲染文本
        if (WordWrap)
        {
            ImGui.PushTextWrapPos(Position.X + Size.X);
            ImGui.TextWrapped(Text);
            ImGui.PopTextWrapPos();
        }
        else if (Truncate)
        {
            var textSize = ImGui.CalcTextSize(Text);
            if (textSize.X > Size.X)
            {
                string truncated = TruncateText(Text, Size.X);
                ImGui.Text(truncated);
            }
            else
            {
                ImGui.Text(Text);
            }
        }
        else
        {
            ImGui.Text(Text);
        }

        // 弹出样式
        ImGui.PopStyleColor();

        if (IsBold)
        {
            FontManager.PopFont();
        }

        // 显示工具提示（如果文本被截断或需要提示）
        if (ImGui.IsItemHovered() && (Truncate || !string.IsNullOrEmpty(Tooltip)))
        {
            ImGui.SetTooltip(string.IsNullOrEmpty(Tooltip) ? Text : Tooltip);
        }
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        if (string.IsNullOrEmpty(Text))
            return Vector2.Zero;

        var textSize = ImGui.CalcTextSize(Text);

        if (WordWrap)
        {
            // 如果允许换行，高度可能增加
            textSize.Y = ImGui.CalcTextSize(Text, false, availableSize.X).Y;
        }

        return new Vector2(
            Math.Min(textSize.X, availableSize.X),
            textSize.Y
        );
    }

    private Vector2 CalculateTextPosition()
    {
        var textSize = ImGui.CalcTextSize(Text);
        var contentRect = GetContentRect();

        float x = contentRect.X;
        float y = contentRect.Y;

        switch (Alignment)
        {
            case TextAlignment.Center:
                x = contentRect.X + (contentRect.Width - textSize.X) * 0.5f;
                break;
            case TextAlignment.Right:
                x = contentRect.X + contentRect.Width - textSize.X;
                break;
        }

        // 垂直居中
        y = contentRect.Y + (contentRect.Height - textSize.Y) * 0.5f;

        return new Vector2(x, y);
    }

    private string TruncateText(string text, float maxWidth)
    {
        string ellipsis = "...";
        float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;

        if (maxWidth <= ellipsisWidth)
            return ellipsis;

        int length = text.Length;
        while (length > 0)
        {
            string truncated = text[..length] + ellipsis;
            if (ImGui.CalcTextSize(truncated).X <= maxWidth)
                return truncated;
            length--;
        }

        return ellipsis;
    }

    #endregion

    #region 辅助方法

    private Vector4 GetTextColor()
    {
        if (IsDisabled)
            return ColorPalette.TextDisabled.ToVector4();

        if (ForegroundColor.HasValue)
            return ForegroundColor.Value;

        return ColorPalette.TextPrimary.ToVector4();
    }

    #endregion
}
