﻿using DataObjects;

namespace Server.ServerMessages;

// Container for all module reporters in the system
public class ModuleReporter
{
    // Reporter for crawler module status
    public CrawlerReporter Crawler { get; set; }

    // Reporter for builder module status
    public BuilderReporter Builder { get; set; }

    // Reporter for tester module status
    public TesterReporter Tester { get; set; }
}

// Reports status information for crawler modules
public class CrawlerReporter
{
    // Current operational status of the crawler
    public ModuleStatus Status { get; set; }

    // Progress percentage of the current operation (0-100)
    public int Progress { get; set; }

    // Status message describing current operation
    public string Message { get; set; } = "";

    // Description of the current task being performed
    public string CurrentTask { get; set; } = "";

    // Information about data ready to be built
    public ReadyToBuildReporter ReadyToBuild { get; set; } = new();
}

// Contains information about data that is ready to be built
public class ReadyToBuildReporter
{
    // Year and month of the data (format: YYYYMM)
    public string DataYearMonth { get; set; } = "";

    // Number of files in the data bundle
    public string FileCount { get; set; } = "";

    // Date when the data was downloaded
    public string DownloadDate { get; set; } = "";

    // Time when the data was downloaded
    public string DownloadTime { get; set; } = "";
}

// Reports status information for builder modules
public class BuilderReporter
{
    // Current operational status of the builder
    public ModuleStatus Status { get; set; }

    // Progress percentage of the current operation (0-100)
    public int Progress { get; set; }

    // Status message describing current operation
    public string Message { get; set; } = "";

    // Description of the current task being performed
    public string CurrentTask { get; set; } = "";

    // Information about completed builds
    public BuildCompleteReporter BuildComplete { get; set; } = new();
}

// Contains information about completed builds
public class BuildCompleteReporter
{
    // Year and month of the completed build data (format: YYYYMM)
    public string DataYearMonth { get; set; } = "";
}

// Reports status information for tester modules
public class TesterReporter
{
    // Current operational status of the tester
    public ModuleStatus Status { get; set; }

    // Progress percentage of the current operation (0-100)
    public int Progress { get; set; }

    // Status message describing current operation
    public string Message { get; set; } = "";

    // Description of the current task being performed
    public string CurrentTask { get; set; } = "";
}
