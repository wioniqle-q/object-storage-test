using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using static ObjectStorage.LinuxNativeIo;

namespace ObjectStorage;

internal sealed class LinuxOs(int fileDescriptor, SafeFileHandle safeFileHandle, ILogger<Storage> logger)
    : PlatformOptimizer(fileDescriptor, safeFileHandle, logger)
{
    public override void Initialize(long fileLength)
    {
        try
        {
            fcntl(FileDescriptor, Constants.LinuxNativeIoConstants.FSetfl,
                Constants.LinuxNativeIoConstants.ODirect | Constants.LinuxNativeIoConstants.OSync);

            posix_fadvise(FileDescriptor, 0, 0, Constants.LinuxNativeIoConstants.PosixFadvSequential);
            posix_fadvise(FileDescriptor, 0, 0, Constants.LinuxNativeIoConstants.PosixFadvWillNeed);

            syscall(Constants.LinuxNativeIoConstants.SysReadahead,
                FileDescriptor,
                0,
                Constants.FileStreamConstants.ReadAheadKb);

            Logger?.LogDebug("Linux file optimizations applied successfully");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to apply Linux file optimizations");
            throw;
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (fdatasync(FileDescriptor) != 0)
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"Linux fdatasync failed with error code: {error}");
            }

            Logger?.LogDebug("Linux flush completed successfully");
        }, cancellationToken);
    }
}