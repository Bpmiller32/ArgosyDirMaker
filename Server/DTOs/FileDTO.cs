using System;
using Server.DataObjects;

namespace Server.DTOs;

public class FileDTO
{
    public int Id { get; set; }
    public string Provider { get; set; }
    public string FileName { get; set; }
    public string Size { get; set; }
    public string DataYearMonth { get; set; }
    public string Cycle { get; set; }  // For USPS
    public int? Day { get; set; }      // For RoyalMail
    
    // Conversion method
    public static FileDTO FromFile(DataFile file)
    {
        return new FileDTO
        {
            Id = file.Id,
            Provider = file.Provider,
            FileName = file.FileName,
            Size = file.Size,
            DataYearMonth = file.DataYearMonth,
            Cycle = file.Cycle,
            Day = file.DataDay
        };
    }
}
