namespace Server.ServerMessages;

// Message for controlling tester module
public class TesterMessage : DataYearMonthMessage
{
    // Type of directory to test (SmartMatch, Parascript, RoyalMail)
    public string TestDirectoryType { get; set; }
}
