namespace ObjectStorage;

public sealed class FileDecryptFileRequest
{
    internal string FileId { get; set; } = string.Empty;
    internal string SourcePath { get; set; } = string.Empty;
    internal string DestinationPath { get; set; } = string.Empty;
}