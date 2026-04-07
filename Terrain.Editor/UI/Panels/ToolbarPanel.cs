#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

public class ToolbarPanel : PanelBase
{
    public EditorMode CurrentMode { get; set; } = EditorMode.Sculpt;

    public event EventHandler<ToolbarButtonEventArgs>? ButtonClicked;
    public event EventHandler<EditorMode>? ModeChanged;

    /// <summary>
    /// 程序化切换编辑模式。
    /// </summary>
    public void SetMode(EditorMode mode)
    {
        if (CurrentMode != mode)
        {
            CurrentMode = mode;
            ModeChanged?.Invoke(this, mode);
        }
    }

    public ToolbarPanel()
    {
        Title = "Toolbar";
        ShowTitleBar = false;
    }

    protected override void RenderContent()
    {
        var drawList = ImGui.GetWindowDrawList();
        float outerPaddingX = EditorStyle.ScaleValue(8.0f);
        float outerPaddingY = EditorStyle.ScaleValue(4.0f);
        float sectionSpacing = EditorStyle.ScaleValue(10.0f);

        drawList.AddRectFilled(
            Position,
            new Vector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.PanelBackground.ToUint());

        drawList.AddLine(
            new Vector2(Position.X, Position.Y + Size.Y - 1),
            new Vector2(Position.X + Size.X, Position.Y + Size.Y - 1),
            ColorPalette.Border.ToUint(),
            1.0f);

        ImGui.SetCursorScreenPos(new Vector2(Position.X + outerPaddingX, Position.Y + outerPaddingY));

        // File operations
        RenderButton("New", Icons.New, "New Project");
        ImGui.SameLine();
        RenderButton("Open", Icons.Open, "Open Project");
        ImGui.SameLine();
        RenderButton("Save", Icons.Save, "Save");

        // Separator
        ImGui.SameLine(0, sectionSpacing);
        RenderInlineSeparator();
        ImGui.SameLine(0, sectionSpacing);

        // Edit operations
        RenderButton("Undo", Icons.Undo, "Undo");
        ImGui.SameLine();
        RenderButton("Redo", Icons.Redo, "Redo");

        // Separator
        ImGui.SameLine(0, sectionSpacing);
        RenderInlineSeparator();
        ImGui.SameLine(0, sectionSpacing);

        // Mode selection
        RenderModeButton(EditorMode.Sculpt, Icons.Terrain, "Sculpt Mode");
        ImGui.SameLine();
        RenderModeButton(EditorMode.Paint, Icons.Brush, "Paint Mode");
        ImGui.SameLine();
        RenderModeButton(EditorMode.Foliage, Icons.Tree, "Foliage Mode");
    }

    private void RenderButton(string name, string icon, string tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());

        FontManager.PushIcons();
        bool pressed = ImGui.Button($"{icon}##{name}", new Vector2(EditorStyle.ScaleValue(28.0f), EditorStyle.ScaleValue(28.0f)));
        FontManager.PopFont();

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (pressed)
        {
            ButtonClicked?.Invoke(this, new ToolbarButtonEventArgs { ButtonName = name });
        }
    }

    private void RenderModeButton(EditorMode mode, string icon, string tooltip)
    {
        bool isSelected = CurrentMode == mode;

        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.Hover.ToVector4());
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.Pressed.ToVector4());
        }

        FontManager.PushIcons();
        bool pressed = ImGui.Button($"{icon}##{mode}", new Vector2(EditorStyle.ScaleValue(28.0f), EditorStyle.ScaleValue(28.0f)));
        FontManager.PopFont();
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (pressed)
        {
            CurrentMode = mode;
            ModeChanged?.Invoke(this, mode);
            ButtonClicked?.Invoke(this, new ToolbarButtonEventArgs { ButtonName = mode.ToString() });
        }
    }

    private void RenderInlineSeparator()
    {
        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new Vector2(EditorStyle.ScaleValue(8.0f), MathF.Max(EditorStyle.ScaleValue(28.0f), Size.Y - EditorStyle.ScaleValue(8.0f)));
        float centerX = pos.X + size.X * 0.5f;

        ImGui.SetNextItemAllowOverlap();
        ImGui.Dummy(size);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(
            new Vector2(centerX, pos.Y + EditorStyle.ScaleValue(4.0f)),
            new Vector2(centerX, pos.Y + size.Y - EditorStyle.ScaleValue(4.0f)),
            ColorPalette.Border.ToUint(),
            1.0f);
    }
}

public class ToolbarButtonEventArgs : EventArgs
{
    public string ButtonName { get; set; } = "";
}
