#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 复选框控件
/// </summary>
public class CheckBox : ControlBase
{
    #region 属性

    /// <summary>
    /// 标签文本
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// 是否选中
    /// </summary>
    public bool IsChecked { get; set; } = false;

    /// <summary>
    /// 是否三态（支持不确定状态）
    /// </summary>
    public bool IsThreeState { get; set; } = false;

    /// <summary>
    /// 当前状态（三态时使用）
    /// </summary>
    public CheckState CheckState { get; set; } = CheckState.Unchecked;

    /// <summary>
    /// 是否显示标签
    /// </summary>
    public bool ShowLabel { get; set; } = true;

    #endregion

    #region 事件

    /// <summary>
    /// 选中状态改变事件
    /// </summary>
    public event EventHandler<CheckBoxChangedEventArgs>? CheckedChanged;

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        ImGui.SetCursorScreenPos(Position);

        bool value = IsChecked;
        bool changed = false;

        if (IsThreeState)
        {
            // 三态复选框
            changed = RenderThreeStateCheckbox();
        }
        else
        {
            // 普通复选框
            string label = ShowLabel ? Label : $"##{Id}";
            changed = ImGui.Checkbox(label, ref value);
        }

        // 更新状态
        if (changed)
        {
            IsChecked = value;
            RaiseCheckedChanged();
        }

        // 更新控件状态
        State = ImGui.IsItemHovered() ? ControlState.Hovered :
                ImGui.IsItemActive() ? ControlState.Pressed :
                ControlState.Normal;

        // 显示工具提示
        ShowTooltip();
    }

    private bool RenderThreeStateCheckbox()
    {
        // ImGui不直接支持三态复选框，使用自定义实现
        var drawList = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();

        float checkboxSize = EditorStyle.CheckboxSizeScaled;
        Vector2 checkboxPos = Position;

        // 绘制复选框背景
        uint bgColor = ColorPalette.InputBackground.ToUint();
        uint borderColor = ColorPalette.Border.ToUint();

        if (ImGui.IsItemHovered())
        {
            borderColor = ColorPalette.Accent.ToUint();
        }

        // 绘制边框
        drawList.AddRect(
            checkboxPos,
            new Vector2(checkboxPos.X + checkboxSize, checkboxPos.Y + checkboxSize),
            borderColor,
            EditorStyle.FrameRounding,
            ImDrawFlags.None,
            1.0f
        );

        // 绘制背景
        drawList.AddRectFilled(
            new Vector2(checkboxPos.X + 1, checkboxPos.Y + 1),
            new Vector2(checkboxPos.X + checkboxSize - 1, checkboxPos.Y + checkboxSize - 1),
            bgColor,
            EditorStyle.FrameRounding
        );

        // 根据状态绘制标记
        if (CheckState == CheckState.Checked)
        {
            // 绘制勾选标记
            uint checkColor = ColorPalette.Accent.ToUint();
            float padding = checkboxSize * 0.2f;
            drawList.AddLine(
                new Vector2(checkboxPos.X + padding, checkboxPos.Y + checkboxSize * 0.5f),
                new Vector2(checkboxPos.X + checkboxSize * 0.4f, checkboxPos.Y + checkboxSize - padding),
                checkColor,
                2.0f
            );
            drawList.AddLine(
                new Vector2(checkboxPos.X + checkboxSize * 0.4f, checkboxPos.Y + checkboxSize - padding),
                new Vector2(checkboxPos.X + checkboxSize - padding, checkboxPos.Y + padding),
                checkColor,
                2.0f
            );
        }
        else if (CheckState == CheckState.Indeterminate)
        {
            // 绘制横线（不确定状态）
            uint indeterminateColor = ColorPalette.Accent.ToUint();
            float padding = checkboxSize * 0.25f;
            drawList.AddLine(
                new Vector2(checkboxPos.X + padding, checkboxPos.Y + checkboxSize * 0.5f),
                new Vector2(checkboxPos.X + checkboxSize - padding, checkboxPos.Y + checkboxSize * 0.5f),
                indeterminateColor,
                2.0f
            );
        }

        // 处理点击
        bool changed = false;
        if (ImGui.InvisibleButton($"##{Id}", new Vector2(checkboxSize, checkboxSize)))
        {
            // 循环切换状态
            if (CheckState == CheckState.Unchecked)
                CheckState = CheckState.Checked;
            else if (CheckState == CheckState.Checked)
                CheckState = CheckState.Indeterminate;
            else
                CheckState = CheckState.Unchecked;

            IsChecked = CheckState == CheckState.Checked;
            changed = true;
        }

        // 绘制标签
        if (ShowLabel)
        {
            var textPos = new Vector2(checkboxPos.X + checkboxSize + EditorStyle.ScaleValue(6.0f), checkboxPos.Y + (checkboxSize - ImGui.CalcTextSize(Label).Y) * 0.5f);
            drawList.AddText(textPos, ColorPalette.TextPrimary.ToUint(), Label);
        }

        return changed;
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        float width = EditorStyle.CheckboxSizeScaled;
        float height = EditorStyle.CheckboxSizeScaled;

        if (ShowLabel && !string.IsNullOrEmpty(Label))
        {
            var textSize = ImGui.CalcTextSize(Label);
            width += EditorStyle.ScaleValue(6.0f) + textSize.X;
            height = Math.Max(height, textSize.Y);
        }

        return new Vector2(width, height);
    }

    #endregion

    #region 事件触发

    private void RaiseCheckedChanged()
    {
        CheckedChanged?.Invoke(this, new CheckBoxChangedEventArgs
        {
            IsChecked = IsChecked,
            CheckState = CheckState
        });
    }

    #endregion
}

/// <summary>
/// 复选框状态
/// </summary>
public enum CheckState
{
    Unchecked,
    Checked,
    Indeterminate
}

/// <summary>
/// 复选框改变事件参数
/// </summary>
public class CheckBoxChangedEventArgs : EventArgs
{
    public bool IsChecked { get; set; }
    public CheckState CheckState { get; set; }
}
