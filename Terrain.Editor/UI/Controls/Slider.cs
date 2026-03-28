#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 滑动条控件
/// </summary>
public class Slider : ControlBase
{
    #region 属性

    /// <summary>
    /// 最小值
    /// </summary>
    public float MinValue { get; set; } = 0.0f;

    /// <summary>
    /// 最大值
    /// </summary>
    public float MaxValue { get; set; } = 1.0f;

    /// <summary>
    /// 当前值
    /// </summary>
    public float Value { get; set; } = 0.0f;

    /// <summary>
    /// 步进值（0表示连续）
    /// </summary>
    public float Step { get; set; } = 0.0f;

    /// <summary>
    /// 是否为整数模式
    /// </summary>
    public bool IsInteger { get; set; } = false;

    /// <summary>
    /// 标签文本
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// 数值格式字符串
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// 是否显示数值
    /// </summary>
    public bool ShowValue { get; set; } = true;

    /// <summary>
    /// 数值文本宽度（像素）
    /// </summary>
    public float ValueTextWidth { get; set; } = 50.0f;

    /// <summary>
    /// 单位后缀（如"%", "px"）
    /// </summary>
    public string? Suffix { get; set; }

    #endregion

    #region 事件

    /// <summary>
    /// 值改变事件（拖动中）
    /// </summary>
    public event EventHandler<SliderChangedEventArgs>? ValueChanging;

    /// <summary>
    /// 值改变事件（拖动完成）
    /// </summary>
    public event EventHandler<SliderChangedEventArgs>? ValueChanged;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        // 推入样式
        PushStyle();

        try
        {
            // 计算布局
            float labelWidth = 0;
            float valueWidth = ShowValue ? EditorStyle.ScaleValue(ValueTextWidth) : 0;

            if (!string.IsNullOrEmpty(Label))
            {
                labelWidth = ImGui.CalcTextSize(Label).X + EditorStyle.ScaleValue(8.0f);
            }

            float sliderWidth = Size.X - labelWidth - valueWidth - EditorStyle.ScaleValue(8.0f);

            // 绘制标签
            if (!string.IsNullOrEmpty(Label))
            {
                ImGui.SetCursorScreenPos(Position);
                ImGui.Text(Label);
            }

            // 绘制滑动条
            Vector2 sliderPos = new Vector2(Position.X + labelWidth, Position.Y);
            ImGui.SetCursorScreenPos(sliderPos);

            float value = Value;
            bool changed = false;

            if (IsInteger)
            {
                int intValue = (int)value;
                changed = ImGui.SliderInt($"##{Id}", ref intValue, (int)MinValue, (int)MaxValue, "");
                value = intValue;
            }
            else
            {
                changed = ImGui.SliderFloat($"##{Id}", ref value, MinValue, MaxValue, "", ImGuiSliderFlags.None);
            }

            // 应用步进
            if (Step > 0)
            {
                value = MathF.Round(value / Step) * Step;
            }

            // 限制范围
            value = Math.Clamp(value, MinValue, MaxValue);

            // 更新值
            if (value != Value)
            {
                Value = value;
                RaiseValueChanging();
            }

            if (changed)
            {
                RaiseValueChanged();
            }

            // 更新状态
            State = ImGui.IsItemHovered() ? ControlState.Hovered :
                    ImGui.IsItemActive() ? ControlState.Pressed :
                    ControlState.Normal;

            // 绘制数值文本
            if (ShowValue)
            {
                Vector2 valuePos = new Vector2(Position.X + labelWidth + sliderWidth + EditorStyle.ScaleValue(8.0f), Position.Y);
                ImGui.SetCursorScreenPos(valuePos);

                string valueText = FormatValue(Value);
                ImGui.Text(valueText);
            }
        }
        finally
        {
            PopStyle();
        }

        ShowTooltip();
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        float height = EditorStyle.InputHeightScaled;
        float width = availableSize.X;

        return new Vector2(width, height);
    }

    #endregion

    #region 辅助方法

    private string FormatValue(float value)
    {
        string format = Format ?? (IsInteger ? "{0}" : "{0:F2}");
        string result = string.Format(format, value);

        if (!string.IsNullOrEmpty(Suffix))
        {
            result += Suffix;
        }

        return result;
    }

    private void PushStyle()
    {
        // 推入紧凑样式
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(EditorStyle.ScaleValue(4.0f), EditorStyle.ScaleValue(3.0f)));
    }

    private void PopStyle()
    {
        ImGui.PopStyleVar();
    }

    #endregion

    #region 事件触发

    private void RaiseValueChanging()
    {
        ValueChanging?.Invoke(this, new SliderChangedEventArgs { Value = Value });
    }

    private void RaiseValueChanged()
    {
        ValueChanged?.Invoke(this, new SliderChangedEventArgs { Value = Value });
    }

    #endregion
}

/// <summary>
/// 滑动条改变事件参数
/// </summary>
public class SliderChangedEventArgs : EventArgs
{
    public float Value { get; set; }
}
