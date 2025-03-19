namespace Server.DataObjects;

// Represents a bundle of USPS (United States Postal Service) data files for a specific time period
public class UspsBundle : BaseBundle
{
    // Collection of files included in this bundle
    public List<UspsFile> BuildFiles { get; set; } = new List<UspsFile>();
    
    // The processing cycle identifier (e.g., "Cycle-O", "Cycle-N2")
    public string Cycle { get; set; }
}
