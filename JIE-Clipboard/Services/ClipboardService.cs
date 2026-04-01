using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using JIE剪切板.Models;
using JIE剪切板.Native;

namespace JIE剪切板.Services;

/// <summary>
/// 剪贴板服务（静态类）。
/// 负责剪贴板的读取、写入、内容预览生成，以及临时文件的生命周期管理。
/// 
/// 主要功能：
/// - ReadFromClipboard: 从系统剪贴板读取内容，支持文本/图片/文件/文件夹
/// - WriteToClipboard: 将记录写回剪贴板，处理加密文件的解密
/// - GetContentPreview: 生成记录的显示预览文本
/// - CleanupPendingTempFiles: 清理托管的临时文件（程序退出时调用）
/// - CleanupStaleTempFiles: 启动时清理上次崩溃残留的临时文件
/// </summary>
public static class ClipboardService
{
    /// <summary>自写计数器：>0 表示当前程序正在向剪贴板写入数据，应忽略 WM_CLIPBOARDUPDATE 消息</summary>
    private static int _selfWriteCount = 0;

    /// <summary>跟踪待清理的临时文件路径（线程安全）</summary>
    private static readonly ConcurrentBag<string> _pendingTempFiles = new();

    /// <summary>跟踪待清理的临时文件夹路径（线程安全）</summary>
    private static readonly ConcurrentBag<string> _pendingTempFolders = new();

    /// <summary>是否正在自己写入剪贴板（原子操作读取）</summary>
    public static bool IsSelfWriting =>
        Interlocked.CompareExchange(ref _selfWriteCount, 0, 0) > 0;

    /// <summary>
    /// 从系统剪贴板读取当前内容，返回一条 ClipboardRecord。
    /// 支持多种格式的优先级：文件拖放 > 图片 > RTF富文本 > 纯文本。
    /// 包含重试机制（最多 3 次），解决 Win11 下剪贴板被其他进程锁定的问题。
    /// </summary>
    /// <returns>剪贴板记录；无内容或失败时返回 null</returns>
    public static ClipboardRecord? ReadFromClipboard()
    {
        // 如果是我们自己写入的，跳过读取（避免重复记录）
        if (IsSelfWriting) return null;

        // 重试机制：指数退避（50/100/200/400/800ms），共 5 次，总窗口约 1.5 秒
        // Win11 剪贴板历史功能或其他进程可能长时间锁定剪贴板，需要足够的等待时间
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 0) Thread.Sleep(50 * (1 << (attempt - 1))); // 50, 100, 200, 400ms

                // 按优先级检测剪贴板内容类型
                if (Clipboard.ContainsFileDropList())
                    return ReadFileDropList();       // 文件/文件夹/视频
                if (Clipboard.ContainsImage())
                    return ReadImage();              // 图片
                if (Clipboard.ContainsData(DataFormats.Rtf))
                    return ReadRtfText();            // RTF 富文本
                if (Clipboard.ContainsText())
                    return ReadPlainText();           // 纯文本
                return null;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < maxAttempts - 1)
            {
                // 剪贴板被其他进程锁定，等待后重试
                continue;
            }
            catch (Exception ex)
            {
                LogService.Log("Failed to read clipboard", ex);
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// 将记录内容写回系统剪贴板。
    /// 支持所有内容类型，包括加密文件的解密写入。
    /// 写入期间设置 _selfWriteCount 标志，避免触发自己的剪贴板监听。
    /// </summary>
    /// <param name="record">要写入的记录</param>
    /// <param name="decryptedContent">已解密的内容（加密记录传入解密后的明文）</param>
    /// <returns>写入是否成功</returns>
    public static bool WriteToClipboard(ClipboardRecord record, string? decryptedContent = null)
    {
        try
        {
            // 标记开始自写，阻止 OnClipboardUpdate 处理自己的写入
            Interlocked.Increment(ref _selfWriteCount);
            var content = decryptedContent ?? record.Content;
            var type = record.ContentType;

            // 最多重试 3 次
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    switch (type)
                    {
                        case ClipboardContentType.PlainText:
                            Clipboard.SetText(content, TextDataFormat.UnicodeText);
                            return true;

                        case ClipboardContentType.RichText:
                            Clipboard.SetData(DataFormats.Rtf, content);
                            return true;

                        case ClipboardContentType.Image:
                            if (File.Exists(content))
                            {
                                if (content.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
                                {
                                    // 加密图片：解密到临时文件 → 读取 → 写入剪贴板 → 删除临时文件
                                    var tempPath = FileService.DecryptFileToTemp(content);
                                    if (tempPath != null)
                                    {
                                        try
                                        {
                                            using var img = Image.FromFile(tempPath);
                                            Clipboard.SetImage(img);
                                        }
                                        finally
                                        {
                                            try { File.Delete(tempPath); } catch { }
                                        }
                                        return true;
                                    }
                                }
                                else
                                {
                                    using var img = Image.FromFile(content);
                                    Clipboard.SetImage(img);
                                    return true;
                                }
                            }
                            return false;

                        case ClipboardContentType.FileDrop:
                        case ClipboardContentType.Video:
                            // 文件/视频：处理加密文件的解密，然后设置文件拖放列表
                            var paths = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            var actualPaths = new List<string>();
                            var tempFiles = new List<string>();
                            foreach (var p in paths)
                            {
                                if (p.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                                {
                                    // .enc 文件需要解密到临时位置
                                    var temp = FileService.DecryptFileToTemp(p);
                                    if (temp != null) { actualPaths.Add(temp); tempFiles.Add(temp); }
                                }
                                else
                                {
                                    actualPaths.Add(p);
                                }
                            }
                            if (actualPaths.Count == 0) return false;
                            var collection = new StringCollection();
                            collection.AddRange(actualPaths.ToArray());
                            Clipboard.SetFileDropList(collection);
                            if (tempFiles.Count > 0)
                            {
                                // 跟踪临时文件，5 秒后延迟清理（给目标应用时间读取）
                                foreach (var tf in tempFiles) _pendingTempFiles.Add(tf);
                                Task.Delay(5000).ContinueWith(_ =>
                                {
                                    foreach (var tf in tempFiles)
                                    {
                                        try { File.Delete(tf); } catch { }
                                    }
                                });
                            }
                            return true;

                        case ClipboardContentType.Folder:
                            // 文件夹：处理加密文件夹（.zip.enc）的解密
                            var folderPaths = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            var actualFolderPaths = new List<string>();
                            var tempFolders = new List<string>();
                            foreach (var p in folderPaths)
                            {
                                if (p.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                                {
                                    // 解密压缩包并解压到临时文件夹
                                    var temp = FileService.DecryptFolderToTemp(p);
                                    if (temp != null)
                                    {
                                        actualFolderPaths.Add(temp);
                                        // 清理时需要删除整个临时包装目录
                                        var parent = Path.GetDirectoryName(temp);
                                        tempFolders.Add(parent != null && parent.Contains("jie_folder_") ? parent : temp);
                                    }
                                }
                                else
                                {
                                    actualFolderPaths.Add(p);
                                }
                            }
                            if (actualFolderPaths.Count == 0) return false;
                            var folderCollection = new StringCollection();
                            folderCollection.AddRange(actualFolderPaths.ToArray());
                            Clipboard.SetFileDropList(folderCollection);
                            if (tempFolders.Count > 0)
                            {
                                // 跟踪临时文件夹，10 秒后延迟清理（文件夹较大，给更多时间）
                                foreach (var tf in tempFolders) _pendingTempFolders.Add(tf);
                                Task.Delay(10000).ContinueWith(_ =>
                                {
                                    foreach (var tf in tempFolders)
                                        try { Directory.Delete(tf, true); } catch { }
                                });
                            }
                            return true;
                        default:
                            Clipboard.SetText(content);
                            return true;
                    }
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to write clipboard", ex);
            return false;
        }
        finally
        {
            // 200ms 后清除自写标志（给系统时间处理剪贴板更新通知）
            Task.Delay(200).ContinueWith(_ => Interlocked.Decrement(ref _selfWriteCount));
        }
    }

    /// <summary>
    /// 清理所有被跟踪的临时文件和文件夹。
    /// 在程序退出时调用，确保解密产生的临时文件被安全删除。
    /// </summary>
    public static void CleanupPendingTempFiles()
    {
        while (_pendingTempFiles.TryTake(out var f))
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        while (_pendingTempFolders.TryTake(out var d))
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }

    /// <summary>
    /// 清理残留的临时文件（崩溃恢复 + 定期清理）。
    /// 启动时 maxAge=Zero 清理所有；运行中 maxAge 可设为 2 分钟，避免误删正在使用的文件。
    /// </summary>
    public static void CleanupStaleTempFiles(TimeSpan? maxAge = null)
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var cutoff = maxAge.HasValue ? DateTime.UtcNow - maxAge.Value : DateTime.MaxValue;

            foreach (var f in Directory.GetFiles(tempDir, "jie_clip_*"))
                try { if (cutoff == DateTime.MaxValue || File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); } catch { }
            foreach (var f in Directory.GetFiles(tempDir, "jie_zip_*"))
                try { if (cutoff == DateTime.MaxValue || File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f); } catch { }
            foreach (var d in Directory.GetDirectories(tempDir, "jie_folder_*"))
                try { if (cutoff == DateTime.MaxValue || Directory.GetLastWriteTimeUtc(d) < cutoff) Directory.Delete(d, true); } catch { }
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to cleanup stale temp files", ex);
        }
    }

    /// <summary>
    /// 生成记录的内容预览文本（显示在列表中）。
    /// 加密记录显示 "[加密内容]" + 提示文字 + 锁定状态。
    /// </summary>
    /// <param name="record">剪贴板记录</param>
    /// <param name="maxLength">预览最大字符数</param>
    /// <returns>截断后的预览字符串</returns>
    public static string GetContentPreview(ClipboardRecord record, int maxLength = 80)
    {
        if (record.IsEncrypted)
        {
            var hint = string.IsNullOrWhiteSpace(record.EncryptedHint) ? "" : $" {record.EncryptedHint}";
            if (record.LockUntil.HasValue && record.LockUntil.Value > DateTime.Now)
            {
                var remaining = record.LockUntil.Value - DateTime.Now;
                return $"[加密内容]{hint} [已锁定，剩余解禁时间{FormatTimeSpan(remaining)}]";
            }
            return $"[加密内容]{hint}";
        }

        string preview = record.ContentType switch
        {
            ClipboardContentType.PlainText => record.Content.Replace("\r", "").Replace("\n", " "),
            ClipboardContentType.RichText => "[富文本] " + ExtractRtfPlainText(record.Content),
            ClipboardContentType.Image => "[图片] " + Path.GetFileName(record.Content),
            ClipboardContentType.FileDrop => "[文件] " + GetFileNames(record.Content),
            ClipboardContentType.Video => "[视频] " + GetFileNames(record.Content),
            ClipboardContentType.Folder => "[文件夹] " + GetFileNames(record.Content),
            _ => record.Content
        };

        return preview.Length > maxLength ? preview[..maxLength] + "..." : preview;
    }

    /// <summary>读取剪贴板中的纯文本内容</summary>
    private static ClipboardRecord? ReadPlainText()
    {
        try
        {
            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (string.IsNullOrEmpty(text)) return null;
            return new ClipboardRecord
            {
                Content = text,
                ContentType = ClipboardContentType.PlainText,
                CreateTime = DateTime.UtcNow,
                ContentHash = EncryptionService.ComputeContentHash(text)
            };
        }
        catch { return null; }
    }

    /// <summary>读取剪贴板中的 RTF 富文本内容</summary>
    private static ClipboardRecord? ReadRtfText()
    {
        try
        {
            var rtf = Clipboard.GetData(DataFormats.Rtf) as string;
            if (string.IsNullOrEmpty(rtf)) return null;
            return new ClipboardRecord
            {
                Content = rtf,
                ContentType = ClipboardContentType.RichText,
                CreateTime = DateTime.UtcNow,
                ContentHash = EncryptionService.ComputeContentHash(rtf)
            };
        }
        catch { return null; }
    }

    /// <summary>读取剪贴板中的图片，保存为 PNG 文件后记录路径</summary>
    private static ClipboardRecord? ReadImage()
    {
        try
        {
            var image = Clipboard.GetImage();
            if (image == null) return null;

            var imagePath = FileService.SaveImage(image);
            if (string.IsNullOrEmpty(imagePath)) return null;

            return new ClipboardRecord
            {
                Content = imagePath,
                ContentType = ClipboardContentType.Image,
                CreateTime = DateTime.UtcNow,
                ContentHash = EncryptionService.ComputeFileHash(imagePath)
            };
        }
        catch { return null; }
    }

    /// <summary>读取剪贴板中的文件拖放列表，自动判断是文件/文件夹/视频</summary>
    private static ClipboardRecord? ReadFileDropList()
    {
        try
        {
            var files = Clipboard.GetFileDropList();
            if (files == null || files.Count == 0) return null;

            var paths = files.Cast<string>().Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths.Count == 0) return null;

            var content = string.Join("\n", paths);
            var contentType = DetermineFileDropType(paths);

            return new ClipboardRecord
            {
                Content = content,
                ContentType = contentType,
                CreateTime = DateTime.UtcNow,
                ContentHash = EncryptionService.ComputeContentHash(content)
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// 根据文件路径列表判断内容类型。
    /// 全是目录 → Folder；全是视频 → Video；其他 → FileDrop。
    /// </summary>
    private static ClipboardContentType DetermineFileDropType(List<string> paths)
    {
        if (paths.Count == 1)
        {
            var path = paths[0];
            if (SafeDirectoryExists(path)) return ClipboardContentType.Folder;
            if (ClipboardRecord.IsVideoFile(path)) return ClipboardContentType.Video;
        }
        else
        {
            bool allDirs = paths.All(SafeDirectoryExists);
            if (allDirs) return ClipboardContentType.Folder;
            bool allVideos = paths.All(p => ClipboardRecord.IsVideoFile(p));
            if (allVideos) return ClipboardContentType.Video;
        }
        return ClipboardContentType.FileDrop;
    }

    /// <summary>安全地检查目录是否存在，跳过网络路径以避免 UI 卡顿</summary>
    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            if (path.StartsWith(@"\\")) return false; // 跳过网络路径，避免 UI 卡顿
            return Directory.Exists(path);
        }
        catch { return false; }
    }

    /// <summary>提取文件名显示文本，多个文件时显示“xxx 等N个项目”</summary>
    private static string GetFileNames(string content)
    {
        var paths = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 1) return Path.GetFileName(paths[0]);
        return $"{Path.GetFileName(paths[0])} 等{paths.Length}个项目";
    }

    /// <summary>从 RTF 格式字符串提取纯文本（用于预览显示）</summary>
    private static string ExtractRtfPlainText(string rtf)
    {
        try
        {
            using var rtb = new RichTextBox();
            rtb.Rtf = rtf;
            var text = rtb.Text.Replace("\r", "").Replace("\n", " ");
            return text.Length > 100 ? text[..100] + "..." : text;
        }
        catch { return "[富文本内容]"; }
    }

    /// <summary>将时间间隔格式化为中文显示（如“2小时30分钟”）</summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
        return $"{ts.Minutes}分钟";
    }
}
