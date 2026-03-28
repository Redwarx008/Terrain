#nullable enable

using Hexa.NET.ImGui;
using System.Runtime.InteropServices;

namespace Terrain.Editor.UI.Styling;

public static class FontManager
{
    private static bool initialized;
    private static readonly uint[] iconGlyphRangeData = { 0xE700, 0xF8FF, 0 };
    private static readonly GCHandle iconGlyphRangeHandle = GCHandle.Alloc(iconGlyphRangeData, GCHandleType.Pinned);
    private static float currentScale = 1.0f;

    public static ImFontPtr Regular { get; private set; }
    public static ImFontPtr Bold { get; private set; }
    public static ImFontPtr Italic { get; private set; }
    public static ImFontPtr Monospace { get; private set; }
    public static ImFontPtr Icons { get; private set; }

    public const float RegularSize = 14.0f;
    public const float SmallSize = 12.0f;
    public const float LargeSize = 16.0f;
    public const float IconSize = 14.0f;
    public static float CurrentScale => currentScale;
    public static float ScaledIconSize => IconSize * currentScale;

    public static void Initialize(ImFontPtr defaultFont)
    {
        Regular = defaultFont;
        Bold = defaultFont;
        Italic = defaultFont;
        Monospace = defaultFont;
        Icons = defaultFont;
        initialized = true;
    }

    public static void UpdateScale(float scale)
    {
        currentScale = scale;
    }

    public static void SetIconFont(ImFontPtr iconFont)
    {
        // Icon rendering falls back to the default font until a dedicated icon
        // face is loaded successfully at runtime.
        Icons = iconFont;
    }

    public static void LoadCustomFonts()
    {
        if (!initialized)
        {
            return;
        }
    }

    public static void PushBold()
    {
        ImGui.PushFont(Bold);
    }

    public static void PushItalic()
    {
        ImGui.PushFont(Italic);
    }

    public static void PushIcons()
    {
        ImGui.PushFont(Icons);
    }

    public static void PopFont()
    {
        ImGui.PopFont();
    }

    public static unsafe uint* GetIconGlyphRanges()
    {
        return (uint*)iconGlyphRangeHandle.AddrOfPinnedObject();
    }
}

public static class Icons
{
    public const string Search = "\uE721";
    public const string Home = "\uE80F";
    public const string Settings = "\uE713";
    public const string Save = "\uE74E";
    public const string Open = "\uE8E5";
    public const string New = "\uE710";
    public const string Delete = "\uE74D";
    public const string Edit = "\uE70F";
    public const string Refresh = "\uE72C";
    public const string Undo = "\uE7A7";
    public const string Redo = "\uE7A6";
    public const string Copy = "\uE8C8";
    public const string Paste = "\uE77F";
    public const string Cut = "\uE8C6";

    public const string Eye = "\uE8F4";
    public const string EyeSlash = "\uE890";
    public const string Expand = "\uE740";
    public const string Compress = "\uE73F";
    public const string Maximize = "\uE922";
    public const string Minimize = "\uE921";
    public const string Restore = "\uE923";

    public const string ArrowLeft = "\uE72B";
    public const string ArrowRight = "\uE72A";
    public const string ArrowUp = "\uE74A";
    public const string ArrowDown = "\uE74B";
    public const string ChevronLeft = "\uE76B";
    public const string ChevronRight = "\uE76C";
    public const string ChevronUp = "\uE70E";
    public const string ChevronDown = "\uE70D";

    public const string Cube = "\uE7B8";
    public const string Camera = "\uE722";
    public const string Light = "\uE793";
    public const string Terrain = "\uE81E";
    public const string Brush = "\uE76D";
    public const string Paint = "\uE790";
    public const string Grid = "\uE71D";
    public const string Wireframe = "\uE81E";

    public const string Play = "\uE768";
    public const string Pause = "\uE769";
    public const string Stop = "\uE71A";
    public const string StepForward = "\uE893";
    public const string StepBackward = "\uE892";

    public const string Check = "\uE73E";
    public const string Times = "\uE711";
    public const string Info = "\uE946";
    public const string Warning = "\uE7BA";
    public const string Error = "\uEA39";
    public const string Question = "\uE897";

    public const string Folder = "\uE8B7";
    public const string FolderOpen = "\uE838";
    public const string File = "\uE7C3";
    public const string FileImage = "\uEB9F";

    // Additional icons
    public const string Plus = "\uE710";
    public const string Trash = "\uE74D";
    public const string Tree = "\uE8D0";
    public const string Water = "\uE7C1";
    public const string Layer = "\uE7F5";
    public const string Circle = "\uE9EA";
    public const string Square = "\uE9EB";
    public const string Noise = "\uE7F6";
    public const string Tools = "\uE90F";
    public const string Lock = "\uE72E";
    public const string Unlock = "\uE785";
    public const string Eraser = "\uE75C";
    public const string EyeOff = "\uE890";
}
