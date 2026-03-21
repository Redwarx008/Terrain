using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TerrainPreProcessor.Resources;
using TerrainPreProcessor.Views;

namespace TerrainPreProcessor.Services;

public class WindowService
{
    private Window? _mainWindow;

    public Window CreateMainWindow()
    {
        _mainWindow = new MainWindow
        {
            DataContext = new ViewModels.MainWindowViewModel(this)
        };
        return _mainWindow;
    }

    public async Task<string?> OpenFilePickerAsync(string title, string[]? filters = null)
    {
        if (_mainWindow == null) return null;

        var storageProvider = _mainWindow.StorageProvider;

        var fileTypes = new List<FilePickerFileType>();
        if (filters != null)
        {
            foreach (var filter in filters)
            {
                var extension = filter.TrimStart('*');
                fileTypes.Add(new FilePickerFileType(extension.ToUpper())
                {
                    Patterns = new[] { filter }
                });
            }
        }

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes.Count > 0 ? fileTypes : null
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFilePickerAsync(string title, string defaultExtension)
    {
        if (_mainWindow == null) return null;

        var storageProvider = _mainWindow.StorageProvider;

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(defaultExtension.TrimStart('*'))
                {
                    Patterns = new[] { defaultExtension }
                }
            }
        });

        return result?.Path.LocalPath;
    }

    public ProgressWindow ShowProgressWindow(string title)
    {
        var progressWindow = new ProgressWindow
        {
            Title = title
        };

        progressWindow.Show(_mainWindow);
        return progressWindow;
    }

    public async Task ShowInfoDialogAsync(string title, string message)
    {
        if (_mainWindow == null) return;

        await MessageBox.Show(_mainWindow, message, title, MessageBox.MessageBoxButtons.Ok);
    }

    public async Task ShowErrorDialogAsync(string title, string message)
    {
        if (_mainWindow == null) return;

        await MessageBox.Show(_mainWindow, message, title, MessageBox.MessageBoxButtons.Ok, MessageBox.MessageBoxIcon.Error);
    }
}

/// <summary>
/// 简单的 MessageBox 实现
/// </summary>
public static class MessageBox
{
    public enum MessageBoxButtons
    {
        Ok,
        OkCancel,
        YesNo
    }

    public enum MessageBoxIcon
    {
        None,
        Information,
        Warning,
        Error
    }

    public static async Task Show(Window owner, string message, string title = "", MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var button = new Button
        {
            Content = Strings.OK,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Thickness(20, 5)
        };

        button.Click += (s, e) => dialog.Close();

        panel.Children.Add(textBlock);
        panel.Children.Add(button);

        dialog.Content = panel;

        await dialog.ShowDialog(owner);
    }
}
