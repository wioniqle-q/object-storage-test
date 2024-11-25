using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ObjectStorage;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IKeyVaultService, KeyVaultService>();
builder.Services.AddScoped<IObjectStorage, Storage>();

builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.SetMinimumLevel(LogLevel.Debug);
});

var app = builder.Build();

try
{
    const string sourcePath = @"";

    var destinationPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "encrypted_" + Path.GetFileName(sourcePath));
    var decryptedPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "decrypted_" + Path.GetFileName(sourcePath));

    var objectStorage = app.Services.GetRequiredService<IObjectStorage>();

    var filePublicMasterKey = GenerateAesKey();
    var fileId = Guid.NewGuid().ToString();
    
    var encryptRequest = new FileEncryptionRequest
    {
        FileId = fileId,
        SourcePath = sourcePath,
        DestinationPath = destinationPath
    };

    Console.WriteLine("Starting encryption...");
    await objectStorage.EncryptFileAsync(encryptRequest, filePublicMasterKey, CancellationToken.None);
    Console.WriteLine("Encryption completed!");

    var decryptRequest = new FileDecryptFileRequest
    {
        FileId = fileId,
        SourcePath = destinationPath,
        DestinationPath = decryptedPath
    };

    Console.WriteLine("Starting decryption...");
    await objectStorage.DecryptFileAsync(decryptRequest, filePublicMasterKey, CancellationToken.None);
    Console.WriteLine("Decryption completed!");

    objectStorage.Dispose();

    Console.WriteLine($"Original file: {sourcePath}");
    Console.WriteLine($"Encrypted file: {destinationPath}");
    Console.WriteLine($"Decrypted file: {decryptedPath}");
}
catch (Exception e)
{
    Console.WriteLine(e);
}

await app.RunAsync();
return;

static string GenerateAesKey(int keySize = 256)
{
    if (keySize is not (128 or 192 or 256))
        throw new ArgumentOutOfRangeException(nameof(keySize), "Key size must be 128, 192, or 256 bits.");

    var keyBytes = new byte[keySize / 8];
    RandomNumberGenerator.Fill(keyBytes);

    return Convert.ToBase64String(keyBytes);
}