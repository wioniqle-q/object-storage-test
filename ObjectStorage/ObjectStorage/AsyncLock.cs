namespace ObjectStorage;

internal sealed class AsyncLock : IDisposable
{
    private readonly Task<IDisposable> _releaser;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public AsyncLock()
    {
        _releaser = Task.FromResult<IDisposable>(new Releaser(this));
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var wait = _semaphore.WaitAsync(cancellationToken);
        return wait.IsCompleted
            ? _releaser
            : wait.ContinueWith((_, state) => (IDisposable)state!,
                _releaser.Result,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)!;
    }

    private sealed class Releaser : IDisposable
    {
        private readonly AsyncLock _toRelease;

        internal Releaser(AsyncLock toRelease)
        {
            _toRelease = toRelease;
        }

        public void Dispose()
        {
            _toRelease._semaphore.Release();
        }
    }
}