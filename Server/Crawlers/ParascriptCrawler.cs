using Microsoft.EntityFrameworkCore;
using Server.DataObjects;

namespace Server.Crawlers;

// Crawler for Parascript files that downloads and manages files from the Parascript portal
public class ParascriptCrawler : BaseModule
{
    // DI
    private readonly ILogger<ParascriptCrawler> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    // Constants
    private const string ADS_FILE_NAME = "ads6";
    private const string DPV_FILE_NAME = "DPVandLACS";

    private readonly List<ParaFile> tempFiles = [];
    private readonly WebBrowserService browserService;

    public ParascriptCrawler(ILogger<ParascriptCrawler> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        browserService = new WebBrowserService(logger);

        Settings.DirectoryName = "Parascript";
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        // Avoid starting if already in progress
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Parascript Crawler");
            Status = ModuleStatus.InProgress;

            // Validate settings from configuration
            Settings.Validate(config);

            // Step 1: Get file metadata from Parascript portal
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
            logger.LogInformation("Finished Parascript Crawling");
            Status = ModuleStatus.Ready;
        }
        catch (TaskCanceledException e)
        {
            Status = ModuleStatus.Ready;
            logger.LogDebug($"Task cancelled: {e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"Error in Parascript Crawler: {e.Message}");
        }
    }

    // Pulls file information from the Parascript portal
    private async Task GetFileMetadata(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Clear tempfiles in case of leftovers from last pass
        tempFiles.Clear();

        // Initialize browser and navigate to portal
        var (browser, page) = await browserService.InitializeBrowser(headless: true, downloadPath: Path.Combine(Settings.AddressDataPath, "Temp"));

        using (browser)
        using (stoppingToken.Register(async () => await browser.CloseAsync()))
        using (page)
        {
            try
            {
                // Navigate to download portal
                await browserService.NavigateToParascriptPortal(page);

                // Wait for download button to appear
                var downloadButton = await browserService.WaitForTextElement(page, "Download", maxAttempts: 10, stabilityDelay: 1500, stoppingToken);

                if (downloadButton == null)
                {
                    // Download button not found, using fallback delay
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                }

                // Wait for and click the ads button
                var adsButton = await browserService.WaitForTextElement(page, ADS_FILE_NAME, maxAttempts: 8, stabilityDelay: 1000, stoppingToken);

                if (adsButton != null)
                {
                    await adsButton.ClickAsync();
                }
                else
                {
                    // Fallback approach if button not found
                    logger.LogWarning("Ads button not found, using fallback JavaScript click");
                    await page.EvaluateExpressionAsync(@"
                        Array.from(document.querySelectorAll('div, span, button, a'))
                            .find(el => el.textContent.toLowerCase().includes('ads6'))?.click();
                    ");
                }

                // Wait for page state change
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                // Extract file information
                var (month, year) = await browserService.ExtractFileInfo(page);

                // Create file records
                string fullYear = string.Concat("20", year);
                string yearMonth = string.Concat(fullYear, month);

                // Create ADS file record
                ParaFile adsFile = new()
                {
                    FileName = ADS_FILE_NAME,
                    DataMonth = int.Parse(month),
                    DataYear = int.Parse(fullYear),
                    DataYearMonth = yearMonth,
                    OnDisk = true
                };
                tempFiles.Add(adsFile);

                // Create DPV file record
                ParaFile dpvFile = new()
                {
                    FileName = DPV_FILE_NAME,
                    DataMonth = int.Parse(month),
                    DataYear = int.Parse(fullYear),
                    DataYearMonth = yearMonth,
                    OnDisk = true
                };
                tempFiles.Add(dpvFile);

                logger.LogInformation($"Found files for {month}/{fullYear}");
            }
            catch (Exception ex)
            {
                logger.LogError("Error pulling files: {Message}", ex.Message);
                throw;
            }
        }
    }

    // Checks files against the database and updates records accordingly
    private async Task CheckFiles(CancellationToken stoppingToken)
    {
        // Cancellation requested or PullFile failed
        if (stoppingToken.IsCancellationRequested || tempFiles.Count == 0)
        {
            return;
        }

        foreach (ParaFile file in tempFiles)
        {
            try
            {
                // Check if file is unique against the db
                bool fileInDb = context.ParaFiles.Any(x => file.FileName == x.FileName && file.DataMonth == x.DataMonth && file.DataYear == x.DataYear);

                // File already in database
                if (fileInDb)
                {
                    continue;
                }

                // Add file to database
                context.ParaFiles.Add(file);

                // Check if the folder exists on the disk
                string filePath = Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.FileName);
                if (!Directory.Exists(filePath))
                {
                    file.OnDisk = false;
                    logger.LogInformation($"Discovered and not on disk: {file.FileName} {file.DataMonth}/{file.DataYear}");
                }

                // Check if bundle exists for this month/year
                bool bundleExists = context.ParaBundles.Any(x => file.DataMonth == x.DataMonth && file.DataYear == x.DataYear);

                if (!bundleExists)
                {
                    // Create new bundle
                    ParaBundle newBundle = new()
                    {
                        DataMonth = file.DataMonth,
                        DataYear = file.DataYear,
                        DataYearMonth = file.DataYearMonth,
                        IsReadyForBuild = false
                    };

                    newBundle.BuildFiles.Add(file);
                    context.ParaBundles.Add(newBundle);
                    logger.LogInformation($"Created new bundle for {file.DataMonth}/{file.DataYear}");
                }
                else
                {
                    // Add to existing bundle
                    ParaBundle existingBundle = context.ParaBundles.Where(x => file.DataMonth == x.DataMonth && file.DataYear == x.DataYear).FirstOrDefault();

                    if (existingBundle != null)
                    {
                        existingBundle.BuildFiles.Add(file);
                        logger.LogInformation($"Added file to existing bundle: {file.DataMonth}/{file.DataYear}");
                    }
                }

                await context.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError("Error checking file {FileName}: {Message}",
                    file.FileName, ex.Message);
            }
        }
    }

    /// Downloads files that are not on disk
    private async Task DownloadFiles(CancellationToken stoppingToken)
    {
        // Get files that need to be downloaded
        List<ParaFile> offDisk = context.ParaFiles.Where(x => !x.OnDisk).ToList();

        // All files are already downloaded or task was cancelled
        if (offDisk.Count == 0 || stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("No new files to download");
            return;
        }

        logger.LogInformation($"New files found for download: {offDisk.Count}");

        // Create directories and clean up existing files
        foreach (ParaFile file in offDisk)
        {
            string dirPath = Path.Combine(Settings.AddressDataPath, file.DataYearMonth);
            Directory.CreateDirectory(dirPath);
            Utils.Cleanup(dirPath, stoppingToken);
        }

        // Initialize browser for downloading
        string downloadPath = Path.Combine(Settings.AddressDataPath, offDisk[0].DataYearMonth);
        var (browser, page) = await browserService.InitializeBrowser(headless: true, downloadPath: downloadPath);

        using (browser)
        using (stoppingToken.Register(async () => await browser.CloseAsync()))
        using (page)
        {
            try
            {
                // Navigate to portal
                await browserService.NavigateToParascriptPortal(page);

                // Wait for download button
                var downloadButton = await browserService.WaitForTextElement(page, "Download", maxAttempts: 15, stabilityDelay: 1500, stoppingToken);

                if (downloadButton == null)
                {
                    logger.LogWarning("Download button not found, using fallback delay");
                    await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
                }

                // Select all files
                var selectAllCheckbox = await browserService.WaitForTextElement(page, "Select All", maxAttempts: 8, stabilityDelay: 1000, stoppingToken);

                if (selectAllCheckbox != null)
                {
                    await selectAllCheckbox.ClickAsync();
                    logger.LogDebug("Clicked Select All checkbox");
                }
                else
                {
                    // Fallback approach
                    logger.LogWarning("Select All checkbox not found, using fallback approach");
                    await page.EvaluateExpressionAsync(@"
                        document.querySelector('input[type=""checkbox""]')?.click();
                    ");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

                // Click download button
                downloadButton = await browserService.WaitForTextElement(page, "Download", maxAttempts: 5, stabilityDelay: 500, stoppingToken);

                if (downloadButton != null)
                {
                    await downloadButton.ClickAsync();
                    logger.LogInformation("Started downloading Parascript files");
                }
                else
                {
                    // Download button not found for clicking, throw error
                    throw new Exception("Could not find Download button, cannot start download");
                }

                // Important delay so the crdownload file can actually be made
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

                // Wait for all downloads to complete
                bool downloadSuccess = await browserService.WaitForDownloadsToComplete(downloadPath, stoppingToken);

                if (downloadSuccess)
                {
                    // Update file records
                    foreach (ParaFile file in offDisk)
                    {
                        file.OnDisk = true;
                        file.DateDownloaded = DateTime.Now;
                        context.ParaFiles.Update(file);
                    }

                    await context.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("All files downloaded successfully");
                }
                else
                {
                    logger.LogWarning("Download process was interrupted or timed out");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error downloading files: {ex.Message}");
                throw;
            }
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
            foreach (ParaBundle bundle in context.ParaBundles.Include("BuildFiles").ToList())
            {
                // Skip bundles that aren't ready or don't have enough files
                if (!bundle.BuildFiles.All(x => x.OnDisk) || bundle.BuildFiles.Count < 2)
                {
                    continue;
                }

                bundle.IsReadyForBuild = true;
                bundle.FileCount = bundle.BuildFiles.Count;

                if (!bundle.DownloadTimestamp.HasValue)
                {
                    bundle.DownloadTimestamp = DateTime.Now;
                }

                logger.LogInformation($"Bundle ready to build: {bundle.DataMonth}/{bundle.DataYear}");
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
