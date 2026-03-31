namespace JIE剪切板.Models;

/// <summary>
/// 快捷键配置模型，存储用户设置的全局快捷键信息。
/// Modifiers 和 Key 对应 Win32 API 的虚拟键码。
/// </summary>
public class HotkeyConfig
{
    /// <summary>修饰键标志位（如 MOD_CONTROL=2, MOD_ALT=1, MOD_SHIFT=4, MOD_WIN=8）</summary>
    public int Modifiers { get; set; } = 2; // MOD_CONTROL 对应 Ctrl 键

    /// <summary>主键的虚拟键码，0x31 对应键盘数字 '1'</summary>
    public int Key { get; set; } = 0x31;    // '1'

    /// <summary>快捷键的显示文本（如 "Ctrl+1"），用于 UI 展示</summary>
    public string DisplayText { get; set; } = "Ctrl+1";
}

/// <summary>
/// 应用程序全局配置模型，存储所有用户可设置的选项。
/// 序列化为 JSON 存储在 %AppData%\JIE剪切板\config.json 中。
/// </summary>
public class AppConfig
{
    // ———— 存储限制 ————

    /// <summary>是否启用最大记录数限制</summary>
    public bool MaxRecordCountEnabled { get; set; } = false;

    /// <summary>最大记录数，超出时自动删除最旧的未置顶记录</summary>
    public int MaxRecordCount { get; set; } = 1000;

    /// <summary>是否启用单条内容大小限制</summary>
    public bool MaxContentSizeEnabled { get; set; } = false;

    /// <summary>单条内容最大大小（KB），超过此大小的剪贴板内容会被忽略</summary>
    public long MaxContentSizeKB { get; set; } = 102400; // 100MB

    /// <summary>大小单位的显示文本（"KB" / "MB"）</summary>
    public string MaxContentSizeUnit { get; set; } = "MB";

    /// <summary>是否启用去重，启用后相同内容的剪贴板记录只保留最新一条</summary>
    public bool EnableDuplicateRemoval { get; set; } = true;

    // ———— 系统设置 ————

    /// <summary>是否开机自启动</summary>
    public bool AutoStartOnBoot { get; set; } = false;

    /// <summary>启动后是否自动开始监听剪贴板</summary>
    public bool AutoStartMonitoring { get; set; } = true;

    /// <summary>窗口失去焦点时是否自动隐藏</summary>
    public bool HideOnLostFocus { get; set; } = true;

    // ———— 快捷键 ————

    /// <summary>唤醒窗口的全局快捷键配置（默认 Ctrl+1）</summary>
    public HotkeyConfig WakeHotkey { get; set; } = new();

    // ———— 外观 ————

    /// <summary>主题模式："Light"（浅色）、"Dark"（深色）、"FollowSystem"（跟随系统）</summary>
    public string ThemeMode { get; set; } = "FollowSystem";

    /// <summary>主题强调色（HTML 颜色码，如 "#0078D7"）</summary>
    public string ThemeColor { get; set; } = "#0078D7";

    /// <summary>自定义字体名称，空字符串表示使用系统默认字体</summary>
    public string ThemeFont { get; set; } = "";

    /// <summary>窗口不透明度（0-100）</summary>
    public int WindowOpacity { get; set; } = 100;

    /// <summary>记录列表每行高度（像素）</summary>
    public int ListRowHeight { get; set; } = 60;

    /// <summary>图片缩略图大小（像素）</summary>
    public int ThumbnailSize { get; set; } = 48;

    // ———— 记录类型过滤 ————
    // 用户可以选择只记录特定类型的剪贴板内容

    /// <summary>是否记录纯文本</summary>
    public bool RecordPlainText { get; set; } = true;

    /// <summary>是否记录富文本</summary>
    public bool RecordRichText { get; set; } = true;

    /// <summary>是否记录图片</summary>
    public bool RecordImage { get; set; } = true;

    /// <summary>是否记录文件</summary>
    public bool RecordFileDrop { get; set; } = true;

    /// <summary>是否记录视频</summary>
    public bool RecordVideo { get; set; } = true;

    /// <summary>是否记录文件夹</summary>
    public bool RecordFolder { get; set; } = true;

    /// <summary>仅记录包含这些扩展名的文件（逗号分隔，如 ".txt,.pdf"），空表示不限制</summary>
    public string IncludeExtensions { get; set; } = "";

    /// <summary>排除包含这些扩展名的文件（逗号分隔），空表示不排除</summary>
    public string ExcludeExtensions { get; set; } = "";

    // ———— 持久化加密存储（按类型独立开关） ————
    // 启用后，对应类型的文件会被复制到本地并经 DPAPI 加密存储（.enc 文件），
    // 这样即使原始文件被删除/移动，剪贴板记录仍然可用。

    /// <summary>是否对图片启用持久化加密存储</summary>
    public bool PersistImage { get; set; } = false;

    /// <summary>是否对文件启用持久化加密存储</summary>
    public bool PersistFileDrop { get; set; } = false;

    /// <summary>是否对视频启用持久化加密存储</summary>
    public bool PersistVideo { get; set; } = false;

    /// <summary>是否对文件夹启用持久化加密存储（文件夹会被压缩为 zip 后加密）</summary>
    public bool PersistFolder { get; set; } = false;

    /// <summary>持久化存储的单文件最大大小（MB），超过此大小的文件只记录路径不复制</summary>
    public int MaxPersistFileSizeMB { get; set; } = 50;

    // ———— 安全设置（全局默认值） ————
    // 这些值作为新加密记录的默认安全策略，单条记录可以覆盖。

    /// <summary>默认最大密码尝试次数</summary>
    public int DefaultMaxPasswordAttempts { get; set; } = 3;

    /// <summary>默认基础锁定时长（分钟）</summary>
    public int DefaultBaseLockMinutes { get; set; } = 60;

    /// <summary>默认是否超限自动删除</summary>
    public bool DefaultAutoDeleteOnExceed { get; set; } = false;

    /// <summary>是否允许搜索加密内容（启用后搜索时会临时解密匹配，有一定安全风险）</summary>
    public bool AllowSearchEncryptedContent { get; set; } = false;

    /// <summary>是否允许搜索加密提示文字（启用后搜索时会匹配加密记录的提示字段）</summary>
    public bool AllowSearchEncryptedHint { get; set; } = false;

    // ———— 数据存储 ————

    /// <summary>自定义数据存储文件夹路径，空字符串表示使用默认位置（%AppData%\JIE剪切板\）</summary>
    public string CustomDataFolder { get; set; } = "";
}
