using Microsoft.Win32;

namespace JIE剪切板.Services;

public static class ThemeService
{
    public static Color ThemeColor { get; private set; } = ColorTranslator.FromHtml("#0078D7");
    public static string ThemeMode { get; private set; } = "FollowSystem";
    public static bool IsDarkMode { get; private set; }
    public static Font GlobalFont { get; private set; } = SystemFonts.DefaultFont;

    // Colors derived from mode
    public static Color WindowBackground => IsDarkMode ? Color.FromArgb(30, 30, 30) : Color.White;
    public static Color SidebarBackground => IsDarkMode ? Color.FromArgb(40, 40, 40) : Color.FromArgb(249, 249, 249);
    public static Color TextColor => IsDarkMode ? Color.FromArgb(230, 230, 230) : Color.Black;
    public static Color SecondaryTextColor => IsDarkMode ? Color.FromArgb(170, 170, 170) : Color.FromArgb(102, 102, 102);
    public static Color BorderColor => IsDarkMode ? Color.FromArgb(51, 51, 51) : Color.FromArgb(229, 229, 229);
    public static Color HoverColor => IsDarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(240, 240, 240);
    public static Color StatsBarBackground => IsDarkMode ? Color.FromArgb(40, 40, 40) : Color.FromArgb(249, 249, 249);

    public static event Action? ThemeChanged;

    public static void Initialize(Models.AppConfig config)
    {
        ThemeMode = config.ThemeMode;
        try { ThemeColor = ColorTranslator.FromHtml(config.ThemeColor); }
        catch { ThemeColor = ColorTranslator.FromHtml("#0078D7"); }

        if (!string.IsNullOrEmpty(config.ThemeFont))
        {
            try { GlobalFont = new Font(config.ThemeFont, SystemFonts.DefaultFont.Size); }
            catch { GlobalFont = SystemFonts.DefaultFont; }
        }

        UpdateDarkMode();
    }

    public static void SetThemeMode(string mode)
    {
        ThemeMode = mode;
        UpdateDarkMode();
        ThemeChanged?.Invoke();
    }

    public static void SetThemeColor(Color color)
    {
        ThemeColor = color;
        ThemeChanged?.Invoke();
    }

    public static void SetFont(string fontName)
    {
        try
        {
            GlobalFont = string.IsNullOrEmpty(fontName)
                ? SystemFonts.DefaultFont
                : new Font(fontName, SystemFonts.DefaultFont.Size);
        }
        catch { GlobalFont = SystemFonts.DefaultFont; }
        ThemeChanged?.Invoke();
    }

    public static void ApplyTheme(Control control)
    {
        try
        {
            if (!control.IsHandleCreated) return;
            control.BackColor = control is Form ? WindowBackground : control.Parent?.BackColor ?? WindowBackground;
            control.ForeColor = TextColor;
            control.Font = GlobalFont;

            if (control is Button btn)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = BorderColor;
                btn.BackColor = WindowBackground;
                btn.ForeColor = TextColor;
            }
            else if (control is TextBox tb)
            {
                tb.BackColor = IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White;
                tb.ForeColor = TextColor;
                tb.BorderStyle = BorderStyle.FixedSingle;
            }

            foreach (Control child in control.Controls)
                ApplyTheme(child);
        }
        catch (Exception ex)
        {
            LogService.Log("Theme apply failed", ex);
        }
    }

    private static void UpdateDarkMode()
    {
        IsDarkMode = ThemeMode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => DetectSystemDarkMode()
        };
    }

    private static bool DetectSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0;
        }
        catch { return false; }
    }
}
