namespace Server.DataObjects;

// Base class for all file types that represents a data file with metadata
public class BaseFile
{
    // Database primary key
    public int Id { get; set; }

    // File metadata
    public string FileName { get; set; }
    
    // File size (consider changing to long for byte count if appropriate)
    public string Size { get; set; }

    // Time period metadata
    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    
    // Formatted year-month string (typically used for display or filtering)
    public string DataYearMonth { get; set; }
    
    // Status flags
    public bool OnDisk { get; set; }
    
    // Timestamp when the file was downloaded
    public DateTime DateDownloaded { get; set; }
}
