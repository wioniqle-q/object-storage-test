using System.Runtime.InteropServices;

namespace ObjectStorage;

internal static class LinuxNativeIo
{
    [DllImport("libc", SetLastError = true)]
    internal static extern int posix_fadvise(int fd, long offset, long len, int advice);

    [DllImport("libc", SetLastError = true)]
    internal static extern int fcntl(int fd, int cmd, int arg);

    [DllImport("libc", SetLastError = true)]
    internal static extern int fdatasync(int fd);

    [DllImport("libc", SetLastError = true)]
    internal static extern long syscall(long number, int fd, long offset, long size);
}