// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AdvGenNoSqlServer.Examples.Messages.Wpf;

/// <summary>Maps a username to a stable accent color so each chatter is visually distinct.</summary>
public sealed class UserToBrushConverter : IValueConverter
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x3B, 0x82, 0xF6), // blue
        Color.FromRgb(0x10, 0xB9, 0x81), // emerald
        Color.FromRgb(0xF5, 0x9E, 0x0B), // amber
        Color.FromRgb(0xEF, 0x44, 0x44), // red
        Color.FromRgb(0x8B, 0x5C, 0xF6), // violet
        Color.FromRgb(0xEC, 0x48, 0x99), // pink
        Color.FromRgb(0x06, 0xB6, 0xD4), // cyan
        Color.FromRgb(0x84, 0xCC, 0x16), // lime
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        var hash = 0;
        foreach (var c in name) hash = (hash * 31 + c) & 0x7FFFFFFF;
        var color = Palette[hash % Palette.Length];
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}