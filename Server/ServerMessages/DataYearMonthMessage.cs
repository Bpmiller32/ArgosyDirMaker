namespace Server.ServerMessages;

// Base message class for modules that require data year/month information
public class DataYearMonthMessage : ModuleMessage
{
    // Year and month of the data to be processed (format: YYYYMM)
    public string DataYearMonth { get; set; }
}
