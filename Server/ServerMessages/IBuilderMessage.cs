namespace Server.ServerMessages;

// Interface for builder messages that require data year/month information
public interface IBuilderMessage : IModuleMessage
{
    // Year and month of the data to be processed (format: YYYYMM)
    string DataYearMonth { get; set; }
}
