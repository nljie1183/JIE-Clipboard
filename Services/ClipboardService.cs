using System.Collections.Specialized;
using System.Drawing.Imaging;
using JIE剪切板.Models;
using JIE剪切板.Native;

namespace JIE剪切板.Services;

public static class ClipboardService
{
    private static int _isSelfWriting = 0;

    public static bool IsSelfWriting
    {
        get => Interlocked.CompareExchange(ref _isSelfWriting, 0, 0) == 1;
        set => Interlocked.Exchange(ref _isSelfWriting, value ? 1 : 0);
    }

    public static ClipboardRecord? ReadFromClipboard()
    {
        if (IsSelfWriting) return null;

        // Retry with increasing delay for Win11 clipboard access reliability
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (attempt > 0) Thread.Sleep(50 * attempt);

                if (Clipboard.ContainsFileDropList())
                    return ReadFileDropList();
                if (Clipboard.ContainsImage())
                    return ReadImage();
                if (Clipboard.ContainsData(DataFormats.Rtf))
                    return ReadRtfText();
                if (Clipboard.ContainsText())
                    return ReadPlainText();
                return null;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 2)
            {
                // Clipboard is locked by another process, retry
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

    public static bool WriteToClipboard(ClipboardRecord record, string? decryptedContent = null)
    {
        try
        {
            IsSelfWriting = true;
            var content = decryptedContent ?? record.Content;
            var type = record.ContentType;

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
                                using var img = Image.FromFile(content);
                                Clipboard.SetImage(img);
                                return true;
                            }
                            return false;
                        case ClipboardContentType.FileDrop:
                        case ClipboardContentType.Video:
                        case ClipboardContentType.Folder:
                            var paths = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            var collection = new StringCollection();
                            collection.AddRange(paths);
                            Clipboard.SetFileDropList(collection);
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
            Task.Delay(200).ContinueWith(_ => IsSelfWriting = false);
        }
    }

    public static string GetContentPreview(ClipboardRecord record, int maxLength = 80)
    {
        if (record.IsEncrypted)
        {
            if (record.LockUntil.HasValue && record.LockUntil.Value > DateTime.Now)
            {
                var remaining = record.LockUntil.Value - DateTime.Now;
                return $"[加密内容] [已锁定，剩余解禁时间{FormatTimeSpan(remaining)}]";
            }
            return "[加密内容]";
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

    private static ClipboardContentType DetermineFileDropType(List<string> paths)
    {
        if (paths.Count == 1)
        {
            var path = paths[0];
            if (Directory.Exists(path)) return ClipboardContentType.Folder;
            if (ClipboardRecord.IsVideoFile(path)) return ClipboardContentType.Video;
        }
        else
        {
            bool allDirs = paths.All(Directory.Exists);
            if (allDirs) return ClipboardContentType.Folder;
            bool allVideos = paths.All(p => ClipboardRecord.IsVideoFile(p));
            if (allVideos) return ClipboardContentType.Video;
        }
        return ClipboardContentType.FileDrop;
    }

    private static string GetFileNames(string content)
    {
        var paths = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (paths.Length == 1) return Path.GetFileName(paths[0]);
        return $"{Path.GetFileName(paths[0])} 等{paths.Length}个项目";
    }

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

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
        return $"{ts.Minutes}分钟";
    }
}
