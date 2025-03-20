namespace Server.DataObjects;

// Represents a bundle of Royal Mail data files for a specific time period
public class RoyalBundle : BaseBundle
{
    // Collection of files included in this bundle
    public List<RoyalFile> BuildFiles { get; set; } = [];
}
