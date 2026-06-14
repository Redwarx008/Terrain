#nullable enable
using System;

namespace Terrain.Editor.Services;

public sealed class EditorDirtyState
{
    private static readonly Lazy<EditorDirtyState> InstanceFactory = new(() => new EditorDirtyState());

    public static EditorDirtyState Instance => InstanceFactory.Value;

    private EditorDirtyState()
    {
    }

    public bool IsDirty { get; private set; }

    public event EventHandler? DirtyChanged;

    public void MarkDirty()
    {
        if (IsDirty)
            return;

        IsDirty = true;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearDirty()
    {
        if (!IsDirty)
            return;

        IsDirty = false;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }
}
