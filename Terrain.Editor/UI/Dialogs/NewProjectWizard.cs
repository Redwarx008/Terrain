#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.Platform;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Dialogs;

/// <summary>
/// 新建项目的模态弹窗向导。
/// </summary>
public class NewProjectWizard
{
    private string projectName = "Untitled";
    private string projectFilePath = "";
    private string heightmapPath = "";
    private string indexMapPath = "";

    private bool isOpen;
    private bool projectFilePicked;
    private bool heightmapPicked;

    /// <summary>
    /// 用户确认创建时触发，传入所有选择的路径。
    /// </summary>
    public event EventHandler<NewProjectEventArgs>? ProjectCreated;

    /// <summary>
    /// 打开弹窗。
    /// </summary>
    public void Open()
    {
        projectName = "Untitled";
        projectFilePath = "";
        heightmapPath = "";
        indexMapPath = "";
        projectFilePicked = false;
        heightmapPicked = false;
        isOpen = true;
    }

    /// <summary>
    /// 每帧渲染弹窗。返回 true 表示弹窗仍然打开。
    /// </summary>
    public bool Render(nint hwnd)
    {
        if (!isOpen)
            return false;

        // 只在弹窗未打开时调用 OpenPopup（避免每帧重复调用）
        if (!ImGui.IsPopupOpen("New Terrain Project"))
            ImGui.OpenPopup("New Terrain Project");

        Vector2 windowSize = ImGui.GetIO().DisplaySize;
        float popupWidth = EditorStyle.ScaleValue(420.0f);
        float popupHeight = EditorStyle.ScaleValue(280.0f);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight));
        ImGui.SetNextWindowPos(new Vector2(
            (windowSize.X - popupWidth) * 0.5f,
            (windowSize.Y - popupHeight) * 0.5f));

        bool result = true;

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse;
        if (ImGui.BeginPopupModal("New Terrain Project", ref isOpen, flags))
        {
            float padding = EditorStyle.ScaleValue(12.0f);
            float itemSpacing = EditorStyle.ScaleValue(8.0f);
            float inputWidth = popupWidth - padding * 2 - itemSpacing - EditorStyle.ScaleValue(60.0f);
            float buttonWidth = EditorStyle.ScaleValue(80.0f);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(itemSpacing, itemSpacing));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, EditorStyle.ScaleValue(4.0f));

            // Project Name
            ImGui.SetCursorPosX(padding);
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Project Name");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##project_name", ref projectName, 128);

            ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(4.0f)));

            // Project File
            ImGui.SetCursorPosX(padding);
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Project File");
            ImGui.SameLine();
            string projectDisplay = projectFilePicked ? System.IO.Path.GetFileName(projectFilePath) : "(none)";
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##project_file", ref projectDisplay, 256, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("...##project_file_browse", new Vector2(EditorStyle.ScaleValue(40.0f), 0)))
            {
                if (FileDialog.ShowSaveDialog(hwnd, "TOML Files (*.toml)|*.toml", "Save Project", projectName + ".toml", out string? path))
                {
                    projectFilePath = path;
                    projectFilePicked = true;
                    if (projectName == "Untitled")
                    {
                        projectName = System.IO.Path.GetFileNameWithoutExtension(path);
                    }
                }
            }

            ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(4.0f)));

            // Heightmap
            ImGui.SetCursorPosX(padding);
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Heightmap");
            ImGui.SameLine();
            string heightmapDisplay = heightmapPicked ? System.IO.Path.GetFileName(heightmapPath) : "(required)";
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##heightmap", ref heightmapDisplay, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("...##heightmap_browse", new Vector2(EditorStyle.ScaleValue(40.0f), 0)))
            {
                if (FileDialog.ShowOpenDialog(hwnd, "PNG Files (*.png)|*.png", "Select Heightmap", out string? path))
                {
                    heightmapPath = path;
                    heightmapPicked = true;
                }
            }

            ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(4.0f)));

            // Index Map (optional)
            ImGui.SetCursorPosX(padding);
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Index Map");
            ImGui.SameLine();
            string indexDisplay = !string.IsNullOrEmpty(indexMapPath) ? System.IO.Path.GetFileName(indexMapPath) : "(optional)";
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##indexmap", ref indexDisplay, 260, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            if (ImGui.Button("...##indexmap_browse", new Vector2(EditorStyle.ScaleValue(40.0f), 0)))
            {
                if (FileDialog.ShowOpenDialog(hwnd, "PNG Files (*.png)|*.png", "Select Index Map", out string? path))
                {
                    indexMapPath = path;
                }
            }

            ImGui.SameLine();
            if (!string.IsNullOrEmpty(indexMapPath))
            {
                if (ImGui.Button("X##indexmap_clear", new Vector2(EditorStyle.ScaleValue(20.0f), 0)))
                {
                    indexMapPath = "";
                }
            }

            // 分隔线
            ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(12.0f)));
            ImGui.SetCursorPosX(padding);
            float lineWidth = popupWidth - padding * 2;
            Vector2 linePos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(
                linePos,
                new Vector2(linePos.X + lineWidth, linePos.Y),
                ColorPalette.Border.ToUint());
            ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(8.0f)));

            // 按钮
            float buttonsWidth = buttonWidth * 2 + itemSpacing;
            ImGui.SetCursorPosX(popupWidth - padding - buttonsWidth);

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                isOpen = false;
                ImGui.CloseCurrentPopup();
                result = false;
            }

            ImGui.SameLine();

            bool canCreate = projectFilePicked && heightmapPicked && !string.IsNullOrEmpty(projectName);
            if (!canCreate)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Create", new Vector2(buttonWidth, 0)))
            {
                ProjectCreated?.Invoke(this, new NewProjectEventArgs
                {
                    ProjectFilePath = projectFilePath,
                    ProjectName = projectName,
                    HeightmapPath = heightmapPath,
                    IndexMapPath = string.IsNullOrEmpty(indexMapPath) ? null : indexMapPath,
                });
                isOpen = false;
                ImGui.CloseCurrentPopup();
                result = false;
            }

            if (!canCreate)
            {
                ImGui.EndDisabled();
            }

            ImGui.PopStyleVar(2);
            ImGui.EndPopup();
        }

        if (!isOpen)
            result = false;

        return result;
    }
}

public class NewProjectEventArgs : EventArgs
{
    public required string ProjectFilePath { get; init; }
    public required string ProjectName { get; init; }
    public required string HeightmapPath { get; init; }
    public string? IndexMapPath { get; init; }
}
