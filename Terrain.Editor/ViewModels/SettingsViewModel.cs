#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;

namespace Terrain.Editor.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private float _heightScale = 100.0f;
}