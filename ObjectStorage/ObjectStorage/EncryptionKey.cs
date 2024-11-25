namespace ObjectStorage;

public sealed class EncryptionKey
{
    internal string FileId { get; set; } = string.Empty;
    internal string EncryptedFilePrivateKey { get; set; } = string.Empty;
}