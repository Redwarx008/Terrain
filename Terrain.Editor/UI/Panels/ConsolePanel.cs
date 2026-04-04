#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? Context { get; set; }
}

/// <summary>
/// 控制台面板 - 显示日志和命令输入
/// </summary>
public class ConsolePanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 日志条目列表
    /// </summary>
    public List<LogEntry> LogEntries { get; } = new();

    /// <summary>
    /// 最大日志条目数
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// 是否显示时间戳
    /// </summary>
    public bool ShowTimestamp { get; set; } = true;

    /// <summary>
    /// 是否自动滚动
    /// </summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// 当前筛选级别
    /// </summary>
    public LogLevel FilterLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 搜索过滤文本
    /// </summary>
    public string SearchFilter { get; set; } = "";

    #endregion

    #region 私有字段

    private string commandBuffer = "";
    private List<string> commandHistory = new();
    private int historyIndex = -1;
    private bool scrollToBottom = false;

    #endregion

    #region 事件

    /// <summary>
    /// 命令提交事件
    /// </summary>
    public event EventHandler<ConsoleCommandEventArgs>? CommandSubmitted;

    /// <summary>
    /// 日志条目双击事件
    /// </summary>
    public event EventHandler<LogEntryEventArgs>? LogEntryDoubleClicked;

    #endregion

    #region 构造函数

    public ConsolePanel()
    {
        Title = "Console";
        Icon = Icons.Info;
        ShowTitleBar = true;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        // 渲染工具栏
        RenderToolbar();

        // 渲染日志列表
        RenderLogList();

        // 渲染命令输入
        RenderCommandInput();
    }

    private void RenderToolbar()
    {
        float toolbarHeight = 28;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 toolbarPos = new Vector2(ContentRect.X, ContentRect.Y);
        Vector2 toolbarEnd = new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight);
        drawList.AddRectFilled(toolbarPos, toolbarEnd, ColorPalette.DarkBackground.ToUint());

        // 清除按钮
        ImGui.SetCursorScreenPos(new Vector2(toolbarPos.X + 8, toolbarPos.Y + 4));
        if (ImGui.Button("Clear", new Vector2(50, 20)))
        {
            Clear();
        }

        ImGui.SameLine();

        // 自动滚动开关
        bool autoScroll = AutoScroll;
        if (ImGui.Checkbox("Auto-scroll", ref autoScroll))
        {
            AutoScroll = autoScroll;
        }

        ImGui.SameLine();

        // 时间戳开关
        bool showTimestamp = ShowTimestamp;
        if (ImGui.Checkbox("Timestamp", ref showTimestamp))
        {
            ShowTimestamp = showTimestamp;
        }

        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();

        // 级别筛选
        ImGui.Text("Filter:");
        ImGui.SameLine();

        string[] levels = { "Debug", "Info", "Warning", "Error", "Fatal" };
        int currentLevel = (int)FilterLevel;
        ImGui.SetNextItemWidth(80);
        if (ImGui.Combo($"##filter_{Id}", ref currentLevel, levels, levels.Length))
        {
            FilterLevel = (LogLevel)currentLevel;
        }

        ImGui.SameLine();

        // 搜索框
        ImGui.SetCursorScreenPos(new Vector2(toolbarEnd.X - 150, toolbarPos.Y + 4));
        ImGui.SetNextItemWidth(140);
        string searchBuffer = SearchFilter;
        if (ImGui.InputTextWithHint($"##search_{Id}", "Search...", ref searchBuffer, 256))
        {
            SearchFilter = searchBuffer;
        }

        // 底部边框
        drawList.AddLine(
            new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight),
            new Vector2(ContentRect.X + ContentRect.Width, ContentRect.Y + toolbarHeight),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void RenderLogList()
    {
        float toolbarHeight = 28;
        float inputHeight = 30;
        float listHeight = ContentRect.Height - toolbarHeight - inputHeight;

        Vector2 listPos = new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight);

        // 开始滚动区域
        ImGui.SetCursorScreenPos(listPos);
        ImGui.BeginChild($"##log_list_{Id}", new Vector2(ContentRect.Width, listHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        // 渲染日志条目
        int index = 0;
        foreach (var entry in LogEntries)
        {
            // 级别过滤
            if (entry.Level < FilterLevel)
                continue;

            // 搜索过滤
            if (!string.IsNullOrEmpty(SearchFilter) &&
                !entry.Message.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            RenderLogEntry(entry, index);
            index++;
        }

        // 自动滚动
        if (AutoScroll && scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            scrollToBottom = false;
        }

        ImGui.EndChild();
    }

    private void RenderLogEntry(LogEntry entry, int index)
    {
        // 获取级别颜色
        uint levelColor = GetLogLevelColor(entry.Level);
        string levelIcon = GetLogLevelIcon(entry.Level);

        // 构建显示文本
        string displayText;
        if (ShowTimestamp)
        {
            displayText = $"[{entry.Timestamp:HH:mm:ss}] {levelIcon} {entry.Message}";
        }
        else
        {
            displayText = $"{levelIcon} {entry.Message}";
        }

        // 渲染条目
        ImGui.PushStyleColor(ImGuiCol.Text, levelColor);

        // 多行文本需要特殊处理
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

        // 右键菜单
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

        // 双击显示详情
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            LogEntryDoubleClicked?.Invoke(this, new LogEntryEventArgs { Entry = entry });
        }

        // 工具提示
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(entry.StackTrace))
        {
            ImGui.SetTooltip(entry.StackTrace);
        }
    }

    private void RenderCommandInput()
    {
        float toolbarHeight = 28;
        float listHeight = ContentRect.Height - toolbarHeight - 30;

        Vector2 inputPos = new Vector2(ContentRect.X, ContentRect.Y + toolbarHeight + listHeight);

        var drawList = ImGui.GetWindowDrawList();

        // 输入框背景
        drawList.AddRectFilled(
            inputPos,
            new Vector2(inputPos.X + ContentRect.Width, inputPos.Y + 30),
            ColorPalette.DarkBackground.ToUint()
        );

        // 顶部边框
        drawList.AddLine(
            inputPos,
            new Vector2(inputPos.X + ContentRect.Width, inputPos.Y),
            ColorPalette.Border.ToUint(),
            1.0f
        );

        // 提示符
        var promptPos = new Vector2(inputPos.X + 8, inputPos.Y + 6);
        drawList.AddText(promptPos, ColorPalette.Accent.ToUint(), ">");

        // 输入框
        ImGui.SetCursorScreenPos(new Vector2(inputPos.X + 20, inputPos.Y + 4));
        ImGui.SetNextItemWidth(ContentRect.Width - 28);

        // 捕获Enter键
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory;

        if (ImGui.InputText($"##command_{Id}", ref commandBuffer, 256, flags))
        {
            SubmitCommand();
        }

        // 历史记录导航
        // TODO: 实现上下键历史记录导航
    }

    #endregion

    #region 辅助方法

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

        // 添加到历史记录
        commandHistory.Add(command);
        if (commandHistory.Count > 50)
            commandHistory.RemoveAt(0);
        historyIndex = commandHistory.Count;

        // 触发事件
        CommandSubmitted?.Invoke(this, new ConsoleCommandEventArgs { Command = command });

        // 清空输入
        commandBuffer = "";
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 添加日志条目
    /// </summary>
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

        // 限制条目数
        if (LogEntries.Count > MaxEntries)
        {
            LogEntries.RemoveAt(0);
        }

        // 标记需要滚动
        scrollToBottom = true;
    }

    /// <summary>
    /// 添加调试日志
    /// </summary>
    public void LogDebug(string message) => Log(message, LogLevel.Debug);

    /// <summary>
    /// 添加信息日志
    /// </summary>
    public void LogInfo(string message) => Log(message, LogLevel.Info);

    /// <summary>
    /// 添加警告日志
    /// </summary>
    public void LogWarning(string message) => Log(message, LogLevel.Warning);

    /// <summary>
    /// 添加错误日志
    /// </summary>
    public void LogError(string message, string? stackTrace = null) => Log(message, LogLevel.Error, stackTrace);

    /// <summary>
    /// 清除所有日志
    /// </summary>
    public void Clear()
    {
        LogEntries.Clear();
    }

    #endregion
}

/// <summary>
/// 控制台命令事件参数
/// </summary>
public class ConsoleCommandEventArgs : EventArgs
{
    public string Command { get; set; } = "";
}

/// <summary>
/// 日志条目事件参数
/// </summary>
public class LogEntryEventArgs : EventArgs
{
    public LogEntry Entry { get; set; } = null!;
}
