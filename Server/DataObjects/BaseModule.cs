namespace Server.DataObjects;

// Base class for all module types that provides common functionality for status tracking and configuration
public class BaseModule
{
    // Current operational status of the module
    public ModuleStatus Status { get; set; }

    // Progress percentage of the current operation (0-100)
    public int Progress { get; set; }

    // Status message describing current operation
    public string Message { get; set; } = "";

    // Description of the current task being performed
    public string CurrentTask { get; set; } = "";

    // Flag indicating whether the module needs to update the database
    public bool SendDbUpdate { get; set; }

    // Module configuration settings
    protected ModuleSettings Settings { get; set; } = new();
}
