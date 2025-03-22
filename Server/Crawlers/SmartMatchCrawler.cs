using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;
using Server.DataObjects;

namespace Server.Crawlers;

// Crawler for SmartMatch files that downloads and manages files from the USPS portal
public class SmartMatchCrawler : BaseModule
{
    // DI
    private readonly ILogger<SmartMatchCrawler> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;
    private readonly WebBrowserService browserService;

    // Fields
    private readonly List<DataFile> tempFiles = [];

    public SmartMatchCrawler(ILogger<SmartMatchCrawler> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        // Initialize browser service
        browserService = new WebBrowserService(logger);

        Settings.DirectoryName = "SmartMatch";
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        // Avoids lag from client click to server, likely unnessasary.... 
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Crawler");
            Status = ModuleStatus.InProgress;

            // Validate settings from configuration
            Settings.Validate(config);

            // Step 1: Get file metadata from FTP server
            Message = "Searching for available new files";
            await GetFileMetadata(stoppingToken);

            // Step 2: Check if file exists in database
            Message = "Verifying files against database";
            await CheckFiles(stoppingToken);

            // Step 3: Download new files
            Message = "Downloading new files";
            await DownloadFiles(stoppingToken);

            // Step 4: Check if bundles are ready to build
            Message = "Checking if directories are ready to build";
            await CheckBuildReady(stoppingToken);

            // Cleanup and reset status
            Message = "";
            logger.LogInformation("Finished Crawling");
            Status = ModuleStatus.Ready;
        }
        catch (TaskCanceledException e)
        {
            Status = ModuleStatus.Ready;
            logger.LogDebug($"{e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"{e.Message}");
        }
    }

    // Gets file metadata from the SmartMatch portal
    private async Task GetFileMetadata(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Clear tempfiles in case of leftovers from last pass
        tempFiles.Clear();

        try
        {
            // Initialize browser and navigate to portal
            (Browser browser, Page page) = await browserService.InitializeBrowser(headless: true);

            using (browser)
            using (stoppingToken.Register(async () => await browser.CloseAsync()))
            using (page)
            {
                // Navigate to SmartMatch portal
                await browserService.NavigateToSmartMatchPortal(page);

                // Login to portal
                await browserService.LoginToSmartMatchPortal(page, Settings.UserName, Settings.Password, stoppingToken);

                // Extract file information from the page
                List<DataFile> fileInfoList = await browserService.ExtractSmartMatchFileInfo(page);

                // Add all files to the temp list
                tempFiles.AddRange(fileInfoList);

                logger.LogInformation($"Found {tempFiles.Count} files in SmartMatch portal");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error getting file metadata from SmartMatch portal: {ex.Message}");
            throw;
        }
    }

    // Checks files against the database and updates records accordingly
    private async Task CheckFiles(CancellationToken stoppingToken)
    {
        // Cancellation requested or GetFileMetadata failed
        if (stoppingToken.IsCancellationRequested || tempFiles.Count == 0)
        {
            return;
        }

        try
        {
            foreach (DataFile file in tempFiles)
            {
                // Check if file is unique against the db
                bool fileInDb = context.UspsFiles().Any(x => file.FileId == x.FileId);

                // File already in database
                if (fileInDb)
                {
                    continue;
                }

                // Add file to database
                context.Files.Add(file);

                // Check if file exists on the disk 
                if (!File.Exists(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle, file.FileName)))
                {
                    file.OnDisk = false;
                    logger.LogInformation($"Discovered and not on disk: {file.FileName} {file.DataMonth}/{file.DataYear} {file.Cycle}");
                }

                // Check if bundle exists for this month/year/cycle
                bool bundleExists = context.UspsBundles().Any(x => file.DataMonth == x.DataMonth && file.DataYear == x.DataYear && file.Cycle == x.Cycle);

                if (!bundleExists)
                {
                    // Create new bundle
                    Bundle newBundle = DatabaseExtensions.CreateUspsBundle(
                        file.DataMonth,
                        file.DataYear,
                        file.DataYearMonth,
                        file.Cycle);

                    newBundle.Files.Add(file);
                    context.Bundles.Add(newBundle);
                    logger.LogInformation($"Created new bundle for {file.DataMonth}/{file.DataYear} {file.Cycle}");
                }
                else
                {
                    // Add to existing bundle
                    Bundle existingBundle = context.UspsBundles().FirstOrDefault(x => file.DataMonth == x.DataMonth && file.DataYear == x.DataYear && file.Cycle == x.Cycle);

                    if (existingBundle != null)
                    {
                        existingBundle.Files.Add(file);
                        logger.LogInformation($"Added file to existing bundle: {file.DataMonth}/{file.DataYear} {file.Cycle}");
                    }
                }

                await context.SaveChangesAsync(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error checking files: {ex.Message}");
            throw;
        }
    }

    // Downloads files that are not on disk
    private async Task DownloadFiles(CancellationToken stoppingToken)
    {
        // Get files that need to be downloaded
        List<DataFile> offDisk = [.. context.UspsFiles().Where(x => !x.OnDisk)];

        // If all files are downloaded, no need to kick open new browser
        if (offDisk.Count == 0 || stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("No new files to download");
            return;
        }

        logger.LogInformation($"New files found for download: {offDisk.Count}");

        try
        {
            // Create directories for each file
            foreach (DataFile file in offDisk)
            {
                // Ensure there is a folder to land in
                Directory.CreateDirectory(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle));
                // Don't cleanup the folder, if some files appear on different days/times then they get overridden
                // Utils.Cleanup(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle), stoppingToken);
            }

            // Initialize browser for downloading
            (Browser browser, Page page) = await browserService.InitializeBrowser(headless: true, downloadPath: Path.Combine(Settings.AddressDataPath, offDisk[0].DataYearMonth, offDisk[0].Cycle));

            using (browser)
            using (stoppingToken.Register(async () => await browser.CloseAsync()))
            using (page)
            {
                // Navigate to SmartMatch portal
                await browserService.NavigateToSmartMatchPortal(page);

                // Login to portal
                await browserService.LoginToSmartMatchPortal(page, Settings.UserName, Settings.Password, stoppingToken);

                // Download each file individually, USPS website corrupts downloads if you do them all at once sometimes
                foreach (DataFile file in offDisk)
                {
                    string downloadPath = Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle);
                    string filePath = Path.Combine(downloadPath, file.FileName);

                    // Download the file
                    await browserService.DownloadSmartMatchFile(page, file.FileId, downloadPath, stoppingToken);

                    logger.LogInformation($"Currently downloading: {file.FileName} {file.DataMonth}/{file.DataYear} {file.Cycle}");

                    // Wait for download to complete
                    bool downloadSuccess = await browserService.WaitForFileDownload(filePath, stoppingToken: stoppingToken);

                    if (downloadSuccess)
                    {
                        // Update file record
                        file.OnDisk = true;
                        file.DateDownloaded = DateTime.Now;
                        context.Files.Update(file);
                        await context.SaveChangesAsync(stoppingToken);

                        // Handle special case for zip files
                        if (file.FileName.Contains("zip4natl") || file.FileName.Contains("zipmovenatl"))
                        {
                            // Since Zip data is the same for N and O, make sure in both folders
                            Directory.CreateDirectory(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-O"));
                            File.Copy(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-N", file.FileName), Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-O", file.FileName), true);
                        }
                    }
                    else
                    {
                        logger.LogWarning($"Download failed for file: {file.FileName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error downloading files: {ex.Message}");
            throw;
        }
    }

    // Checks if bundles are ready to build
    private async Task CheckBuildReady(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // Get all bundles with their associated files. Skip bundles that aren't ready or don't have enough files
            foreach (Bundle bundle in context.UspsBundles().Include(b => b.Files).ToList())
            {
                // Cycle-N requires at least 6 files
                if (bundle.Cycle == "Cycle-N" && (!bundle.Files.All(x => x.OnDisk) || bundle.Files.Count < 6))
                {
                    continue;
                }

                // Cycle-O requires at least 4 files
                if (bundle.Cycle == "Cycle-O" && (!bundle.Files.All(x => x.OnDisk) || bundle.Files.Count < 4))
                {
                    continue;
                }

                // Special check for Cycle-O, it needs zip files from Cycle-N
                if (bundle.Cycle == "Cycle-O")
                {
                    Bundle cycleNEquivalent = context.UspsBundles().Where(x => x.DataYearMonth == bundle.DataYearMonth && x.Cycle == "Cycle-N").Include(b => b.Files).FirstOrDefault();

                    if (cycleNEquivalent == null || !cycleNEquivalent.Files.Any(x => x.FileName == "zip4natl.tar") || !cycleNEquivalent.Files.Any(x => x.FileName == "zipmovenatl.tar"))
                    {
                        continue;
                    }
                }

                // Mark bundle as ready to build
                bundle.IsReadyForBuild = true;
                bundle.FileCount = bundle.Files.Count;

                if (!bundle.DownloadTimestamp.HasValue)
                {
                    bundle.DownloadTimestamp = DateTime.Now;
                }

                logger.LogInformation($"Bundle ready to build: {bundle.DataMonth}/{bundle.DataYear} {bundle.Cycle}");
            }

            await context.SaveChangesAsync(stoppingToken);
            SendDbUpdate = true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error checking build readiness: {ex.Message}");
            throw;
        }
    }
}
