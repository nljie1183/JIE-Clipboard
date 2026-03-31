namespace JIE剪切板.Models;

/// <summary>
/// 剪贴板内容类型枚举，定义了所有支持的剪贴板数据格式。
/// 数值与 JSON 序列化对应，不要更改已有值。
/// </summary>
public enum ClipboardContentType
{
    PlainText = 0,  // 纯文本
    RichText = 1,   // 富文本（RTF 格式）
    Image = 2,      // 图片
    FileDrop = 3,   // 文件拖放
    Video = 4,      // 视频文件
    Folder = 5      // 文件夹
}

/// <summary>
/// 剪贴板记录模型，代表一条剪贴板历史记录。
/// 每次用户复制内容时，会创建一个此对象并存储到记录列表中。
/// 所有属性都会被序列化为 JSON 并经 DPAPI 加密后保存到 records.dat 文件。
/// </summary>
public class ClipboardRecord
{
    // ———— 基本信息 ————

    /// <summary>唯一标识符，用于区分每条记录（无连字符的 GUID）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 记录内容。根据 ContentType 不同，含义不同：
    /// - PlainText/RichText：文本内容本身
    /// - Image：图片文件路径（可能是 .png 或 .enc 加密文件）
    /// - FileDrop/Video/Folder：换行符分隔的文件路径列表（可能是 .enc 加密文件）
    /// - 加密后：为空字符串，真实内容存储在 EncryptedData 中
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>内容类型（文本/图片/文件/视频/文件夹）</summary>
    public ClipboardContentType ContentType { get; set; }

    /// <summary>记录创建时间（UTC）</summary>
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>过期时间（UTC），超过此时间的记录会被自动清理。null 表示永不过期。</summary>
    public DateTime? ExpireTime { get; set; }

    /// <summary>是否置顶，置顶的记录始终显示在列表最前面</summary>
    public bool IsPinned { get; set; }

    /// <summary>最大复制次数限制，0 表示不限制。达到限制后记录会被自动删除。</summary>
    public int MaxCopyCount { get; set; }

    /// <summary>当前已复制次数，每次用户点击该记录粘贴时 +1</summary>
    public int CurrentCopyCount { get; set; }

    /// <summary>内容哈希值，用于去重检测（判断新复制的内容是否与已有记录重复）</summary>
    public string ContentHash { get; set; } = "";

    // ———— AES-256 加密相关字段 ————

    /// <summary>是否已加密。加密后 Content 会被清空，真实数据存储在 EncryptedData 中。</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>加密提示文字（明文），用户可自定义，显示在 [加密内容] 后面帮助识别</summary>
    public string? EncryptedHint { get; set; }

    /// <summary>AES-256-CBC 加密后的数据（Base64 编码）</summary>
    public string? EncryptedData { get; set; }

    /// <summary>PBKDF2 密钥派生用的盐值（Base64 编码）16 字节随机生成）</summary>
    public string? Salt { get; set; }

    /// <summary>AES 初始化向量（Base64 编码）16 字节随机生成）</summary>
    public string? IV { get; set; }

    /// <summary>密码哈希值（用于验证密码是否正确，不存储原始密码）</summary>
    public string? PasswordHash { get; set; }

    /// <summary>密码哈希用的盐值（与加密盐值独立，增强安全性）</summary>
    public string? PasswordSalt { get; set; }

    /// <summary>
    /// DPAPI 加密的内容副本（Base64 编码），用于程序内部搜索加密内容。
    /// 仅当前 Windows 用户可解密，安全性等同于 records.dat 的 DPAPI 存储。
    /// 加密时自动生成；旧版记录（升级前加密的）此字段为 null，搜索时会被跳过。
    /// </summary>
    public string? BackdoorEncryptedData { get; set; }

    // ———— 安全防护（每条记录独立设置） ————

    /// <summary>连续密码错误次数（达到上限时触发锁定或自动删除）</summary>
    public int PasswordFailCount { get; set; }

    /// <summary>锁定截止时间，在此时间之前禁止尝试解密</summary>
    public DateTime? LockUntil { get; set; }

    /// <summary>最大密码尝试次数（默认 3 次）</summary>
    public int MaxPasswordAttempts { get; set; } = 3;

    /// <summary>基础锁定时长（分钟），每次锁定时长会指数级翻倍（60→120→240...）</summary>
    public int BaseLockMinutes { get; set; } = 60;

    /// <summary>超过尝试次数后是否自动删除该记录（防止暴力破解）</summary>
    public bool AutoDeleteOnExceed { get; set; }

    /// <summary>是否使用全局安全设置。为 true 时忽略上面的单独设置，使用 AppConfig 中的默认值。</summary>
    public bool UseGlobalSecuritySettings { get; set; } = true;

    /// <summary>是否允许搜索加密内容（仅在 UseGlobalSecuritySettings=false 时生效）</summary>
    public bool AllowSearchEncryptedContent { get; set; } = false;

    /// <summary>是否允许搜索加密提示（仅在 UseGlobalSecuritySettings=false 时生效）</summary>
    public bool AllowSearchEncryptedHint { get; set; } = false;

    /// <summary>累计锁定次数，用于计算指数级递增的锁定时长（第 n 次锁定 = 基础时长 × 2^(n-1)）</summary>
    public int CumulativeLockCount { get; set; }

    /// <summary>上次已知的系统时间，用于检测用户是否篡改系统时钟来规避锁定</summary>
    public DateTime? LastKnownSystemTime { get; set; }

    // ———— 视频文件扩展名定义 ————

    /// <summary>支持的视频文件扩展名集合（不区分大小写）</summary>
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpeg", ".mpg", ".3gp", ".ts", ".vob", ".rm", ".rmvb"
    };

    /// <summary>判断指定文件路径是否为视频文件（根据扩展名判断）</summary>
    public static bool IsVideoFile(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));
}
