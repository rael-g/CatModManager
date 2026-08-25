using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CatModManager.Core.Services;
using CatModManager.Theme;

namespace CatModManager.Ui.Converters;

/// <summary>Paints a storefront badge in that store's brand colour.</summary>
public class StoreBadgeBrushConverter : IValueConverter
{
    public static readonly StoreBadgeBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "GOG"  => CmmPalette.Brushes.StoreGog,
            "Epic" => CmmPalette.Brushes.StoreEpic,
            _      => CmmPalette.Brushes.StoreSteam,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Names the game mode a scan matched, or "Generic" when nothing matched and the user must pick.
/// </summary>
public class GameModeNameConverter : IValueConverter
{
    public static readonly GameModeNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as IGameSupport)?.DisplayName ?? "Generic";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Green when a game mode was auto-detected, neutral when the user still has to choose.</summary>
public class GameModeBrushConverter : IValueConverter
{
    public static readonly GameModeBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IGameSupport ? CmmPalette.Brushes.StatusActive : CmmPalette.Brushes.SurfaceSelected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
