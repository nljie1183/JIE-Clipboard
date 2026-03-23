namespace JIE剪切板.Models;

public enum ClipboardContentType
{
    PlainText = 0,
    RichText = 1,
    Image = 2,
    FileDrop = 3,
    Video = 4,
    Folder = 5
}

public class ClipboardRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Content { get; set; } = "";
    public ClipboardContentType ContentType { get; set; }
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    public DateTime? ExpireTime { get; set; }
    public bool IsPinned { get; set; }
    public int MaxCopyCount { get; set; }
    public int CurrentCopyCount { get; set; }
    public string ContentHash { get; set; } = "";

    // Encryption
    public bool IsEncrypted { get; set; }
    public string? EncryptedData { get; set; }
    public string? Salt { get; set; }
    public string? IV { get; set; }
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }

    // Security protection per-record
    public int PasswordFailCount { get; set; }
    public DateTime? LockUntil { get; set; }
    public int MaxPasswordAttempts { get; set; } = 3;
    public int BaseLockMinutes { get; set; } = 60;
    public bool AutoDeleteOnExceed { get; set; }
    public bool UseGlobalSecuritySettings { get; set; } = true;
    public int CumulativeLockCount { get; set; }
    public DateTime? LastKnownSystemTime { get; set; }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".mpeg", ".mpg", ".3gp", ".ts", ".vob", ".rm", ".rmvb"
    };

    public static bool IsVideoFile(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));
}
