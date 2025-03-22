using System;
using System.Collections.Generic;

namespace Server.DataObjects;

// Consolidated bundle class that replaces all provider-specific bundle classes
public class Bundle
{
    // Database primary key
    public int Id { get; set; }
    
    // Provider type
    public string Provider { get; set; }  // "USPS", "RoyalMail", or "Parascript"
    
    // Time period metadata
    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    public int FileCount { get; set; }
    public string DataYearMonth { get; set; }
    
    // Timestamps for tracking bundle processing
    public DateTime? DownloadTimestamp { get; set; }
    public DateTime? CompileTimestamp { get; set; }
    
    // Status flags for build process
    public bool IsReadyForBuild { get; set; }
    public bool IsBuildComplete { get; set; }
    
    // Provider-specific properties
    public string Cycle { get; set; }  // For USPS only
    
    // Navigation property
    public List<DataFile> Files { get; set; } = new List<DataFile>();
}
