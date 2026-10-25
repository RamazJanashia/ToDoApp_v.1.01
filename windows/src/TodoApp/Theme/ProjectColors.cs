using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TodoCore;

namespace TodoApp;

/// <summary>
/// Project colours as brushes: the Mac's <c>hex</c> in Dark and its deeper <c>lightHex</c> variant in Light
/// (Theme.swift). UI thread only.
/// </summary>
public static class ProjectColors
{
    private static readonly Dictionary<(ProjectColor Color, bool Light), SolidColorBrush> Cache = new();

    /// <summary>Pass the element's <c>ActualTheme</c>; anything other than Light gets the dark value.</summary>
    public static SolidColorBrush Brush(ProjectColor color, ElementTheme theme)
    {
        var light = theme == ElementTheme.Light;
        if (!Cache.TryGetValue((color, light), out var brush))
        {
            var hex = light ? LightHex(color) : color.DarkHex();
            brush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex));
            Cache[(color, light)] = brush;
        }

        return brush;
    }

    /// <summary>Deeper variants that hold up on a white background.</summary>
    private static uint LightHex(ProjectColor color) => color switch
    {
        ProjectColor.Blue => 0x2F6FE4,
        ProjectColor.Green => 0x22A06B,
        ProjectColor.Red => 0xE0443C,
        ProjectColor.Orange => 0xE5822B,
        ProjectColor.Yellow => 0xD9A400,
        ProjectColor.Purple => 0x8B5CF6,
        ProjectColor.Pink => 0xE0529C,
        ProjectColor.Teal => 0x1FA99A,
        ProjectColor.Graphite => 0x8E8E93,
        ProjectColor.White => 0x3A3A3C,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };
}
