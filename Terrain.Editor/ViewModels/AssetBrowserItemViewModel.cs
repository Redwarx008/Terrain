#nullable enable

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Terrain.Editor.ViewModels;

public sealed partial class AssetBrowserItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _kind = string.Empty;

    [ObservableProperty]
    private string _previewBackground = "#3A3A3A";

    [ObservableProperty]
    private string _previewForeground = "#FFFFFF";

    [ObservableProperty]
    private string _iconGlyph = "\xE80A";

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isCreateItem;

    /// <summary>
    /// Material slot index when this item represents a MaterialSlot (Paint mode).
    /// </summary>
    [ObservableProperty]
    private int _materialSlotIndex = -1;

    public AssetBrowserItemViewModel() { }

    public AssetBrowserItemViewModel(
        string name,
        string category,
        string kind,
        string previewBackground,
        string previewForeground,
        string iconGlyph,
        bool isEmpty = false,
        bool isCreateItem = false,
        Bitmap? previewImage = null,
        int materialSlotIndex = -1)
    {
        _name = name;
        _category = category;
        _kind = kind;
        _previewBackground = previewBackground;
        _previewForeground = previewForeground;
        _iconGlyph = iconGlyph;
        _isEmpty = isEmpty;
        _isCreateItem = isCreateItem;
        _previewImage = previewImage;
        _materialSlotIndex = materialSlotIndex;
    }
}