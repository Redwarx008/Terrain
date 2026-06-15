#nullable enable

namespace Terrain.Editor.Services.Resources;

public readonly struct AuthoringSaveProgress
{
    public const int TotalSteps = 9;

    private readonly string? message;

    public int Current { get; }
    public int Total { get; }
    public string Message => message ?? string.Empty;
    public bool IsCompleted { get; }
    public string? ErrorMessage { get; }

    private AuthoringSaveProgress(
        int current,
        int total,
        string? message,
        bool isCompleted,
        string? errorMessage)
    {
        Current = current;
        Total = total;
        this.message = message;
        IsCompleted = isCompleted;
        ErrorMessage = errorMessage;
    }

    public static AuthoringSaveProgress Running(int current, int total, string message)
    {
        return new AuthoringSaveProgress(current, total, message, isCompleted: false, errorMessage: null);
    }

    public static AuthoringSaveProgress Completed(int current, int total, string message = "Save completed.")
    {
        return new AuthoringSaveProgress(current, total, message, isCompleted: true, errorMessage: null);
    }

    public static AuthoringSaveProgress Failed(int current, int total, string error)
    {
        return new AuthoringSaveProgress(current, total, error, isCompleted: true, errorMessage: error);
    }
}
