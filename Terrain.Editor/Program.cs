#nullable enable

using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Fonts.Inter;

namespace Terrain.Editor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new DebugBreakTraceListener());
        Trace.AutoFlush = true;
#endif

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }

#if DEBUG
    private sealed class DebugBreakTraceListener : DefaultTraceListener
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
}
