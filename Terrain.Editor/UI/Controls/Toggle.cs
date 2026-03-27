#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 开关样式
/// </summary>
public enum ToggleStyle
{
    Switch,     // 滑动开关
    CheckBox    // 复选框样式
}

/// <summary>
/// 开关控件（类似Unity的Toggle）
/// </summary>
public class Toggle : ControlBase
{
    #region 属性

    /// <summary>
    /// 标签文本
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 是否开启
    /// </summary>
    public bool IsOn { get; set; } = false;

    /// <summary>
    /// 开关样式
    /// </summary>
    public ToggleStyle Style { get; set; } = ToggleStyle.Switch;

    /// <summary>
    /// 开关宽度
    /// </summary>
    public float SwitchWidth { get; set; } = 40.0f;

    /// <summary>
    /// 开关高度
    /// </summary>
    public float SwitchHeight { get; set; } = 20.0f;

    /// <summary>
    /// 是否显示标签
    /// </summary>
    public bool ShowLabel { get; set; } = true;

    #endregion

    #region 事件

    /// <summary>
    /// 状态改变事件
    /// </summary>
    public event EventHandler<ToggleChangedEventArgs>? Toggled;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        if (Style == ToggleStyle.CheckBox)
        {
            RenderAsCheckBox();
        }
        else
        {
            RenderAsSwitch();
        }
    }

    private void RenderAsCheckBox()
    {
        ImGui.SetCursorScreenPos(Position);

        bool value = IsOn;
        string label = ShowLabel ? Label : $"##{Id}";

        if (ImGui.Checkbox(label, ref value))
        {
            IsOn = value;
            RaiseToggled();
        }

        State = ImGui.IsItemHovered() ? ControlState.Hovered :
                ImGui.IsItemActive() ? ControlState.Pressed :
                ControlState.Normal;

        ShowTooltip();
    }

    private void RenderAsSwitch()
    {
        var drawList = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();

        Vector2 switchPos = Position;
        float width = SwitchWidth;
        float height = SwitchHeight;
        float radius = height * 0.5f;

        // 绘制开关背景
        uint bgColor = IsOn ? ColorPalette.Accent.ToUint() : ColorPalette.InputBackground.ToUint();
        if (ImGui.IsItemHovered())
        {
            bgColor = IsOn ?
                new Vector4(ColorPalette.Accent.R * 1.1f, ColorPalette.Accent.G * 1.1f, ColorPalette.Accent.B * 1.1f, 1.0f).ToUint() :
                ColorPalette.Hover.ToUint();
        }

        // 绘制圆角矩形背景
        drawList.AddRectFilled(
            switchPos,
            new Vector2(switchPos.X + width, switchPos.Y + height),
            bgColor,
            radius
        );

        // 绘制边框
        drawList.AddRect(
            switchPos,
            new Vector2(switchPos.X + width, switchPos.Y + height),
            ColorPalette.Border.ToUint(),
            radius,
            ImDrawFlags.None,
            1.0f
        );

        // 计算滑块位置
        float knobRadius = radius - 2;
        float knobX = IsOn ?
            switchPos.X + width - radius :
            switchPos.X + radius;
        float knobY = switchPos.Y + height * 0.5f;

        // 绘制滑块
        drawList.AddCircleFilled(
            new Vector2(knobX, knobY),
            knobRadius,
            ColorPalette.TextHighlight.ToUint()
        );

        // 处理点击
        bool clicked = false;
        if (ImGui.InvisibleButton($"##{Id}", new Vector2(width, height)))
        {
            IsOn = !IsOn;
            clicked = true;
        }

        // 更新状态
        State = ImGui.IsItemHovered() ? ControlState.Hovered :
                ImGui.IsItemActive() ? ControlState.Pressed :
                ControlState.Normal;

        // 绘制标签
        if (ShowLabel && !string.IsNullOrEmpty(Label))
        {
            var textPos = new Vector2(
                switchPos.X + width + 8,
                switchPos.Y + (height - ImGui.CalcTextSize(Label).Y) * 0.5f
            );
            drawList.AddText(textPos, ColorPalette.TextPrimary.ToUint(), Label);
        }

        // 触发事件
        if (clicked)
        {
            RaiseToggled();
        }

        ShowTooltip();
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        float width = SwitchWidth;
        float height = SwitchHeight;

        if (Style == ToggleStyle.CheckBox)
        {
            width = EditorStyle.CheckboxSize;
            height = EditorStyle.CheckboxSize;
        }

        if (ShowLabel && !string.IsNullOrEmpty(Label))
        {
            var textSize = ImGui.CalcTextSize(Label);
            width += 8 + textSize.X;
            height = Math.Max(height, textSize.Y);
        }

        return new Vector2(width, height);
    }

    #endregion

    #region 事件触发

    private void RaiseToggled()
    {
        Toggled?.Invoke(this, new ToggleChangedEventArgs { IsOn = IsOn });
    }

    #endregion
}

/// <summary>
/// 开关改变事件参数
/// </summary>
public class ToggleChangedEventArgs : EventArgs
{
    public bool IsOn { get; set; }
}

public static class Vector4Extensions
{
    public static uint ToUint(this Vector4 color)
    {
        uint r = (uint)(color.X * 255.0f);
        uint g = (uint)(color.Y * 255.0f);
        uint b = (uint)(color.Z * 255.0f);
        uint a = (uint)(color.W * 255.0f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
