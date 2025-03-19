namespace Server.ServerMessages;

// Message for controlling tester module
public class TesterMessage : IBuilderMessage
{
    // Command to be executed by the tester module (Start/Stop)
    public ModuleCommandType ModuleCommand { get; set; }

    // Year and month of the data to be processed (format: YYYYMM)
    public string DataYearMonth { get; set; }

    // Type of directory to test (SmartMatch, Parascript, RoyalMail)
    public string TestDirectoryType { get; set; }
}
