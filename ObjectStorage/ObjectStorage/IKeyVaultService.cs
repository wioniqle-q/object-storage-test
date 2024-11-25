namespace ObjectStorage;

public interface IKeyVaultService
{
    Task<string> StoreKeyAsync(string fileId, string filePrivateKey, string filePublicMasterKey);
    Task<string> RetrieveKeyAsync(string fileId, string filePublicMasterKey);
}