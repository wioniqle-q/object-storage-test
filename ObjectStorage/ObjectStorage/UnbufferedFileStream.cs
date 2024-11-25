using System.Runtime.InteropServices;
using static ObjectStorage.LinuxNativeIo;

namespace ObjectStorage;

internal sealed class UnbufferedFileStream : FileStream
{
    private readonly int _fd;
    private readonly Lock _flushLock = new();
    private readonly bool _isLinux;
    private readonly bool _isWindows;
    private volatile int _isFlushInProgress;

    public UnbufferedFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize,
        FileOptions options)
        : base(path, mode, access, share, bufferSize, options | FileOptions.WriteThrough)
    {
        _fd = SafeFileHandle.DangerousGetHandle().ToInt32();
        _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (_isWindows)
            OptimizeForWindows();
        else if (_isLinux)
            OptimizeForLinux();
        else
            throw new PlatformNotSupportedException("This platform is not supported.");
    }

    private void OptimizeForWindows()
    {
        try
        {
            Kernel32.SetFileValidData(SafeFileHandle, Length);
            Kernel32.FlushFileBuffers(SafeFileHandle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Windows optimization failed: {ex.Message}");
        }
    }

    private void OptimizeForLinux()
    {
        try
        {
            fcntl(_fd, Constants.LinuxNativeIoConstants.FSetfl, Constants.LinuxNativeIoConstants.ODirect | Constants.LinuxNativeIoConstants.OSync);
            posix_fadvise(_fd, 0, 0, Constants.LinuxNativeIoConstants.PosixFadvSequential);
            posix_fadvise(_fd, 0, 0, Constants.LinuxNativeIoConstants.PosixFadvWillNeed);

            syscall(Constants.LinuxNativeIoConstants.SysReadahead, _fd, 0, Constants.FileStreamConstants.ReadAheadKb);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Linux optimization failed: {ex.Message}");
        }
    }

    public override void Flush(bool flushToDisk)
    {
        if (flushToDisk is not true || Interlocked.Exchange(ref _isFlushInProgress, 1) is 1) return;

        lock (_flushLock)
        {
            try
            {
                base.Flush(true);

                if (_isLinux)
                {
                    if (fdatasync(_fd) != 0)
                        throw new IOException("fdatasync failed.");
                }
                else if (_isWindows)
                {
                    if (Kernel32.FlushFileBuffers(SafeFileHandle) is not true)
                        throw new IOException("FlushFileBuffers failed.");
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isFlushInProgress, 0);
            }
        }
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isFlushInProgress, 1) == 0)
            try
            {
                await Task.Run(async () =>
                {
                    lock (_flushLock)
                    {
                        try
                        {
                            if (_isLinux)
                            {
                                if (fdatasync(_fd) != 0)
                                    throw new IOException("fdatasync failed.");
                            }
                            else if (_isWindows)
                            {
                                if (!Kernel32.FlushFileBuffers(SafeFileHandle))
                                    throw new IOException("FlushFileBuffers failed.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"FlushAsync error: {ex.Message}");
                            throw;
                        }
                    }
                }, cancellationToken);

                await base.FlushAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Exchange(ref _isFlushInProgress, 0);
            }
    }
}