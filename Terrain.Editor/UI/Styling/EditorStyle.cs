#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;

namespace Terrain.Editor.UI.Styling;

/// <summary>
/// 编辑器全局样式配置。
/// </summary>
public static class EditorStyle
{
    public static float CurrentScale { get; private set; } = 1.0f;

    // 字体大小
    public const float FontSizeSmall = 11.0f;
    public const float FontSizeNormal = 13.0f;
    public const float FontSizeLarge = 15.0f;
    public const float FontSizeTitle = 17.0f;

    // 间距与尺寸
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

    // 控件尺寸
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
    public const float MaxPanelWidth = 400.0f;
    public const float MaxPanelHeight = 400.0f;

    // 动画
    public const float AnimationSpeed = 0.15f;

    public static float ButtonHeightScaled => ScaleValue(ButtonHeight);
    public static float InputHeightScaled => ScaleValue(InputHeight);
    public static float CheckboxSizeScaled => ScaleValue(CheckboxSize);
    public static float IconSizeScaled => ScaleValue(IconSize);
    public static float ToolbarHeightScaled => ScaleValue(ToolbarHeight);
    public static float TabHeightScaled => ScaleValue(TabHeight);
    public static float HeaderHeightScaled => ScaleValue(HeaderHeight);
    public static float SplitterThicknessScaled => MathF.Max(2.0f, ScaleValue(SplitterThickness));
    public static float MinPanelWidthScaled => ScaleValue(MinPanelWidth);
    public static float MinPanelHeightScaled => ScaleValue(MinPanelHeight);
    public static float MaxPanelWidthScaled => ScaleValue(MaxPanelWidth);
    public static float MaxPanelHeightScaled => ScaleValue(MaxPanelHeight);

    public static float ScaleValue(float value)
    {
        return value * CurrentScale;
    }

    /// <summary>
    /// 应用编辑器样式到 ImGui。
    /// </summary>
    public static void Apply(float scale = 1.0f)
    {
        CurrentScale = scale;

        var style = ImGui.GetStyle();
        var colors = style.Colors;

        style.WindowPadding = new Vector2(ScaleValue(WindowPadding), ScaleValue(WindowPadding));
        style.FramePadding = new Vector2(ScaleValue(FramePadding), ScaleValue(FramePadding * 0.6f));
        style.ItemSpacing = new Vector2(ScaleValue(ItemSpacing), ScaleValue(ItemSpacing));
        style.ItemInnerSpacing = new Vector2(ScaleValue(ItemInnerSpacing), ScaleValue(ItemInnerSpacing));
        style.IndentSpacing = ScaleValue(IndentSpacing);
        style.ScrollbarSize = ScaleValue(ScrollbarSize);
        style.GrabMinSize = ScaleValue(GrabMinSize);

        style.WindowRounding = ScaleValue(WindowRounding);
        style.FrameRounding = ScaleValue(FrameRounding);
        style.PopupRounding = ScaleValue(PopupRounding);
        style.ScrollbarRounding = ScaleValue(ScrollbarRounding);
        style.GrabRounding = ScaleValue(GrabRounding);
        style.TabRounding = ScaleValue(TabRounding);

        style.WindowBorderSize = WindowBorderSize;
        style.FrameBorderSize = FrameBorderSize;
        style.PopupBorderSize = PopupBorderSize;

        colors[(int)ImGuiCol.Text] = ColorPalette.TextPrimary.ToVector4();
        colors[(int)ImGuiCol.TextDisabled] = ColorPalette.TextDisabled.ToVector4();
        colors[(int)ImGuiCol.WindowBg] = ColorPalette.Background.ToVector4();
        colors[(int)ImGuiCol.ChildBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.PopupBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.Border] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);

        colors[(int)ImGuiCol.FrameBg] = ColorPalette.InputBackground.ToVector4();
        colors[(int)ImGuiCol.FrameBgHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.FrameBgActive] = ColorPalette.Selection.ToVector4();

        colors[(int)ImGuiCol.TitleBg] = ColorPalette.TitleBar.ToVector4();
        colors[(int)ImGuiCol.TitleBgActive] = ColorPalette.TitleBarActive.ToVector4();
        colors[(int)ImGuiCol.TitleBgCollapsed] = ColorPalette.TitleBar.ToVector4();

        colors[(int)ImGuiCol.MenuBarBg] = ColorPalette.PanelBackground.ToVector4();

        colors[(int)ImGuiCol.ScrollbarBg] = ColorPalette.ScrollbarTrack.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrab] = ColorPalette.ScrollbarThumb.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = ColorPalette.ScrollbarThumbHover.ToVector4();
        colors[(int)ImGuiCol.ScrollbarGrabActive] = ColorPalette.Accent.ToVector4();

        colors[(int)ImGuiCol.CheckMark] = ColorPalette.Accent.ToVector4();

        colors[(int)ImGuiCol.SliderGrab] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.SliderGrabActive] = ColorPalette.AccentHover.ToVector4();

        colors[(int)ImGuiCol.Button] = ColorPalette.ButtonDefault.ToVector4();
        colors[(int)ImGuiCol.ButtonHovered] = ColorPalette.ButtonHover.ToVector4();
        colors[(int)ImGuiCol.ButtonActive] = ColorPalette.ButtonPressed.ToVector4();

        colors[(int)ImGuiCol.Header] = ColorPalette.Selection.ToVector4();
        colors[(int)ImGuiCol.HeaderHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.HeaderActive] = ColorPalette.Pressed.ToVector4();

        colors[(int)ImGuiCol.Separator] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.SeparatorHovered] = ColorPalette.BorderLight.ToVector4();
        colors[(int)ImGuiCol.SeparatorActive] = ColorPalette.Accent.ToVector4();

        colors[(int)ImGuiCol.ResizeGrip] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.ResizeGripHovered] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.ResizeGripActive] = ColorPalette.AccentHover.ToVector4();

        colors[(int)ImGuiCol.Tab] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TabHovered] = ColorPalette.Hover.ToVector4();
        colors[(int)ImGuiCol.TabSelected] = ColorPalette.Selection.ToVector4();
        colors[(int)ImGuiCol.TabDimmed] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TabDimmedSelected] = ColorPalette.SelectionInactive.ToVector4();

        colors[(int)ImGuiCol.DockingPreview] = new Vector4(ColorPalette.Accent.R, ColorPalette.Accent.G, ColorPalette.Accent.B, 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = ColorPalette.Background.ToVector4();

        colors[(int)ImGuiCol.PlotLines] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.PlotLinesHovered] = ColorPalette.AccentHover.ToVector4();
        colors[(int)ImGuiCol.PlotHistogram] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.PlotHistogramHovered] = ColorPalette.AccentHover.ToVector4();

        colors[(int)ImGuiCol.TableHeaderBg] = ColorPalette.PanelBackground.ToVector4();
        colors[(int)ImGuiCol.TableBorderStrong] = ColorPalette.Border.ToVector4();
        colors[(int)ImGuiCol.TableBorderLight] = new Vector4(ColorPalette.Border.R, ColorPalette.Border.G, ColorPalette.Border.B, 0.5f);
        colors[(int)ImGuiCol.TableRowBg] = new Vector4(0, 0, 0, 0);
        colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1, 1, 1, 0.03f);

        colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(ColorPalette.Selection.R, ColorPalette.Selection.G, ColorPalette.Selection.B, 0.6f);
        colors[(int)ImGuiCol.DragDropTarget] = ColorPalette.Success.ToVector4();
        colors[(int)ImGuiCol.NavCursor] = ColorPalette.Accent.ToVector4();
        colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1, 1, 1, 0.7f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0, 0, 0, 0.2f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0, 0, 0, 0.5f);
    }

    /// <summary>
    /// 压入禁用态样式。
    /// </summary>
    public static void PushDisabled()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
    }

    /// <summary>
    /// 弹出禁用态样式。
    /// </summary>
    public static void PopDisabled()
    {
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// 压入更紧凑的局部样式。
    /// </summary>
    public static void PushCompact()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(ScaleValue(4.0f), ScaleValue(2.0f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ScaleValue(4.0f), ScaleValue(4.0f)));
    }

    /// <summary>
    /// 弹出紧凑样式。
    /// </summary>
    public static void PopCompact()
    {
        ImGui.PopStyleVar(2);
    }
}
