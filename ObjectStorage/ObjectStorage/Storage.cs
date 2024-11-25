using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace ObjectStorage;

public sealed class Storage(ILogger<Storage> logger, IKeyVaultService keyVaultService) : IObjectStorage, IDisposable
{
    private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

    private readonly IKeyVaultService _keyVaultService =
        keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));

    private readonly ILogger<Storage> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private bool _disposed;

    public async Task EncryptFileAsync(FileEncryptionRequest request, string filePublicMasterKey,
        CancellationToken cancellationToken)
    {
        LogDebugInfo(request.FileId, filePublicMasterKey);

        var filePrivateKey = Convert.ToBase64String(GenerateRandomKey());
        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(filePrivateKey);
        aes.GenerateIV();

        await _keyVaultService.StoreKeyAsync(request.FileId, filePrivateKey, filePublicMasterKey);
        await ProcessEncryptedFileAsync(request, aes, cancellationToken);
    }

    public async Task DecryptFileAsync(FileDecryptFileRequest request, string filePublicMasterKey,
        CancellationToken cancellationToken)
    {
        LogDebugInfo(request.FileId, filePublicMasterKey);

        var filePrivateKey = await _keyVaultService.RetrieveKeyAsync(request.FileId, filePublicMasterKey);
        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(filePrivateKey);

        await using var sourceStream = CreateFileStream(request.SourcePath, FileMode.Open, FileAccess.Read);
        await using var destinationStream =
            CreateFileStream(request.DestinationPath, FileMode.Create, FileAccess.Write);

        await ProcessDecryptionAsync(aes, sourceStream, destinationStream, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _memoryPool.Dispose();
        _disposed = true;
    }

    private static byte[] GenerateRandomKey()
    {
        using var aes = Aes.Create();
        aes.GenerateKey();
        return aes.Key;
    }

    private static UnbufferedFileStream CreateFileStream(string path, FileMode mode, FileAccess access)
    {
        return new UnbufferedFileStream(path, mode, access, FileShare.None, Constants.StorageConstants.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan |
            (access is FileAccess.Write ? FileOptions.WriteThrough : FileOptions.None));
    }

    private static async Task ProcessDecryptionAsync(Aes aes, Stream sourceStream, Stream destinationStream,
        CancellationToken cancellationToken)
    {
        var iv = ArrayPool.Rent(Constants.StorageConstants.IvSize);
        try
        {
            await sourceStream.ReadExactlyAsync(iv.AsMemory(0, Constants.StorageConstants.IvSize), cancellationToken)
                .ConfigureAwait(false);
            aes.IV = iv.AsSpan(0, Constants.StorageConstants.IvSize).ToArray();
        }
        finally
        {
            ArrayPool.Return(iv);
        }

        using var decryptor = aes.CreateDecryptor();
        await using var cryptoStream = new CryptoStream(sourceStream, decryptor, CryptoStreamMode.Read);

        var buffer = ArrayPool.Rent(Constants.StorageConstants.BufferSize);
        try
        {
            int bytesRead;
            while ((bytesRead =
                       await cryptoStream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool.Return(buffer);
        }
    }

    private void LogDebugInfo(string fileId, string filePublicKey)
    {
        if (_logger.IsEnabled(LogLevel.Debug) is not true) return;

        _logger.LogDebug("File Id: {FileId}", fileId);
        _logger.LogDebug("File Public Key: {FilePublicKey}", filePublicKey);
    }

    private async ValueTask ProcessEncryptedFileAsync(FileEncryptionRequest request, Aes aes,
        CancellationToken cancellationToken)
    {
        var pipeOptions = new PipeOptions(
            _memoryPool,
            minimumSegmentSize: Constants.StorageConstants.BufferSize,
            pauseWriterThreshold: Constants.StorageConstants.PauseWriterThreshold,
            resumeWriterThreshold: Constants.StorageConstants.ResumeWriterThreshold);

        var pipe = new Pipe(pipeOptions);

        await using var destinationStream = new UnbufferedFileStream(
            request.DestinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            Constants.StorageConstants.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await destinationStream.WriteAsync(aes.IV.AsMemory(), cancellationToken).ConfigureAwait(false);
        await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        await Task.WhenAll(
            FillPipeAsync(pipe, request.SourcePath, cancellationToken),
            ProcessPipeAsync(pipe, aes, destinationStream, cancellationToken)
        ).ConfigureAwait(false);
    }

    private static async Task ProcessPipeAsync(Pipe pipe, Aes aes, Stream destinationStream,
        CancellationToken cancellationToken)
    {
        using var encryptor = aes.CreateEncryptor();
        await using var cryptoStream = new CryptoStream(destinationStream, encryptor, CryptoStreamMode.Write, true);

        try
        {
            while (cancellationToken.IsCancellationRequested is not true)
            {
                var result = await pipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                foreach (var segment in result.Buffer)
                    await cryptoStream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);

                pipe.Reader.AdvanceTo(result.Buffer.End);

                if (result.IsCompleted is not true) continue;

                await cryptoStream.FlushFinalBlockAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
        }
        catch (Exception)
        {
            pipe.Reader.CancelPendingRead();
        }
        finally
        {
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async Task FillPipeAsync(Pipe pipe, string sourcePath, CancellationToken cancellationToken)
    {
        await using var sourceStream = new UnbufferedFileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            Constants.StorageConstants.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = ArrayPool.Rent(Constants.StorageConstants.BufferSize);
        try
        {
            while (cancellationToken.IsCancellationRequested is not true)
            {
                var readBytes = await sourceStream.ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (readBytes is 0) break;

                var memory = pipe.Writer.GetMemory(readBytes);
                buffer.AsSpan(0, readBytes).CopyTo(memory.Span);
                pipe.Writer.Advance(readBytes);

                var result = await pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCompleted) break;
            }
        }
        catch (Exception)
        {
            pipe.Writer.CancelPendingFlush();
        }
        finally
        {
            ArrayPool.Return(buffer);
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }
    }
}