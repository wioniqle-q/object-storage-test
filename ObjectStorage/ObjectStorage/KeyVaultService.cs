using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ObjectStorage;

public class KeyVaultService : IKeyVaultService
{
    private readonly ConcurrentDictionary<string, EncryptionKey> _keyStore = new();
    private readonly string _systemSecurityKey = GenerateSecureRandomString(256);

    public async Task<string> StoreKeyAsync(string fileId, string filePrivateKey, string filePublicMasterKey)
    {
        var firstLayerEncryption = await EncryptAsync(filePrivateKey, filePublicMasterKey);
        var finalEncryptedKey = await EncryptAsync(firstLayerEncryption, _systemSecurityKey);

        var encryptionKey = new EncryptionKey
        {
            FileId = fileId,
            EncryptedFilePrivateKey = finalEncryptedKey
        };

        _keyStore.TryAdd(fileId, encryptionKey);
        return finalEncryptedKey;
    }

    public async Task<string> RetrieveKeyAsync(string fileId, string filePublicMasterKey)
    {
        if (!_keyStore.TryGetValue(fileId, out var encryptionKey))
            throw new KeyNotFoundException($"No key found for file ID: {fileId}");

        var firstLayerDecryption = await DecryptAsync(encryptionKey.EncryptedFilePrivateKey, _systemSecurityKey);

        return await DecryptAsync(firstLayerDecryption, filePublicMasterKey);
    }

    private static string GenerateSecureRandomString(int keySize)
    {
        if (keySize is not (128 or 192 or 256))
            throw new ArgumentOutOfRangeException(nameof(keySize), "Key size must be 128, 192, or 256 bits.");

        var keyBytes = new byte[keySize / 8];
        RandomNumberGenerator.Fill(keyBytes);

        return Convert.ToBase64String(keyBytes);
    }

    private static async Task<string> EncryptAsync(string data, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(key);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        await msEncrypt.WriteAsync(aes.IV);

        await using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        await using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            await swEncrypt.WriteAsync(data);
        }

        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    private static async Task<string> DecryptAsync(string encryptedData, string key)
    {
        var fullCipher = Convert.FromBase64String(encryptedData);

        using var aes = Aes.Create();
        var iv = new byte[16];
        var cipher = new byte[fullCipher.Length - 16];

        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.Key = Convert.FromBase64String(key);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream(cipher);
        await using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);

        return await srDecrypt.ReadToEndAsync();
    }
}