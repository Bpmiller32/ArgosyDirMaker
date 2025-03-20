namespace Server.DataObjects;

// Base class for all bundle types that represents a collection of data files for a specific time period (month/year)
public class BaseBundle
{
    // Database primary key
    public int Id { get; set; }

    // Time period metadata
    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    public int FileCount { get; set; }

    // Formatted year-month string (typically used for display or filtering)
    public string DataYearMonth { get; set; }

    // Timestamps for tracking bundle processing
    public DateTime? DownloadTimestamp { get; set; }
    public DateTime? CompileTimestamp { get; set; }

    // Status flags for build process
    public bool IsReadyForBuild { get; set; }
    public bool IsBuildComplete { get; set; }
}
