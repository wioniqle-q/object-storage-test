namespace ObjectStorage;

public sealed class FileDecryptFileRequest
{
    public string FileId { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
}