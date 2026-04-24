#nullable enable

using System;

namespace Terrain.Editor.Rendering.SharedTexture;

public readonly record struct SharedTextureFrame(
    nint SharedHandle,
    string HandleType,
    int Width,
    int Height,
    double DpiScale,
    ulong Version,
    string Format,
    bool UsesKeyedMutex,
    uint KeyedMutexAcquireKey,
    uint KeyedMutexReleaseKey,
    string? SharedNtHandleName,
    string? DiagnosticMessage);
