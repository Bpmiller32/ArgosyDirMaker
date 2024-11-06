namespace DataObjects;

public class UspsFile : BaseFile
{
    // Data pulled from website
    public bool PreviouslyDownloaded { get; set; }
    public DateTime UploadDate { get; set; }

    public string ProductKey { get; set; }
    public string FileId { get; set; }

    // Data relevant to build process
    public string Cycle { get; set; }
}