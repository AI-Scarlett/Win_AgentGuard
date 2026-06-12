using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace AgentGuard.App.Services;

public static class AppThemeService
{
    private static bool _watchingSystemTheme;
    private static string _currentPalette = "porcelain";
    private static string _currentAppearance = "system";

    public static readonly IReadOnlyList<ThemeChoice> Palettes =
    [
        new("porcelain", "Default White", "纯白默认", "#496AAF", "#FFFFFF", "#E4E8F0"),
        new("aurora", "Aurora Cyan", "极光青绿", "#21C8D6", "#34D990", "#EAFBFB"),
        new("rose", "Rose Signal", "玫瑰行动", "#EE448D", "#B35DED", "#FFF0F8"),
        new("shield", "Blue Shield", "蓝盾主题色", "#496AAF", "#649CFF", "#E9F2FF")
    ];

    public static readonly IReadOnlyList<AppearanceChoice> Appearances =
    [
        new("system", "Follow system", "跟随系统"),
        new("light", "Light", "浅色"),
        new("dark", "Dark", "深色")
    ];

    public static void Apply(string paletteName, string appearanceMode)
    {
        _currentPalette = paletteName;
        _currentAppearance = appearanceMode;
        EnsureSystemThemeWatcher();
        var palette = PaletteSpec.Resolve(paletteName);
        var dark = appearanceMode.Equals("dark", StringComparison.OrdinalIgnoreCase) ||
                   appearanceMode.Equals("system", StringComparison.OrdinalIgnoreCase) && IsSystemDark();
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        Put(resources, "AppBackgroundBrush", palette.Brush(dark ? palette.BackgroundDark : palette.BackgroundLight));
        Put(resources, "SurfaceBrush", palette.Brush(dark ? palette.SurfaceDark : palette.SurfaceLight));
        Put(resources, "SidebarBrush", palette.Brush(dark ? palette.SidebarDark : palette.SidebarLight));
        Put(resources, "CardBrush", palette.Brush(dark ? palette.CardDark : palette.CardLight));
        Put(resources, "ElevatedBrush", palette.Brush(dark ? palette.ElevatedDark : palette.ElevatedLight));
        Put(resources, "HoverBrush", palette.Brush(dark ? palette.HoverDark : palette.HoverLight));
        Put(resources, "BorderBrush", palette.Brush(dark ? palette.BorderDark : palette.BorderLight));
        Put(resources, "TextPrimaryBrush", palette.Brush(dark ? palette.TextPrimaryDark : palette.TextPrimaryLight));
        Put(resources, "TextSecondaryBrush", palette.Brush(dark ? palette.TextSecondaryDark : palette.TextSecondaryLight));
        Put(resources, "TextTertiaryBrush", palette.Brush(dark ? palette.TextTertiaryDark : palette.TextTertiaryLight));
        Put(resources, "AccentBrush", palette.Brush(palette.Accent));
        Put(resources, "InfoBrush", palette.Brush(palette.Info));
        Put(resources, "SuccessBrush", palette.Brush(palette.Success));
        Put(resources, "WarningBrush", palette.Brush("#F0A93A"));
        Put(resources, "DangerBrush", palette.Brush("#F46565"));
        Put(resources, "AccentSoftBrush", palette.BrushWithOpacity(palette.Accent, dark ? 0.22 : 0.12));
        Put(resources, "SuccessSoftBrush", palette.BrushWithOpacity(palette.Success, dark ? 0.20 : 0.11));
        Put(resources, "AccentGradientBrush", new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(palette.Accent),
            (Color)ColorConverter.ConvertFromString(palette.Info),
            45));
        Put(resources, "CanvasGradientBrush", new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(dark ? palette.SurfaceDark : palette.SurfaceLight),
            (Color)ColorConverter.ConvertFromString(dark ? palette.BackgroundDark : palette.BackgroundLight),
            135));
    }

    private static void EnsureSystemThemeWatcher()
    {
        if (_watchingSystemTheme) return;
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle) ||
                !_currentAppearance.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Application.Current?.Dispatcher.BeginInvoke(() => Apply(_currentPalette, _currentAppearance));
        };
        _watchingSystemTheme = true;
    }

    private static void Put(ResourceDictionary resources, string key, object value) => resources[key] = value;

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public sealed record ThemeChoice(string Code, string EnglishName, string ChineseName, string Primary, string Secondary, string Background)
    {
        public string Display => $"{EnglishName} / {ChineseName}";
    }

    public sealed record AppearanceChoice(string Code, string EnglishName, string ChineseName)
    {
        public string Display => $"{EnglishName} / {ChineseName}";
    }

    private sealed record PaletteSpec(
        string BackgroundLight, string BackgroundDark,
        string SurfaceLight, string SurfaceDark,
        string SidebarLight, string SidebarDark,
        string CardLight, string CardDark,
        string ElevatedLight, string ElevatedDark,
        string HoverLight, string HoverDark,
        string BorderLight, string BorderDark,
        string TextPrimaryLight, string TextPrimaryDark,
        string TextSecondaryLight, string TextSecondaryDark,
        string TextTertiaryLight, string TextTertiaryDark,
        string Accent, string Success, string Info)
    {
        public SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

        public SolidColorBrush BrushWithOpacity(string value, double opacity)
        {
            var brush = Brush(value);
            brush.Opacity = opacity;
            return brush;
        }

        public static PaletteSpec Resolve(string name) => name.ToLowerInvariant() switch
        {
            "aurora" => new(
                "#EAFBFB", "#0A1315", "#F8FFFF", "#102024", "#DFF8F5", "#0C1A1C",
                "#FFFFFF", "#14272B", "#FFFFFF", "#193238", "#DDF8F5", "#17363A",
                "#B8D7D9", "#356065", "#10252C", "#E7FAF8", "#55717A", "#A6CACD", "#7E989F", "#749CA1",
                "#21C8D6", "#34D990", "#4BA3FF"),
            "rose" => new(
                "#FFF0F8", "#1C0B14", "#FFF9FC", "#26111C", "#FFE9F4", "#20101A",
                "#FFFFFF", "#2E1421", "#FFFFFF", "#361825", "#FCE1EF", "#3A1B2A",
                "#E1B7CC", "#694054", "#2E121F", "#FFEAF5", "#805066", "#DCA6C0", "#A67089", "#B87D99",
                "#EE448D", "#2FCA86", "#8C80FF"),
            "shield" => new(
                "#E9F2FF", "#050A15", "#F8FBFF", "#0A1120", "#E2EDFF", "#07101F",
                "#FFFFFF", "#0D172A", "#FFFFFF", "#11203A", "#DDEAFF", "#142746",
                "#B6C9E8", "#36547D", "#0C182B", "#E8F2FF", "#4F6B95", "#A8C2E5", "#7D95B8", "#7690B8",
                "#496AAF", "#649CFF", "#4085FF"),
            _ => new(
                "#FEFEFF", "#0C0D0F", "#FFFFFF", "#121418", "#FAFBFD", "#0F1116",
                "#FFFFFF", "#181A1F", "#FFFFFF", "#1E2127", "#F3F5F8", "#252932",
                "#E0E4EA", "#3C424E", "#0D121A", "#F0F2F5", "#4F5C6E", "#B0BAC9", "#8490A3", "#79869A",
                "#496AAF", "#649CFF", "#4085FF")
        };
    }
}
