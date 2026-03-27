#nullable enable

using Hexa.NET.ImGui;
using Stride.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Editor.UI.Controls;
using Terrain.Editor.UI.Layout;
using Terrain.Editor.UI.Panels;
using Terrain.Editor.UI.Styling;

namespace Terrain.Editor.UI;

public class MainWindow : ControlBase
{
    public LayoutManager LayoutManager { get; }

    public ToolbarPanel Toolbar { get; }

    public SceneViewPanel SceneView { get; }

    public HierarchyPanel Hierarchy { get; }

    public InspectorPanel Inspector { get; }

    public ConsolePanel Console { get; }

    public AssetsPanel Assets { get; }

    public StatusBarPanel StatusBar { get; }


    private GraphicsDevice? graphicsDevice;
    private bool showMenuBar = true;
    private bool showDemoWindow = false;


    public MainWindow()
    {
        Toolbar = new ToolbarPanel();
        SceneView = new SceneViewPanel();
        Hierarchy = new HierarchyPanel();
        Inspector = new InspectorPanel();
        Console = new ConsolePanel();
        Assets = new AssetsPanel();
        StatusBar = new StatusBarPanel();

        LayoutManager = new LayoutManager
        {
            TopPanel = Toolbar,
            CenterPanel = SceneView,
            LeftPanel = Hierarchy,
            RightPanel = Inspector,
            BottomPanel = new TabPanelContainer(Console, Assets),
            StatusBar = StatusBar
        };

        InitializeHierarchyData();

        InitializeInspectorData();
    }


    public void Initialize(GraphicsDevice device)
    {
        graphicsDevice = device;

        EditorStyle.Apply();


        LayoutManager.WindowSize = new Vector2(
            graphicsDevice.Presenter.BackBuffer.Width,
            graphicsDevice.Presenter.BackBuffer.Height
        );
        LayoutManager.Initialize();

        Toolbar.Initialize();
        SceneView.Initialize();
        Hierarchy.Initialize();
        Inspector.Initialize();
        Console.Initialize();
        Assets.Initialize();
        StatusBar.Initialize();

        LayoutManager.CalculateLayout();

        SubscribeEvents();
    }

    private void InitializeHierarchyData()
    {
        var terrainNode = new HierarchyNode
        {
            Name = "Terrain",
            Icon = Icons.Terrain,
            IsExpanded = true
        };

        var chunk1 = new HierarchyNode
        {
            Name = "Chunk_0_0",
            Icon = Icons.Grid,
            Parent = terrainNode
        };
        terrainNode.Children.Add(chunk1);

        var chunk2 = new HierarchyNode
        {
            Name = "Chunk_0_1",
            Icon = Icons.Grid,
            Parent = terrainNode
        };
        terrainNode.Children.Add(chunk2);

        Hierarchy.Nodes.Add(terrainNode);

        var cameraNode = new HierarchyNode
        {
            Name = "Main Camera",
            Icon = Icons.Camera
        };
        Hierarchy.Nodes.Add(cameraNode);

        var lightNode = new HierarchyNode
        {
            Name = "Directional Light",
            Icon = Icons.Light
        };
        Hierarchy.Nodes.Add(lightNode);
    }

    private void InitializeInspectorData()
    {
        var transformGroup = Inspector.AddGroup("Transform");
        transformGroup.AddProperty("Position", () => new System.Numerics.Vector3(0, 0, 0), v => { });
        transformGroup.AddProperty("Rotation", () => new System.Numerics.Vector3(0, 0, 0), v => { });
        transformGroup.AddProperty("Scale", () => new System.Numerics.Vector3(1, 1, 1), v => { });

        var terrainGroup = Inspector.AddGroup("Terrain");
        terrainGroup.AddProperty("Height Scale", () => 100f, v => { });
        terrainGroup.AddProperty("Tile Size", () => 129, v => { });
        terrainGroup.AddProperty("Leaf Node Size", () => 32, v => { });
    }

    private void SubscribeEvents()
    {
        Hierarchy.SelectionChanged += (s, e) =>
        {
            StatusBar.SetMessage($"Selected: {e.SelectedNodes.Count} objects");
        };

        Toolbar.ButtonClicked += (s, e) =>
        {
            HandleToolbarAction(e.ButtonName);
        };
    }


    protected override void OnRender()
    {
        if (graphicsDevice == null)
            return;

        var io = ImGui.GetIO();
        LayoutManager.WindowSize = io.DisplaySize;

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.MenuBar |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoBackground;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));

        bool open = true;
        bool began = ImGui.Begin("MainWindow", ref open, windowFlags);
        ImGui.PopStyleVar(3);

        if (began)
        {
            float menuBarHeight = 0.0f;

            if (showMenuBar)
            {
                RenderMenuBar();
                menuBarHeight = ImGui.GetCursorPosY();
            }

            LayoutManager.TopInset = menuBarHeight;
            LayoutManager.Render();
        }
        ImGui.End();

        if (showDemoWindow)
        {
            ImGui.ShowDemoWindow(ref showDemoWindow);
        }
    }

    private void RenderMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New", "Ctrl+N"))
                {
                }

                if (ImGui.MenuItem("Open...", "Ctrl+O"))
                {
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                }

                if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
                {
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z"))
                {
                }

                if (ImGui.MenuItem("Redo", "Ctrl+Y"))
                {
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Cut", "Ctrl+X"))
                {
                }

                if (ImGui.MenuItem("Copy", "Ctrl+C"))
                {
                }

                if (ImGui.MenuItem("Paste", "Ctrl+V"))
                {
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                bool showHierarchy = Hierarchy.IsVisible;
                if (ImGui.MenuItem("Hierarchy", "", ref showHierarchy))
                {
                    LayoutManager.ToggleLeftPanel();
                }

                bool showInspector = Inspector.IsVisible;
                if (ImGui.MenuItem("Inspector", "", ref showInspector))
                {
                    LayoutManager.ToggleRightPanel();
                }

                bool showConsole = Console.IsVisible;
                if (ImGui.MenuItem("Console", "", ref showConsole))
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

            if (ImGui.BeginMenu("Tools"))
            {
                if (ImGui.MenuItem("Terrain Editor"))
                {
                }

                if (ImGui.MenuItem("Texture Painter"))
                {
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Preferences"))
                {
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("Documentation"))
                {
                }

                if (ImGui.MenuItem("About"))
                {
                }

                ImGui.Separator();

                if (ImGui.MenuItem("ImGui Demo", "", ref showDemoWindow))
                {
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
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
                Console.LogInfo("New project");
                break;

            case "Open":
                Console.LogInfo("Open project");
                break;

            case "Save":
                Console.LogInfo("Save project");
                break;

            case "Undo":
                Console.LogInfo("Undo");
                break;

            case "Redo":
                Console.LogInfo("Redo");
                break;

            case "Play":
                Console.LogInfo("Play");
                break;

            case "Pause":
                Console.LogInfo("Pause");
                break;

            case "Stop":
                Console.LogInfo("Stop");
                break;
        }
    }

}

public class TabPanelContainer : PanelBase
{
    private readonly List<PanelBase> panels;
    private int selectedTab = 0;

    public TabPanelContainer(params PanelBase[] panels)
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

