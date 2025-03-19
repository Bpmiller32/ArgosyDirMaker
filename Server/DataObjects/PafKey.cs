namespace Server.DataObjects;

// Represents a Postcode Address File (PAF) key associated with a specific time period
public class PafKey
{
    // Database primary key
    public int Id { get; set; }

    // Time period metadata
    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    
    // The actual PAF key value
    public string Value { get; set; }
}
