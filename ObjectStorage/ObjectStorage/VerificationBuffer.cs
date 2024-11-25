namespace ObjectStorage;

internal sealed class VerificationBuffer(int size) : IDisposable
{
    private readonly byte[] _buffer = new byte[size];
    private bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed) return;

        Array.Clear(_buffer, 0, _buffer.Length);
        _isDisposed = true;
    }

    public async Task VerifyDataAsync(
        FileStream stream,
        long position,
        byte[] originalData,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await stream.ReadExactlyAsync(_buffer, 0, count, cancellationToken);

        for (var i = 0; i < count; i++)
            if (originalData[offset + i] != _buffer[i])
                throw new IOException($"Data verification failed at position {position + i}");
    }

    private void ThrowIfDisposed()
    {
        if (!_isDisposed) return;

        throw new ObjectDisposedException(nameof(VerificationBuffer));
    }
}