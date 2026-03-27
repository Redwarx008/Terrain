#nullable enable

using Hexa.NET.ImGui;
using System.Numerics;
using System.Collections.Generic;
using System;

namespace Terrain.Editor.UI.Controls;

/// <summary>
/// 控件事件参数
/// </summary>
public class ControlEventArgs : EventArgs
{
    public Vector2 MousePosition { get; set; }
    public bool Handled { get; set; }
}

/// <summary>
/// 控件状态
/// </summary>
public enum ControlState
{
    Normal,
    Hovered,
    Pressed,
    Disabled,
    Focused
}

/// <summary>
/// 边距结构
/// </summary>
public struct Margin
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

    public Margin(float uniform) : this(uniform, uniform, uniform, uniform) { }
    public Margin(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    public Margin(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static readonly Margin Zero = new(0);
    public static readonly Margin Default = new(4);
}

/// <summary>
/// 控件基类 - 所有UI控件的抽象基类
/// </summary>
public abstract class ControlBase
{
    #region 属性

    /// <summary>
    /// 控件唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 控件名称（显示用）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 相对于父容器的位置
    /// </summary>
    public Vector2 Position { get; set; }

    /// <summary>
    /// 控件尺寸
    /// </summary>
    public Vector2 Size { get; set; }

    /// <summary>
    /// 内边距
    /// </summary>
    public Margin Padding { get; set; } = Margin.Default;

    /// <summary>
    /// 外边距
    /// </summary>
    public Margin Margin { get; set; } = Margin.Zero;

    /// <summary>
    /// 最小尺寸约束
    /// </summary>
    public Vector2 MinSize { get; set; } = Vector2.Zero;

    /// <summary>
    /// 最大尺寸约束
    /// </summary>
    public Vector2 MaxSize { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// 首选尺寸（用于自动布局）
    /// </summary>
    public Vector2? PreferredSize { get; set; }

    /// <summary>
    /// 是否可见
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 当前状态
    /// </summary>
    public ControlState State { get; protected set; } = ControlState.Normal;

    /// <summary>
    /// 父控件
    /// </summary>
    public ControlBase? Parent { get; internal set; }

    /// <summary>
    /// 子控件集合
    /// </summary>
    public List<ControlBase> Children { get; } = new();

    /// <summary>
    /// 是否可以获得焦点
    /// </summary>
    public virtual bool CanFocus { get; } = false;

    /// <summary>
    /// 是否已获取焦点
    /// </summary>
    public bool IsFocused { get; internal set; }

    /// <summary>
    /// 是否已初始化
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// 控件提示文本
    /// </summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// 背景色（null表示使用主题色）
    /// </summary>
    public Vector4? BackgroundColor { get; set; }

    /// <summary>
    /// 前景色（文字颜色，null表示使用主题色）
    /// </summary>
    public Vector4? ForegroundColor { get; set; }

    /// <summary>
    /// 边框颜色（null表示使用主题色）
    /// </summary>
    public Vector4? BorderColor { get; set; }

    /// <summary>
    /// 边框粗细
    /// </summary>
    public float BorderThickness { get; set; } = 0;

    #endregion

    #region 事件

    /// <summary>
    /// 点击事件
    /// </summary>
    public event EventHandler<ControlEventArgs>? Click;

    /// <summary>
    /// 双击事件
    /// </summary>
    public event EventHandler<ControlEventArgs>? DoubleClick;

    /// <summary>
    /// 鼠标进入事件
    /// </summary>
    public event EventHandler<ControlEventArgs>? MouseEnter;

    /// <summary>
    /// 鼠标离开事件
    /// </summary>
    public event EventHandler<ControlEventArgs>? MouseLeave;

    /// <summary>
    /// 鼠标移动事件
    /// </summary>
    public event EventHandler<ControlEventArgs>? MouseMove;

    /// <summary>
    /// 获取焦点事件
    /// </summary>
    public event EventHandler? GotFocus;

    /// <summary>
    /// 失去焦点事件
    /// </summary>
    public event EventHandler? LostFocus;

    /// <summary>
    /// 尺寸改变事件
    /// </summary>
    public event EventHandler? SizeChanged;

    /// <summary>
    /// 位置改变事件
    /// </summary>
    public event EventHandler? PositionChanged;

    #endregion

    #region 生命周期方法

    /// <summary>
    /// 初始化控件
    /// </summary>
    public virtual void Initialize()
    {
        if (IsInitialized) return;

        OnInitialize();
        IsInitialized = true;
    }

    /// <summary>
    /// 初始化逻辑（子类重写）
    /// </summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// 更新控件状态
    /// </summary>
    public virtual void Update(float deltaTime)
    {
        if (!IsVisible || !IsInitialized) return;

        OnUpdate(deltaTime);

        // 更新子控件
        foreach (var child in Children)
        {
            child.Update(deltaTime);
        }
    }

    /// <summary>
    /// 更新逻辑（子类重写）
    /// </summary>
    protected virtual void OnUpdate(float deltaTime) { }

    /// <summary>
    /// 渲染控件
    /// </summary>
    public void Render()
    {
        if (!IsVisible) return;

        // 推入样式
        PushStyles();

        try
        {
            OnRender();
        }
        finally
        {
            // 弹出样式
            PopStyles();
        }

        // 渲染子控件
        foreach (var child in Children)
        {
            child.Render();
        }
    }

    /// <summary>
    /// 渲染逻辑（子类必须重写）
    /// </summary>
    protected abstract void OnRender();

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        OnDispose();

        foreach (var child in Children)
        {
            child.Dispose();
        }
    }

    /// <summary>
    /// 释放逻辑（子类重写）
    /// </summary>
    protected virtual void OnDispose() { }

    #endregion

    #region 布局方法

    /// <summary>
    /// 测量控件所需尺寸
    /// </summary>
    public virtual Vector2 Measure(Vector2 availableSize)
    {
        if (PreferredSize.HasValue)
        {
            return ClampSize(PreferredSize.Value);
        }

        var desiredSize = OnMeasure(availableSize);
        return ClampSize(desiredSize);
    }

    /// <summary>
    /// 测量逻辑（子类重写）
    /// </summary>
    protected virtual Vector2 OnMeasure(Vector2 availableSize)
    {
        return Size;
    }

    /// <summary>
    /// 排列控件位置和尺寸
    /// </summary>
    public virtual void Arrange(Vector2 position, Vector2 size)
    {
        var oldPosition = Position;
        var oldSize = Size;

        Position = position;
        Size = ClampSize(size);

        OnArrange(position, size);

        if (Position != oldPosition)
            PositionChanged?.Invoke(this, EventArgs.Empty);

        if (Size != oldSize)
            SizeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 排列逻辑（子类重写）
    /// </summary>
    protected virtual void OnArrange(Vector2 position, Vector2 size) { }

    /// <summary>
    /// 约束尺寸在最小和最大范围内
    /// </summary>
    protected Vector2 ClampSize(Vector2 size)
    {
        return new Vector2(
            Math.Clamp(size.X, MinSize.X, MaxSize.X),
            Math.Clamp(size.Y, MinSize.Y, MaxSize.Y)
        );
    }

    /// <summary>
    /// 获取内容区域（去除Padding）
    /// </summary>
    public Rect GetContentRect()
    {
        return new Rect(
            Position.X + Padding.Left,
            Position.Y + Padding.Top,
            Size.X - Padding.Horizontal,
            Size.Y - Padding.Vertical
        );
    }

    /// <summary>
    /// 获取完整区域（包含Margin）
    /// </summary>
    public Rect GetBoundingRect()
    {
        return new Rect(
            Position.X - Margin.Left,
            Position.Y - Margin.Top,
            Size.X + Margin.Horizontal,
            Size.Y + Margin.Vertical
        );
    }

    #endregion

    #region 输入处理

    /// <summary>
    /// 处理输入事件
    /// </summary>
    public virtual bool HandleInput(InputEvent evt)
    {
        if (!IsVisible || !IsEnabled) return false;

        // 先让子控件处理
        foreach (var child in Children)
        {
            if (child.HandleInput(evt))
                return true;
        }

        return OnHandleInput(evt);
    }

    /// <summary>
    /// 输入处理逻辑（子类重写）
    /// </summary>
    protected virtual bool OnHandleInput(InputEvent evt)
    {
        return false;
    }

    #endregion

    #region 子控件管理

    /// <summary>
    /// 添加子控件
    /// </summary>
    public void AddChild(ControlBase child)
    {
        if (child.Parent != null)
            throw new InvalidOperationException("控件已属于其他父控件");

        child.Parent = this;
        Children.Add(child);
        child.Initialize();
    }

    /// <summary>
    /// 移除子控件
    /// </summary>
    public bool RemoveChild(ControlBase child)
    {
        if (Children.Remove(child))
        {
            child.Parent = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 插入子控件
    /// </summary>
    public void InsertChild(int index, ControlBase child)
    {
        if (child.Parent != null)
            throw new InvalidOperationException("控件已属于其他父控件");

        child.Parent = this;
        Children.Insert(index, child);
        child.Initialize();
    }

    /// <summary>
    /// 清除所有子控件
    /// </summary>
    public void ClearChildren()
    {
        foreach (var child in Children)
        {
            child.Parent = null;
        }
        Children.Clear();
    }

    #endregion

    #region 事件触发

    protected void RaiseClick(Vector2 mousePos)
    {
        var args = new ControlEventArgs { MousePosition = mousePos };
        Click?.Invoke(this, args);
    }

    protected void RaiseDoubleClick(Vector2 mousePos)
    {
        var args = new ControlEventArgs { MousePosition = mousePos };
        DoubleClick?.Invoke(this, args);
    }

    protected void RaiseMouseEnter(Vector2 mousePos)
    {
        State = ControlState.Hovered;
        var args = new ControlEventArgs { MousePosition = mousePos };
        MouseEnter?.Invoke(this, args);
    }

    protected void RaiseMouseLeave(Vector2 mousePos)
    {
        State = ControlState.Normal;
        var args = new ControlEventArgs { MousePosition = mousePos };
        MouseLeave?.Invoke(this, args);
    }

    protected void RaiseGotFocus()
    {
        IsFocused = true;
        State = ControlState.Focused;
        GotFocus?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseLostFocus()
    {
        IsFocused = false;
        State = ControlState.Normal;
        LostFocus?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region 样式管理

    private void PushStyles()
    {
        if (!IsEnabled)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        }

        if (BackgroundColor.HasValue)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, BackgroundColor.Value);
        }
    }

    private void PopStyles()
    {
        if (!IsEnabled)
        {
            ImGui.PopStyleVar();
        }

        if (BackgroundColor.HasValue)
        {
            ImGui.PopStyleColor();
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 检查点是否在控件内
    /// </summary>
    public bool ContainsPoint(Vector2 point)
    {
        return point.X >= Position.X && point.X <= Position.X + Size.X &&
               point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;
    }

    /// <summary>
    /// 获取ImGui控件ID
    /// </summary>
    protected string GetControlId(string? suffix = null)
    {
        return suffix != null ? $"{Id}##{suffix}" : Id;
    }

    /// <summary>
    /// 显示工具提示
    /// </summary>
    protected void ShowTooltip()
    {
        if (!string.IsNullOrEmpty(Tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Tooltip);
        }
    }

    #endregion
}

/// <summary>
/// 矩形结构
/// </summary>
public struct Rect
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Vector2 point)
    {
        return point.X >= Left && point.X <= Right &&
               point.Y >= Top && point.Y <= Bottom;
    }

    public bool Intersects(Rect other)
    {
        return Left < other.Right && Right > other.Left &&
               Top < other.Bottom && Bottom > other.Top;
    }
}

/// <summary>
/// 输入事件
/// </summary>
public class InputEvent
{
    public InputEventType Type { get; set; }
    public Vector2 MousePosition { get; set; }
    public int MouseButton { get; set; }
    public float MouseWheelDelta { get; set; }
    public bool Shift { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
}

public enum InputEventType
{
    MouseDown,
    MouseUp,
    MouseMove,
    MouseWheel,
    KeyDown,
    KeyUp,
    Char
}
