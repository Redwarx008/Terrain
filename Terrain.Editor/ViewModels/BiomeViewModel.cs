#nullable enable

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Terrain.Editor.Services;
using Terrain.Editor.Services.Resources;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps <see cref="BiomeRuleService"/> for Avalonia data binding.
/// Exposes Biomes/Layers collections, CRUD commands, and selected layer state.
/// </summary>
public sealed partial class BiomeViewModel : ObservableObject, IDisposable
{
    private const int MaxLayersPerBiome = 5;

    private readonly BiomeRuleService _service = BiomeRuleService.Instance;
    private readonly EditorState _editorState = EditorState.Instance;
    private readonly MaterialSlotManager _materialSlotManager = MaterialSlotManager.Instance;
    private readonly Func<EditorResourceSession?> _resourceSessionProvider;
    private RuleViewModel? _observedLayer;

    public ObservableCollection<BiomeDefinitionViewModel> Biomes { get; } = new();

    public ObservableCollection<RuleViewModel> Layers { get; } = new();

    public ObservableCollection<RuleViewModel> VisibleLayers { get; } = new();

    public ObservableCollection<MaterialSlotOptionViewModel> AvailableMaterialSlots { get; } = new();

    [ObservableProperty]
    private BiomeDefinitionViewModel? _selectedBiome;

    [ObservableProperty]
    private RuleViewModel? _selectedLayer;

    [ObservableProperty]
    private bool _showMaskOverlay = false;

    [ObservableProperty]
    private int _selectedLayerIndex = -1;

    [ObservableProperty]
    private int _selectedModifierIndex = -1;

    [ObservableProperty]
    private int _addModifierTypeIndex = 0;

    [ObservableProperty]
    private MaterialSlotOptionViewModel? _selectedLayerMaterialSlotOption;

    public string SelectedBiomeLayerSummary => $"{VisibleLayers.Count}/{MaxLayersPerBiome} layers";

    public string SelectedLayerMaterialTitle => SelectedLayerMaterialSlotOption?.Label
        ?? (SelectedLayer != null ? "未分配材质" : "No layer selected");

    public string SelectedLayerMaterialDetail => SelectedLayerMaterialSlotOption?.Detail
        ?? (SelectedLayer != null ? "Assign an imported material slot to this biome layer." : "Select a biome layer to configure its material.");

    public bool CanAddLayer => SelectedBiome != null && VisibleLayers.Count < MaxLayersPerBiome;

    public bool CanRemoveSelectedBiome => SelectedBiome != null && _service.CanRemoveBiome(SelectedBiome.Id);

    public bool CanRemoveSelectedLayer => SelectedLayer != null;

    public bool CanMoveSelectedLayerUp => SelectedLayer != null && GetSelectedLayerLocalIndex() > 0;

    public bool CanMoveSelectedLayerDown => SelectedLayer != null && GetSelectedLayerLocalIndex() < VisibleLayers.Count - 1;

    public bool CanAddModifier => SelectedLayer != null;

    public bool CanRemoveSelectedModifier => SelectedLayer?.SelectedModifier != null;

    public bool CanMoveSelectedModifierUp => SelectedLayer?.SelectedModifierIndex > 0;

    public bool CanMoveSelectedModifierDown => SelectedLayer != null
        && SelectedLayer.SelectedModifierIndex >= 0
        && SelectedLayer.SelectedModifierIndex < SelectedLayer.Modifiers.Count - 1;

    public BiomeViewModel()
        : this(static () => null)
    {
    }

    public BiomeViewModel(Func<EditorResourceSession?> resourceSessionProvider)
    {
        _resourceSessionProvider = resourceSessionProvider ?? throw new ArgumentNullException(nameof(resourceSessionProvider));

        RefreshCollections();
        RefreshMaterialSlots();

        _service.StateChanged += OnServiceStateChanged;
        _editorState.OverlayChanged += OnOverlayChanged;
        _editorState.RuleSelectionChanged += OnRuleSelectionChanged;
        _editorState.BiomeSelectionChanged += OnBiomeSelectionChanged;
        _materialSlotManager.SlotsChanged += OnMaterialSlotsChanged;

        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        SelectedLayerIndex = _editorState.SelectedRuleIndex;

        // Sync initial biome selection from EditorState
        int initialBiomeId = _editorState.CurrentBiomeId;
        SelectedBiome = Biomes.FirstOrDefault(b => b.Id == initialBiomeId) ?? Biomes.FirstOrDefault();
        SyncSelectedLayerMaterialSlotOption();
        RefreshCommandStates();
    }

    // ── Biome CRUD ──

    [RelayCommand]
    private void AddBiome()
    {
        _service.AddBiome();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedBiome))]
    private void RemoveBiome(BiomeDefinitionViewModel? biome)
    {
        if (biome == null || !_service.CanRemoveBiome(biome.Id))
        {
            return;
        }

        _service.RemoveBiome(biome.Id);
    }

    // ── Layer CRUD ──

    [RelayCommand(CanExecute = nameof(CanAddLayer))]
    private void AddLayer()
    {
        if (SelectedBiome == null || VisibleLayers.Count >= MaxLayersPerBiome)
        {
            return;
        }

        _service.AddLayer(SelectedBiome.Id);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedLayer))]
    private void RemoveSelectedLayer()
    {
        if (SelectedLayer == null)
        {
            return;
        }

        int globalIndex = Layers.IndexOf(SelectedLayer);
        _service.RemoveLayerAt(globalIndex);
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedLayerUp))]
    private void MoveSelectedLayerUp()
    {
        MoveSelectedLayer(-1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedLayerDown))]
    private void MoveSelectedLayerDown()
    {
        MoveSelectedLayer(1);
    }

    // ── Modifier CRUD ──

    [RelayCommand(CanExecute = nameof(CanAddModifier))]
    private void AddModifier()
    {
        if (SelectedLayer == null) return;
        var layer = _service.GetLayerByGlobalIndex(SelectedLayerIndex);
        if (layer == null) return;
        var type = (BiomeModifierType)AddModifierTypeIndex;
        _service.AddModifier(layer, type);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedModifier))]
    private void RemoveSelectedModifier()
    {
        if (SelectedLayer == null) return;
        var layer = _service.GetLayerByGlobalIndex(SelectedLayerIndex);
        if (layer == null) return;
        _service.RemoveModifier(layer, SelectedLayer.SelectedModifierIndex);
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedModifierUp))]
    private void MoveSelectedModifierUp()
    {
        if (SelectedLayer == null || SelectedLayer.SelectedModifierIndex <= 0) return;
        var layer = _service.GetLayerByGlobalIndex(SelectedLayerIndex);
        if (layer == null) return;
        _service.MoveModifier(layer, SelectedLayer.SelectedModifierIndex, SelectedLayer.SelectedModifierIndex - 1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedModifierDown))]
    private void MoveSelectedModifierDown()
    {
        if (SelectedLayer == null || SelectedLayer.SelectedModifierIndex >= SelectedLayer.Modifiers.Count - 1) return;
        var layer = _service.GetLayerByGlobalIndex(SelectedLayerIndex);
        if (layer == null) return;
        _service.MoveModifier(layer, SelectedLayer.SelectedModifierIndex, SelectedLayer.SelectedModifierIndex + 1);
    }

    partial void OnSelectedBiomeChanged(BiomeDefinitionViewModel? value)
    {
        if (value != null && _editorState.CurrentBiomeId != value.Id)
        {
            _editorState.CurrentBiomeId = value.Id;
        }

        RefreshVisibleLayers();

        if (SelectedLayer != null && SelectedLayer.BiomeId != value?.Id)
        {
            SelectedLayer = VisibleLayers.FirstOrDefault();
        }

        SyncSelectedLayerMaterialSlotOption();
        RefreshCommandStates();
    }

    partial void OnSelectedLayerChanged(RuleViewModel? value)
    {
        if (!ReferenceEquals(_observedLayer, value))
        {
            if (_observedLayer != null)
            {
                _observedLayer.PropertyChanged -= OnSelectedLayerPropertyChanged;
            }

            _observedLayer = value;

            if (_observedLayer != null)
            {
                _observedLayer.PropertyChanged += OnSelectedLayerPropertyChanged;
            }
        }

        int newIndex = value != null ? Layers.IndexOf(value) : -1;

        if (SelectedLayerIndex != newIndex)
        {
            SelectedLayerIndex = newIndex;
        }

        if (_editorState.SelectedRuleIndex != newIndex)
        {
            _editorState.SelectedRuleIndex = newIndex;
        }

        if (value != null && SelectedBiome?.Id != value.BiomeId)
        {
            SelectedBiome = Biomes.FirstOrDefault(b => b.Id == value.BiomeId);
        }

        SyncSelectedLayerMaterialSlotOption();
        RefreshCommandStates();
    }

    partial void OnShowMaskOverlayChanged(bool value)
    {
        if (_editorState.ShowMaskOverlay != value)
        {
            _editorState.ShowMaskOverlay = value;
        }
    }

    partial void OnSelectedLayerIndexChanged(int value)
    {
        if (_editorState.SelectedRuleIndex != value)
        {
            _editorState.SelectedRuleIndex = value;
        }

        SelectLayerByGlobalIndex(value);
    }

    partial void OnSelectedLayerMaterialSlotOptionChanged(MaterialSlotOptionViewModel? value)
    {
        if (value == null || SelectedLayer == null)
        {
            return;
        }

        BiomeRuleLayer? sourceLayer = _service.Layers.FirstOrDefault(layer => layer.Id == SelectedLayer.Id);
        if (SelectedLayer.MaterialSlotIndex != value.Index
            || sourceLayer?.MaterialSlotIndex != value.Index)
        {
            SelectedLayer.MaterialSlotIndex = value.Index;
            if (sourceLayer != null)
            {
                sourceLayer.MaterialSlotIndex = value.Index;
            }

            SelectedLayer.NotifyMaterialPreviewChanged();
            EditorDirtyState.Instance.MarkDirty(EditorDirtyResource.BiomeSettings);
            OnPropertyChanged(nameof(SelectedLayerMaterialTitle));
            OnPropertyChanged(nameof(SelectedLayerMaterialDetail));
            _service.NotifyMutated();
        }
    }

    public void Dispose()
    {
        if (_observedLayer != null)
        {
            _observedLayer.PropertyChanged -= OnSelectedLayerPropertyChanged;
        }

        _service.StateChanged -= OnServiceStateChanged;
        _editorState.OverlayChanged -= OnOverlayChanged;
        _editorState.RuleSelectionChanged -= OnRuleSelectionChanged;
        _editorState.BiomeSelectionChanged -= OnBiomeSelectionChanged;
        _materialSlotManager.SlotsChanged -= OnMaterialSlotsChanged;
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        RefreshCollections();
        RefreshMaterialSlots();
        RefreshCommandStates();
    }

    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
    }

    private void OnRuleSelectionChanged(object? sender, EventArgs e)
    {
        int index = _editorState.SelectedRuleIndex;
        if (SelectedLayerIndex != index)
        {
            SelectedLayerIndex = index;
        }
    }

    private void OnBiomeSelectionChanged(object? sender, EventArgs e)
    {
        int id = _editorState.CurrentBiomeId;
        var match = Biomes.FirstOrDefault(b => b.Id == id);
        if (SelectedBiome != match)
        {
            SelectedBiome = match;
        }
    }

    private void OnMaterialSlotsChanged(object? sender, EventArgs e)
    {
        RefreshMaterialSlots();
        SyncSelectedLayerMaterialSlotOption();

        foreach (var layer in Layers)
        {
            layer.NotifyMaterialPreviewChanged();
        }
    }

    private void OnSelectedLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RuleViewModel.SelectedModifier)
            or nameof(RuleViewModel.SelectedModifierIndex)
            or nameof(RuleViewModel.MaterialSlotIndex))
        {
            SyncSelectedLayerMaterialSlotOption();
            RefreshCommandStates();
        }
    }

    private void RefreshCollections()
    {
        // Sync Biomes
        var sourceBiomes = _service.Biomes;
        for (int i = 0; i < sourceBiomes.Count; i++)
        {
            if (i < Biomes.Count && Biomes[i].Id == sourceBiomes[i].Id)
            {
                Biomes[i].SyncFromSource();
            }
            else
            {
                // Insert or replace
                if (i < Biomes.Count)
                {
                    Biomes[i] = new BiomeDefinitionViewModel(sourceBiomes[i]);
                }
                else
                {
                    Biomes.Add(new BiomeDefinitionViewModel(sourceBiomes[i]));
                }
            }
        }

        while (Biomes.Count > sourceBiomes.Count)
        {
            Biomes.RemoveAt(Biomes.Count - 1);
        }

        // Sync Layers
        var sourceLayers = _service.Layers;
        for (int i = 0; i < sourceLayers.Count; i++)
        {
            if (i < Layers.Count && Layers[i].Id == sourceLayers[i].Id)
            {
                Layers[i].SyncFromSource(i);
            }
            else
            {
                // Insert or replace
                if (i < Layers.Count)
                {
                    Layers[i] = new RuleViewModel(sourceLayers[i], i, _resourceSessionProvider);
                }
                else
                {
                    Layers.Add(new RuleViewModel(sourceLayers[i], i, _resourceSessionProvider));
                }
            }
        }

        while (Layers.Count > sourceLayers.Count)
        {
            Layers.RemoveAt(Layers.Count - 1);
        }

        // Re-sync selected biome
        if (SelectedBiome != null)
        {
            var match = Biomes.FirstOrDefault(b => b.Id == SelectedBiome.Id);
            if (match != SelectedBiome)
            {
                SelectedBiome = match;
            }
        }
        else
        {
            SelectedBiome = Biomes.FirstOrDefault();
        }

        RefreshVisibleLayers();
        SelectLayerByGlobalIndex(_editorState.SelectedRuleIndex);
        SyncSelectedLayerMaterialSlotOption();
        RefreshCommandStates();
    }

    private void RefreshVisibleLayers()
    {
        int? selectedBiomeId = SelectedBiome?.Id;
        var sourceLayers = selectedBiomeId.HasValue
            ? Layers.Where(layer => layer.BiomeId == selectedBiomeId.Value).ToList()
            : [];

        for (int i = 0; i < sourceLayers.Count; i++)
        {
            if (i < VisibleLayers.Count && ReferenceEquals(VisibleLayers[i], sourceLayers[i]))
            {
                continue;
            }

            if (i < VisibleLayers.Count && VisibleLayers[i].Id == sourceLayers[i].Id)
            {
                // The backing RuleViewModel already refreshed in Layers via SyncFromSource.
                // Keep the same instance in the filtered list so slider drags don't clear selection.
                continue;
            }

            if (i < VisibleLayers.Count)
            {
                VisibleLayers[i] = sourceLayers[i];
            }
            else
            {
                VisibleLayers.Add(sourceLayers[i]);
            }
        }

        while (VisibleLayers.Count > sourceLayers.Count)
        {
            VisibleLayers.RemoveAt(VisibleLayers.Count - 1);
        }

        OnPropertyChanged(nameof(SelectedBiomeLayerSummary));
    }

    private void RefreshMaterialSlots()
    {
        RepairSelectedDefaultBaseMaterialSlot();

        var sourceSlots = _materialSlotManager
            .GetActiveSlots()
            .OrderBy(static slot => slot.Index)
            .Select(CreateMaterialSlotOption)
            .ToList();

        if (SelectedLayer != null && sourceSlots.All(slot => slot.Index != SelectedLayer.MaterialSlotIndex))
        {
            sourceSlots.Insert(0, CreateMaterialSlotOption(_materialSlotManager[SelectedLayer.MaterialSlotIndex]));
        }

        for (int i = 0; i < sourceSlots.Count; i++)
        {
            if (i < AvailableMaterialSlots.Count && Equals(AvailableMaterialSlots[i], sourceSlots[i]))
            {
                continue;
            }

            if (i < AvailableMaterialSlots.Count && AvailableMaterialSlots[i].Index == sourceSlots[i].Index)
            {
                // Same logical slot, just refreshed details; avoid replacing the item unless content changed.
                if (AvailableMaterialSlots[i].Name == sourceSlots[i].Name
                    && AvailableMaterialSlots[i].HasNormal == sourceSlots[i].HasNormal
                    && AvailableMaterialSlots[i].HasProperties == sourceSlots[i].HasProperties)
                {
                    continue;
                }

                AvailableMaterialSlots[i] = sourceSlots[i];
            }
            else if (i < AvailableMaterialSlots.Count)
            {
                AvailableMaterialSlots[i] = sourceSlots[i];
            }
            else
            {
                AvailableMaterialSlots.Add(sourceSlots[i]);
            }
        }

        while (AvailableMaterialSlots.Count > sourceSlots.Count)
        {
            AvailableMaterialSlots.RemoveAt(AvailableMaterialSlots.Count - 1);
        }

        SyncSelectedLayerMaterialSlotOption();
    }

    private void RepairSelectedDefaultBaseMaterialSlot()
    {
        if (SelectedLayer == null
            || !string.Equals(SelectedBiome?.Name, "Default Biome", StringComparison.Ordinal)
            || !string.Equals(SelectedLayer.Name, "Default Base", StringComparison.OrdinalIgnoreCase)
            || !IsMaterialSlotEmpty(SelectedLayer.MaterialSlotIndex))
        {
            return;
        }

        int firstActiveSlotIndex = _materialSlotManager
            .GetActiveSlots()
            .Select(static slot => slot.Index)
            .DefaultIfEmpty(-1)
            .First();
        if (firstActiveSlotIndex < 0)
            return;

        SelectedLayer.MaterialSlotIndex = firstActiveSlotIndex;
        SelectedLayer.NotifyMaterialPreviewChanged();
        EditorDirtyState.Instance.MarkDirty(EditorDirtyResource.BiomeSettings);
        _service.NotifyMutated();
    }

    private bool IsMaterialSlotEmpty(int materialSlotIndex)
    {
        return (uint)materialSlotIndex >= 256
            || _materialSlotManager[materialSlotIndex].IsEmpty;
    }

    private void SyncSelectedLayerMaterialSlotOption()
    {
        MaterialSlotOptionViewModel? match = null;
        if (SelectedLayer != null)
        {
            match = AvailableMaterialSlots.FirstOrDefault(slot => slot.Index == SelectedLayer.MaterialSlotIndex);
        }

        if (SelectedLayerMaterialSlotOption != match)
        {
            SelectedLayerMaterialSlotOption = match;
        }

        if (SelectedLayer != null)
        {
            if (_materialSlotManager.SelectedSlotIndex != SelectedLayer.MaterialSlotIndex)
            {
                _materialSlotManager.SelectedSlotIndex = SelectedLayer.MaterialSlotIndex;
            }

            if (_editorState.SelectedMaterialSlotIndex != SelectedLayer.MaterialSlotIndex)
            {
                _editorState.SelectedMaterialSlotIndex = SelectedLayer.MaterialSlotIndex;
            }
        }

        OnPropertyChanged(nameof(SelectedLayerMaterialTitle));
        OnPropertyChanged(nameof(SelectedLayerMaterialDetail));
    }

    private void SelectLayerByGlobalIndex(int index)
    {
        RuleViewModel? matchingLayer = index >= 0 && index < Layers.Count ? Layers[index] : null;
        if (matchingLayer != null && SelectedBiome?.Id != matchingLayer.BiomeId)
        {
            SelectedBiome = Biomes.FirstOrDefault(b => b.Id == matchingLayer.BiomeId);
        }

        if (SelectedLayer != matchingLayer)
        {
            SelectedLayer = matchingLayer;
        }
    }

    private void MoveSelectedLayer(int direction)
    {
        if (SelectedLayer == null)
        {
            return;
        }

        int localIndex = GetSelectedLayerLocalIndex();
        int targetLocalIndex = localIndex + direction;
        if (localIndex < 0 || targetLocalIndex < 0 || targetLocalIndex >= VisibleLayers.Count)
        {
            return;
        }

        RuleViewModel targetLayer = VisibleLayers[targetLocalIndex];
        int fromGlobalIndex = Layers.IndexOf(SelectedLayer);
        int toGlobalIndex = Layers.IndexOf(targetLayer);
        if (fromGlobalIndex < 0 || toGlobalIndex < 0)
        {
            return;
        }

        _service.MoveLayer(fromGlobalIndex, toGlobalIndex);
    }

    private int GetSelectedLayerLocalIndex()
    {
        return SelectedLayer == null ? -1 : VisibleLayers.IndexOf(SelectedLayer);
    }

    private void RefreshCommandStates()
    {
        RemoveBiomeCommand.NotifyCanExecuteChanged();
        AddLayerCommand.NotifyCanExecuteChanged();
        RemoveSelectedLayerCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerUpCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerDownCommand.NotifyCanExecuteChanged();
        AddModifierCommand.NotifyCanExecuteChanged();
        RemoveSelectedModifierCommand.NotifyCanExecuteChanged();
        MoveSelectedModifierUpCommand.NotifyCanExecuteChanged();
        MoveSelectedModifierDownCommand.NotifyCanExecuteChanged();
    }

    private static MaterialSlotOptionViewModel CreateMaterialSlotOption(MaterialSlot slot)
    {
        return new MaterialSlotOptionViewModel(
            slot.Index,
            GetMaterialSlotDisplayName(slot),
            !string.IsNullOrWhiteSpace(slot.NormalTexturePath),
            !string.IsNullOrWhiteSpace(slot.PropertiesTexturePath));
    }

    private static string GetMaterialSlotDisplayName(MaterialSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.Name)
            && !slot.Name.StartsWith("Texture ", StringComparison.Ordinal))
        {
            return slot.Name;
        }

        if (!string.IsNullOrWhiteSpace(slot.AlbedoTexturePath))
        {
            return Path.GetFileNameWithoutExtension(slot.AlbedoTexturePath);
        }

        return "未分配材质";
    }

    public void NotifyMaterialPreviewsChanged()
    {
        foreach (RuleViewModel layer in Layers)
        {
            layer.NotifyMaterialPreviewChanged();
        }
    }
}
