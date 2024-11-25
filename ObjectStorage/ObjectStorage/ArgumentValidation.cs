namespace ObjectStorage;

internal static class ArgumentValidation
{
    public static void ValidateWriteParameters(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null");

        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative");

        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");

        if (offset + count > buffer.Length)
            throw new ArgumentException("The sum of offset and count is greater than the buffer length");
    }
}