using Microsoft.Extensions.Logging;

namespace ObjectStorage;

internal sealed class UnbufferedFileStream : FileStream
{
    private readonly AsyncLock _flushLock;
    private readonly ILogger<Storage> _logger;
    private readonly MetricsCollector _metricsCollector;
    private readonly PlatformOptimizer _platformOptimizer;
    private readonly VerificationBuffer _verificationBuffer;

    private int _isFlushPending;

    public UnbufferedFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        int bufferSize,
        FileOptions options,
        ILogger<Storage> logger = null!) : base(
        path,
        mode,
        access == FileAccess.Write ? FileAccess.ReadWrite : access,
        share,
        bufferSize,
        options | FileOptions.WriteThrough)
    {
        EnsureValidBufferSize(bufferSize);

        var fileDescriptor = SafeFileHandle.DangerousGetHandle().ToInt32();
        _flushLock = new AsyncLock();
        _logger = logger;
        _verificationBuffer = new VerificationBuffer(bufferSize);
        _metricsCollector = new MetricsCollector(logger);
        _platformOptimizer = PlatformOptimizer.Create(fileDescriptor, SafeFileHandle, logger);

        InitializeStream();
    }

    private void InitializeStream()
    {
        try
        {
            _platformOptimizer.Initialize(Length);
            _logger?.LogInformation("Stream initialized successfully with platform-specific optimizations");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize stream with platform optimizations");
            throw;
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentValidation.ValidateWriteParameters(buffer, offset, count);

        var writePosition = Position;

        try
        {
            await base.WriteAsync(buffer, offset, count, cancellationToken);
            _metricsCollector.RecordBytesWritten(count);

            await VerifyWriteOperationAsync(writePosition, buffer, offset, count, cancellationToken);
        }
        catch (Exception ex)
        {
            await HandleWriteFailureAsync(ex, writePosition);
            throw;
        }
    }

    private async Task VerifyWriteOperationAsync(
        long position,
        byte[] originalData,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var currentPosition = Position;

        try
        {
            Position = position;
            await _verificationBuffer.VerifyDataAsync(this, position, originalData, offset, count, cancellationToken);
        }
        finally
        {
            Position = currentPosition;
        }
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isFlushPending, 1) == 1) return;

        using var lockToken = await _flushLock.AcquireAsync(cancellationToken);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Constants.FileStreamConstants.FlushTimeoutMs);

            await base.FlushAsync(cts.Token);
            await _platformOptimizer.FlushAsync(cts.Token);
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushPending, 0);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flushLock.Dispose();
            _metricsCollector.RecordFinalMetrics();
            _verificationBuffer.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void EnsureValidBufferSize(int bufferSize)
    {
        if (bufferSize % Constants.FileStreamConstants.SectorAlignment != 0)
            throw new ArgumentException(
                $"Buffer size must be aligned to {Constants.FileStreamConstants.SectorAlignment} bytes",
                nameof(bufferSize));
    }

    private Task HandleWriteFailureAsync(Exception exception, long position)
    {
        _logger?.LogError(exception, "Write operation failed at position {Position}", position);

        var filePath = Name;
        Close();

        try
        {
            File.Delete(filePath);
            _logger?.LogWarning("File deleted after write failure: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete file after write error: {FilePath}", filePath);
        }

        return Task.CompletedTask;
    }
}