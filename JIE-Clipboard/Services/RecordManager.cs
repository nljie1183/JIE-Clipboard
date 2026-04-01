using JIE剪切板.Models;

namespace JIE剪切板.Services;

/// <summary>
/// 记录管理器。
/// 负责剪贴板记录的完整生命周期管理：
///   - 剪贴板变化 → 读取 → 过滤 → 去重 → 持久化加密 → 裁剪 → 保存
///   - 记录的增删改查
///   - 节流式保存（500ms 合并多次写盘请求）
///   - 过期记录自动清理
///   - 持久化加密存储（图片/文件/视频/文件夹 → DPAPI .enc）
///
/// 从 MainForm 中拆分，使其专注于 UI 和窗口管理。
/// </summary>
public class RecordManager : IDisposable
{
    private readonly Func<AppConfig> _getConfig;

    /// <summary>所有剪贴板记录</summary>
    public List<ClipboardRecord> Records { get; set; }

    // ==================== 节流保存 ====================
    private System.Windows.Forms.Timer? _saveThrottleTimer;
    private bool _saveDataPending;

    // ==================== 防重入 ====================
    private bool _isProcessing; // 剪贴板处理流水线防重入标志

    /// <summary>记录列表发生变化时触发，通知 UI 刷新</summary>
    public event Action? RecordsChanged;

    private AppConfig Config => _getConfig();

    public RecordManager(Func<AppConfig> getConfig, List<ClipboardRecord> records)
    {
        _getConfig = getConfig;
        Records = records;
    }

    /// <summary>初始化节流保存定时器（必须在 UI 线程调用）</summary>
    public void InitializeSaveTimer()
    {
        _saveThrottleTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _saveThrottleTimer.Tick += (_, _) => { _saveThrottleTimer.Stop(); FlushSaveData(); };
    }

    #region Clipboard Update Processing

    /// <summary>
    /// 处理剪贴板内容变化（核心流水线）。
    /// 由 MainForm.WndProc 收到 WM_CLIPBOARDUPDATE 后调用。
    /// 流程：读取 → 类型过滤 → 后缀过滤 → 大小限制 → 去重 → 持久化(后台) → 裁剪 → 保存
    /// async void：WndProc 回调，已用 try-catch + 防重入保护。
    /// </summary>
    public async void ProcessClipboardUpdate()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try
        {
            var record = ClipboardService.ReadFromClipboard();
            if (record == null) return;

            // 检查记录类型是否允许（用户可在设置中禁用某些类型）
            if (!IsTypeAllowed(record.ContentType)) return;

            // 检查后缀名过滤（仅对文件类型有效）
            if (record.ContentType is ClipboardContentType.FileDrop or ClipboardContentType.Video or ClipboardContentType.Folder)
            {
                var paths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (!AreExtensionsAllowed(paths)) return;
            }

            // 检查单条内容大小是否超限
            if (Config.MaxContentSizeEnabled && !string.IsNullOrEmpty(record.Content))
            {
                long sizeKB = System.Text.Encoding.UTF8.GetByteCount(record.Content) / 1024;
                if (sizeKB > Config.MaxContentSizeKB) return;
            }

            // 去重处理：相同 hash 的记录不重复添加，而是移到顶部
            if (Config.EnableDuplicateRemoval && !string.IsNullOrEmpty(record.ContentHash))
            {
                var existing = Records.FirstOrDefault(r =>
                    !r.IsEncrypted && r.ContentHash == record.ContentHash);
                if (existing != null)
                {
                    // 将已有记录移到顶部（更新时间），重置复制计数以便再次使用
                    existing.CreateTime = DateTime.UtcNow;
                    existing.CurrentCopyCount = 0;
                    SaveData();
                    RecordsChanged?.Invoke();
                    return;
                }
            }

            Records.Insert(0, record);

            // 先通知 UI 显示新记录（让用户立即看到）
            RecordsChanged?.Invoke();

            // 按类型应用持久化加密存储（图片/文件/视频/文件夹）
            // 文件加密/压缩是重 I/O 操作，放到后台线程避免阻塞 UI
            await Task.Run(() => ApplyPersistentStorage(record));

            // 强制最大记录数限制
            TrimExcessRecords();

            SaveData();
            RecordsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogService.Log("Clipboard update handler failed", ex);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    #endregion

    #region Record CRUD

    /// <summary>删除单条记录（清理关联文件 + 从列表移除）</summary>
    public void DeleteRecord(ClipboardRecord record)
    {
        FileService.DeleteRecordFiles(record);
        Records.Remove(record);
    }

    /// <summary>清空所有记录</summary>
    public void ClearAllRecords()
    {
        foreach (var r in Records.ToList())
            FileService.DeleteRecordFiles(r);
        Records.Clear();
        SaveData();
    }

    #endregion

    #region Save (Throttled)

    /// <summary>请求保存数据（节流式：500ms 内多次调用只刷盘一次）</summary>
    public void SaveData()
    {
        _saveDataPending = true;
        if (_saveThrottleTimer != null && !_saveThrottleTimer.Enabled)
            _saveThrottleTimer.Start();
    }

    /// <summary>立即刷盘保存（节流定时器触发或程序退出时调用）</summary>
    public void FlushSaveData()
    {
        if (!_saveDataPending) return;
        _saveDataPending = false;
        try
        {
            FileService.SaveRecords(Records);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save data", ex);
        }
    }

    #endregion

    #region Cleanup

    /// <summary>清理已过期且未置顶的记录</summary>
    public void CleanupExpiredRecords()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expired = Records.Where(r => r.ExpireTime.HasValue && r.ExpireTime.Value <= now && !r.IsPinned).ToList();
            foreach (var r in expired)
            {
                FileService.DeleteRecordFiles(r);
                Records.Remove(r);
            }
            if (expired.Count > 0)
            {
                SaveData();
                LogService.Log($"Cleaned up {expired.Count} expired records");
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Expired record cleanup failed", ex);
        }
    }

    /// <summary>裁剪超量记录（移除最旧的非置顶记录）</summary>
    private void TrimExcessRecords()
    {
        if (Config.MaxRecordCountEnabled && Records.Count > Config.MaxRecordCount)
        {
            var toRemove = Records
                .Where(r => !r.IsPinned)
                .OrderBy(r => r.CreateTime)
                .Take(Records.Count - Config.MaxRecordCount)
                .ToList();
            foreach (var r in toRemove)
            {
                FileService.DeleteRecordFiles(r);
                Records.Remove(r);
            }
        }
    }

    #endregion

    #region Record Type Filtering

    /// <summary>检查指定类型是否允许记录（根据配置中的 6 个开关）</summary>
    private bool IsTypeAllowed(ClipboardContentType type) => type switch
    {
        ClipboardContentType.PlainText => Config.RecordPlainText,
        ClipboardContentType.RichText => Config.RecordRichText,
        ClipboardContentType.Image => Config.RecordImage,
        ClipboardContentType.FileDrop => Config.RecordFileDrop,
        ClipboardContentType.Video => Config.RecordVideo,
        ClipboardContentType.Folder => Config.RecordFolder,
        _ => true
    };

    /// <summary>
    /// 检查文件后缀是否允许：
    /// - 包含列表非空 → 至少一个文件匹配才允许
    /// - 排除列表非空 → 所有文件都匹配排除列表才拒绝
    /// </summary>
    private bool AreExtensionsAllowed(string[] paths)
    {
        var include = ParseExtensions(Config.IncludeExtensions);
        var exclude = ParseExtensions(Config.ExcludeExtensions);

        if (include.Count > 0)
            return paths.Any(p => include.Contains(Path.GetExtension(p).ToLowerInvariant()));
        if (exclude.Count > 0)
            return !paths.All(p => exclude.Contains(Path.GetExtension(p).ToLowerInvariant()));
        return true;
    }

    /// <summary>解析逗号分隔的后缀列表字符串为 HashSet，自动补全前导点</summary>
    private static HashSet<string> ParseExtensions(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return new();
        return ext.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                  .ToHashSet();
    }

    #endregion

    #region Persistent Encrypted Storage

    /// <summary>
    /// 根据记录类型应用持久化加密存储：
    /// - Image → DPAPI 加密图片文件（.enc）
    /// - Video/FileDrop → 复制+DPAPI 加密（受单文件大小限制）
    /// - Folder → zip压缩+DPAPI 加密（.zip.enc）
    /// </summary>
    private void ApplyPersistentStorage(ClipboardRecord record)
    {
        try
        {
            long maxBytes = Config.MaxPersistFileSizeMB * 1024L * 1024;

            switch (record.ContentType)
            {
                case ClipboardContentType.Image:
                    if (!Config.PersistImage) return;
                    if (File.Exists(record.Content) && !record.Content.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
                    {
                        var encPath = FileService.EncryptExistingFile(record.Content);
                        if (!string.IsNullOrEmpty(encPath))
                        {
                            try { File.Delete(record.Content); } catch { }
                            record.Content = encPath;
                            record.ContentHash = EncryptionService.ComputeContentHash(record.Content);
                        }
                    }
                    break;

                case ClipboardContentType.Video:
                    if (!Config.PersistVideo) return;
                    EncryptFileDropPaths(record, maxBytes);
                    break;

                case ClipboardContentType.FileDrop:
                    if (!Config.PersistFileDrop) return;
                    EncryptFileDropPaths(record, maxBytes);
                    break;

                case ClipboardContentType.Folder:
                    if (!Config.PersistFolder) return;
                    var folderPaths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var encFolderPaths = new List<string>();
                    foreach (var fp in folderPaths)
                    {
                        if (Directory.Exists(fp))
                        {
                            var enc = FileService.SaveAndEncryptFolder(fp, maxBytes);
                            encFolderPaths.Add(!string.IsNullOrEmpty(enc) ? enc : fp);
                        }
                        else
                        {
                            encFolderPaths.Add(fp);
                        }
                    }
                    record.Content = string.Join("\n", encFolderPaths);
                    record.ContentHash = EncryptionService.ComputeContentHash(record.Content);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to apply persistent storage", ex);
        }
    }

    /// <summary>对文件拖放路径逐个复制并加密（超过大小限制的保留原路径）</summary>
    private void EncryptFileDropPaths(ClipboardRecord record, long maxBytes)
    {
        var paths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var encPaths = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.Length <= maxBytes)
                {
                    var enc = FileService.SaveAndEncryptFile(path);
                    encPaths.Add(!string.IsNullOrEmpty(enc) ? enc : path);
                }
                else
                {
                    encPaths.Add(path);
                }
            }
            else
            {
                encPaths.Add(path);
            }
        }
        record.Content = string.Join("\n", encPaths);
        record.ContentHash = EncryptionService.ComputeContentHash(record.Content);
    }

    #endregion

    /// <summary>释放节流定时器资源</summary>
    public void Dispose()
    {
        _saveThrottleTimer?.Stop();
        _saveThrottleTimer?.Dispose();
    }
}
