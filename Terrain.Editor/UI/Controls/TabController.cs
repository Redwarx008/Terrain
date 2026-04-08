#nullable enable

using System;
using System.Collections.Generic;

namespace Terrain.Editor.UI.Controls;

public sealed class TabItemState
{
    public TabItemState(string id, string title, string? icon = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Tab id cannot be empty.", nameof(id));

        Id = id;
        Title = title;
        Icon = icon;
    }

    public string Id { get; }

    public string Title { get; set; }

    public string? Icon { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsClosable { get; set; } = true;

    public bool IsClosed { get; set; }

    public bool RequestActivate { get; set; }
}

public sealed class TabController
{
    private readonly List<TabItemState> items = new();
    private readonly Dictionary<string, TabItemState> index = new(StringComparer.Ordinal);

    public IReadOnlyList<TabItemState> Items => items;

    public string? ActiveTabId { get; private set; }

    public TabItemState Register(TabItemState tab)
    {
        if (index.ContainsKey(tab.Id))
            return index[tab.Id];

        items.Add(tab);
        index[tab.Id] = tab;
        return tab;
    }

    public TabItemState? Get(string id)
    {
        return index.GetValueOrDefault(id);
    }

    public TabItemState GetRequired(string id)
    {
        if (!index.TryGetValue(id, out var tab))
            throw new InvalidOperationException($"Tab '{id}' has not been registered.");

        return tab;
    }

    public void SetVisible(string id, bool visible)
    {
        var tab = GetRequired(id);
        tab.IsVisible = visible;
    }

    public void RequestActivate(string id)
    {
        // One-shot activation request consumed by the render layer.
        var tab = GetRequired(id);
        tab.RequestActivate = true;
    }

    public void SetActive(string id)
    {
        ActiveTabId = id;
    }

    public void ClearActive(string id)
    {
        if (ActiveTabId == id)
        {
            ActiveTabId = null;
        }
    }
}
