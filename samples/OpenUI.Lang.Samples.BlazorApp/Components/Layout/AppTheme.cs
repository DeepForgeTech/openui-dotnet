using MudBlazor;

namespace OpenUI.Lang.Samples.BlazorApp.Components.Layout;

/// <summary>The sample's look: quiet neutrals, one muted blue, and a small type scale.</summary>
public static class AppTheme
{
    private static readonly string[] Font = ["-apple-system", "BlinkMacSystemFont", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    /// <summary>The theme the layout draws.</summary>
    public static MudTheme Instance { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#2f5d8a",
            PrimaryContrastText = "#ffffff",
            Secondary = "#6b7c8f",
            SecondaryContrastText = "#ffffff",
            Tertiary = "#2e7d6b",
            TertiaryContrastText = "#ffffff",
            Info = "#2f6fa7",
            Success = "#2e7d4f",
            Warning = "#a8700f",
            Error = "#b93a32",
            Background = "#f6f7f9",
            BackgroundGray = "#eef0f3",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#1f2933",
            DrawerBackground = "#ffffff",
            DrawerText = "#364152",
            DrawerIcon = "#5b6b7b",
            TextPrimary = "#1f2933",
            TextSecondary = "#5b6b7b",
            ActionDefault = "#5b6b7b",
            LinesDefault = "#e3e6eb",
            LinesInputs = "#c9ced6",
            TableLines = "#e3e6eb",
            Divider = "#e3e6eb",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7fb0de",
            PrimaryContrastText = "#0f1720",
            Secondary = "#9aa8b6",
            SecondaryContrastText = "#0f1720",
            Tertiary = "#6cc2ac",
            TertiaryContrastText = "#0f1720",
            Info = "#7fb0de",
            InfoContrastText = "#0f1720",
            Success = "#6cc28f",
            SuccessContrastText = "#0f1720",
            Warning = "#e0b35c",
            WarningContrastText = "#0f1720",
            Error = "#f08a80",
            ErrorContrastText = "#0f1720",
            Background = "#12161c",
            BackgroundGray = "#0d1116",
            Surface = "#1a2028",
            AppbarBackground = "#1a2028",
            AppbarText = "#e6eaef",
            DrawerBackground = "#1a2028",
            DrawerText = "#c9d1da",
            DrawerIcon = "#9aa8b6",
            TextPrimary = "#e6eaef",
            TextSecondary = "#9aa8b6",
            ActionDefault = "#9aa8b6",
            LinesDefault = "#2b333d",
            LinesInputs = "#3a444f",
            TableLines = "#2b333d",
            Divider = "#2b333d",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "248px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Font, FontSize = ".875rem", LineHeight = "1.5", LetterSpacing = "normal" },
            H4 = new H4Typography { FontFamily = Font, FontSize = "1.5rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = Font, FontSize = "1.0625rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "normal" },
            H6 = new H6Typography { FontFamily = Font, FontSize = ".9375rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "normal" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Font, FontSize = ".9375rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = "normal" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Font, FontSize = ".8125rem", FontWeight = "600", LineHeight = "1.5", LetterSpacing = "normal" },
            Body1 = new Body1Typography { FontFamily = Font, FontSize = ".875rem", LineHeight = "1.55", LetterSpacing = "normal" },
            Body2 = new Body2Typography { FontFamily = Font, FontSize = ".8125rem", LineHeight = "1.5", LetterSpacing = "normal" },
            Caption = new CaptionTypography { FontFamily = Font, FontSize = ".75rem", LineHeight = "1.5", LetterSpacing = "normal" },
            Button = new ButtonTypography { FontFamily = Font, FontSize = ".8125rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = "normal", TextTransform = "none" },
        },
    };
}
