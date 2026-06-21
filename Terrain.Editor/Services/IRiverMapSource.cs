#nullable enable

using System;
using Terrain.Editor.Models;

namespace Terrain.Editor.Services;

internal interface IRiverMapSource
{
    event EventHandler? RiverMapChanged;

    RiverCell[,]? RiverMap { get; }

    string? CurrentRiverMapPath { get; }

    float RiverMinWidth { get; }

    float RiverMaxWidth { get; }
}
