using Microsoft.EntityFrameworkCore;
using Server.DataObjects;

namespace Server.Crawlers;

// Crawler for Royal Mail data files
public class RoyalMailCrawler : BaseModule
{
    // DI
    private readonly ILogger<RoyalMailCrawler> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;
    private readonly FtpService ftpService;

    // Fields
    private DataFile tempFile = new();
    private const string RoyalMailFtpUrl = "ftp://pafdownload.afd.co.uk/SetupRM.exe";

    public RoyalMailCrawler(ILogger<RoyalMailCrawler> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        ftpService = new FtpService(logger);

        Settings.DirectoryName = "RoyalMail";
    }

    public async Task Start(CancellationToken stoppingToken)
    {
        // Avoid processing if module is already running
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Royal Mail Crawler");
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
            logger.LogInformation("Finished Royal Mail Crawling");
            Status = ModuleStatus.Ready;
        }
        catch (TaskCanceledException e)
        {
            Status = ModuleStatus.Ready;
            logger.LogDebug($"Operation was canceled: {e.Message}");
        }
        catch (HttpRequestException e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"HTTP request error: {e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"Unexpected error in Royal Mail Crawler: {e.Message}");
            logger.LogDebug($"Stack trace: {e.StackTrace}");
        }
    }

    // Gets file metadata from the FTP server
    private async Task GetFileMetadata(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Clear tempfiles in case of leftovers from last pass
        tempFile = new();

        try
        {
            // Get the last modified date of the file
            DateTime lastModified = await ftpService.GetFileLastModifiedDate(RoyalMailFtpUrl, Settings.UserName, Settings.Password, stoppingToken);

            // Set file metadata
            tempFile.FileName = "SetupRM.exe";
            tempFile.DataMonth = lastModified.Month;
            tempFile.DataDay = lastModified.Day;
            tempFile.DataYear = lastModified.Year;

            // Format the year-month string (e.g., "202503" for March 2025)
            if (tempFile.DataMonth < 10)
            {
                tempFile.DataYearMonth = $"{tempFile.DataYear}0{tempFile.DataMonth}";
            }
            else
            {
                tempFile.DataYearMonth = $"{tempFile.DataYear}{tempFile.DataMonth}";
            }

            logger.LogInformation($"Found file with date: {lastModified:yyyy-MM-dd}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error getting file metadata: {ex.Message}");
            throw;
        }
    }

    // Checks if the file exists in the database and adds it if it doesn't
    private async Task CheckFiles(CancellationToken stoppingToken)
    {
        // Cancellation requested or GetFileMetadata failed
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // Check if file is unique against the database
            bool fileInDb = context.RoyalFiles().Any(x => tempFile.FileName == x.FileName && tempFile.DataMonth == x.DataMonth && tempFile.DataYear == x.DataYear);

            if (fileInDb)
            {
                logger.LogInformation($"File already exists in database: {tempFile.FileName} {tempFile.DataMonth}/{tempFile.DataYear}");
                return;
            }

            // Create a new DataFile with RoyalMail provider
            DataFile newFile = DatabaseExtensions.CreateRoyalFile(
                tempFile.FileName,
                tempFile.DataMonth,
                tempFile.DataYear,
                tempFile.DataYearMonth,
                tempFile.DataDay);

            // Add new file to database
            context.Files.Add(newFile);

            // Check if the file exists on disk
            string filePath = Path.Combine(Settings.AddressDataPath, tempFile.DataYearMonth, tempFile.FileName);
            newFile.OnDisk = File.Exists(filePath);

            if (!newFile.OnDisk)
            {
                logger.LogInformation($"Discovered new file not on disk: {newFile.FileName} {newFile.DataMonth}/{newFile.DataYear}");
            }

            // Check if a bundle exists for this month/year
            bool bundleExists = context.RoyalBundles().Any(x => tempFile.DataMonth == x.DataMonth && tempFile.DataYear == x.DataYear);

            if (!bundleExists)
            {
                // Create a new bundle
                Bundle newBundle = DatabaseExtensions.CreateRoyalBundle(
                    tempFile.DataMonth,
                    tempFile.DataYear,
                    tempFile.DataYearMonth);

                newBundle.Files.Add(newFile);
                context.Bundles.Add(newBundle);

                logger.LogInformation($"Created new bundle for {tempFile.DataMonth}/{tempFile.DataYear}");
            }
            else
            {
                // Add file to existing bundle
                Bundle existingBundle = context.RoyalBundles().Where(x => tempFile.DataMonth == x.DataMonth && tempFile.DataYear == x.DataYear).FirstOrDefault();

                if (existingBundle != null)
                {
                    existingBundle.Files.Add(newFile);
                    logger.LogInformation($"Added file to existing bundle for {tempFile.DataMonth}/{tempFile.DataYear}");
                }
            }

            // Save changes to database
            await context.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Error checking file in database: {ex.Message}");
            throw;
        }
    }

    // Downloads files that are not on disk
    private async Task DownloadFiles(CancellationToken stoppingToken)
    {
        // Get files that are not on disk
        List<DataFile> offDisk = context.RoyalFiles().Where(x => !x.OnDisk).ToList();

        // Cancellation requested or no files to download
        if (offDisk.Count == 0 || stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("No new files to download");
            return;
        }

        logger.LogInformation($"New files found for download: {offDisk.Count}");

        try
        {
            // For each file that needs to be downloaded
            foreach (var file in offDisk)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                logger.LogInformation($"Downloading: {file.FileName} {file.DataMonth}/{file.DataYear}");

                // Create directory and clean it up
                string directoryPath = Path.Combine(Settings.AddressDataPath, file.DataYearMonth);
                Directory.CreateDirectory(directoryPath);
                Utils.Cleanup(directoryPath, stoppingToken);

                // Download file using stream-based approach
                string destinationPath = Path.Combine(directoryPath, file.FileName);
                long fileSize = await ftpService.DownloadFile(RoyalMailFtpUrl, Settings.UserName, Settings.Password, destinationPath, stoppingToken);

                // Update file metadata
                file.Size = FtpService.FormatFileSize(fileSize);
                file.OnDisk = true;
                file.DateDownloaded = DateTime.Now;

                // Update database
                context.Files.Update(file);
                await context.SaveChangesAsync(stoppingToken);

                logger.LogInformation($"Successfully downloaded {file.FileName} ({file.Size})");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error downloading file: {ex.Message}");
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
            // Get all bundles with their files
            List<Bundle> bundles = context.RoyalBundles().Include(b => b.Files).ToList();

            foreach (var bundle in bundles)
            {
                // Skip bundles that are already marked as ready or have no files
                if (bundle.IsReadyForBuild || bundle.Files.Count < 1)
                {
                    continue;
                }

                // Check if all files in the bundle are on disk
                bool allFilesOnDisk = bundle.Files.All(x => x.OnDisk);

                if (allFilesOnDisk)
                {
                    // Mark bundle as ready to build
                    bundle.IsReadyForBuild = true;
                    bundle.FileCount = bundle.Files.Count;

                    // Set download timestamp if not already set
                    if (!bundle.DownloadTimestamp.HasValue)
                    {
                        bundle.DownloadTimestamp = DateTime.Now;
                    }

                    logger.LogInformation($"Bundle ready to build: {bundle.DataMonth}/{bundle.DataYear} with {bundle.FileCount} files");
                    await context.SaveChangesAsync(stoppingToken);
                }
            }

            // Signal that the database has been updated
            SendDbUpdate = true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error checking build readiness: {ex.Message}");
            throw;
        }
    }
}
