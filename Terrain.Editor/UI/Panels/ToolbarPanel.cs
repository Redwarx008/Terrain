#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

public class ToolbarPanel : PanelBase
{
    public string? SelectedTool { get; set; }

    public event EventHandler<ToolbarButtonEventArgs>? ButtonClicked;

    public ToolbarPanel()
    {
        Title = "Toolbar";
        ShowTitleBar = false;
    }

    protected override void RenderContent()
    {
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(
            Position,
            new Vector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.PanelBackground.ToUint());

        drawList.AddLine(
            new Vector2(Position.X, Position.Y + Size.Y - 1),
            new Vector2(Position.X + Size.X, Position.Y + Size.Y - 1),
            ColorPalette.Border.ToUint(),
            1.0f);

        ImGui.SetCursorScreenPos(new Vector2(Position.X + 8, Position.Y + 4));

        RenderButton("New", Icons.New, "New Project");
        ImGui.SameLine();
        RenderButton("Open", Icons.Open, "Open Project");
        ImGui.SameLine();
        RenderButton("Save", Icons.Save, "Save");

        ImGui.SameLine(0, 10);
        RenderInlineSeparator();
        ImGui.SameLine(0, 10);

        RenderButton("Undo", Icons.Undo, "Undo");
        ImGui.SameLine();
        RenderButton("Redo", Icons.Redo, "Redo");

        ImGui.SameLine(0, 10);
        RenderInlineSeparator();
        ImGui.SameLine(0, 10);

        RenderButton("Play", Icons.Play, "Play", ButtonStyle.Primary);
        ImGui.SameLine();
        RenderButton("Pause", Icons.Pause, "Pause");
        ImGui.SameLine();
        RenderButton("Stop", Icons.Stop, "Stop", ButtonStyle.Danger);

        ImGui.SameLine(0, 10);
        RenderInlineSeparator();
        ImGui.SameLine(0, 10);

        RenderToolButton("Select", Icons.Cube, "Select Tool");
        ImGui.SameLine();
        RenderToolButton("Move", Icons.ArrowUp, "Move Tool");
        ImGui.SameLine();
        RenderToolButton("Rotate", Icons.Refresh, "Rotate Tool");
        ImGui.SameLine();
        RenderToolButton("Scale", Icons.Expand, "Scale Tool");

        ImGui.SameLine(0, 10);
        RenderInlineSeparator();
        ImGui.SameLine(0, 10);

        RenderToolButton("Height", Icons.Terrain, "Height Brush");
        ImGui.SameLine();
        RenderToolButton("Paint", Icons.Brush, "Paint Brush");
    }

    private void RenderButton(string name, string icon, string tooltip, ButtonStyle style = ButtonStyle.Default)
    {
        PushButtonStyle(style);
        // Toolbar buttons render icon-only labels, so switch to the icon font for
        // the button text and then restore the normal font immediately after.
        FontManager.PushIcons();
        bool pressed = ImGui.Button($"{icon}##{name}", new Vector2(28, 28));
        FontManager.PopFont();

        PopButtonStyle();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (pressed)
        {
            ButtonClicked?.Invoke(this, new ToolbarButtonEventArgs { ButtonName = name });
        }
    }

    private void RenderToolButton(string name, string icon, string tooltip)
    {
        bool isSelected = SelectedTool == name;

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

        // Tool buttons use the same icon-only rendering path as action buttons.
        FontManager.PushIcons();
        bool pressed = ImGui.Button($"{icon}##{name}", new Vector2(28, 28));
        FontManager.PopFont();
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (pressed)
        {
            SelectedTool = name;
            ButtonClicked?.Invoke(this, new ToolbarButtonEventArgs { ButtonName = name });
        }
    }

    private void RenderInlineSeparator()
    {
        Vector2 pos = ImGui.GetCursorScreenPos();
        Vector2 size = new Vector2(8, MathF.Max(28.0f, Size.Y - 8.0f));
        float centerX = pos.X + size.X * 0.5f;

        // Keep layout spacing, but let nearby buttons/tooltips win hover tests.
        ImGui.SetNextItemAllowOverlap();
        ImGui.Dummy(size);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddLine(
            new Vector2(centerX, pos.Y + 4),
            new Vector2(centerX, pos.Y + size.Y - 4),
            ColorPalette.Border.ToUint(),
            1.0f);
    }

    private void PushButtonStyle(ButtonStyle style)
    {
        switch (style)
        {
            case ButtonStyle.Primary:
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Accent.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.AccentHover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.AccentPressed.ToVector4());
                break;
            case ButtonStyle.Danger:
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.Error.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(
                    ColorPalette.Error.R * 1.2f,
                    ColorPalette.Error.G * 1.2f,
                    ColorPalette.Error.B * 1.2f,
                    1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(
                    ColorPalette.Error.R * 0.8f,
                    ColorPalette.Error.G * 0.8f,
                    ColorPalette.Error.B * 0.8f,
                    1.0f));
                break;
            default:
                ImGui.PushStyleColor(ImGuiCol.Button, ColorPalette.ButtonDefault.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorPalette.ButtonHover.ToVector4());
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorPalette.ButtonPressed.ToVector4());
                break;
        }
    }

    private static void PopButtonStyle()
    {
        ImGui.PopStyleColor(3);
    }
}

public enum ButtonStyle
{
    Default,
    Primary,
    Danger
}

public class ToolbarButtonEventArgs : EventArgs
{
    public string ButtonName { get; set; } = "";
}
