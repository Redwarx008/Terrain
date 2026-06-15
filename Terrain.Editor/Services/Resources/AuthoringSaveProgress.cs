#nullable enable

namespace Terrain.Editor.Services.Resources;

public struct AuthoringSaveProgress
{
    public int Current;
    public int Total;
    public string Message;
    public bool IsCompleted;
    public string? ErrorMessage;

    public static AuthoringSaveProgress Running(int current, int total, string message)
    {
        return new AuthoringSaveProgress
        {
            Current = current,
            Total = total,
            Message = message,
            IsCompleted = false,
            ErrorMessage = null,
        };
    }

    public static AuthoringSaveProgress Completed(int current, int total, string message = "Save completed.")
    {
        return new AuthoringSaveProgress
        {
            Current = current,
            Total = total,
            Message = message,
            IsCompleted = true,
            ErrorMessage = null,
        };
    }

    public static AuthoringSaveProgress Failed(int current, int total, string error)
    {
        return new AuthoringSaveProgress
        {
            Current = current,
            Total = total,
            Message = error,
            IsCompleted = false,
            ErrorMessage = error,
        };
    }
}
