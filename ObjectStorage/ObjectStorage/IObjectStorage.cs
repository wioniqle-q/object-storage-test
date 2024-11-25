namespace ObjectStorage;

internal interface IObjectStorage
{
    Task EncryptFileAsync(FileEncryptionRequest request, string filePublicMasterKey,
        CancellationToken cancellationToken);

    Task DecryptFileAsync(FileDecryptFileRequest request, string filePublicMasterKey,
        CancellationToken cancellationToken);

    void Dispose();
}