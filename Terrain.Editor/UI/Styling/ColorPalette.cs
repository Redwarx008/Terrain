#nullable enable

using Stride.Core.Mathematics;

namespace Terrain.Editor.UI.Styling;

/// <summary>
/// 编辑器颜色方案 - 深黑色专业主题
/// </summary>
public static class ColorPalette
{
    // 背景色
    public static readonly Color4 Background = new(0.118f, 0.118f, 0.118f, 1.0f);        // #1E1E1E
    public static readonly Color4 PanelBackground = new(0.145f, 0.145f, 0.149f, 1.0f);   // #252526
    public static readonly Color4 DarkBackground = new(0.09f, 0.09f, 0.09f, 1.0f);       // #171717

    // 交互状态色
    public static readonly Color4 Selection = new(0.035f, 0.294f, 0.443f, 1.0f);         // #094771
    public static readonly Color4 SelectionInactive = new(0.15f, 0.15f, 0.15f, 1.0f);    // #262626
    public static readonly Color4 Hover = new(0.165f, 0.176f, 0.18f, 1.0f);              // #2A2D2E
    public static readonly Color4 HoverLight = new(0.2f, 0.21f, 0.22f, 1.0f);            // #333638
    public static readonly Color4 Pressed = new(0.063f, 0.247f, 0.373f, 1.0f);           // #103E5F

    // 边框和分隔线
    public static readonly Color4 Border = new(0.243f, 0.243f, 0.259f, 1.0f);            // #3E3E42
    public static readonly Color4 BorderLight = new(0.337f, 0.337f, 0.353f, 1.0f);       // #56565A
    public static readonly Color4 BorderDark = new(0.1f, 0.1f, 0.1f, 1.0f);              // #1A1A1A

    // 文字颜色
    public static readonly Color4 TextPrimary = new(0.8f, 0.8f, 0.8f, 1.0f);             // #CCCCCC
    public static readonly Color4 TextSecondary = new(0.522f, 0.522f, 0.522f, 1.0f);     // #858585
    public static readonly Color4 TextDisabled = new(0.4f, 0.4f, 0.4f, 1.0f);            // #666666
    public static readonly Color4 TextHighlight = new(1.0f, 1.0f, 1.0f, 1.0f);           // #FFFFFF

    // 强调色
    public static readonly Color4 Accent = new(0.0f, 0.478f, 0.8f, 1.0f);                // #007ACC
    public static readonly Color4 AccentHover = new(0.0f, 0.557f, 0.933f, 1.0f);         // #008Eee
    public static readonly Color4 AccentPressed = new(0.0f, 0.4f, 0.667f, 1.0f);         // #006AAA

    // 状态色
    public static readonly Color4 Success = new(0.306f, 0.788f, 0.69f, 1.0f);            // #4EC9B0
    public static readonly Color4 Warning = new(0.808f, 0.569f, 0.47f, 1.0f);            // #CE9178
    public static readonly Color4 Error = new(0.957f, 0.278f, 0.278f, 1.0f);             // #F44747
    public static readonly Color4 Info = new(0.38f, 0.61f, 0.92f, 1.0f);                 // #61AFEF

    // 控件特定颜色
    public static readonly Color4 ButtonDefault = new(0.2f, 0.2f, 0.2f, 1.0f);           // #333333
    public static readonly Color4 ButtonHover = new(0.25f, 0.25f, 0.25f, 1.0f);          // #404040
    public static readonly Color4 ButtonPressed = new(0.15f, 0.15f, 0.15f, 1.0f);        // #262626

    public static readonly Color4 InputBackground = new(0.1f, 0.1f, 0.1f, 1.0f);         // #1A1A1A
    public static readonly Color4 InputBorder = new(0.3f, 0.3f, 0.3f, 1.0f);             // #4D4D4D
    public static readonly Color4 InputFocus = new(0.0f, 0.478f, 0.8f, 1.0f);            // #007ACC

    public static readonly Color4 ScrollbarTrack = new(0.15f, 0.15f, 0.15f, 1.0f);       // #262626
    public static readonly Color4 ScrollbarThumb = new(0.3f, 0.3f, 0.3f, 1.0f);          // #4D4D4D
    public static readonly Color4 ScrollbarThumbHover = new(0.4f, 0.4f, 0.4f, 1.0f);     // #666666

    // 标题栏颜色
    public static readonly Color4 TitleBar = new(0.2f, 0.2f, 0.2f, 1.0f);                // #333333
    public static readonly Color4 TitleBarActive = new(0.0f, 0.478f, 0.8f, 1.0f);        // #007ACC

    // 转换为System.Numerics.Vector4用于ImGui
    public static System.Numerics.Vector4 ToVector4(this Color4 color)
    {
        return new System.Numerics.Vector4(color.R, color.G, color.B, color.A);
    }

    public static uint ToUint(this Color4 color)
    {
        uint r = (uint)(color.R * 255.0f);
        uint g = (uint)(color.G * 255.0f);
        uint b = (uint)(color.B * 255.0f);
        uint a = (uint)(color.A * 255.0f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
