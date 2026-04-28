#nullable enable

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Terrain.Editor.ViewModels;

namespace Terrain.Editor.Views;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Button> _assetTabs = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyStartupSizeForPhysicalPixels();

        _assetTabs["Textures"] = this.FindControl<Button>("AssetTabTextures")!;
        _assetTabs["Meshes"] = this.FindControl<Button>("AssetTabMeshes")!;
        _assetTabs["Foliage"] = this.FindControl<Button>("AssetTabFoliage")!;
        _assetTabs["Prefabs"] = this.FindControl<Button>("AssetTabPrefabs")!;

        foreach (var tab in _assetTabs.Values)
        {
            tab.Click += OnAssetTabClicked;
        }
    }

    private void OnAssetTabClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string category)
            return;

        UpdateAssetTabStyles(category);
    }

    private void UpdateAssetTabStyles(string activeCategory)
    {
        foreach (var (name, button) in _assetTabs)
        {
            button.Classes.Remove("assetTab");
            button.Classes.Remove("assetTabActive");
            button.Classes.Add(name == activeCategory ? "assetTabActive" : "assetTab");
        }
    }

    private void ApplyStartupSizeForPhysicalPixels()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        uint dpi = GetDpiForSystem();
        if (dpi == 0)
        {
            dpi = 96;
        }

        double scale = dpi / 96d;
        Width = 1920d / scale;
        Height = 1080d / scale;
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        if (DataContext is EditorShellViewModel viewModel)
        {
            viewModel.Dispose();
        }

        foreach (var tab in _assetTabs.Values)
        {
            tab.Click -= OnAssetTabClicked;
        }

        base.OnClosed(e);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
