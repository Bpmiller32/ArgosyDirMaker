namespace Server.DataObjects;

// Represents the operational status of a module
public enum ModuleStatus
{
    // Module is initialized and ready to perform operations
    Ready,
    
    // Module is currently performing an operation
    InProgress,
    
    // Module encountered an error during operation
    Error,
}
