namespace Server.ServerMessages;

public class SmartMatchBuilderMessage
{
    public string ModuleCommand { get; set; }
    public string DataYearMonth { get; set; }

    public string Cycle { get; set; }
    public string ExpireDays { get; set; }
}
