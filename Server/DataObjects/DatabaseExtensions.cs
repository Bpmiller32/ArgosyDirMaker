using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Server.DataObjects;

// Extension methods to provide backward compatibility with the old database model
public static class DatabaseExtensions
{
    // USPS Bundle extensions
    public static IQueryable<Bundle> UspsBundles(this DatabaseContext context)
    {
        return context.Bundles.Where(b => b.Provider == "USPS");
    }
    
    // USPS File extensions
    public static IQueryable<DataFile> UspsFiles(this DatabaseContext context)
    {
        return context.Files.Where(f => f.Provider == "USPS");
    }
    
    // Royal Mail Bundle extensions
    public static IQueryable<Bundle> RoyalBundles(this DatabaseContext context)
    {
        return context.Bundles.Where(b => b.Provider == "RoyalMail");
    }
    
    // Royal Mail File extensions
    public static IQueryable<DataFile> RoyalFiles(this DatabaseContext context)
    {
        return context.Files.Where(f => f.Provider == "RoyalMail");
    }
    
    // Parascript Bundle extensions
    public static IQueryable<Bundle> ParaBundles(this DatabaseContext context)
    {
        return context.Bundles.Where(b => b.Provider == "Parascript");
    }
    
    // Parascript File extensions
    public static IQueryable<DataFile> ParaFiles(this DatabaseContext context)
    {
        return context.Files.Where(f => f.Provider == "Parascript");
    }
    
    // Helper methods for creating new bundles
    public static Bundle CreateUspsBundle(int month, int year, string yearMonth, string cycle)
    {
        return new Bundle
        {
            Provider = "USPS",
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            Cycle = cycle,
            IsReadyForBuild = false,
            IsBuildComplete = false
        };
    }
    
    public static Bundle CreateRoyalBundle(int month, int year, string yearMonth)
    {
        return new Bundle
        {
            Provider = "RoyalMail",
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            IsReadyForBuild = false,
            IsBuildComplete = false
        };
    }
    
    public static Bundle CreateParaBundle(int month, int year, string yearMonth)
    {
        return new Bundle
        {
            Provider = "Parascript",
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            IsReadyForBuild = false,
            IsBuildComplete = false
        };
    }
    
    // Helper methods for creating new files
    public static DataFile CreateUspsFile(string fileName, int month, int year, string yearMonth, string cycle)
    {
        return new DataFile
        {
            Provider = "USPS",
            FileName = fileName,
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            Cycle = cycle,
            OnDisk = false
        };
    }
    
    public static DataFile CreateRoyalFile(string fileName, int month, int year, string yearMonth, int? day)
    {
        return new DataFile
        {
            Provider = "RoyalMail",
            FileName = fileName,
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            DataDay = day,
            OnDisk = false
        };
    }
    
    public static DataFile CreateParaFile(string fileName, int month, int year, string yearMonth)
    {
        return new DataFile
        {
            Provider = "Parascript",
            FileName = fileName,
            DataMonth = month,
            DataYear = year,
            DataYearMonth = yearMonth,
            OnDisk = false
        };
    }
}
