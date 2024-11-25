using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ObjectStorage;

internal static class Kernel32
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetFileValidData(SafeFileHandle handle, long length);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool FlushFileBuffers(SafeFileHandle handle);
}