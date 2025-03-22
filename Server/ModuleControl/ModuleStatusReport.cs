using Server.DataObjects;

namespace Server.ServerMessages;

// Module status report
public class ModuleStatusReport
{
    // Current operational status of the module
    public ModuleStatus Status { get; set; }
    
    // Progress percentage of the current operation (0-100)
    public int Progress { get; set; }
    
    // Status message describing current operation
    public string Message { get; set; } = "";
    
    // Description of the current task being performed
    public string CurrentTask { get; set; } = "";
}

// Ready to build data
public class ReadyToBuildData
{
    // List of bundles ready to be built
    public List<BundleInfo> Bundles { get; set; } = new List<BundleInfo>();
}

// Bundle information
public class BundleInfo
{
    // Year and month of the data (format: YYYYMM)
    public string DataYearMonth { get; set; }
    
    // Number of files in the bundle
    public int FileCount { get; set; }
    
    // When the bundle was downloaded
    public DateTime? DownloadTimestamp { get; set; }
}

// Complete system status
public class SystemStatus
{
    // Status of all modules in the system
    public Dictionary<string, ModuleStatusReport> Modules { get; set; } = new Dictionary<string, ModuleStatusReport>();
    
    // Data ready to be built by directory type
    public Dictionary<string, ReadyToBuildData> ReadyToBuild { get; set; } = new Dictionary<string, ReadyToBuildData>();
    
    // Completed builds by directory type
    public Dictionary<string, List<string>> CompletedBuilds { get; set; } = new Dictionary<string, List<string>>();
}
