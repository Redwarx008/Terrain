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
    private ListBox? _assetListBox;

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

        _assetListBox = this.FindControl<ListBox>("AssetListBox");
        if (_assetListBox != null)
        {
            _assetListBox.SelectionChanged += OnAssetSelectionChanged;
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

    // MVVM note: selection deselection is UI logic (ViewModel can't deselect a ListBoxItem).
    // The command dispatch is acceptable here because it's a UI-triggered action
    // with no clean pure-binding alternative (similar to OnAssetTabClicked).
    private void OnAssetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is not AssetBrowserItemViewModel item)
            return;

        if (item.IsCreateItem && DataContext is EditorShellViewModel vm)
        {
            // Deselect the create item and trigger the add command
            listBox.SelectedItem = null;
            vm.AddAssetForCategoryCommand.Execute(item.Category);
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

        if (_assetListBox != null)
        {
            _assetListBox.SelectionChanged -= OnAssetSelectionChanged;
        }

        base.OnClosed(e);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}