using System;

namespace Server.DataObjects;

// Consolidated file class that replaces all provider-specific file classes
public class DataFile
{
    // Database primary key
    public int Id { get; set; }
    
    // Provider type
    public string Provider { get; set; }  // "USPS", "RoyalMail", or "Parascript"
    
    // File metadata
    public string FileName { get; set; }
    public string Size { get; set; }
    
    // Time period metadata
    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    public string DataYearMonth { get; set; }
    
    // Status flags
    public bool OnDisk { get; set; }
    public DateTime DateDownloaded { get; set; }
    
    // Provider-specific properties
    public bool PreviouslyDownloaded { get; set; }  // USPS only
    public DateTime? UploadDate { get; set; }       // USPS only
    public string ProductKey { get; set; }          // USPS only
    public string FileId { get; set; }              // USPS only
    public string Cycle { get; set; }               // USPS only
    public int? DataDay { get; set; }               // RoyalMail only
    
    // Foreign key
    public int BundleId { get; set; }
    
    // Navigation property
    public Bundle Bundle { get; set; }
}
