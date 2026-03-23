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
        _recordsFile = Path.Combine(_dataFolder, "records.json");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
    }

    public static void SetCustomDataFolder(string? folder)
    {
        _dataFolder = string.IsNullOrEmpty(folder) ? _configFolder : folder;
        _recordsFile = Path.Combine(_dataFolder, "records.json");
        _imagesFolder = Path.Combine(_dataFolder, "Images");
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

            var newRecordsFile = Path.Combine(newFolder, "records.json");
            var newImagesFolder = Path.Combine(newFolder, "Images");

            // Copy records file
            if (File.Exists(oldRecordsFile) && !File.Exists(newRecordsFile))
                File.Copy(oldRecordsFile, newRecordsFile);

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
            File.WriteAllText(_configFile, json);
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
            if (!File.Exists(_recordsFile)) return new List<ClipboardRecord>();
            var json = File.ReadAllText(_recordsFile);
            return JsonSerializer.Deserialize<List<ClipboardRecord>>(json, _jsonOptions)
                   ?? new List<ClipboardRecord>();
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
            File.WriteAllText(_recordsFile, json);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save records", ex);
            return false;
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

    public static void DeleteRecordFiles(ClipboardRecord record)
    {
        try
        {
            if (record.ContentType == ClipboardContentType.Image && !record.IsEncrypted)
            {
                if (File.Exists(record.Content))
                    File.Delete(record.Content);
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

    public static string ExportRecords(List<ClipboardRecord> records, AppConfig? config, string filePath)
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
            File.WriteAllText(filePath, json);
            return "";
        }
        catch (Exception ex)
        {
            LogService.Log("Export failed", ex);
            return $"导出失败: {ex.Message}";
        }
    }

    public static (List<ClipboardRecord>? records, AppConfig? config, string error) ImportRecords(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
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

    private static void BackupCorruptFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var backupPath = $"{filePath}_bak_{DateTime.Now:yyyyMMdd_HHmmss}.json";
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
