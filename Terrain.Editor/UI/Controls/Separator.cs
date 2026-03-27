#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Controls;

public enum SeparatorOrientation
{
    Horizontal,
    Vertical
}

public class Separator : ControlBase
{
    public SeparatorOrientation Orientation { get; set; } = SeparatorOrientation.Horizontal;
    public float Thickness { get; set; } = 1.0f;
    public float Length { get; set; }
    public string? Label { get; set; }
    public TextAlignment LabelAlignment { get; set; } = TextAlignment.Center;

    protected override void OnRender()
    {
        if (!string.IsNullOrEmpty(Label) && Orientation == SeparatorOrientation.Horizontal)
        {
            RenderLabeledSeparator();
        }
        else
        {
            RenderSimpleSeparator();
        }
    }

    private void RenderSimpleSeparator()
    {
        var drawList = ImGui.GetWindowDrawList();

        if (Orientation == SeparatorOrientation.Horizontal)
        {
            float width = Length > 0 ? Length : Size.X;
            float startX = Position.X + Math.Max(0.0f, Size.X - width) * 0.5f;
            float y = Position.Y + Size.Y * 0.5f;
            drawList.AddLine(
                new Vector2(startX, y),
                new Vector2(startX + width, y),
                ColorPalette.Border.ToUint(),
                Thickness);
        }
        else
        {
            float height = Length > 0 ? Length : Size.Y;
            float x = Position.X + Size.X * 0.5f;
            float startY = Position.Y + Math.Max(0.0f, Size.Y - height) * 0.5f;
            drawList.AddLine(
                new Vector2(x, startY),
                new Vector2(x, startY + height),
                ColorPalette.Border.ToUint(),
                Thickness);
        }
    }

    private void RenderLabeledSeparator()
    {
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(Label!);
        float spacing = 8.0f;

        float availableWidth = Length > 0 ? Length : Size.X;
        float lineWidth = Math.Max(0.0f, (availableWidth - textSize.X - spacing * 2) * 0.5f);
        float y = Position.Y + Size.Y * 0.5f;

        drawList.AddLine(
            new Vector2(Position.X, y),
            new Vector2(Position.X + lineWidth, y),
            ColorPalette.Border.ToUint(),
            Thickness);

        float textX = Position.X + lineWidth + spacing;
        if (LabelAlignment == TextAlignment.Center)
        {
            textX = Position.X + (availableWidth - textSize.X) * 0.5f;
        }
        else if (LabelAlignment == TextAlignment.Right)
        {
            textX = Position.X + availableWidth - lineWidth - spacing - textSize.X;
        }

        drawList.AddText(
            new Vector2(textX, y - textSize.Y * 0.5f),
            ColorPalette.TextSecondary.ToUint(),
            Label);

        float rightLineStart = textX + textSize.X + spacing;
        drawList.AddLine(
            new Vector2(rightLineStart, y),
            new Vector2(Position.X + availableWidth, y),
            ColorPalette.Border.ToUint(),
            Thickness);
    }

    protected override Vector2 OnMeasure(Vector2 availableSize)
    {
        if (Orientation == SeparatorOrientation.Horizontal)
        {
            float height = Thickness;
            if (!string.IsNullOrEmpty(Label))
            {
                height = Math.Max(height, ImGui.CalcTextSize(Label!).Y);
            }

            float width = Length > 0 ? Length : availableSize.X;
            return new Vector2(width, height);
        }

        float verticalHeight = Length > 0 ? Length : availableSize.Y;
        return new Vector2(Thickness, verticalHeight);
    }
}
