#nullable enable

using System;
using System.ComponentModel;

namespace Terrain.Editor.Rendering.SharedTexture;

public interface ISharedTextureViewportSource : INotifyPropertyChanged
{
    event EventHandler? FrameChanged;

    SharedTextureFrame? CurrentFrame { get; }

    string Status { get; }

    bool IsAvailable { get; }

    void RequestResize(int pixelWidth, int pixelHeight, double dpiScale);
}
