using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JIE剪切板.Models;

namespace JIE剪切板.Services;

/// <summary>
/// 文件服务（静态类）。
/// 负责所有数据的持久化存储，包括：
/// - 配置文件 (config.json) 的读写
/// - 剪贴板记录 (records.dat) 的 DPAPI 加密存储
/// - 图片/文件/文件夹的本地加密存储
/// - 数据导出/导入（支持 JIEEXP 加密格式）
/// - 数据目录迁移
/// 
/// 存储目录结构：
///   %APPDATA%/JIE剪切板/         ← 默认数据目录
///     ├─ config.json                  ← 配置文件（明文）
///     ├─ records.dat                  ← 记录数据（DPAPI 加密）
///     ├─ Images/                      ← 图片存储目录
///     └─ Files/                       ← 加密文件存储目录
/// </summary>
public static class FileService
{
    // ========== 路径字段 ==========
    private static readonly string _configFolder;  // 配置文件夹（固定在 %APPDATA%）
    private static readonly string _configFile;    // 配置文件路径
    private static string _dataFolder;             // 实际数据存储目录（可由用户自定义）
    private static string _recordsFile;            // 记录文件路径
    private static string _imagesFolder;           // 图片存储目录
    private static string _filesFolder;            // 加密文件存储目录

    // DPAPI 加密时使用的额外熵（增强安全性）
    private static readonly byte[] _storageSalt = System.Text.Encoding.UTF8.GetBytes("JIE剪切板_Storage_v2");

    /// <summary>JSON 序列化选项：格式化输出、不区分大小写、枚举转字符串</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>当前数据存储目录（可能是默认位置或用户自定义位置）</summary>
    public static string DataFolder => _dataFolder;

    /// <summary>图片存储目录路径</summary>
    public static string ImagesFolder => _imagesFolder;

    /// <summary>静态构造函数：初始化默认路径</summary>
    static FileService()
    {
        // 配置目录固定在 %APPDATA%\JIE剪切板
        _configFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JIE剪切板");
        _configFile = Path.Combine(_configFolder, "config.json");
        // 数据目录默认与配置目录相同，可通过 SetCustomDataFolder 修改
        _dataFolder = _configFolder;
        _recordsFile = Path.Combine(_dataFolder, "records.dat");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
        _filesFolder = Path.Combine(_dataFolder, "Files");
    }

    /// <summary>
    /// 设置自定义数据存储目录。传入 null 或空字符串则恢复默认目录。
    /// </summary>
    /// <param name="folder">自定义目录路径，或 null 表示恢复默认</param>
    public static void SetCustomDataFolder(string? folder)
    {
        _dataFolder = string.IsNullOrEmpty(folder) ? _configFolder : folder;
        _recordsFile = Path.Combine(_dataFolder, "records.dat");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
        _filesFolder = Path.Combine(_dataFolder, "Files");
        EnsureDirectories();
    }

    /// <summary>
    /// 将现有数据迁移到新目录。
    /// 复制 records.dat 和 Images 文件夹到新位置，然后切换数据目录。
    /// </summary>
    /// <param name="newFolder">新的数据存储目录</param>
    /// <returns>迁移是否成功</returns>
    public static bool MoveDataToFolder(string newFolder)
    {
        try
        {
            var oldRecordsFile = _recordsFile;
            var oldImagesFolder = _imagesFolder;

            if (!Directory.Exists(newFolder))
                Directory.CreateDirectory(newFolder);

            var newRecordsFile = Path.Combine(newFolder, "records.dat");
            var newImagesFolder = Path.Combine(newFolder, "Images");

            // 复制记录文件（.dat 或 .json）
            if (File.Exists(oldRecordsFile) && !File.Exists(newRecordsFile))
                File.Copy(oldRecordsFile, newRecordsFile);
            else
            {
                // 同时检查旧版 .json 格式
                var oldJsonFile = Path.ChangeExtension(oldRecordsFile, ".json");
                var newJsonFile = Path.Combine(newFolder, "records.json");
                if (File.Exists(oldJsonFile) && !File.Exists(newJsonFile))
                    File.Copy(oldJsonFile, newJsonFile);
            }

            // 复制图片文件夹
            if (Directory.Exists(oldImagesFolder))
            {
                if (!Directory.Exists(newImagesFolder))
                    Directory.CreateDirectory(newImagesFolder);
                foreach (var file in Directory.GetFiles(oldImagesFolder))
                {
                    var destFile = Path.Combine(newImagesFolder, Path.GetFileName(file));
                    if (!File.Exists(destFile))
                        File.Copy(file, destFile);
                }
            }

            SetCustomDataFolder(newFolder);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to move data to new folder", ex);
            return false;
        }
    }

    /// <summary>确保所有必要的目录存在，不存在则自动创建</summary>
    public static bool EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(_configFolder)) Directory.CreateDirectory(_configFolder);
            if (!Directory.Exists(_dataFolder)) Directory.CreateDirectory(_dataFolder);
            if (!Directory.Exists(_imagesFolder)) Directory.CreateDirectory(_imagesFolder);
            if (!Directory.Exists(_filesFolder)) Directory.CreateDirectory(_filesFolder);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to create data directories", ex);
            return false;
        }
    }

    /// <summary>从 config.json 加载应用配置，文件不存在或损坏时返回默认配置</summary>
    public static AppConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(_configFile)) return new AppConfig();
            var json = File.ReadAllText(_configFile);
            return JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to load config, backing up and creating new", ex);
            BackupCorruptFile(_configFile);
            return new AppConfig();
        }
    }

    /// <summary>
    /// 保存应用配置到 config.json。
    /// 使用“先写临时文件再重命名”的安全写入策略。
    /// </summary>
    public static bool SaveConfig(AppConfig config)
    {
        try
        {
            EnsureDirectories();
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            var tempFile = _configFile + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _configFile, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save config", ex);
            return false;
        }
    }

    /// <summary>
    /// 加载剪贴板记录。
    /// 优先读取 DPAPI 加密的 records.dat；
    /// 如果不存在，尝试从旧版明文 records.json 迁移。
    /// </summary>
    public static List<ClipboardRecord> LoadRecords()
    {
        try
        {
            // 优先尝试加载 DPAPI 加密格式（.dat）
            if (File.Exists(_recordsFile))
            {
                var encryptedBytes = File.ReadAllBytes(_recordsFile);
                var json = DecryptStorage(encryptedBytes);
                if (json != null)
                    return JsonSerializer.Deserialize<List<ClipboardRecord>>(json, _jsonOptions)
                           ?? new List<ClipboardRecord>();
            }

            // 从旧版明文格式（.json）迁移
            var oldJsonFile = Path.Combine(_dataFolder, "records.json");
            if (File.Exists(oldJsonFile))
            {
                var json = File.ReadAllText(oldJsonFile);
                var records = JsonSerializer.Deserialize<List<ClipboardRecord>>(json, _jsonOptions)
                              ?? new List<ClipboardRecord>();
                // 重新以加密格式保存，并删除旧文件
                SaveRecords(records);
                try { File.Delete(oldJsonFile); } catch { }
                return records;
            }

            return new List<ClipboardRecord>();
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to load records, backing up and creating new", ex);
            BackupCorruptFile(_recordsFile);
            return new List<ClipboardRecord>();
        }
    }

    /// <summary>
    /// 保存剪贴板记录。
    /// 使用 DPAPI 加密后写入 records.dat，同样使用安全写入策略。
    /// </summary>
    public static bool SaveRecords(List<ClipboardRecord> records)
    {
        try
        {
            EnsureDirectories();
            var json = JsonSerializer.Serialize(records, _jsonOptions);
            var encryptedBytes = EncryptStorage(json);
            var tempFile = _recordsFile + ".tmp";
            File.WriteAllBytes(tempFile, encryptedBytes);
            File.Move(tempFile, _recordsFile, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save records", ex);
            return false;
        }
    }

    /// <summary>使用 DPAPI 加密字符串（仅当前 Windows 用户可解密）</summary>
    private static byte[] EncryptStorage(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(plainBytes, _storageSalt, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// 解密存储数据。优先尝试 DPAPI 解密（新格式），
    /// 失败时回退到旧版 AES 格式（用于老版本迁移）。
    /// </summary>
    private static string? DecryptStorage(byte[] data)
    {
        // 优先尝试 DPAPI 解密（新格式）
        try
        {
            var plainBytes = ProtectedData.Unprotect(data, _storageSalt, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch { }

        // 回退到旧版 AES 格式（用于老版本数据迁移）
        return DecryptStorageLegacy(data);
    }

    /// <summary>
    /// 旧版 AES 解密方式（只读，用于迁移）。
    /// 使用机器名+用户名生成密钥，数据格式：[16字节IV] + [加密数据]。
    /// 新安装不会产生此格式，仅用于兼容老数据。
    /// </summary>
    private static string? DecryptStorageLegacy(byte[] data)
    {
        if (data.Length < 17) return null;
        byte[]? legacyKey = null;
        try
        {
            var identity = $"{Environment.MachineName}|{Environment.UserName}|JIE剪切板_SecureStorage";
            var identityBytes = System.Text.Encoding.UTF8.GetBytes(identity);
            using var pbkdf2 = new Rfc2898DeriveBytes(identityBytes, _storageSalt, 50000, HashAlgorithmName.SHA256);
            legacyKey = pbkdf2.GetBytes(32);

            var iv = new byte[16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            var encrypted = new byte[data.Length - 16];
            Buffer.BlockCopy(data, 16, encrypted, 0, encrypted.Length);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = legacyKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return System.Text.Encoding.UTF8.GetString(decrypted);
        }
        catch { return null; }
        finally
        {
            if (legacyKey != null) CryptographicOperations.ZeroMemory(legacyKey);
        }
    }

    /// <summary>保存图片到本地 Images 目录，返回保存路径</summary>
    public static string SaveImage(Image image)
    {
        try
        {
            EnsureDirectories();
            var fileName = $"{Guid.NewGuid():N}.png";
            var filePath = Path.Combine(_imagesFolder, fileName);
            using var bitmap = new Bitmap(image);
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            return filePath;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save image", ex);
            return "";
        }
    }

    /// <summary>将已有文件就地 DPAPI 加密，生成 .enc 文件（用于图片加密存储）</summary>
    public static string EncryptExistingFile(string sourcePath)
    {
        try
        {
            var encPath = sourcePath + ".enc";
            var fileBytes = File.ReadAllBytes(sourcePath);
            var encrypted = ProtectedData.Protect(fileBytes, _storageSalt, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(encPath, encrypted);
            return encPath;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to encrypt existing file", ex);
            return "";
        }
    }

    /// <summary>复制文件到本地 Files/ 目录并用 DPAPI 加密（用于文件/视频持久化）</summary>
    public static string SaveAndEncryptFile(string sourcePath)
    {
        try
        {
            EnsureDirectories();
            var ext = Path.GetExtension(sourcePath);
            var fileName = $"{Guid.NewGuid():N}{ext}.enc";
            var destPath = Path.Combine(_filesFolder, fileName);

            var fileBytes = File.ReadAllBytes(sourcePath);
            var encrypted = ProtectedData.Protect(fileBytes, _storageSalt, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(destPath, encrypted);
            return destPath;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save and encrypt file", ex);
            return "";
        }
    }

    /// <summary>解密 .enc 文件到临时目录，返回临时文件路径。调用方负责清理。</summary>
    public static string? DecryptFileToTemp(string encPath)
    {
        try
        {
            var encrypted = File.ReadAllBytes(encPath);
            var decrypted = ProtectedData.Unprotect(encrypted, _storageSalt, DataProtectionScope.CurrentUser);

            // 移除 .enc 后缀获取原始扩展名
            var nameWithoutEnc = Path.GetFileNameWithoutExtension(encPath); // 例如 guid.png
            var ext = Path.GetExtension(nameWithoutEnc); // 例如 .png
            if (string.IsNullOrEmpty(ext)) ext = ".tmp";

            var tempPath = Path.Combine(Path.GetTempPath(), $"jie_clip_{Guid.NewGuid():N}{ext}");
            File.WriteAllBytes(tempPath, decrypted);
            return tempPath;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to decrypt file to temp", ex);
            return null;
        }
    }

    /// <summary>解密 .enc 文件并返回原始字节数组（用于内存中处理，如缩略图生成）</summary>
    public static byte[]? DecryptFileBytes(string encPath)
    {
        try
        {
            var encrypted = File.ReadAllBytes(encPath);
            return ProtectedData.Unprotect(encrypted, _storageSalt, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to decrypt file bytes", ex);
            return null;
        }
    }

    /// <summary>
    /// 将文件夹压缩为 ZIP 并用 DPAPI 加密保存到 Files/ 目录。
    /// ZIP 超过 maxBytes 时跳过加密。临时 ZIP 文件在 finally 中清理。
    /// </summary>
    /// <param name="folderPath">原始文件夹路径</param>
    /// <param name="maxBytes">最大允许的 ZIP 大小（字节）</param>
    /// <returns>加密文件路径；失败或超大时返回空字符串</returns>
    public static string SaveAndEncryptFolder(string folderPath, long maxBytes)
    {
        string? tempZip = null;
        try
        {
            EnsureDirectories();
            tempZip = Path.Combine(Path.GetTempPath(), $"jie_zip_{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(folderPath, tempZip, CompressionLevel.Fastest, includeBaseDirectory: true);

            var zipInfo = new FileInfo(tempZip);
            if (zipInfo.Length > maxBytes)
            {
                LogService.Log($"Folder zip too large ({zipInfo.Length} bytes), skipping encryption");
                return "";
            }

            var zipBytes = File.ReadAllBytes(tempZip);
            var encrypted = ProtectedData.Protect(zipBytes, _storageSalt, DataProtectionScope.CurrentUser);

            var fileName = $"{Guid.NewGuid():N}.zip.enc";
            var destPath = Path.Combine(_filesFolder, fileName);
            File.WriteAllBytes(destPath, encrypted);
            return destPath;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save and encrypt folder", ex);
            return "";
        }
        finally
        {
            if (tempZip != null)
                try { File.Delete(tempZip); } catch { }
        }
    }

    /// <summary>
    /// 解密 .zip.enc 文件并解压到临时文件夹。调用方负责清理。
    /// 解压后如果只有一个子目录，返回该子目录（因为压缩时包含了基目录）。
    /// </summary>
    public static string? DecryptFolderToTemp(string encPath)
    {
        string? tempZip = null;
        try
        {
            var encrypted = File.ReadAllBytes(encPath);
            var decrypted = ProtectedData.Unprotect(encrypted, _storageSalt, DataProtectionScope.CurrentUser);

            tempZip = Path.Combine(Path.GetTempPath(), $"jie_zip_{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(tempZip, decrypted);

            var tempFolder = Path.Combine(Path.GetTempPath(), $"jie_folder_{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(tempZip, tempFolder);

            // Zip Slip 防护：校验解压出的所有文件是否都在目标目录内
            var fullTempFolder = Path.GetFullPath(tempFolder) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.GetFiles(tempFolder, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetFullPath(file).StartsWith(fullTempFolder, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Log($"Zip Slip detected: {file} escapes {tempFolder}");
                    try { Directory.Delete(tempFolder, true); } catch { }
                    return null;
                }
            }

            // ZIP 压缩时包含了基目录，所以解压后只有一个子目录
            var subdirs = Directory.GetDirectories(tempFolder);
            return subdirs.Length == 1 ? subdirs[0] : tempFolder;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to decrypt folder to temp", ex);
            return null;
        }
        finally
        {
            if (tempZip != null)
                try { File.Delete(tempZip); } catch { }
        }
    }

    /// <summary>
    /// 删除记录关联的本地文件（图片、加密文件等）。
    /// 仅删除存储在我们 Files/ 目录下的 .enc 文件，不会删除用户原始文件。
    /// </summary>
    public static void DeleteRecordFiles(ClipboardRecord record)
    {
        try
        {
            if (record.IsEncrypted) return;

            if (record.ContentType == ClipboardContentType.Image)
            {
                // 删除图片文件（明文或加密版本）
                if (File.Exists(record.Content))
                    File.Delete(record.Content);
            }
            else if (record.ContentType is ClipboardContentType.FileDrop or ClipboardContentType.Video
                     or ClipboardContentType.Folder)
            {
                // 删除 Files/ 目录下的加密本地副本（.enc 文件）
                var paths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    if (path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) &&
                        path.StartsWith(_filesFolder, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to delete record files", ex);
        }
    }

    /// <summary>清理 Images 目录下不再被任何记录引用的孤立图片文件</summary>
    public static void CleanupOrphanedImages(List<ClipboardRecord> records)
    {
        try
        {
            if (!Directory.Exists(_imagesFolder)) return;
            var usedPaths = records
                .Where(r => r.ContentType == ClipboardContentType.Image && !r.IsEncrypted)
                .Select(r => r.Content)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(_imagesFolder))
            {
                if (!usedPaths.Contains(file))
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to cleanup orphaned images", ex);
        }
    }

    /// <summary>
    /// 导出记录和/或配置到文件。
    /// 如果指定密码，则使用 JIEEXP 加密格式：
    ///   [6字节魔数 "JIEEXP"] + [版本号 1字节] + [salt 16字节] + [iv 16字节] + [AES加密数据]
    /// </summary>
    /// <param name="records">要导出的记录列表</param>
    /// <param name="config">要导出的配置（可为 null）</param>
    /// <param name="filePath">导出文件路径</param>
    /// <param name="password">加密密码（null 表示不加密）</param>
    /// <returns>空字符串表示成功，否则返回错误信息</returns>
    public static string ExportRecords(List<ClipboardRecord> records, AppConfig? config, string filePath, string? password = null)
    {
        try
        {
            var exportData = new ExportData
            {
                Records = records,
                Config = config,
                ExportTime = DateTime.UtcNow,
                Version = "1.0.0"
            };
            var json = JsonSerializer.Serialize(exportData, _jsonOptions);

            if (!string.IsNullOrEmpty(password))
            {
                // 加密导出：JIEEXP 魔数 + 版本(1字节) + salt(16字节) + iv(16字节) + 加密数据
                var salt = RandomNumberGenerator.GetBytes(16);
                var iv = RandomNumberGenerator.GetBytes(16);
                byte[]? key = null;
                try
                {
                    using var pbkdf2 = new Rfc2898DeriveBytes(
                        System.Text.Encoding.UTF8.GetBytes(password), salt, 100000, HashAlgorithmName.SHA256);
                    key = pbkdf2.GetBytes(32);

                    using var aes = Aes.Create();
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    var plainBytes = System.Text.Encoding.UTF8.GetBytes(json);
                    byte[] encrypted;
                    using (var encryptor = aes.CreateEncryptor())
                        encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    var magic = System.Text.Encoding.ASCII.GetBytes("JIEEXP");
                    fs.Write(magic, 0, 6);
                    fs.WriteByte(1); // 版本号
                    fs.Write(salt, 0, 16);
                    fs.Write(iv, 0, 16);
                    fs.Write(encrypted, 0, encrypted.Length);
                }
                finally
                {
                    if (key != null) CryptographicOperations.ZeroMemory(key);
                }
            }
            else
            {
                File.WriteAllText(filePath, json);
            }
            return "";
        }
        catch (Exception ex)
        {
            LogService.Log("Export failed", ex);
            return $"导出失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 从文件导入记录和配置。自动检测是否为 JIEEXP 加密格式。
    /// </summary>
    /// <param name="filePath">导入文件路径</param>
    /// <param name="owner">父窗口（用于显示密码对话框）</param>
    /// <returns>(记录列表, 配置, 错误信息) 元组</returns>
    public static (List<ClipboardRecord>? records, AppConfig? config, string error) ImportRecords(string filePath, Control? owner = null)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);

            // 检查是否为加密导出文件（JIEEXP 魔数头）
            if (fileBytes.Length > 39 && System.Text.Encoding.ASCII.GetString(fileBytes, 0, 6) == "JIEEXP")
            {
                return ImportEncryptedRecords(fileBytes, owner);
            }

            var json = System.Text.Encoding.UTF8.GetString(fileBytes);
            var data = JsonSerializer.Deserialize<ExportData>(json, _jsonOptions);
            if (data == null) return (null, null, "备份文件格式无效");
            return (data.Records, data.Config, "");
        }
        catch (Exception ex)
        {
            LogService.Log("Import failed", ex);
            return (null, null, $"导入失败: {ex.Message}");
        }
    }

    /// <summary>导入加密的导出文件（JIEEXP 格式），会弹出密码输入对话框</summary>
    private static (List<ClipboardRecord>? records, AppConfig? config, string error) ImportEncryptedRecords(byte[] fileBytes, Control? owner)
    {
        // 弹出密码输入框
        using var pwDialog = new JIE剪切板.Dialogs.PasswordDialog();
        pwDialog.Text = "导入文件已加密";
        pwDialog.TopMost = true;
        if (owner != null)
        {
            if (pwDialog.ShowDialog(owner) != DialogResult.OK)
                return (null, null, "已取消导入");
        }
        else
        {
            if (pwDialog.ShowDialog() != DialogResult.OK)
                return (null, null, "已取消导入");
        }

        byte[]? key = null;
        try
        {
            var version = fileBytes[6];
            if (version != 1) return (null, null, "不支持的导出文件版本");

            var salt = new byte[16];
            var iv = new byte[16];
            Buffer.BlockCopy(fileBytes, 7, salt, 0, 16);
            Buffer.BlockCopy(fileBytes, 23, iv, 0, 16);
            var encrypted = new byte[fileBytes.Length - 39];
            Buffer.BlockCopy(fileBytes, 39, encrypted, 0, encrypted.Length);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                System.Text.Encoding.UTF8.GetBytes(pwDialog.Password), salt, 100000, HashAlgorithmName.SHA256);
            key = pbkdf2.GetBytes(32);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            byte[] decrypted;
            using (var decryptor = aes.CreateDecryptor())
                decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

            var json = System.Text.Encoding.UTF8.GetString(decrypted);
            var data = JsonSerializer.Deserialize<ExportData>(json, _jsonOptions);
            if (data == null) return (null, null, "解密成功但数据格式无效");
            return (data.Records, data.Config, "");
        }
        catch (CryptographicException)
        {
            return (null, null, "密码错误，无法解密导出文件");
        }
        catch (Exception ex)
        {
            LogService.Log("Import encrypted failed", ex);
            return (null, null, $"导入解密失败: {ex.Message}");
        }
        finally
        {
            if (key != null) CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>备份损坏的文件（重命名加时间戳后缀）</summary>
    private static void BackupCorruptFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var ext = Path.GetExtension(filePath);
                var backupPath = $"{filePath}_bak_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                File.Move(filePath, backupPath);
            }
        }
        catch { /* Ignore backup failures */ }
    }

    /// <summary>导出数据的包装类，包含记录、配置、导出时间和版本号</summary>
    private class ExportData
    {
        public List<ClipboardRecord>? Records { get; set; }
        public AppConfig? Config { get; set; }
        public DateTime ExportTime { get; set; }
        public string Version { get; set; } = "1.0.0";
    }
}
