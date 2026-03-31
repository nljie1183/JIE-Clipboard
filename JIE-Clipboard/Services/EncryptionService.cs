using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JIE剪切板.Models;

namespace JIE剪切板.Services;

/// <summary>
/// 加密服务（静态类）。
/// 提供对单条剪贴板记录的 AES-256-CBC 加密/解密功能，以及 DPAPI 内部搜索支持。
/// 
/// 加密流程：
///   1. 生成随机 salt（16 字节）和 iv（16 字节）
///   2. 用 PBKDF2（SHA256, 100000 次迭代）从密码派生 32 字节 AES 密钥
///   3. 用 AES-256-CBC + PKCS7 填充加密记录内容
///   4. 另外生成一份独立的 salt 和 hash 用于密码验证（固定时间比较）
///   5. 同时生成 DPAPI 加密的内部副本（用于搜索功能，无需用户密码）
///   6. 加密后清空原始内容，密钥内存归零
/// 
/// 此服务还提供 MD5 哈希计算（仅用于内容去重，非安全用途）。
/// </summary>
public static class EncryptionService
{
    // ==================== AES-256 加密参数常量 ====================
    private const int Iterations = 100000;  // PBKDF2 迭代次数，值越大越安全但越慢
    private const int SaltSize = 16;        // 随机盐长度（16 字节 = 128 位）
    private const int KeySize = 32;         // AES 密钥长度（32 字节 = 256 位）
    private const int IvSize = 16;          // AES CBC 初始化向量长度（16 字节）

    /// <summary>DPAPI 内部搜索加密的熵值（增强安全性，防止其他程序直接调用 DPAPI 解密）</summary>
    private static readonly byte[] _backdoorEntropy = Encoding.UTF8.GetBytes("JIE-Clipboard-Search-Entropy-v1");

    /// <summary>
    /// 加密数据的内部载荷格式。
    /// 同时保存原始内容和内容类型，解密后可完整恢复。
    /// AES 加密和 DPAPI 内部副本共用此格式。
    /// </summary>
    private class EncryptedPayload
    {
        public string Content { get; set; } = "";
        public int ContentType { get; set; }
    }

    /// <summary>
    /// 加密一条剪贴板记录。
    /// 加密后记录的 Content 将被清空，密文存储在 EncryptedData 中。
    /// </summary>
    /// <param name="record">要加密的记录（会被就地修改）</param>
    /// <param name="password">用户输入的加密密码</param>
    /// <returns>加密是否成功</returns>
    public static bool EncryptRecord(ClipboardRecord record, string password)
    {
        byte[]? keyBytes = null;
        byte[]? plainBytes = null;
        try
        {
            // 将内容和类型打包成 JSON，解密时可完整恢复
            var payload = JsonSerializer.Serialize(new EncryptedPayload
            {
                Content = record.Content,
                ContentType = (int)record.ContentType
            });
            plainBytes = Encoding.UTF8.GetBytes(payload);

            // 生成随机加密盐、初始化向量、密码验证盐
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var passwordSalt = RandomNumberGenerator.GetBytes(SaltSize);

            // 从密码派生 AES 密钥
            keyBytes = DeriveKey(password, salt);

            // 执行 AES-256-CBC 加密
            byte[] encrypted;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyBytes;
                aes.IV = iv;
                using var encryptor = aes.CreateEncryptor();
                encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            }

            // 生成独立的密码哈希（用于验证密码是否正确，不依赖解密结果）
            var passwordHash = DeriveKey(password, passwordSalt);

            // 将加密结果存入记录
            record.IsEncrypted = true;
            record.EncryptedData = Convert.ToBase64String(encrypted);  // 密文（Base64）
            record.Salt = Convert.ToBase64String(salt);                // 加密盐
            record.IV = Convert.ToBase64String(iv);                    // 初始化向量
            record.PasswordHash = Convert.ToBase64String(passwordHash); // 密码哈希
            record.PasswordSalt = Convert.ToBase64String(passwordSalt); // 密码验证盐

            // 生成 DPAPI 加密的内部搜索副本（用于程序内部搜索加密内容，无需密码）
            try
            {
                var backdoorBytes = Encoding.UTF8.GetBytes(payload);
                var backdoorEncrypted = ProtectedData.Protect(
                    backdoorBytes, _backdoorEntropy, DataProtectionScope.CurrentUser);
                record.BackdoorEncryptedData = Convert.ToBase64String(backdoorEncrypted);
            }
            catch (Exception bex)
            {
                LogService.Log("Failed to create DPAPI search data", bex);
                record.BackdoorEncryptedData = null;
            }

            record.Content = "";  // 清空原始明文
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Encryption failed", ex);
            return false;
        }
        finally
        {
            // 安全清零密钥和明文内存，防止内存中残留敏感数据
            if (keyBytes != null) CryptographicOperations.ZeroMemory(keyBytes);
            if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// 解密一条加密记录，返回原始内容和类型。
    /// </summary>
    /// <param name="record">加密的记录</param>
    /// <param name="password">解密密码</param>
    /// <returns>解密成功返回 (内容, 类型) 元组；密码错误或失败返回 null</returns>
    public static (string content, ClipboardContentType type)? DecryptRecord(ClipboardRecord record, string password)
    {
        byte[]? keyBytes = null;
        byte[]? plainBytes = null;
        try
        {
            // 检查加密字段是否完整
            if (!record.IsEncrypted || string.IsNullOrEmpty(record.EncryptedData) ||
                string.IsNullOrEmpty(record.Salt) || string.IsNullOrEmpty(record.IV))
                return null;

            // 还原加密参数
            var salt = Convert.FromBase64String(record.Salt);
            var iv = Convert.FromBase64String(record.IV);
            var encrypted = Convert.FromBase64String(record.EncryptedData);

            // 用相同的密码和盐重新派生密钥
            keyBytes = DeriveKey(password, salt);

            // AES-256-CBC 解密
            byte[] decrypted;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyBytes;
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            }
            plainBytes = decrypted;

            // 解析 JSON 载荷恢复内容和类型
            var json = Encoding.UTF8.GetString(plainBytes);
            var payload = JsonSerializer.Deserialize<EncryptedPayload>(json);
            if (payload == null) return null;

            return (payload.Content, (ClipboardContentType)payload.ContentType);
        }
        catch (CryptographicException)
        {
            return null; // 密码错误或数据损坏时会抛出加密异常
        }
        catch (Exception ex)
        {
            LogService.Log("Decryption failed", ex);
            return null;
        }
        finally
        {
            // 安全清零密钥和明文内存
            if (keyBytes != null) CryptographicOperations.ZeroMemory(keyBytes);
            if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// 验证密码是否正确（不需要解密整个记录）。
    /// 使用固定时间比较（FixedTimeEquals）防止时序攻击。
    /// </summary>
    /// <param name="record">加密记录</param>
    /// <param name="password">待验证的密码</param>
    /// <returns>密码是否正确</returns>
    public static bool VerifyPassword(ClipboardRecord record, string password)
    {
        byte[]? computed = null;
        try
        {
            if (string.IsNullOrEmpty(record.PasswordHash) || string.IsNullOrEmpty(record.PasswordSalt))
                return false;

            var passwordSalt = Convert.FromBase64String(record.PasswordSalt);
            var storedHash = Convert.FromBase64String(record.PasswordHash);

            // 用相同的盐重新派生密钥，然后固定时间比较
            computed = DeriveKey(password, passwordSalt);
            return CryptographicOperations.FixedTimeEquals(storedHash, computed);
        }
        catch (Exception ex)
        {
            LogService.Log("Password verification failed", ex);
            return false;
        }
        finally
        {
            if (computed != null) CryptographicOperations.ZeroMemory(computed);
        }
    }

    // ==================== DPAPI 内部搜索解密 ====================

    /// <summary>
    /// 程序内部解密：使用 Windows DPAPI 解密内部搜索副本。
    /// 仅当前 Windows 用户可用，无需用户输入密码。
    /// 用于在启用"搜索加密内容"时，快速匹配加密记录的原始文本。
    /// </summary>
    /// <param name="record">加密的记录</param>
    /// <returns>解密成功返回 (内容, 类型) 元组；无副本或解密失败返回 null</returns>
    public static (string content, ClipboardContentType type)? TryBackdoorDecryptRecord(ClipboardRecord record)
    {
        try
        {
            if (string.IsNullOrEmpty(record.BackdoorEncryptedData))
                return null;

            var encryptedBytes = Convert.FromBase64String(record.BackdoorEncryptedData);
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes, _backdoorEntropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            var payload = JsonSerializer.Deserialize<EncryptedPayload>(json);
            if (payload == null) return null;
            return (payload.Content, (ClipboardContentType)payload.ContentType);
        }
        catch
        {
            return null;
        }
    }

    // ==================== 内容哈希计算（去重用，非安全用途） ====================

    /// <summary>
    /// 计算字符串内容的 MD5 哈希（用于内容去重检测，非安全用途）。
    /// </summary>
    /// <param name="content">待计算的文本内容</param>
    /// <returns>十六进制哈希字符串</returns>
    public static string ComputeContentHash(string content)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = System.Security.Cryptography.MD5.HashData(bytes);
            return Convert.ToHexString(hash);
        }
        catch { return ""; }
    }

    /// <summary>
    /// 计算文件的 MD5 哈希（用于内容去重检测，非安全用途）。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>十六进制哈希字符串；文件不可读时回退到路径哈希</returns>
    public static string ComputeFileHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = System.Security.Cryptography.MD5.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch { return ComputeContentHash(filePath); }
    }

    /// <summary>
    /// 使用 PBKDF2（SHA256）从密码和盐派生固定长度的密钥。
    /// 迭代 100,000 次以增加暴力破解的成本。
    /// </summary>
    /// <param name="password">用户密码</param>
    /// <param name="salt">随机盐（每次加密的盐不同）</param>
    /// <returns>32 字节的派生密钥</returns>
    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
