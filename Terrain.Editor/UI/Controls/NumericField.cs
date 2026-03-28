#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 数值输入框控件
/// </summary>
public class NumericField : ControlBase
{
    #region 属性

    /// <summary>
    /// 当前值
    /// </summary>
    public float Value { get; set; } = 0.0f;

    /// <summary>
    /// 最小值
    /// </summary>
    public float MinValue { get; set; } = float.MinValue;

    /// <summary>
    /// 最大值
    /// </summary>
    public float MaxValue { get; set; } = float.MaxValue;

    /// <summary>
    /// 步进值
    /// </summary>
    public float Step { get; set; } = 1.0f;

    /// <summary>
    /// 是否为整数模式
    /// </summary>
    public bool IsInteger { get; set; } = false;

    /// <summary>
    /// 标签文本
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// 单位后缀
    /// </summary>
    public string? Suffix { get; set; }

    /// <summary>
    /// 数值格式
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// 是否允许通过拖拽调整数值
    /// </summary>
    public bool EnableDrag { get; set; } = true;

    /// <summary>
    /// 拖拽速度
    /// </summary>
    public float DragSpeed { get; set; } = 1.0f;

    /// <summary>
    /// 是否显示增减按钮
    /// </summary>
    public bool ShowButtons { get; set; } = true;

    /// <summary>
    /// 按钮宽度
    /// </summary>
    public float ButtonWidth { get; set; } = 20.0f;

    #endregion

    #region 事件

    /// <summary>
    /// 值改变事件
    /// </summary>
    public event EventHandler<NumericFieldChangedEventArgs>? ValueChanged;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        PushStyle();

        try
        {
            float labelWidth = 0;
            if (!string.IsNullOrEmpty(Label))
            {
                labelWidth = ImGui.CalcTextSize(Label).X + EditorStyle.ScaleValue(8.0f);
                ImGui.SetCursorScreenPos(Position);
                ImGui.Text(Label);
            }

            float scaledButtonWidth = EditorStyle.ScaleValue(ButtonWidth);
            float buttonsWidth = ShowButtons ? scaledButtonWidth * 2 + EditorStyle.ScaleValue(4.0f) : 0;
            float inputWidth = Size.X - labelWidth - buttonsWidth;

            Vector2 inputPos = new Vector2(Position.X + labelWidth, Position.Y);
            ImGui.SetCursorScreenPos(inputPos);

            // 渲染输入框
            bool changed = RenderInputField(inputWidth);

            // 渲染增减按钮
            if (ShowButtons)
            {
                Vector2 buttonPos = new Vector2(Position.X + labelWidth + inputWidth + EditorStyle.ScaleValue(4.0f), Position.Y);
                RenderButtons(buttonPos, scaledButtonWidth);
            }

            // 更新状态
            State = ImGui.IsItemHovered() ? ControlState.Hovered :
                    ImGui.IsItemActive() ? ControlState.Pressed :
                    ControlState.Normal;

            if (changed)
            {
                RaiseValueChanged();
            }
        }
        finally
        {
            PopStyle();
        }

        ShowTooltip();
    }

    private bool RenderInputField(float width)
    {
        float value = Value;
        bool changed = false;

        ImGui.PushItemWidth(width);

        if (EnableDrag)
        {
            // 使用DragFloat支持拖拽
            string format = Format ?? (IsInteger ? "%.0f" : "%.2f");
            if (!string.IsNullOrEmpty(Suffix))
            {
                format += $" {Suffix}";
            }

            changed = ImGui.DragFloat($"##{Id}", ref value, DragSpeed * Step, MinValue, MaxValue, format);
        }
        else
        {
            // 使用InputFloat
            if (IsInteger)
            {
                int intValue = (int)value;
                changed = ImGui.InputInt($"##{Id}", ref intValue);
                value = intValue;
            }
            else
            {
                changed = ImGui.InputFloat($"##{Id}", ref value, 0, 0, Format ?? "%.2f");
            }
        }

        ImGui.PopItemWidth();

        // 应用约束
        if (changed)
        {
            value = Math.Clamp(value, MinValue, MaxValue);
            if (Step > 0)
            {
                value = MathF.Round(value / Step) * Step;
            }
            Value = value;
        }

        return changed;
    }

    private void RenderButtons(Vector2 position, float buttonWidth)
    {
        float buttonHeight = Size.Y * 0.5f;

        // 增加按钮
        ImGui.SetCursorScreenPos(position);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 0));

        if (ImGui.Button($"+##{Id}_inc", new Vector2(buttonWidth, buttonHeight - 1)))
        {
            Value = Math.Clamp(Value + Step, MinValue, MaxValue);
            RaiseValueChanged();
        }

        // 减少按钮
        ImGui.SetCursorScreenPos(new Vector2(position.X, position.Y + buttonHeight + 1));
        if (ImGui.Button($"-##{Id}_dec", new Vector2(buttonWidth, buttonHeight - 1)))
        {
            Value = Math.Clamp(Value - Step, MinValue, MaxValue);
            RaiseValueChanged();
        }

        ImGui.PopStyleVar();
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

    #region 样式

    private void PushStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(EditorStyle.ScaleValue(6.0f), EditorStyle.ScaleValue(4.0f)));
    }

    private void PopStyle()
    {
        ImGui.PopStyleVar();
    }

    #endregion

    #region 事件触发

    private void RaiseValueChanged()
    {
        ValueChanged?.Invoke(this, new NumericFieldChangedEventArgs { Value = Value });
    }

    #endregion
}

/// <summary>
/// 数值输入框改变事件参数
/// </summary>
public class NumericFieldChangedEventArgs : EventArgs
{
    public float Value { get; set; }
}
