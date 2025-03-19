namespace Server.DataObjects;

// Represents a Royal Mail data file with metadata
public class RoyalFile : BaseFile
{
    // The day component of the data date (in addition to month/year from base class)
    public int DataDay { get; set; }
}
