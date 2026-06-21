#nullable enable

using System;
using Terrain.Rivers;

namespace Terrain.Editor.Services;

internal interface IRiverMapSource
{
    event EventHandler? RiverMapChanged;

    RiverCell[,]? RiverMap { get; }

    string? CurrentRiverMapPath { get; }

    float RiverMinWidth { get; }

    float RiverMaxWidth { get; }
}
