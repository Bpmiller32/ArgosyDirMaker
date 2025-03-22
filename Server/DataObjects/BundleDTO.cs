using System;
using Server.DataObjects;

namespace Server.DataObjects;

public class BundleDTO
{
    public int Id { get; set; }
    public string Provider { get; set; }
    public string DataYearMonth { get; set; }
    public int FileCount { get; set; }
    public bool IsReadyForBuild { get; set; }
    public bool IsBuildComplete { get; set; }
    public string Cycle { get; set; }  // For USPS
    
    // Conversion method
    public static BundleDTO FromBundle(Bundle bundle)
    {
        return new BundleDTO
        {
            Id = bundle.Id,
            Provider = bundle.Provider,
            DataYearMonth = bundle.DataYearMonth,
            FileCount = bundle.FileCount,
            IsReadyForBuild = bundle.IsReadyForBuild,
            IsBuildComplete = bundle.IsBuildComplete,
            Cycle = bundle.Cycle
        };
    }
}
