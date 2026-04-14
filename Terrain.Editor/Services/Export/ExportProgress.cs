#nullable enable

namespace Terrain.Editor.Services.Export;

/// <summary>
/// Progress report for export operations.
/// </summary>
public struct ExportProgress
{
    public int Current;
    public int Total;
    public string Message;
    public bool IsCompleted;
    public string? ErrorMessage;

    public static ExportProgress Running(int current, int total, string message) => new()
    {
        Current = current,
        Total = total,
        Message = message,
        IsCompleted = false,
        ErrorMessage = null,
    };

    public static ExportProgress Completed() => new()
    {
        IsCompleted = true,
        Message = "Export completed",
    };

    public static ExportProgress Failed(string error) => new()
    {
        IsCompleted = true,
        ErrorMessage = error,
    };
}