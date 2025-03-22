using System.Text.Json;
using Server.Builders;
using Server.DataObjects;
using Server.Crawlers;
using Server.Tester;
using Server.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Server.ServerMessages;

// Manages status reporting for all system modules
public class StatusReporter
{
    private readonly DatabaseContext context;

    // Module instances
    private readonly SmartMatchCrawler smartMatchCrawler;
    private readonly SmartMatchBuilder smartMatchBuilder;
    private readonly ParascriptCrawler parascriptCrawler;
    private readonly ParascriptBuilder parascriptBuilder;
    private readonly RoyalMailCrawler royalMailCrawler;
    private readonly RoyalMailBuilder royalMailBuilder;
    private readonly DirTester dirTester;

    // Directory types supported by the system
    private static readonly string[] DirectoryTypes = { "SmartMatch", "Parascript", "RoyalMail" };

    // Module registry for efficient iteration
    private readonly Dictionary<string, BaseModule> modules = [];

    // Status data structure
    private readonly Dictionary<string, ModuleReporter> jsonObject = new()
    {
        { "SmartMatch", new() { Crawler = new(), Builder = new() } },
        { "Parascript", new() { Crawler = new(), Builder = new() } },
        { "RoyalMail", new() { Crawler = new(), Builder = new() } },
        { "Tester", new() { Tester = new() } }
    };

    // Initializes a new instance of the StatusReporter class
    public StatusReporter(
        DatabaseContext context,
        SmartMatchCrawler smartMatchCrawler,
        SmartMatchBuilder smartMatchBuilder,
        ParascriptCrawler parascriptCrawler,
        ParascriptBuilder parascriptBuilder,
        RoyalMailCrawler royalMailCrawler,
        RoyalMailBuilder royalMailBuilder,
        DirTester dirTester)
    {
        this.context = context;
        this.smartMatchCrawler = smartMatchCrawler;
        this.smartMatchBuilder = smartMatchBuilder;
        this.parascriptCrawler = parascriptCrawler;
        this.parascriptBuilder = parascriptBuilder;
        this.royalMailCrawler = royalMailCrawler;
        this.royalMailBuilder = royalMailBuilder;
        this.dirTester = dirTester;

        // Register all modules for efficient iteration
        RegisterModules();

        // Initial population of database values
        InitializeDbValues();
    }

    // Registers all modules in the modules dictionary for efficient iteration
    private void RegisterModules()
    {
        // SmartMatch modules
        modules.Add("smartMatchCrawler", smartMatchCrawler);
        modules.Add("smartMatchBuilder", smartMatchBuilder);

        // Parascript modules
        modules.Add("parascriptCrawler", parascriptCrawler);
        modules.Add("parascriptBuilder", parascriptBuilder);

        // RoyalMail modules
        modules.Add("royalMailCrawler", royalMailCrawler);
        modules.Add("royalMailBuilder", royalMailBuilder);

        // Tester module
        modules.Add("dirTester", dirTester);
    }

    // Initializes database values for all directory types
    private void InitializeDbValues()
    {
        // Using ConfigureAwait(false) to avoid deadlocks
        Task.Run(async () =>
        {
            foreach (var directoryType in DirectoryTypes)
            {
                await UpdateAndStringifyDbValuesAsync(directoryType).ConfigureAwait(false);
            }
        });
    }

    // Updates database values for a specific directory type
    private async Task UpdateAndStringifyDbValuesAsync(string directoryType)
    {
        try
        {
            // Reset crawler values
            ResetCrawlerValues(directoryType);

            // Update crawler values from database
            await UpdateCrawlerValuesFromDatabaseAsync(directoryType).ConfigureAwait(false);

            // Trim trailing pipe characters if data exists
            TrimTrailingPipes(directoryType);

            // Reset and update builder values
            await UpdateBuilderValuesAsync(directoryType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log the exception (in a real system, use a proper logging framework)
            Console.Error.WriteLine($"Error updating {directoryType} values: {ex.Message}");
        }
    }

    // Resets crawler values for a specific directory type
    private void ResetCrawlerValues(string directoryType)
    {
        var readyToBuild = jsonObject[directoryType].Crawler.ReadyToBuild;
        readyToBuild.DataYearMonth = "";
        readyToBuild.FileCount = "";
        readyToBuild.DownloadDate = "";
        readyToBuild.DownloadTime = "";
    }

    // Updates crawler values from the database for a specific directory type
    private async Task UpdateCrawlerValuesFromDatabaseAsync(string directoryType)
    {
        var readyToBuild = jsonObject[directoryType].Crawler.ReadyToBuild;

        switch (directoryType)
        {
            case "SmartMatch":
                await context.UspsBundles()
                    .Where(x => x.IsReadyForBuild && x.Cycle == "Cycle-O")
                    .ForEachAsync(bundle => AppendBundleData(readyToBuild, bundle))
                    .ConfigureAwait(false);
                break;

            case "Parascript":
                await context.ParaBundles()
                    .Where(x => x.IsReadyForBuild)
                    .ForEachAsync(bundle => AppendBundleData(readyToBuild, bundle))
                    .ConfigureAwait(false);
                break;

            case "RoyalMail":
                await context.RoyalBundles()
                    .Where(x => x.IsReadyForBuild)
                    .ForEachAsync(bundle => AppendBundleData(readyToBuild, bundle))
                    .ConfigureAwait(false);
                break;
        }
    }

    // Appends bundle data to the ready-to-build reporter
    private void AppendBundleData(ReadyToBuildReporter reporter, Bundle bundle)
    {
        // Convert to DTO for client communication
        var bundleDto = BundleDTO.FromBundle(bundle);
        
        reporter.DataYearMonth += $"{bundleDto.DataYearMonth}|";
        reporter.FileCount += $"{bundle.FileCount}|";
        
        // Format the DownloadTimestamp into date and time strings
        if (bundle.DownloadTimestamp.HasValue)
        {
            reporter.DownloadDate += $"{bundle.DownloadTimestamp.Value.ToShortDateString()}|";
            reporter.DownloadTime += $"{bundle.DownloadTimestamp.Value.ToShortTimeString()}|";
        }
        else
        {
            reporter.DownloadDate += "|";
            reporter.DownloadTime += "|";
        }
    }

    // Trims trailing pipe characters from crawler values if data exists
    private void TrimTrailingPipes(string directoryType)
    {
        var readyToBuild = jsonObject[directoryType].Crawler.ReadyToBuild;

        if (readyToBuild.DataYearMonth.Length > 0)
        {
            readyToBuild.DataYearMonth = TrimLastCharacter(readyToBuild.DataYearMonth);
            readyToBuild.FileCount = TrimLastCharacter(readyToBuild.FileCount);
            readyToBuild.DownloadDate = TrimLastCharacter(readyToBuild.DownloadDate);
            readyToBuild.DownloadTime = TrimLastCharacter(readyToBuild.DownloadTime);
        }
    }

    // Removes the last character from a string
    private string TrimLastCharacter(string value)
    {
        if (value.Length > 0)
        {
            return value.Remove(value.Length - 1);
        }
        else
        {
            return value;
        }
    }

    // Updates builder values for a specific directory type
    private async Task UpdateBuilderValuesAsync(string directoryType)
    {
        // Reset builder values
        jsonObject[directoryType].Builder.BuildComplete.DataYearMonth = "";

        // Update builder values from database
        switch (directoryType)
        {
            case "SmartMatch":
                await context.UspsBundles()
                    .Where(x => x.IsBuildComplete && x.Cycle == "Cycle-O")
                    .ForEachAsync(bundle =>
                    {
                        var bundleDto = BundleDTO.FromBundle(bundle);
                        jsonObject[directoryType].Builder.BuildComplete.DataYearMonth += $"{bundleDto.DataYearMonth}|";
                    })
                    .ConfigureAwait(false);
                break;

            case "Parascript":
                await context.ParaBundles()
                    .Where(x => x.IsBuildComplete)
                    .ForEachAsync(bundle =>
                    {
                        var bundleDto = BundleDTO.FromBundle(bundle);
                        jsonObject[directoryType].Builder.BuildComplete.DataYearMonth += $"{bundleDto.DataYearMonth}|";
                    })
                    .ConfigureAwait(false);
                break;

            case "RoyalMail":
                await context.RoyalBundles()
                    .Where(x => x.IsBuildComplete)
                    .ForEachAsync(bundle =>
                    {
                        var bundleDto = BundleDTO.FromBundle(bundle);
                        jsonObject[directoryType].Builder.BuildComplete.DataYearMonth += $"{bundleDto.DataYearMonth}|";
                    })
                    .ConfigureAwait(false);
                break;
        }

        // Trim trailing pipe character if data exists
        var dataYearMonth = jsonObject[directoryType].Builder.BuildComplete.DataYearMonth;
        if (dataYearMonth.Length > 0)
        {
            jsonObject[directoryType].Builder.BuildComplete.DataYearMonth = TrimLastCharacter(dataYearMonth);
        }
    }

    // Updates the status report with the latest module information
    public async Task<string> UpdateReport()
    {
        try
        {
            // Process database updates for modules that have requested it
            await ProcessDatabaseUpdatesAsync().ConfigureAwait(false);

            // Update module status information
            UpdateModuleStatusInformation();

            // Serialize the status report to JSON
            return JsonSerializer.Serialize(jsonObject);
        }
        catch (Exception ex)
        {
            // Log the exception and return an error message
            Console.Error.WriteLine($"Error updating report: {ex.Message}");
            return JsonSerializer.Serialize(new { Error = ex.Message });
        }
    }

    // Processes database updates for modules that have requested it
    private async Task ProcessDatabaseUpdatesAsync()
    {
        // Group modules by directory type to process updates efficiently
        var modulesByType = new Dictionary<string, List<KeyValuePair<string, BaseModule>>>();

        // Find modules that need database updates
        var modulesToUpdate = modules.Where(m => m.Value.SendDbUpdate).ToList();

        // Process updates by directory type
        foreach (var directoryType in DirectoryTypes)
        {
            var typeModules = modulesToUpdate.Where(m => m.Key.Contains(directoryType.ToLower(), StringComparison.OrdinalIgnoreCase)).ToList();

            if (typeModules.Count != 0)
            {
                // Update database values for this directory type
                await UpdateAndStringifyDbValuesAsync(directoryType).ConfigureAwait(false);

                // Reset the SendDbUpdate flag for processed modules
                foreach (var module in typeModules)
                {
                    module.Value.SendDbUpdate = false;
                }
            }
        }
    }

    // Updates module status information in the JSON object
    private void UpdateModuleStatusInformation()
    {
        // Update SmartMatch module status
        UpdateModuleStatus("SmartMatch", smartMatchCrawler, smartMatchBuilder);

        // Update Parascript module status
        UpdateModuleStatus("Parascript", parascriptCrawler, parascriptBuilder);

        // Update RoyalMail module status
        UpdateModuleStatus("RoyalMail", royalMailCrawler, royalMailBuilder);

        // Update Tester module status
        UpdateTesterStatus();
    }

    // Updates status information for a specific directory type
    private void UpdateModuleStatus(string directoryType, BaseModule crawler, BaseModule builder)
    {
        // Update crawler status
        jsonObject[directoryType].Crawler.Status = crawler.Status;
        jsonObject[directoryType].Crawler.Progress = crawler.Progress;
        jsonObject[directoryType].Crawler.Message = crawler.Message;

        // Update builder status
        jsonObject[directoryType].Builder.Status = builder.Status;
        jsonObject[directoryType].Builder.Progress = builder.Progress;
        jsonObject[directoryType].Builder.Message = builder.Message;
        jsonObject[directoryType].Builder.CurrentTask = builder.CurrentTask;
    }

    // Updates tester status information
    private void UpdateTesterStatus()
    {
        jsonObject["Tester"].Tester.Status = dirTester.Status;
        jsonObject["Tester"].Tester.Progress = dirTester.Progress;
        jsonObject["Tester"].Tester.Message = dirTester.Message;
        jsonObject["Tester"].Tester.CurrentTask = dirTester.CurrentTask;
    }
}
