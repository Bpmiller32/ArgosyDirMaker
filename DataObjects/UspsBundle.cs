namespace DataObjects;

public class UspsBundle : BaseBundle
{
    public List<UspsFile> BuildFiles { get; set; } = new List<UspsFile>();
    public string Cycle { get; set; }
}
