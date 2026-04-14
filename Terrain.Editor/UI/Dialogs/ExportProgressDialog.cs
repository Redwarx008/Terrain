#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using System.Threading;
using Terrain.Editor.Services.Export;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Dialogs;

/// <summary>
/// Modal progress dialog for export operations.
/// Thread-safe: progress updates from background threads are safely read on the UI thread.
/// </summary>
public class ExportProgressDialog
{
    private volatile bool isOpen;
    private volatile bool isCancelling;

    // All mutable state protected by this lock for cross-thread visibility
    private readonly object stateLock = new();
    private ExportProgress currentProgress;
    private bool finishedFlag;
    private bool succeededFlag;
    private string? errorMessageField;

    /// <summary>
    /// CancellationTokenSource for cancelling the export.
    /// </summary>
    private CancellationTokenSource? cts;

    /// <summary>
    /// Open the dialog and reset state.
    /// </summary>
    public void Open()
    {
        isOpen = true;
        isCancelling = false;
        cts = new CancellationTokenSource();
        lock (stateLock)
        {
            currentProgress = default;
            finishedFlag = false;
            succeededFlag = false;
            errorMessageField = null;
        }
    }

    /// <summary>
    /// Get the cancellation token for the current export.
    /// </summary>
    public CancellationToken CancellationToken => cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Update progress from the export operation. Thread-safe.
    /// </summary>
    public void UpdateProgress(ExportProgress progress)
    {
        lock (stateLock)
        {
            currentProgress = progress;

            if (progress.IsCompleted && progress.ErrorMessage != null)
            {
                finishedFlag = true;
                succeededFlag = false;
                errorMessageField = progress.ErrorMessage;
            }
            else if (progress.IsCompleted)
            {
                finishedFlag = true;
                succeededFlag = true;
            }
        }
    }

    /// <summary>
    /// Set the final result manually. Thread-safe.
    /// </summary>
    public void SetResult(bool success, string? error = null)
    {
        lock (stateLock)
        {
            finishedFlag = true;
            succeededFlag = success;
            errorMessageField = error;
        }
    }

    /// <summary>
    /// Render the dialog each frame. Returns true while the dialog is still open.
    /// </summary>
    public bool Render()
    {
        if (!isOpen)
            return false;

        // Snapshot state under lock
        ExportProgress progress;
        bool finished;
        bool succeeded;
        string? error;
        lock (stateLock)
        {
            progress = currentProgress;
            finished = finishedFlag;
            succeeded = succeededFlag;
            error = errorMessageField;
        }

        if (!ImGui.IsPopupOpen("Export"))
            ImGui.OpenPopup("Export");

        Vector2 windowSize = ImGui.GetIO().DisplaySize;
        float popupWidth = EditorStyle.ScaleValue(420.0f);
        float popupHeight = EditorStyle.ScaleValue(finished ? 160.0f : 150.0f);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight));
        ImGui.SetNextWindowPos(new Vector2(
            (windowSize.X - popupWidth) * 0.5f,
            (windowSize.Y - popupHeight) * 0.5f));

        bool result = true;

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse;
        if (ImGui.BeginPopupModal("Export", ref isOpen, flags))
        {
            float padding = EditorStyle.ScaleValue(12.0f);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(
                EditorStyle.ScaleValue(6.0f),
                EditorStyle.ScaleValue(6.0f)));

            ImGui.SetCursorPosX(padding);

            if (finished)
            {
                if (succeeded)
                {
                    ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), "Export completed successfully.");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.3f, 0.3f, 1.0f), "Export failed:");
                    ImGui.SetCursorPosX(padding);
                    ImGui.TextWrapped(error ?? "Unknown error");
                }

                ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(8.0f)));
                ImGui.SetCursorPosX(popupWidth - padding - EditorStyle.ScaleValue(80.0f));

                if (ImGui.Button("Close", new Vector2(EditorStyle.ScaleValue(80.0f), 0)))
                {
                    CloseDialog();
                    result = false;
                }
            }
            else
            {
                string message = progress.Message ?? "Preparing...";
                ImGui.Text(message);

                ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(4.0f)));

                float fraction = progress.Total > 0
                    ? (float)progress.Current / progress.Total
                    : 0.0f;

                ImGui.SetCursorPosX(padding);
                float progressBarWidth = popupWidth - padding * 2;
                ImGui.ProgressBar(fraction, new Vector2(progressBarWidth, 0));

                // Cancel button
                ImGui.Dummy(new Vector2(0, EditorStyle.ScaleValue(4.0f)));
                ImGui.SetCursorPosX(popupWidth - padding - EditorStyle.ScaleValue(80.0f));
                if (!isCancelling)
                {
                    if (ImGui.Button("Cancel", new Vector2(EditorStyle.ScaleValue(80.0f), 0)))
                    {
                        isCancelling = true;
                        cts?.Cancel();
                    }
                }
                else
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Cancelling...", new Vector2(EditorStyle.ScaleValue(80.0f), 0));
                    ImGui.EndDisabled();
                }
            }

            ImGui.PopStyleVar();
            ImGui.EndPopup();
        }

        if (!isOpen)
            result = false;

        return result;
    }

    private void CloseDialog()
    {
        isOpen = false;
        ImGui.CloseCurrentPopup();
        cts?.Dispose();
        cts = null;
    }
}