using System.Text.Json;
using Server.Builders;
using Server.DataObjects;
using Server.Crawlers;
using Server.Tester;
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

    // Status data structure using our new simplified model
    private readonly SystemStatus systemStatus = new();

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

        // Initialize the system status structure
        InitializeSystemStatus();

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

    // Initialize the system status structure
    private void InitializeSystemStatus()
    {
        // Initialize module status reports
        foreach (var directoryType in DirectoryTypes)
        {
            // Initialize crawler module status
            systemStatus.Modules[$"{directoryType}Crawler"] = new ModuleStatusReport();
            
            // Initialize builder module status
            systemStatus.Modules[$"{directoryType}Builder"] = new ModuleStatusReport();
            
            // Initialize ready to build data
            systemStatus.ReadyToBuild[directoryType] = new ReadyToBuildData();
            
            // Initialize completed builds
            systemStatus.CompletedBuilds[directoryType] = new List<string>();
        }

        // Initialize tester module status
        systemStatus.Modules["Tester"] = new ModuleStatusReport();
    }

    // Initializes database values for all directory types
    private void InitializeDbValues()
    {
        // Using ConfigureAwait(false) to avoid deadlocks
        Task.Run(async () =>
        {
            foreach (var directoryType in DirectoryTypes)
            {
                await UpdateDatabaseValuesAsync(directoryType).ConfigureAwait(false);
            }
        });
    }

    // Updates database values for a specific directory type
    private async Task UpdateDatabaseValuesAsync(string directoryType)
    {
        try
        {
            // Clear existing data
            systemStatus.ReadyToBuild[directoryType].Bundles.Clear();
            systemStatus.CompletedBuilds[directoryType].Clear();

            // Update ready-to-build bundles
            await UpdateReadyToBuildBundlesAsync(directoryType).ConfigureAwait(false);

            // Update completed builds
            await UpdateCompletedBuildsAsync(directoryType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log the exception (in a real system, use a proper logging framework)
            Console.Error.WriteLine($"Error updating {directoryType} values: {ex.Message}");
        }
    }

    // Updates ready-to-build bundles from the database for a specific directory type
    private async Task UpdateReadyToBuildBundlesAsync(string directoryType)
    {
        switch (directoryType)
        {
            case "SmartMatch":
                await context.UspsBundles()
                    .Where(x => x.IsReadyForBuild && x.Cycle == "Cycle-O")
                    .ForEachAsync(bundle => AddBundleToReadyToBuild(directoryType, bundle))
                    .ConfigureAwait(false);
                break;

            case "Parascript":
                await context.ParaBundles()
                    .Where(x => x.IsReadyForBuild)
                    .ForEachAsync(bundle => AddBundleToReadyToBuild(directoryType, bundle))
                    .ConfigureAwait(false);
                break;

            case "RoyalMail":
                await context.RoyalBundles()
                    .Where(x => x.IsReadyForBuild)
                    .ForEachAsync(bundle => AddBundleToReadyToBuild(directoryType, bundle))
                    .ConfigureAwait(false);
                break;
        }
    }

    // Adds a bundle to the ready-to-build list
    private void AddBundleToReadyToBuild(string directoryType, Bundle bundle)
    {
        BundleDTO bundleDto = BundleDTO.FromBundle(bundle);
        
        systemStatus.ReadyToBuild[directoryType].Bundles.Add(new BundleInfo
        {
            DataYearMonth = bundleDto.DataYearMonth,
            FileCount = bundle.FileCount,
            DownloadTimestamp = bundle.DownloadTimestamp
        });
    }

    // Updates completed builds from the database for a specific directory type
    private async Task UpdateCompletedBuildsAsync(string directoryType)
    {
        switch (directoryType)
        {
            case "SmartMatch":
                await context.UspsBundles()
                    .Where(x => x.IsBuildComplete && x.Cycle == "Cycle-O")
                    .ForEachAsync(bundle =>
                    {
                        BundleDTO bundleDto = BundleDTO.FromBundle(bundle);
                        systemStatus.CompletedBuilds[directoryType].Add(bundleDto.DataYearMonth);
                    })
                    .ConfigureAwait(false);
                break;

            case "Parascript":
                await context.ParaBundles()
                    .Where(x => x.IsBuildComplete)
                    .ForEachAsync(bundle =>
                    {
                        BundleDTO bundleDto = BundleDTO.FromBundle(bundle);
                        systemStatus.CompletedBuilds[directoryType].Add(bundleDto.DataYearMonth);
                    })
                    .ConfigureAwait(false);
                break;

            case "RoyalMail":
                await context.RoyalBundles()
                    .Where(x => x.IsBuildComplete)
                    .ForEachAsync(bundle =>
                    {
                        BundleDTO bundleDto = BundleDTO.FromBundle(bundle);
                        systemStatus.CompletedBuilds[directoryType].Add(bundleDto.DataYearMonth);
                    })
                    .ConfigureAwait(false);
                break;
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
            return JsonSerializer.Serialize(systemStatus);
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
        // Find modules that need database updates
        List<KeyValuePair<string, BaseModule>> modulesToUpdate = modules.Where(m => m.Value.SendDbUpdate).ToList();

        // Process updates by directory type
        foreach (var directoryType in DirectoryTypes)
        {
            List<KeyValuePair<string, BaseModule>> typeModules = modulesToUpdate.Where(m => m.Key.Contains(directoryType.ToLower(), StringComparison.OrdinalIgnoreCase)).ToList();

            if (typeModules.Count != 0)
            {
                // Update database values for this directory type
                await UpdateDatabaseValuesAsync(directoryType).ConfigureAwait(false);

                // Reset the SendDbUpdate flag for processed modules
                foreach (var module in typeModules)
                {
                    module.Value.SendDbUpdate = false;
                }
            }
        }
    }

    // Updates module status information
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
        ModuleStatusReport crawlerStatus = systemStatus.Modules[$"{directoryType}Crawler"];
        crawlerStatus.Status = crawler.Status;
        crawlerStatus.Progress = crawler.Progress;
        crawlerStatus.Message = crawler.Message;
        crawlerStatus.CurrentTask = crawler.CurrentTask;

        // Update builder status
        ModuleStatusReport builderStatus = systemStatus.Modules[$"{directoryType}Builder"];
        builderStatus.Status = builder.Status;
        builderStatus.Progress = builder.Progress;
        builderStatus.Message = builder.Message;
        builderStatus.CurrentTask = builder.CurrentTask;
    }

    // Updates tester status information
    private void UpdateTesterStatus()
    {
        ModuleStatusReport testerStatus = systemStatus.Modules["Tester"];
        testerStatus.Status = dirTester.Status;
        testerStatus.Progress = dirTester.Progress;
        testerStatus.Message = dirTester.Message;
        testerStatus.CurrentTask = dirTester.CurrentTask;
    }
}
