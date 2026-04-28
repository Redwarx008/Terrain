#nullable enable

using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Terrain.Editor.Converters;

/// <summary>
/// Converts null/false to true and non-null/true to false (invert null check).
/// Usage: IsVisible="{Binding PreviewImage, Converter={StaticResource InvertedNullToBool}}"
/// means: visible when PreviewImage IS null.
/// </summary>
public sealed class InvertedNullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}