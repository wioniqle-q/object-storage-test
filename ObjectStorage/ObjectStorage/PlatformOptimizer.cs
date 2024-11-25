using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace ObjectStorage;

internal abstract class PlatformOptimizer(int fileDescriptor, SafeFileHandle safeFileHandle, ILogger<Storage> logger)
{
    protected readonly int FileDescriptor = fileDescriptor;
    protected readonly ILogger<Storage> Logger = logger;
    protected readonly SafeFileHandle SafeFileHandle = safeFileHandle;

    public static PlatformOptimizer Create(int fileDescriptor, SafeFileHandle safeFileHandle, ILogger<Storage> logger)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsOs(fileDescriptor, safeFileHandle, logger);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxOs(fileDescriptor, safeFileHandle, logger);

        throw new PlatformNotSupportedException("Unsupported operating system");
    }

    public abstract void Initialize(long fileLength);
    public abstract Task FlushAsync(CancellationToken cancellationToken);
}