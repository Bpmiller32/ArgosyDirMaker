namespace DataObjects;

public class BaseModule
{
    public ModuleStatus Status { get; set; }
    public int Progress { get; set; }
    public string Message { get; set; } = "";
    public string CurrentTask { get; set; } = "";

    public bool SendDbUpdate { get; set; }
    protected ModuleSettings Settings { get; set; } = new();
}
