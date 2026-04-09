#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? Context { get; set; }
}

public class ConsolePanel : PanelBase
{
    public List<LogEntry> LogEntries { get; } = new();
    public int MaxEntries { get; set; } = 1000;
    public bool ShowTimestamp { get; set; } = true;
    public new bool AutoScroll { get; set; } = true;
    public LogLevel FilterLevel { get; set; } = LogLevel.Debug;
    public string SearchFilter { get; set; } = "";

    private string commandBuffer = "";
    private readonly List<string> commandHistory = new();
    private int historyIndex = -1;
    private bool scrollToBottom;

    public event EventHandler<ConsoleCommandEventArgs>? CommandSubmitted;
    public event EventHandler<LogEntryEventArgs>? LogEntryDoubleClicked;

    public ConsolePanel()
    {
        Title = "Console";
        Icon = null;
        ShowTitleBar = true;
    }

    protected override void RenderContent()
    {
        RenderToolbar();
        RenderLogList();
        RenderCommandInput();
    }

    private static float GetToolbarHeight()
    {
        return MathF.Max(EditorStyle.ScaleValue(28.0f), EditorStyle.ButtonHeightScaled + EditorStyle.ScaleValue(8.0f));
    }

    private static float GetInputBarHeight()
    {
        return MathF.Max(EditorStyle.ScaleValue(30.0f), EditorStyle.InputHeightScaled + EditorStyle.ScaleValue(8.0f));
    }

    private void RenderToolbar()
    {
        float toolbarHeight = GetToolbarHeight();
        float horizontalPadding = EditorStyle.ScaleValue(8.0f);
        float verticalPadding = EditorStyle.ScaleValue(4.0f);
        float searchWidth = EditorStyle.ScaleValue(140.0f);

        var drawList = ImGui.GetWindowDrawList();
        Vector2 toolbarPos = new(ContentRect.X, ContentRect.Y);
        Vector2 toolbarEnd = new(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        ImGui.SetCursorScreenPos(new Vector2(toolbarPos.X + horizontalPadding, toolbarPos.Y + verticalPadding));
        if (ImGui.Button("Clear", new Vector2(EditorStyle.ScaleValue(50.0f), EditorStyle.ButtonHeightScaled)))
        {
            Clear();
        }

        ImGui.SameLine();

        bool autoScroll = AutoScroll;
        if (ImGui.Checkbox("Auto-scroll", ref autoScroll))
        {
            AutoScroll = autoScroll;
        }

        ImGui.SameLine();

        bool showTimestamp = ShowTimestamp;
        if (ImGui.Checkbox("Timestamp", ref showTimestamp))
        {
            ShowTimestamp = showTimestamp;
        }

        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        ImGui.Text("Filter:");
        ImGui.SameLine();

        string[] levels = { "Debug", "Info", "Warning", "Error", "Fatal" };
        int currentLevel = (int)FilterLevel;
        ImGui.SetNextItemWidth(EditorStyle.ScaleValue(80.0f));
        if (ImGui.Combo($"##filter_{Id}", ref currentLevel, levels, levels.Length))
        {
            FilterLevel = (LogLevel)currentLevel;
        }

        ImGui.SetCursorScreenPos(new Vector2(toolbarEnd.X - searchWidth - horizontalPadding, toolbarPos.Y + verticalPadding));
        ImGui.SetNextItemWidth(searchWidth);

        string searchBuffer = SearchFilter;
        bool searchChanged = false;
        TextInputStyle.Render(() =>
        {
            searchChanged = ImGui.InputTextWithHint($"##search_{Id}", "Search...", ref searchBuffer, 256);
        });
        if (searchChanged)
        {
            SearchFilter = searchBuffer;
        }

        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f);
    }

    private void RenderLogList()
    {
        float toolbarHeight = GetToolbarHeight();
        float inputHeight = GetInputBarHeight();
        float listHeight = MathF.Max(0.0f, ContentRect.Height - toolbarHeight - inputHeight);

        Vector2 listPos = new(ContentRect.X, ContentRect.Y + toolbarHeight);
        ImGui.SetCursorScreenPos(listPos);
        ImGui.BeginChild(
            $"##log_list_{Id}",
            new Vector2(ContentRect.Width, listHeight),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        int index = 0;
        foreach (var entry in LogEntries)
        {
            if (entry.Level < FilterLevel)
                continue;

            if (!string.IsNullOrEmpty(SearchFilter) &&
                !entry.Message.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            RenderLogEntry(entry, index);
            index++;
        }

        if (AutoScroll && scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            scrollToBottom = false;
        }

        ImGui.EndChild();
    }

    private void RenderLogEntry(LogEntry entry, int index)
    {
        uint levelColor = GetLogLevelColor(entry.Level);
        string levelIcon = GetLogLevelIcon(entry.Level);
        string displayText = ShowTimestamp
            ? $"[{entry.Timestamp:HH:mm:ss}] {levelIcon} {entry.Message}"
            : $"{levelIcon} {entry.Message}";

        ImGui.PushStyleColor(ImGuiCol.Text, levelColor);

        if (entry.Message.Contains('\n'))
        {
            var lines = entry.Message.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = i == 0 ? $"{levelIcon} {lines[i]}" : $"    {lines[i]}";
                ImGui.Text(line);
            }
        }
        else
        {
            ImGui.Text(displayText);
        }

        ImGui.PopStyleColor();

        if (ImGui.BeginPopupContextItem($"##log_context_{index}"))
        {
            if (ImGui.MenuItem("Copy"))
            {
                ImGui.SetClipboardText(entry.Message);
            }

            if (ImGui.MenuItem("Copy with Stack Trace") && !string.IsNullOrEmpty(entry.StackTrace))
            {
                ImGui.SetClipboardText($"{entry.Message}\n\n{entry.StackTrace}");
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Select Similar"))
            {
                SearchFilter = entry.Message.Split(':')[0];
            }

            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            LogEntryDoubleClicked?.Invoke(this, new LogEntryEventArgs { Entry = entry });
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(entry.StackTrace))
        {
            ImGui.SetTooltip(entry.StackTrace);
        }
    }

    private void RenderCommandInput()
    {
        float toolbarHeight = GetToolbarHeight();
        float inputHeight = GetInputBarHeight();
        float listHeight = MathF.Max(0.0f, ContentRect.Height - toolbarHeight - inputHeight);
        float promptOffsetX = EditorStyle.ScaleValue(8.0f);
        float promptWidth = EditorStyle.ScaleValue(12.0f);
        float inputPaddingY = EditorStyle.ScaleValue(4.0f);

        Vector2 inputPos = new(ContentRect.X, ContentRect.Y + toolbarHeight + listHeight);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            inputPos,
            new Vector2(inputPos.X + ContentRect.Width, inputPos.Y + inputHeight),
            ColorPalette.DarkBackground.ToUint());

        drawList.AddLine(
            inputPos,
            new Vector2(inputPos.X + ContentRect.Width, inputPos.Y),
            ColorPalette.Border.ToUint(),
            1.0f);

        Vector2 promptPos = new(inputPos.X + promptOffsetX, inputPos.Y + inputPaddingY + EditorStyle.ScaleValue(2.0f));
        drawList.AddText(promptPos, ColorPalette.Accent.ToUint(), ">");

        ImGui.SetCursorScreenPos(new Vector2(inputPos.X + promptOffsetX + promptWidth, inputPos.Y + inputPaddingY));
        ImGui.SetNextItemWidth(MathF.Max(0.0f, ContentRect.Width - promptOffsetX - promptWidth - EditorStyle.ScaleValue(8.0f)));

        ImGuiInputTextFlags flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory;

        bool submitted = false;
        TextInputStyle.Render(() =>
        {
            submitted = ImGui.InputText($"##command_{Id}", ref commandBuffer, 256, flags);
        });

        if (submitted)
        {
            SubmitCommand();
        }
    }

    private uint GetLogLevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => ColorPalette.TextSecondary.ToUint(),
            LogLevel.Info => ColorPalette.TextPrimary.ToUint(),
            LogLevel.Warning => ColorPalette.Warning.ToUint(),
            LogLevel.Error => ColorPalette.Error.ToUint(),
            LogLevel.Fatal => ColorPalette.Error.ToUint(),
            _ => ColorPalette.TextPrimary.ToUint()
        };
    }

    private string GetLogLevelIcon(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => "[DBG]",
            LogLevel.Info => "[INF]",
            LogLevel.Warning => "[WRN]",
            LogLevel.Error => "[ERR]",
            LogLevel.Fatal => "[FTL]",
            _ => "[???]"
        };
    }

    private void SubmitCommand()
    {
        if (string.IsNullOrWhiteSpace(commandBuffer))
            return;

        string command = commandBuffer.Trim();
        commandHistory.Add(command);
        if (commandHistory.Count > 50)
            commandHistory.RemoveAt(0);

        historyIndex = commandHistory.Count;
        CommandSubmitted?.Invoke(this, new ConsoleCommandEventArgs { Command = command });
        commandBuffer = "";
    }

    public void Log(string message, LogLevel level = LogLevel.Info, string? stackTrace = null, string? context = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            StackTrace = stackTrace,
            Context = context
        };

        LogEntries.Add(entry);
        if (LogEntries.Count > MaxEntries)
        {
            LogEntries.RemoveAt(0);
        }

        scrollToBottom = true;
    }

    public void LogDebug(string message) => Log(message, LogLevel.Debug);
    public void LogInfo(string message) => Log(message, LogLevel.Info);
    public void LogWarning(string message) => Log(message, LogLevel.Warning);
    public void LogError(string message, string? stackTrace = null) => Log(message, LogLevel.Error, stackTrace);

    public void Clear()
    {
        LogEntries.Clear();
    }
}

public class ConsoleCommandEventArgs : EventArgs
{
    public string Command { get; set; } = "";
}

public class LogEntryEventArgs : EventArgs
{
    public LogEntry Entry { get; set; } = null!;
}
