namespace Server.DataObjects;

// Represents a bundle of Parascript data files for a specific time period
public class ParaBundle : BaseBundle
{
    // Collection of files included in this bundle
    public List<ParaFile> BuildFiles { get; set; } = new List<ParaFile>();
}
