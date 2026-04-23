using System;
using System.Diagnostics;
using Stride.Engine;
using Terrain.Editor;

#if DEBUG
Trace.Listeners.Clear();
Trace.Listeners.Add(new DebugBreakTraceListener());
Trace.AutoFlush = true;
#endif

using var game = new EditorGame();
game.Run();

#if DEBUG
file sealed class DebugBreakTraceListener : DefaultTraceListener
{
    public override void Fail(string? message, string? detailMessage)
    {
        string combined = string.IsNullOrWhiteSpace(detailMessage)
            ? message ?? "Debug assertion failed."
            : $"{message}{Environment.NewLine}{detailMessage}";

        if (Debugger.IsAttached)
            Debugger.Break();

        throw new InvalidOperationException(combined);
    }
}
#endif
