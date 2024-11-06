namespace DataObjects;

public class BaseFile
{
    public int Id { get; set; }

    public string FileName { get; set; }
    public string Size { get; set; }

    public int DataMonth { get; set; }
    public int DataYear { get; set; }
    public string DataYearMonth { get; set; }
    public bool OnDisk { get; set; }
    public DateTime DateDownloaded { get; set; }
}
