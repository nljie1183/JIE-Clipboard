using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JIE剪切板.Models;

namespace JIE剪切板.Services;

public static class FileService
{
    private static readonly string _configFolder;
    private static readonly string _configFile;
    private static string _dataFolder;
    private static string _recordsFile;
    private static string _imagesFolder;
    private static string _filesFolder;

    // Data-at-rest encryption salt for DPAPI additional entropy
    private static readonly byte[] _storageSalt = System.Text.Encoding.UTF8.GetBytes("JIE剪切板_Storage_v2");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataFolder => _dataFolder;
    public static string ImagesFolder => _imagesFolder;

    static FileService()
    {
        _configFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JIE剪切板");
        _configFile = Path.Combine(_configFolder, "config.json");
        _dataFolder = _configFolder;
        _recordsFile = Path.Combine(_dataFolder, "records.dat");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
        _filesFolder = Path.Combine(_dataFolder, "Files");
    }

    public static void SetCustomDataFolder(string? folder)
    {
        _dataFolder = string.IsNullOrEmpty(folder) ? _configFolder : folder;
        _recordsFile = Path.Combine(_dataFolder, "records.dat");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
        _filesFolder = Path.Combine(_dataFolder, "Files");
        EnsureDirectories();
    }

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

            // Copy records file (.dat or .json)
            if (File.Exists(oldRecordsFile) && !File.Exists(newRecordsFile))
                File.Copy(oldRecordsFile, newRecordsFile);
            else
            {
                // Also check for old .json format
                var oldJsonFile = Path.ChangeExtension(oldRecordsFile, ".json");
                var newJsonFile = Path.Combine(newFolder, "records.json");
                if (File.Exists(oldJsonFile) && !File.Exists(newJsonFile))
                    File.Copy(oldJsonFile, newJsonFile);
            }

            // Copy images folder
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

    public static List<ClipboardRecord> LoadRecords()
    {
        try
        {
            // Try loading encrypted format first (.dat)
            if (File.Exists(_recordsFile))
            {
                var encryptedBytes = File.ReadAllBytes(_recordsFile);
                var json = DecryptStorage(encryptedBytes);
                if (json != null)
                    return JsonSerializer.Deserialize<List<ClipboardRecord>>(json, _jsonOptions)
                           ?? new List<ClipboardRecord>();
            }

            // Migrate from old unencrypted format (.json)
            var oldJsonFile = Path.Combine(_dataFolder, "records.json");
            if (File.Exists(oldJsonFile))
            {
                var json = File.ReadAllText(oldJsonFile);
                var records = JsonSerializer.Deserialize<List<ClipboardRecord>>(json, _jsonOptions)
                              ?? new List<ClipboardRecord>();
                // Re-save in encrypted format and remove old file
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

    private static byte[] EncryptStorage(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(plainBytes, _storageSalt, DataProtectionScope.CurrentUser);
    }

    private static string? DecryptStorage(byte[] data)
    {
        // Try DPAPI first (new format)
        try
        {
            var plainBytes = ProtectedData.Unprotect(data, _storageSalt, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch { }

        // Fall back to legacy AES format for migration
        return DecryptStorageLegacy(data);
    }

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

    /// <summary>Encrypt an existing file in-place (returns path to .enc file).</summary>
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

    /// <summary>Copy a file to local Files/ folder and encrypt it with DPAPI.</summary>
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

    /// <summary>Decrypt a .enc file and write to a temp file. Caller should clean up.</summary>
    public static string? DecryptFileToTemp(string encPath)
    {
        try
        {
            var encrypted = File.ReadAllBytes(encPath);
            var decrypted = ProtectedData.Unprotect(encrypted, _storageSalt, DataProtectionScope.CurrentUser);

            // Get original extension by removing .enc suffix
            var nameWithoutEnc = Path.GetFileNameWithoutExtension(encPath); // e.g., guid.png
            var ext = Path.GetExtension(nameWithoutEnc); // e.g., .png
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

    /// <summary>Decrypt a .enc file and return the raw bytes (for in-memory use like thumbnails).</summary>
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

    /// <summary>Zip a folder, encrypt the zip with DPAPI, save to Files/ folder. Returns .enc path or empty on failure.</summary>
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

    /// <summary>Decrypt a .zip.enc file and extract to a temp folder. Caller should clean up.</summary>
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

            // The zip was created with includeBaseDirectory=true, so there's a single subfolder
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

    public static void DeleteRecordFiles(ClipboardRecord record)
    {
        try
        {
            if (record.IsEncrypted) return;

            if (record.ContentType == ClipboardContentType.Image)
            {
                // Delete the image file (plain or encrypted)
                if (File.Exists(record.Content))
                    File.Delete(record.Content);
            }
            else if (record.ContentType is ClipboardContentType.FileDrop or ClipboardContentType.Video
                     or ClipboardContentType.Folder)
            {
                // Delete encrypted local copies (.enc files in our Files folder)
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
                // Encrypted export: JIEEXP magic + version(1) + salt(16) + iv(16) + encrypted data
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
                    fs.WriteByte(1); // version
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

    public static (List<ClipboardRecord>? records, AppConfig? config, string error) ImportRecords(string filePath, Control? owner = null)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);

            // Check for encrypted export (JIEEXP magic header)
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

    private static (List<ClipboardRecord>? records, AppConfig? config, string error) ImportEncryptedRecords(byte[] fileBytes, Control? owner)
    {
        // Ask for password
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

    private class ExportData
    {
        public List<ClipboardRecord>? Records { get; set; }
        public AppConfig? Config { get; set; }
        public DateTime ExportTime { get; set; }
        public string Version { get; set; } = "1.0.0";
    }
}
