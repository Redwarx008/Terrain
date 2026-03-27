#nullable enable

using Hexa.NET.ImGui;
using System.Numerics;

namespace Terrain.Editor.UI.Styling;

/// <summary>
/// 编辑器全局样式配置
/// </summary>
public static class EditorStyle
{
    // 字体大小
    public const float FontSizeSmall = 11.0f;
    public const float FontSizeNormal = 13.0f;
    public const float FontSizeLarge = 15.0f;
    public const float FontSizeTitle = 17.0f;

    // 间距和尺寸
    public const float WindowPadding = 8.0f;
    public const float FramePadding = 6.0f;
    public const float ItemSpacing = 6.0f;
    public const float ItemInnerSpacing = 6.0f;
    public const float IndentSpacing = 20.0f;
    public const float ScrollbarSize = 14.0f;
    public const float GrabMinSize = 10.0f;

    // 圆角
    public const float WindowRounding = 4.0f;
    public const float FrameRounding = 3.0f;
    public const float PopupRounding = 4.0f;
    public const float ScrollbarRounding = 6.0f;
    public const float GrabRounding = 3.0f;
    public const float TabRounding = 3.0f;

    // 边框
    public const float BorderSize = 1.0f;
    public const float WindowBorderSize = 1.0f;
    public const float FrameBorderSize = 0.0f;
    public const float PopupBorderSize = 1.0f;

    // 按钮尺寸
    public const float ButtonHeight = 24.0f;
    public const float InputHeight = 24.0f;
    public const float CheckboxSize = 16.0f;
    public const float IconSize = 16.0f;
    public const float ToolbarHeight = 36.0f;
    public const float TabHeight = 28.0f;
    public const float HeaderHeight = 26.0f;

    // 分割条
    public const float SplitterThickness = 4.0f;
    public const float MinPanelWidth = 200.0f;
    public const float MinPanelHeight = 150.0f;

    // 动画
    public const float AnimationSpeed = 0.15f;

    /// <summary>
    /// 应用编辑器样式到ImGui
    /// </summary>
    public static void Apply()
    {
        var style = ImGui.GetStyle();
        var colors = style.Colors;

        // 基础样式
        style.WindowPadding = new Vector2(WindowPadding, WindowPadding);
        style.FramePadding = new Vector2(FramePadding, FramePadding * 0.6f);
        style.ItemSpacing = new Vector2(ItemSpacing, ItemSpacing);
        style.ItemInnerSpacing = new Vector2(ItemInnerSpacing, ItemInnerSpacing);
        style.IndentSpacing = IndentSpacing;
        style.ScrollbarSize = ScrollbarSize;
        style.GrabMinSize = GrabMinSize;

        // 圆角
        style.WindowRounding = WindowRounding;
        style.FrameRounding = FrameRounding;
        style.PopupRounding = PopupRounding;
        style.ScrollbarRounding = ScrollbarRounding;
        style.GrabRounding = GrabRounding;
        style.TabRounding = TabRounding;

        // 边框
        style.WindowBorderSize = WindowBorderSize;
        style.FrameBorderSize = FrameBorderSize;
        style.PopupBorderSize = PopupBorderSize;

        // 颜色方案
        colors[(int)ImGuiCol.Text] = ColorPalette.TextPrimary.ToVector4();
        colors[(int)ImGuiCol.TextDisabled] = ColorPalette.TextDisabled.ToVector4();
        colors[(int)ImGuiCol.WindowBg] = ColorPalette.Background.ToVector4();
        colors[(int)ImGuiCol.ChildBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.PopupBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.Border] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);

        // 框架背景
        colors[(int)ImGuiCol.FrameBg] = ColorPalette.InputBackground.ToVector4();
        colors[(int)ImGuiCol.FrameBgHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.FrameBgActive] = ColorPalette.Selection.ToVector4();

        // 标题栏
        colors[(int)ImGuiCol.TitleBg] = ColorPalette.TitleBar.ToVector4();
        colors[(int)ImGuiCol.TitleBgActive] = ColorPalette.TitleBarActive.ToVector4();
        colors[(int)ImGuiCol.TitleBgCollapsed] = ColorPalette.TitleBar.ToVector4();

        // 菜单栏
        colors[(int)ImGuiCol.MenuBarBg] = ColorPalette.PanelBackground.ToVector4();

        // 滚动条
        colors[(int)ImGuiCol.ScrollbarBg] = ColorPalette.ScrollbarTrack.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrab] = ColorPalette.ScrollbarThumb.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = ColorPalette.ScrollbarThumbHover.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrabActive] = ColorPalette.Accent.ToVector4();

        // 复选框/单选框
        colors[(int)ImGuiCol.CheckMark] = ColorPalette.Accent.ToVector4();

        // 滑动条
        colors[(int)ImGuiCol.SliderGrab] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.SliderGrabActive] = ColorPalette.AccentHover.ToVector4();

        // 按钮
        colors[(int)ImGuiCol.Button] = ColorPalette.ButtonDefault.ToVector4();
        colors[(int)ImGuiCol.ButtonHovered] = ColorPalette.ButtonHover.ToVector4();
        colors[(int)ImGuiCol.ButtonActive] = ColorPalette.ButtonPressed.ToVector4();

        // 标题/折叠头
        colors[(int)ImGuiCol.Header] = ColorPalette.Selection.ToVector4();
        colors[(int)ImGuiCol.HeaderHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.HeaderActive] = ColorPalette.Pressed.ToVector4();

        // 分隔线
        colors[(int)ImGuiCol.Separator] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.SeparatorHovered] = ColorPalette.BorderLight.ToVector4();
        colors[(int)ImGuiCol.SeparatorActive] = ColorPalette.Accent.ToVector4();

        // 调整大小手柄
        colors[(int)ImGuiCol.ResizeGrip] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.ResizeGripHovered] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.ResizeGripActive] = ColorPalette.AccentHover.ToVector4();

        // 标签页
        colors[(int)ImGuiCol.Tab] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TabHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.TabSelected] = ColorPalette.Selection.ToVector4();
        colors[(int)ImGuiCol.TabDimmed] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TabDimmedSelected] = ColorPalette.SelectionInactive.ToVector4();

        // 可停靠
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(ColorPalette.Accent.R, ColorPalette.Accent.G, ColorPalette.Accent.B, 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = ColorPalette.Background.ToVector4();

        // 图表
        colors[(int)ImGuiCol.PlotLines] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.PlotLinesHovered] = ColorPalette.AccentHover.ToVector4();
        colors[(int)ImGuiCol.PlotHistogram] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.PlotHistogramHovered] = ColorPalette.AccentHover.ToVector4();

        // 表格
        colors[(int)ImGuiCol.TableHeaderBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TableBorderStrong] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.TableBorderLight] = new Vector4(ColorPalette.Border.R, ColorPalette.Border.G, ColorPalette.Border.B, 0.5f);
        colors[(int)ImGuiCol.TableRowBg] = new Vector4(0, 0, 0, 0);
        colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1, 1, 1, 0.03f);

        // 文本选择
        colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(ColorPalette.Selection.R, ColorPalette.Selection.G, ColorPalette.Selection.B, 0.6f);

        // 拖拽目标
        colors[(int)ImGuiCol.DragDropTarget] = ColorPalette.Success.ToVector4();

        // 导航高亮
        colors[(int)ImGuiCol.NavCursor] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1, 1, 1, 0.7f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0, 0, 0, 0.2f);

        // 模态背景
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0, 0, 0, 0.5f);
    }

    /// <summary>
    /// 推入禁用状态样式
    /// </summary>
    public static void PushDisabled()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
    }

    /// <summary>
    /// 弹出禁用状态样式
    /// </summary>
    public static void PopDisabled()
    {
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// 推入紧凑模式（减少间距）
    /// </summary>
    public static void PushCompact()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
    }

    /// <summary>
    /// 弹出紧凑模式
    /// </summary>
    public static void PopCompact()
    {
        ImGui.PopStyleVar(2);
    }
}
