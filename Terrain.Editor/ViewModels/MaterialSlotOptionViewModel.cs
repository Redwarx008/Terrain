#nullable enable

namespace Terrain.Editor.ViewModels;

public sealed record MaterialSlotOptionViewModel(
    int Index,
    string Name,
    bool HasNormal,
    bool HasProperties)
{
    public string Label => Name;

    public string Detail => HasNormal
        ? (HasProperties ? "Albedo / Normal / Properties" : "Albedo / Normal")
        : (HasProperties ? "Albedo / Properties" : "Albedo only");
}
