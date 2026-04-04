#nullable enable

using Hexa.NET.ImGui;
using Stride.Core;
using Stride.Games;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Editor.Platform;
using Terrain.Editor.Services;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Layout;
using Terrain.Editor.UI.Panels;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI;

public class MainWindow : ControlBase
{
    // 自绘标题栏的固定尺寸，按钮宽度与 Windows 常见标题栏接近，便于命中。
    private const float TitleBarHeight = 28.0f;
    // 恢复自绘无边框窗口后，这里保持 false，让标题栏按钮和顶部布局重新走编辑器自己的渲染逻辑。
    private static bool UseSystemTitleBar => false;
    private const float TitleBarButtonWidth = 40.0f;
    // 无边框窗口最大化后，系统仍会保留一圈不可见边框；拿不到 DPI 信息时使用这个兜底值。
    private const float BorderlessMaximizedInsetFallback = 8.0f;

    public LayoutManager LayoutManager { get; }

    public ToolbarPanel Toolbar { get; }

    public ToolsPanel Tools { get; }

    public SceneViewPanel Viewport { get; }

    public RightPanel RightPanel { get; }

    public AssetsPanel Assets { get; }

    public ConsolePanel Console { get; }

    private GraphicsDevice? graphicsDevice;
    private GameWindow? gameWindow;
    private bool showMenuBar = true;
    private bool showDemoWindow;
    private bool shouldClose;

    public MainWindow()
    {
        Toolbar = new ToolbarPanel();
        Tools = new ToolsPanel();
        Viewport = new SceneViewPanel();
        RightPanel = new RightPanel();
        Assets = new AssetsPanel();
        Console = new ConsolePanel();

        LayoutManager = new LayoutManager
        {
            TopPanel = Toolbar,
            LeftPanel = Tools,
            CenterPanel = Viewport,
            RightPanel = RightPanel,
            BottomPanel = new TabPanel(Assets, Console)
        };

        InitializeDefaultData();
    }

    private float GetScaledTitleBarHeight()
    {
        return EditorStyle.ScaleValue(TitleBarHeight);
    }

    private float GetScaledTitleBarButtonWidth()
    {
        return EditorStyle.ScaleValue(TitleBarButtonWidth);
    }

    private void UpdateLayoutMetrics()
    {
        LayoutManager.ToolbarHeight = EditorStyle.ToolbarHeightScaled;
        LayoutManager.MinPanelWidth = EditorStyle.MinPanelWidthScaled;
        LayoutManager.MinPanelHeight = EditorStyle.MinPanelHeightScaled;
        LayoutManager.MaxPanelWidth = EditorStyle.MaxPanelWidthScaled;
        LayoutManager.MaxPanelHeight = EditorStyle.MaxPanelHeightScaled;
        LayoutManager.SplitterThickness = EditorStyle.SplitterThicknessScaled;
        LayoutManager.SplitterHitPadding = EditorStyle.ScaleValue(3.0f);
    }

    private void UpdateChromeMetrics()
    {
        if (UseSystemTitleBar)
            return;

        // 自绘标题栏的拖拽热区也要跟着 UI 缩放一起更新，
        // 否则视觉尺寸变了，系统 hit-test 还是旧尺寸。
        WindowInterop.EnableCustomChrome(
            GetNativeWindowHandle(),
            (int)MathF.Round(GetScaledTitleBarHeight()),
            (int)MathF.Round(GetScaledTitleBarButtonWidth() * 3.0f),
            (int)MathF.Round(EditorStyle.ScaleValue(8.0f)));
    }

    public void Initialize(GraphicsDevice device, GameWindow window, IServiceRegistry serviceRegistry)
    {
        graphicsDevice = device;
        gameWindow = window;
        // 自绘无边框窗口改用系统原生 hit-test 处理拖拽和缩放，避免手写标题栏状态机出现一次可拖一次不可拖的问题。
        WindowInterop.EnableCustomChrome(GetNativeWindowHandle(), (int)TitleBarHeight, (int)(TitleBarButtonWidth * 3.0f), 8);

        EditorStyle.Apply();
        UpdateLayoutMetrics();
        UpdateChromeMetrics();

        LayoutManager.WindowSize = new Vector2(
            graphicsDevice.Presenter.BackBuffer.Width,
            graphicsDevice.Presenter.BackBuffer.Height
        );
        LayoutManager.Initialize();

        Toolbar.Initialize();
        Tools.Initialize();
        Viewport.Initialize();
        RightPanel.Initialize();
        Assets.Initialize();
        Console.Initialize();

        LayoutManager.CalculateLayout();
        SubscribeEvents();
    }

    private void InitializeDefaultData()
    {
        Tools.SelectedTool = "Raise";
    }

    private void SubscribeEvents()
    {
        Toolbar.ButtonClicked += (s, e) => HandleToolbarAction(e.ButtonName);
        Toolbar.ModeChanged += (s, e) => HandleModeChange(e);

        Tools.ToolSelected += (s, e) => Console.LogInfo($"Tool selected: {e.Tool.Name}");
        RightPanel.BrushSelected += (s, e) => Console.LogInfo($"Brush selected: {e.BrushName}");
        RightPanel.BrushParamsChanged += (s, e) => Console.LogInfo($"Brush {e.Param} changed to {e.Value:F2}");
        Assets.TextureSlotSelected += (s, e) => Console.LogInfo($"Texture slot selected: {e.SlotIndex}");
        Assets.FoliageSelected += (s, e) => Console.LogInfo($"Foliage selected: {e.Item.Name}");
        Assets.LayerSelected += (s, e) => Console.LogInfo($"Layer selected: {e.Layer.Name}");

        // Subscribe to terrain events
        Viewport.HeightmapLoaded += (s, path) =>
        {
            Console.LogInfo($"Heightmap loaded successfully: {path}");
        };

        Viewport.HeightmapLoadFailed += (s, error) =>
        {
            Console.LogError($"Failed to load heightmap: {error}");
        };
    }

    protected override void OnRender()
    {
        if (graphicsDevice == null)
            return;

        var io = ImGui.GetIO();
        Vector2 hostWindowSize = io.DisplaySize;

        UpdateLayoutMetrics();
        UpdateChromeMetrics();
        LayoutManager.WindowSize = hostWindowSize;

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(hostWindowSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        bool open = true;
        bool began = ImGui.Begin("MainWindow", ref open, windowFlags);
        ImGui.PopStyleVar(3);

        if (began)
        {
            // 无边框窗口在“最大化”状态下要主动留出安全边距，否则右上角按钮会被屏幕边缘裁掉。
            float windowInset = UseSystemTitleBar ? 0.0f : GetWindowContentInset();

            if (!UseSystemTitleBar)
            {
                RenderCustomTitleBar(windowInset);
            }

            float menuBarHeight = 0.0f;
            if (showMenuBar)
            {
                menuBarHeight = RenderMenuBar(windowInset);
            }

            float titleBarInset = UseSystemTitleBar ? 0.0f : GetScaledTitleBarHeight();
            LayoutManager.TopInset = titleBarInset + menuBarHeight;
            LayoutManager.Render();
        }

        ImGui.End();

        if (shouldClose || !open)
        {
            Environment.Exit(0);
        }

        if (showDemoWindow)
        {
            ImGui.ShowDemoWindow(ref showDemoWindow);
        }
    }

    private void RenderCustomTitleBar(float windowInset)
    {
        var drawList = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();
        Vector2 cursorPos = io.MousePos;
        Vector2 windowPos = ImGui.GetWindowPos();
        Vector2 windowSize = ImGui.GetWindowSize();
        bool isWindowMaximized = WindowInterop.IsMaximized(GetNativeWindowHandle());
        float titleBarHeight = GetScaledTitleBarHeight();
        float titleBarButtonWidth = GetScaledTitleBarButtonWidth();

        float rightInset = windowInset;
        Vector2 titleBarMin = new Vector2(windowPos.X, windowPos.Y);
        float titleBarWidth = Math.Max(0.0f, windowSize.X);
        Vector2 titleBarMax = new Vector2(titleBarMin.X + titleBarWidth, titleBarMin.Y + titleBarHeight);
        drawList.AddRectFilled(titleBarMin, titleBarMax, ColorPalette.TitleBar.ToUint());

        // 右侧固定预留三个系统按钮的宽度，标题文本和拖拽热区都只使用剩余区域。
        float buttonAreaWidth = titleBarButtonWidth * 3.0f;
        float dragRegionWidth = Math.Max(0.0f, titleBarWidth - buttonAreaWidth - rightInset);

        Vector2 textSize = ImGui.CalcTextSize("Terrain Editor");
        Vector2 textPos = new Vector2(
            titleBarMin.X + Math.Max(0.0f, (dragRegionWidth - textSize.X) * 0.5f),
            titleBarMin.Y + (titleBarHeight - textSize.Y) * 0.5f
        );
        drawList.AddText(textPos, ColorPalette.TextPrimary.ToUint(), "Terrain Editor");

        float buttonY = titleBarMin.Y;
        float buttonsStartX = titleBarMax.X - buttonAreaWidth - rightInset;

        RenderTitleBarButton(buttonsStartX, buttonY, titleBarButtonWidth, titleBarHeight, Icons.Minimize, "Minimize", MinimizeNativeWindow);

        string maximizeIcon = isWindowMaximized ? Icons.Restore : Icons.Maximize;
        string maximizeTooltip = isWindowMaximized ? "Restore" : "Maximize";
        RenderTitleBarButton(buttonsStartX + titleBarButtonWidth, buttonY, titleBarButtonWidth, titleBarHeight, maximizeIcon, maximizeTooltip, ToggleNativeWindowMaximize);

        RenderTitleBarButton(buttonsStartX + titleBarButtonWidth * 2.0f, buttonY, titleBarButtonWidth, titleBarHeight, Icons.Times, "Close", () =>
        {
            shouldClose = true;
        }, isCloseButton: true);

        bool isDragRegionHovered = IsPointInsideRect(cursorPos, titleBarMin, new Vector2(titleBarMin.X + dragRegionWidth, titleBarMin.Y + titleBarHeight));
        if (isDragRegionHovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        drawList.AddLine(
            new Vector2(titleBarMin.X, titleBarMax.Y),
            new Vector2(titleBarMax.X, titleBarMax.Y),
            ColorPalette.Border.ToUint(),
            1.0f
        );
    }

    private void RenderTitleBarButton(float x, float y, float width, float height, string icon, string tooltip, Action onClick, bool isCloseButton = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();
        Vector2 buttonMin = new Vector2(x, y);
        Vector2 buttonMax = new Vector2(x + width, y + height);
        bool isHovered = IsPointInsideRect(io.MousePos, buttonMin, buttonMax);
        bool isActive = isHovered && io.MouseDown[0];

        uint bgColor = 0;
        if (isActive && isCloseButton)
        {
            bgColor = ColorPalette.Error.ToUint();
        }
        else if (isHovered)
        {
            bgColor = isCloseButton ? ColorPalette.Error.ToUint() : ColorPalette.Hover.ToUint();
        }

        if (bgColor != 0)
        {
            drawList.AddRectFilled(buttonMin, buttonMax, bgColor);
        }

        FontManager.PushIcons();
        Vector2 iconSize = ImGui.CalcTextSize(icon);
        Vector2 iconPos = new Vector2(
            x + (width - iconSize.X) * 0.5f,
            y + (height - iconSize.Y) * 0.5f
        );
        drawList.AddText(iconPos, isHovered ? ColorPalette.TextHighlight.ToUint() : ColorPalette.TextPrimary.ToUint(), icon);
        FontManager.PopFont();

        if (isHovered)
        {
            ImGui.SetTooltip(tooltip);
        }

        // 标题栏按钮改成手动命中测试，避免拖拽结束后还受 ImGui 控件激活状态影响。
        if (isHovered && io.MouseReleased[0])
        {
            onClick();
        }
    }

    private static bool IsPointInsideRect(Vector2 point, Vector2 min, Vector2 max)
    {
        return
            point.X >= min.X &&
            point.X <= max.X &&
            point.Y >= min.Y &&
            point.Y <= max.Y;
    }

    private float RenderMenuBar(float windowInset)
    {
        float menuBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.Y * 2.0f;
        Vector2 windowPos = ImGui.GetWindowPos();
        Vector2 windowSize = ImGui.GetWindowSize();
        float titleBarOffset = UseSystemTitleBar ? 0.0f : GetScaledTitleBarHeight();
        Vector2 menuBarPos = new Vector2(windowPos.X, windowPos.Y + titleBarOffset);
        Vector2 menuBarSize = new Vector2(
            Math.Max(0.0f, windowSize.X),
            menuBarHeight
        );

        // 菜单栏使用独立子区域承载，避免与自绘标题栏共享同一块 ImGui 顶部区域而互相覆盖。
        ImGui.SetCursorScreenPos(menuBarPos);
        bool began = ImGui.BeginChild(
            "##main_menu_bar",
            menuBarSize,
            ImGuiChildFlags.None,
            ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
        );

        if (began && ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New", "Ctrl+N"))
                {
                    HandleToolbarAction("New");
                }

                if (ImGui.MenuItem("Open...", "Ctrl+O"))
                {
                    HandleToolbarAction("Open");
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    HandleToolbarAction("Save");
                }

                if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
                {
                    Console.LogInfo("Save As...");
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    shouldClose = true;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z"))
                {
                    HandleToolbarAction("Undo");
                }

                if (ImGui.MenuItem("Redo", "Ctrl+Y"))
                {
                    HandleToolbarAction("Redo");
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                bool showTools = Tools.IsVisible;
                if (ImGui.MenuItem("Tools Panel", "", ref showTools))
                {
                    LayoutManager.ToggleLeftPanel();
                }

                bool showRight = RightPanel.IsVisible;
                if (ImGui.MenuItem("Properties Panel", "", ref showRight))
                {
                    LayoutManager.ToggleRightPanel();
                }

                bool showBottom = Assets.IsVisible || Console.IsVisible;
                if (ImGui.MenuItem("Bottom Panel", "", ref showBottom))
                {
                    LayoutManager.ToggleBottomPanel();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Reset Layout"))
                {
                    LayoutManager.ResetLayout();
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("Documentation"))
                {
                    Console.LogInfo("Opening documentation...");
                }

                if (ImGui.MenuItem("About"))
                {
                    Console.LogInfo("Terrain Editor v1.0");
                }

                ImGui.Separator();

                if (ImGui.MenuItem("ImGui Demo", "", ref showDemoWindow))
                {
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        ImGui.EndChild();

        return menuBarHeight;
    }

    private float GetWindowContentInset()
    {
        return WindowInterop.GetBorderlessMaximizedInset(GetNativeWindowHandle(), BorderlessMaximizedInsetFallback);
    }

    private void MinimizeNativeWindow()
    {
        WindowInterop.Minimize(GetNativeWindowHandle());
    }

    private void ToggleNativeWindowMaximize()
    {
        WindowInterop.ToggleMaximize(GetNativeWindowHandle());
    }

    private nint GetNativeWindowHandle()
    {
        // Stride.GameWindow.NativeWindow 实际包装了平台窗口句柄，这里取出 HWND 交给 Win32 API。
        return gameWindow?.NativeWindow?.Handle ?? nint.Zero;
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        LayoutManager.Update(deltaTime);
    }

    private void HandleToolbarAction(string buttonName)
    {
        switch (buttonName)
        {
            case "New":
                Console.LogInfo("New terrain project");
                break;

            case "Open":
                OpenHeightmap();
                break;

            case "Save":
                Console.LogInfo("Save terrain project");
                break;

            case "Undo":
                Console.LogInfo("Undo");
                break;

            case "Redo":
                Console.LogInfo("Redo");
                break;
        }
    }

    private void OpenHeightmap()
    {
        nint hwnd = GetNativeWindowHandle();
        if (FileDialog.ShowOpenDialog(hwnd, "PNG Files (*.png)|*.png", "Open Heightmap", out string? filePath))
        {
            Console.LogInfo($"Loading heightmap: {filePath}");
            Viewport.LoadHeightmap(filePath);
        }
    }

    private void HandleModeChange(EditorMode mode)
    {
        Tools.SetMode(mode);
        Assets.SetMode(mode);

        Console.LogInfo($"Mode changed to: {mode}");
    }

}

public class TabPanel : PanelBase
{
    private readonly List<PanelBase> panels;
    private int selectedTab;

    public TabPanel(params PanelBase[] panels)
    {
        this.panels = panels.ToList();
        ShowTitleBar = false;
        Padding = Margin.Zero;
    }

    protected override void RenderContent()
    {
        ImGui.SetCursorScreenPos(new Vector2(ContentRect.X, ContentRect.Y));
        if (ImGui.BeginChild($"##tab_container_{Id}", new Vector2(ContentRect.Width, ContentRect.Height), ImGuiChildFlags.None))
        {
            if (ImGui.BeginTabBar($"##tabs_{Id}", ImGuiTabBarFlags.Reorderable))
            {
                for (int i = 0; i < panels.Count; i++)
                {
                    var panel = panels[i];
                    bool isOpen = !panel.IsClosed;

                    ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                    if (panel.IsClosed)
                        flags |= ImGuiTabItemFlags.UnsavedDocument;

                    if (ImGui.BeginTabItem($"{panel.Icon} {panel.Title}###{panel.Id}", ref isOpen, flags))
                    {
                        selectedTab = i;
                        panel.IsVisible = true;

                        Vector2 panelPosition = ImGui.GetCursorScreenPos();
                        Vector2 panelSize = ImGui.GetContentRegionAvail();
                        panelSize.X = Math.Max(0.0f, panelSize.X);
                        panelSize.Y = Math.Max(0.0f, panelSize.Y);

                        bool originalShowTitleBar = panel.ShowTitleBar;
                        panel.ShowTitleBar = false;

                        try
                        {
                            panel.Arrange(panelPosition, panelSize);
                            panel.Render();
                        }
                        finally
                        {
                            panel.ShowTitleBar = originalShowTitleBar;
                        }

                        ImGui.EndTabItem();
                    }
                    else
                    {
                        panel.IsVisible = false;
                    }

                    if (!isOpen)
                    {
                        panel.IsClosed = true;
                    }
                }

                ImGui.EndTabBar();
            }
        }

        ImGui.EndChild();
    }
}
