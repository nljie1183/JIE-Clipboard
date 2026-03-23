namespace JIE剪切板.Models;

public class HotkeyConfig
{
    public int Modifiers { get; set; } = 2; // MOD_CONTROL
    public int Key { get; set; } = 0x31;    // '1'
    public string DisplayText { get; set; } = "Ctrl+1";
}

public class AppConfig
{
    // Storage limits
    public bool MaxRecordCountEnabled { get; set; } = false;
    public int MaxRecordCount { get; set; } = 1000;
    public bool MaxContentSizeEnabled { get; set; } = false;
    public long MaxContentSizeKB { get; set; } = 102400; // 100MB
    public string MaxContentSizeUnit { get; set; } = "MB";
    public bool EnableDuplicateRemoval { get; set; } = true;

    // System
    public bool AutoStartOnBoot { get; set; } = false;
    public bool AutoStartMonitoring { get; set; } = true;
    public bool HideOnLostFocus { get; set; } = true;

    // Hotkey
    public HotkeyConfig WakeHotkey { get; set; } = new();

    // Appearance
    public string ThemeMode { get; set; } = "FollowSystem";
    public string ThemeColor { get; set; } = "#0078D7";
    public string ThemeFont { get; set; } = "";
    public int WindowOpacity { get; set; } = 100;
    public int ListRowHeight { get; set; } = 60;
    public int ThumbnailSize { get; set; } = 48;

    // Security
    public int DefaultMaxPasswordAttempts { get; set; } = 3;
    public int DefaultBaseLockMinutes { get; set; } = 60;
    public bool DefaultAutoDeleteOnExceed { get; set; } = false;
    public bool AllowSearchEncryptedContent { get; set; } = false;

    // Data storage
    public string CustomDataFolder { get; set; } = "";
}
