#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;

namespace Terrain.Editor.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _showTerrain = true;

    [ObservableProperty]
    private bool _showRivers = true;

    [ObservableProperty]
    private float _heightScale = 100.0f;

    [ObservableProperty]
    private float _riverMaxVisibleCameraHeight = 3000.0f;
}
