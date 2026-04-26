#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Terrain.Editor.Services;

namespace Terrain.Editor.ViewModels;

/// <summary>
/// Wraps <see cref="ClimateRuleService"/> for Avalonia data binding.
/// Exposes Climates/Rules collections, CRUD commands, and selected rule state.
/// </summary>
public sealed partial class ClimateViewModel : ObservableObject, IDisposable
{
    private readonly ClimateRuleService _service = ClimateRuleService.Instance;
    private readonly EditorState _editorState = EditorState.Instance;

    public ObservableCollection<ClimateDefinitionViewModel> Climates { get; } = new();

    public ObservableCollection<RuleViewModel> Rules { get; } = new();

    [ObservableProperty]
    private ClimateDefinitionViewModel? _selectedClimate;

    [ObservableProperty]
    private RuleViewModel? _selectedRule;

    [ObservableProperty]
    private bool _showMaskOverlay = true;

    [ObservableProperty]
    private int _selectedRuleIndex = -1;

    public ClimateViewModel()
    {
        RefreshCollections();

        _service.StateChanged += OnServiceStateChanged;
        _editorState.OverlayChanged += OnOverlayChanged;
        _editorState.RuleSelectionChanged += OnRuleSelectionChanged;
        _editorState.ClimateSelectionChanged += OnClimateSelectionChanged;

        ShowMaskOverlay = _editorState.ShowMaskOverlay;
        SelectedRuleIndex = _editorState.SelectedRuleIndex;

        // Sync initial climate selection from EditorState
        int initialClimateId = _editorState.CurrentClimateId;
        SelectedClimate = Climates.FirstOrDefault(c => c.Id == initialClimateId) ?? Climates.FirstOrDefault();
    }

    // ── Climate CRUD ──

    [RelayCommand]
    private void AddClimate()
    {
        _service.AddClimate();
    }

    [RelayCommand]
    private void RemoveClimate(int climateId)
    {
        _service.RemoveClimate(climateId);
    }

    // ── Rule CRUD ──

    [RelayCommand]
    private void AddRule(int climateId)
    {
        // Fallback to the no-argument overload which picks the first climate.
        if (climateId < 0 || (climateId == 0 && SelectedClimate == null))
        {
            _service.AddRule();
        }
        else
        {
            _service.AddRule(climateId);
        }
    }

    [RelayCommand]
    private void RemoveRuleAt(int index)
    {
        _service.RemoveRuleAt(index);
    }

    [RelayCommand]
    private void MoveRuleUp(int index)
    {
        if (index > 0)
        {
            _service.MoveRule(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveRuleDown(int index)
    {
        if (index < Rules.Count - 1)
        {
            _service.MoveRule(index, index + 1);
        }
    }

    partial void OnSelectedClimateChanged(ClimateDefinitionViewModel? value)
    {
        if (value != null && _editorState.CurrentClimateId != value.Id)
        {
            _editorState.CurrentClimateId = value.Id;
        }
    }

    partial void OnSelectedRuleChanged(RuleViewModel? value)
    {
        int newIndex = value != null ? Rules.IndexOf(value) : -1;

        if (SelectedRuleIndex != newIndex)
        {
            SelectedRuleIndex = newIndex;
        }

        if (_editorState.SelectedRuleIndex != newIndex)
        {
            _editorState.SelectedRuleIndex = newIndex;
        }
    }

    partial void OnShowMaskOverlayChanged(bool value)
    {
        if (_editorState.ShowMaskOverlay != value)
        {
            _editorState.ShowMaskOverlay = value;
        }
    }

    partial void OnSelectedRuleIndexChanged(int value)
    {
        if (_editorState.SelectedRuleIndex != value)
        {
            _editorState.SelectedRuleIndex = value;
        }

        // Sync the selected RuleViewModel with the index
        var matchingRule = value >= 0 && value < Rules.Count ? Rules[value] : null;
        if (SelectedRule != matchingRule)
        {
            SelectedRule = matchingRule;
        }
    }

    public void Dispose()
    {
        _service.StateChanged -= OnServiceStateChanged;
        _editorState.OverlayChanged -= OnOverlayChanged;
        _editorState.RuleSelectionChanged -= OnRuleSelectionChanged;
        _editorState.ClimateSelectionChanged -= OnClimateSelectionChanged;
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        RefreshCollections();
    }

    private void OnOverlayChanged(object? sender, EventArgs e)
    {
        ShowMaskOverlay = _editorState.ShowMaskOverlay;
    }

    private void OnRuleSelectionChanged(object? sender, EventArgs e)
    {
        int index = _editorState.SelectedRuleIndex;
        if (SelectedRuleIndex != index)
        {
            SelectedRuleIndex = index;
        }
    }

    private void OnClimateSelectionChanged(object? sender, EventArgs e)
    {
        int id = _editorState.CurrentClimateId;
        var match = Climates.FirstOrDefault(c => c.Id == id);
        if (SelectedClimate != match)
        {
            SelectedClimate = match;
        }
    }

    private void RefreshCollections()
    {
        // Sync Climates
        var sourceClimates = _service.Climates;
        for (int i = 0; i < sourceClimates.Count; i++)
        {
            if (i < Climates.Count && Climates[i].Id == sourceClimates[i].Id)
            {
                Climates[i].SyncFromSource();
            }
            else
            {
                // Insert or replace
                if (i < Climates.Count)
                {
                    Climates[i] = new ClimateDefinitionViewModel(sourceClimates[i]);
                }
                else
                {
                    Climates.Add(new ClimateDefinitionViewModel(sourceClimates[i]));
                }
            }
        }

        while (Climates.Count > sourceClimates.Count)
        {
            Climates.RemoveAt(Climates.Count - 1);
        }

        // Sync Rules
        var sourceRules = _service.Rules;
        for (int i = 0; i < sourceRules.Count; i++)
        {
            if (i < Rules.Count && Rules[i].Id == sourceRules[i].Id)
            {
                Rules[i].SyncFromSource(i);
            }
            else
            {
                // Insert or replace
                if (i < Rules.Count)
                {
                    Rules[i] = new RuleViewModel(sourceRules[i], i);
                }
                else
                {
                    Rules.Add(new RuleViewModel(sourceRules[i], i));
                }
            }
        }

        while (Rules.Count > sourceRules.Count)
        {
            Rules.RemoveAt(Rules.Count - 1);
        }

        // Re-sync selected climate
        if (SelectedClimate != null)
        {
            var match = Climates.FirstOrDefault(c => c.Id == SelectedClimate.Id);
            if (match != SelectedClimate)
            {
                SelectedClimate = match;
            }
        }
        else
        {
            SelectedClimate = Climates.FirstOrDefault();
        }

        // Re-sync selected rule by index
        int currentIdx = _editorState.SelectedRuleIndex;
        SelectedRule = currentIdx >= 0 && currentIdx < Rules.Count ? Rules[currentIdx] : null;
    }
}
