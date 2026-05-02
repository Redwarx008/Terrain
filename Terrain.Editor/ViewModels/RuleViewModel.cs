#nullable enable

using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps a single <see cref="BiomeRuleLayer"/> for Avalonia data binding.
/// Exposes the modifier stack for procedural rule editing.
/// </summary>
public sealed partial class RuleViewModel : ObservableObject
{
    private readonly BiomeRuleLayer _source;
    private readonly MaterialSlotManager _materialSlotManager = MaterialSlotManager.Instance;
    private int _globalIndex;
    private bool _syncing;

    public RuleViewModel(BiomeRuleLayer source, int globalIndex)
    {
        _source = source;
        _globalIndex = globalIndex;
        SyncFromSource(globalIndex);
    }

    public int Id => _source.Id;

    public int BiomeId => _source.BiomeId;

    public string ModifierCountLabel => Modifiers.Count == 1 ? "1 modifier" : $"{Modifiers.Count} modifiers";

    public Bitmap? MaterialPreviewImage => TextureThumbnailProvider.LoadFromPath(_materialSlotManager[MaterialSlotIndex].AlbedoTexturePath);

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private int _materialSlotIndex;

    public ObservableCollection<ModifierViewModel> Modifiers { get; } = new();

    [ObservableProperty]
    private ModifierViewModel? _selectedModifier;

    [ObservableProperty]
    private int _selectedModifierIndex = -1;

    partial void OnNameChanged(string value)
    {
        if (_syncing) return;
        if (_source.Name != value)
        {
            _source.Name = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing) return;
        if (_source.Enabled != value)
        {
            _source.Enabled = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_syncing) return;
        if (_source.Visible != value)
        {
            _source.Visible = value;
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnMaterialSlotIndexChanged(int value)
    {
        if (_syncing) return;
        if (_source.MaterialSlotIndex != value)
        {
            _source.MaterialSlotIndex = value;
            OnPropertyChanged(nameof(MaterialPreviewImage));
            BiomeRuleService.Instance.NotifyMutated();
        }
    }

    partial void OnSelectedModifierIndexChanged(int value)
    {
        var matchingModifier = value >= 0 && value < Modifiers.Count ? Modifiers[value] : null;
        if (SelectedModifier != matchingModifier)
        {
            SelectedModifier = matchingModifier;
        }
    }

    /// <summary>
    /// Refreshes properties from the source model.
    /// </summary>
    public void SyncFromSource(int globalIndex)
    {
        _syncing = true;
        _globalIndex = globalIndex;
        Name = _source.Name;
        IsEnabled = _source.Enabled;
        IsVisible = _source.Visible;
        MaterialSlotIndex = _source.MaterialSlotIndex;

        // Sync Modifiers collection
        var sourceModifiers = _source.Modifiers;
        for (int i = 0; i < sourceModifiers.Count; i++)
        {
            if (i < Modifiers.Count && Modifiers[i].Id == sourceModifiers[i].Id)
            {
                Modifiers[i].SyncFromSource();
            }
            else
            {
                if (i < Modifiers.Count)
                    Modifiers[i] = new ModifierViewModel(sourceModifiers[i]);
                else
                    Modifiers.Add(new ModifierViewModel(sourceModifiers[i]));
            }
        }

        while (Modifiers.Count > sourceModifiers.Count)
        {
            Modifiers.RemoveAt(Modifiers.Count - 1);
        }

        OnPropertyChanged(nameof(ModifierCountLabel));
        OnPropertyChanged(nameof(MaterialPreviewImage));

        // Re-sync selected modifier by index
        int currentIdx = SelectedModifierIndex;
        SelectedModifier = currentIdx >= 0 && currentIdx < Modifiers.Count ? Modifiers[currentIdx] : null;

        _syncing = false;
    }

    public void NotifyMaterialPreviewChanged()
    {
        OnPropertyChanged(nameof(MaterialPreviewImage));
    }
}
