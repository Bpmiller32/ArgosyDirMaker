namespace Server.DataObjects;

// Represents a USPS data file with metadata
public class UspsFile : BaseFile
{
    // Data pulled from website
    public bool PreviouslyDownloaded { get; set; }  // Flag indicating if file was previously downloaded
    public DateTime UploadDate { get; set; }        // Date when the file was uploaded to the USPS system
    public string ProductKey { get; set; }          // Product identifier in the USPS system
    public string FileId { get; set; }              // Unique file identifier in the USPS system

    // Data relevant to build process
    public string Cycle { get; set; }               // The processing cycle identifier (e.g., "Cycle-O", "Cycle-N2")
}
