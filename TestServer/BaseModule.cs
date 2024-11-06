public class BaseModule
{
    public ModuleStatus Status { get; set; }
    public int Progress { get; set; }
    public string Message { get; set; } = "";

    public bool SendDbUpdate { get; set; }
}
