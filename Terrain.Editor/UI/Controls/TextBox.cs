#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 文本输入框控件
/// </summary>
public class TextBox : ControlBase
{
    #region 属性

    /// <summary>
    /// 当前文本
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 占位符文本
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// 标签文本
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// 最大字符数（0表示无限制）
    /// </summary>
    public int MaxLength { get; set; } = 0;

    /// <summary>
    /// 是否多行
    /// </summary>
    public bool IsMultiline { get; set; } = false;

    /// <summary>
    /// 是否只读
    /// </summary>
    public bool IsReadOnly { get; set; } = false;

    /// <summary>
    /// 是否密码框
    /// </summary>
    public bool IsPassword { get; set; } = false;

    /// <summary>
    /// 是否自动聚焦
    /// </summary>
    public bool AutoFocus { get; set; } = false;

    /// <summary>
    /// 是否选择所有文本（首次聚焦时）
    /// </summary>
    public bool SelectAllOnFocus { get; set; } = false;

    #endregion

    #region 事件

    /// <summary>
    /// 文本改变事件（每次输入）
    /// </summary>
    public event EventHandler<TextBoxChangedEventArgs>? TextChanged;

    /// <summary>
    /// 文本提交事件（按Enter时）
    /// </summary>
    public event EventHandler<TextBoxChangedEventArgs>? TextSubmitted;

    /// <summary>
    /// 获取焦点事件
    /// </summary>
    public event EventHandler? FocusEntered;

    /// <summary>
    /// 失去焦点事件
    /// </summary>
    public event EventHandler? FocusLeft;

    #endregion

    #region 私有字段

    private string buffer = "";
    private bool wasFocused = false;
    private bool firstFocus = true;

    #endregion

    #region 初始化

    protected override void OnInitialize()
    {
        buffer = Text;
    }

    #endregion

    #region 渲染

    protected override void OnRender()
    {
        PushStyle();

        try
        {
            // 同步缓冲区
            if (buffer != Text)
            {
                buffer = Text;
            }

            float labelWidth = 0;
            if (!string.IsNullOrEmpty(Label))
            {
                labelWidth = ImGui.CalcTextSize(Label).X + EditorStyle.ScaleValue(8.0f);
                ImGui.SetCursorScreenPos(Position);
                ImGui.Text(Label);
            }

            Vector2 inputPos = new Vector2(Position.X + labelWidth, Position.Y);
            ImGui.SetCursorScreenPos(inputPos);

            float inputWidth = Size.X - labelWidth;

            // 处理自动聚焦
            if (AutoFocus && firstFocus)
            {
                ImGui.SetKeyboardFocusHere();
            }

            // 渲染输入框
            bool changed = RenderInput(inputWidth);

            // 检测焦点变化
            bool isFocused = ImGui.IsItemFocused();
            if (isFocused && !wasFocused)
            {
                wasFocused = true;
                FocusEntered?.Invoke(this, EventArgs.Empty);

                firstFocus = false;
            }
            else if (!isFocused && wasFocused)
            {
                wasFocused = false;
                FocusLeft?.Invoke(this, EventArgs.Empty);
            }

            // 更新状态
            State = ImGui.IsItemHovered() ? ControlState.Hovered :
                    ImGui.IsItemActive() ? ControlState.Pressed :
                    isFocused ? ControlState.Focused :
                    ControlState.Normal;

            // 处理改变
            TextInputStyle.DrawFocusOutline();

            if (changed)
            {
                Text = buffer;
                RaiseTextChanged();
            }

            // 处理提交（Enter键）
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                RaiseTextSubmitted();
            }
        }
        finally
        {
            PopStyle();
        }

        ShowTooltip();
    }

    private bool RenderInput(float width)
    {
        ImGui.PushItemWidth(width);

        bool changed = false;
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None;

        if (IsReadOnly)
            flags |= ImGuiInputTextFlags.ReadOnly;

        if (MaxLength > 0)
            flags |= ImGuiInputTextFlags.CallbackResize;

        if (SelectAllOnFocus)
            flags |= ImGuiInputTextFlags.AutoSelectAll;

        if (IsMultiline)
        {
            changed = ImGui.InputTextMultiline(
                $"##{Id}",
                ref buffer,
                MaxLength > 0 ? (uint)MaxLength : 256,
                new Vector2(width, Size.Y),
                flags
            );
        }
        else
        {
            if (IsPassword)
            {
                changed = ImGui.InputTextWithHint(
                    $"##{Id}",
                    Placeholder ?? "",
                    ref buffer,
                    MaxLength > 0 ? (uint)MaxLength : 256,
                    flags | ImGuiInputTextFlags.Password
                );
            }
            else
            {
                changed = ImGui.InputTextWithHint(
                    $"##{Id}",
                    Placeholder ?? "",
                    ref buffer,
                    MaxLength > 0 ? (uint)MaxLength : 256,
                    flags
                );
            }
        }

        ImGui.PopItemWidth();

        return changed;
    }

    #endregion

    #region 布局

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        float height = IsMultiline ? Math.Max(EditorStyle.ScaleValue(60.0f), Size.Y) : EditorStyle.InputHeightScaled;
        float width = availableSize.X;

        return new Vector2(width, height);
    }

    #endregion

    #region 样式

    private void PushStyle()
    {
        TextInputStyle.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(EditorStyle.ScaleValue(6.0f), EditorStyle.ScaleValue(4.0f)));
    }

    private void PopStyle()
    {
        ImGui.PopStyleVar();
        TextInputStyle.Pop();
    }

    #endregion

    #region 事件触发

    private void RaiseTextChanged()
    {
        TextChanged?.Invoke(this, new TextBoxChangedEventArgs { Text = Text });
    }

    private void RaiseTextSubmitted()
    {
        TextSubmitted?.Invoke(this, new TextBoxChangedEventArgs { Text = Text });
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置焦点到此输入框
    /// </summary>
    public void Focus()
    {
        // 在下一帧设置焦点
        AutoFocus = true;
    }

    /// <summary>
    /// 选择所有文本
    /// </summary>
    public void SelectAll()
    {
        // ImGui的文本选择需要通过API实现
        // 这里可以存储一个标志，在渲染时处理
    }

    /// <summary>
    /// 清除文本
    /// </summary>
    public void Clear()
    {
        Text = "";
        buffer = "";
        RaiseTextChanged();
    }

    #endregion
}

/// <summary>
/// 文本框改变事件参数
/// </summary>
public class TextBoxChangedEventArgs : EventArgs
{
    public string Text { get; set; } = "";
}
