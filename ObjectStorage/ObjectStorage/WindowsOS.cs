using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace ObjectStorage;

internal sealed class WindowsOs(int fileDescriptor, SafeFileHandle safeFileHandle, ILogger<Storage> logger)
    : PlatformOptimizer(fileDescriptor, safeFileHandle, logger)
{
    public override void Initialize(long fileLength)
    {
        try
        {
            Kernel32.SetFileValidData(SafeFileHandle, fileLength);
            Kernel32.FlushFileBuffers(SafeFileHandle);
            Logger?.LogDebug("Windows file optimizations applied successfully");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to apply Windows file optimizations");
            throw;
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (!Kernel32.FlushFileBuffers(SafeFileHandle))
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"Windows FlushFileBuffers failed with error code: {error}");
            }

            Logger?.LogDebug("Windows flush completed successfully");
        }, cancellationToken);
    }
}