#nullable enable

using Hexa.NET.ImGui;
using System;
using System.Numerics;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI.Panels;

/// <summary>
/// 状态栏面板
/// </summary>
public class StatusBarPanel : PanelBase
{
    #region 属性

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = "Ready";

    /// <summary>
    /// 进度值（0-1，负数表示不显示）
    /// </summary>
    public float Progress { get; set; } = -1.0f;

    /// <summary>
    /// 是否显示进度条
    /// </summary>
    public bool ShowProgress => Progress >= 0;

    /// <summary>
    /// 右侧信息文本
    /// </summary>
    public string RightInfo { get; set; } = "";

    /// <summary>
    /// 坐标信息
    /// </summary>
    public string Coordinates { get; set; } = "";

    #endregion

    #region 私有字段

    private DateTime messageTime = DateTime.Now;
    private string defaultMessage = "Ready";

    #endregion

    #region 构造函数

    public StatusBarPanel()
    {
        Title = "StatusBar";
        ShowTitleBar = false;
    }

    #endregion

    #region 渲染

    protected override void RenderContent()
    {
        var drawList = ImGui.GetWindowDrawList();

        // 背景
        drawList.AddRectFilled(
            Position,
            new Vector2(Position.X + Size.X, Position.Y + Size.Y),
            ColorPalette.DarkBackground.ToUint()
        );

        // 顶部边框
        drawList.AddLine(
            new Vector2(Position.X, Position.Y),
            new Vector2(Position.X + Size.X, Position.Y),
            ColorPalette.Border.ToUint(),
            1.0f
        );

        // 左侧消息
        var messagePos = new Vector2(Position.X + 8, Position.Y + 4);
        drawList.AddText(messagePos, ColorPalette.TextSecondary.ToUint(), Message);

        // 进度条
        if (ShowProgress)
        {
            float progressX = Position.X + 200;
            float progressY = Position.Y + 6;
            float progressWidth = 150;
            float progressHeight = 10;

            // 背景
            drawList.AddRectFilled(
                new Vector2(progressX, progressY),
                new Vector2(progressX + progressWidth, progressY + progressHeight),
                ColorPalette.InputBackground.ToUint(),
                3
            );

            // 进度
            float fillWidth = progressWidth * Math.Clamp(Progress, 0, 1);
            drawList.AddRectFilled(
                new Vector2(progressX, progressY),
                new Vector2(progressX + fillWidth, progressY + progressHeight),
                ColorPalette.Accent.ToUint(),
                3
            );

            // 边框
            drawList.AddRect(
                new Vector2(progressX, progressY),
                new Vector2(progressX + progressWidth, progressY + progressHeight),
                ColorPalette.Border.ToUint(),
                3,
                ImDrawFlags.None,
                1.0f
            );
        }

        // 坐标信息（中间）
        if (!string.IsNullOrEmpty(Coordinates))
        {
            var coordSize = ImGui.CalcTextSize(Coordinates);
            var coordPos = new Vector2(
                Position.X + Size.X * 0.5f - coordSize.X * 0.5f,
                Position.Y + 4
            );
            drawList.AddText(coordPos, ColorPalette.TextSecondary.ToUint(), Coordinates);
        }

        // 右侧信息
        if (!string.IsNullOrEmpty(RightInfo))
        {
            var infoSize = ImGui.CalcTextSize(RightInfo);
            var infoPos = new Vector2(
                Position.X + Size.X - infoSize.X - 8,
                Position.Y + 4
            );
            drawList.AddText(infoPos, ColorPalette.TextSecondary.ToUint(), RightInfo);
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置状态消息
    /// </summary>
    public void SetMessage(string message, float displayDuration = 5.0f)
    {
        Message = message;
        messageTime = DateTime.Now;

        // 可以在这里设置定时器恢复默认消息
    }

    /// <summary>
    /// 清除状态消息（恢复默认）
    /// </summary>
    public void ClearMessage()
    {
        Message = defaultMessage;
        Progress = -1.0f;
    }

    /// <summary>
    /// 设置进度
    /// </summary>
    public void SetProgress(float progress, string? message = null)
    {
        Progress = Math.Clamp(progress, 0, 1);

        if (!string.IsNullOrEmpty(message))
        {
            Message = message;
        }
    }

    /// <summary>
    /// 隐藏进度条
    /// </summary>
    public void HideProgress()
    {
        Progress = -1.0f;
    }

    /// <summary>
    /// 设置坐标信息
    /// </summary>
    public void SetCoordinates(float x, float y, float z)
    {
        Coordinates = $"X: {x:F2} Y: {y:F2} Z: {z:F2}";
    }

    /// <summary>
    /// 设置右侧信息
    /// </summary>
    public void SetRightInfo(string info)
    {
        RightInfo = info;
    }

    #endregion
}
