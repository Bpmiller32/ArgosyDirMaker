namespace DataObjects;

public class ParaBundle : BaseBundle
{
    public List<ParaFile> BuildFiles { get; set; } = new List<ParaFile>();
}
