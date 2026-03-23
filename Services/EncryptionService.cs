using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JIE剪切板.Models;

namespace JIE剪切板.Services;

public static class EncryptionService
{
    private const int Iterations = 100000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int IvSize = 16;

    private class EncryptedPayload
    {
        public string Content { get; set; } = "";
        public int ContentType { get; set; }
    }

    public static bool EncryptRecord(ClipboardRecord record, string password)
    {
        byte[]? keyBytes = null;
        byte[]? plainBytes = null;
        try
        {
            var payload = JsonSerializer.Serialize(new EncryptedPayload
            {
                Content = record.Content,
                ContentType = (int)record.ContentType
            });
            plainBytes = Encoding.UTF8.GetBytes(payload);

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var passwordSalt = RandomNumberGenerator.GetBytes(SaltSize);

            keyBytes = DeriveKey(password, salt);

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

            var passwordHash = DeriveKey(password, passwordSalt);

            record.IsEncrypted = true;
            record.EncryptedData = Convert.ToBase64String(encrypted);
            record.Salt = Convert.ToBase64String(salt);
            record.IV = Convert.ToBase64String(iv);
            record.PasswordHash = Convert.ToBase64String(passwordHash);
            record.PasswordSalt = Convert.ToBase64String(passwordSalt);
            record.Content = "";
            return true;
        }
        catch (Exception ex)
        {
            LogService.Log("Encryption failed", ex);
            return false;
        }
        finally
        {
            if (keyBytes != null) CryptographicOperations.ZeroMemory(keyBytes);
            if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public static (string content, ClipboardContentType type)? DecryptRecord(ClipboardRecord record, string password)
    {
        byte[]? keyBytes = null;
        byte[]? plainBytes = null;
        try
        {
            if (!record.IsEncrypted || string.IsNullOrEmpty(record.EncryptedData) ||
                string.IsNullOrEmpty(record.Salt) || string.IsNullOrEmpty(record.IV))
                return null;

            var salt = Convert.FromBase64String(record.Salt);
            var iv = Convert.FromBase64String(record.IV);
            var encrypted = Convert.FromBase64String(record.EncryptedData);

            keyBytes = DeriveKey(password, salt);

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

            var json = Encoding.UTF8.GetString(plainBytes);
            var payload = JsonSerializer.Deserialize<EncryptedPayload>(json);
            if (payload == null) return null;

            return (payload.Content, (ClipboardContentType)payload.ContentType);
        }
        catch (CryptographicException)
        {
            return null; // Wrong password or corrupted data
        }
        catch (Exception ex)
        {
            LogService.Log("Decryption failed", ex);
            return null;
        }
        finally
        {
            if (keyBytes != null) CryptographicOperations.ZeroMemory(keyBytes);
            if (plainBytes != null) CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public static bool VerifyPassword(ClipboardRecord record, string password)
    {
        byte[]? computed = null;
        try
        {
            if (string.IsNullOrEmpty(record.PasswordHash) || string.IsNullOrEmpty(record.PasswordSalt))
                return false;

            var passwordSalt = Convert.FromBase64String(record.PasswordSalt);
            var storedHash = Convert.FromBase64String(record.PasswordHash);

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

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }
}
