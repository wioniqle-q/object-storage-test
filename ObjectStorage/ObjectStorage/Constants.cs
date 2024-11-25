namespace ObjectStorage;

internal static class Constants
{
    internal static class StorageConstants
    {
        internal const int BufferSize = 1024 * 1024 * 4;
        internal const int PauseWriterThreshold = BufferSize * 4;
        internal const int ResumeWriterThreshold = BufferSize * 2;
        internal const int IvSize = 16;
    }

    internal static class FileStreamConstants
    {
        internal const int ReadAheadKb = 1024 * 32;
        internal const int SectorAlignment = 512;
        internal const int FlushTimeoutMs = 30000;
    }

    internal static class LinuxNativeIoConstants
    {
        internal const int PosixFadvSequential = 2;
        internal const int PosixFadvWillNeed = 3;
        internal const int FSetfl = 4;
        internal const int ODirect = 0x4000;
        internal const int OSync = 0x101000;
        internal const long SysReadahead = 187;
    }
}