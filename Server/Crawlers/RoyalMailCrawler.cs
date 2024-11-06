using System.Net;
using Microsoft.EntityFrameworkCore;
using DataObjects;

#pragma warning disable SYSLIB0014 // ignore that WebRequest and WebClient are deprecated in net6.0, replace with httpClient later

namespace Server.Crawlers;

public class RoyalMailCrawler : BaseModule
{
    private readonly ILogger<RoyalMailCrawler> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private readonly RoyalFile tempFile = new();

    public RoyalMailCrawler(ILogger<RoyalMailCrawler> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        Settings.DirectoryName = "RoyalMail";
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

            Settings.Validate(config);

            Message = "Searching for available new files";
            PullFile(stoppingToken);

            Message = "Veifying files against database";
            await CheckFile(stoppingToken);

            Message = "Downloading new files";
            await DownloadFile(stoppingToken);

            Message = "Checking if directories are ready to build";
            await CheckBuildReady(stoppingToken);

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

    public void PullFile(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        FtpWebRequest request = (FtpWebRequest)WebRequest.Create("ftp://pafdownload.afd.co.uk/SetupRM.exe");
        request.Credentials = new NetworkCredential(Settings.UserName, Settings.Password);
        request.Method = WebRequestMethods.Ftp.GetDateTimestamp;

        DateTime lastModified;

        using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
        {
            lastModified = response.LastModified;
        }

        tempFile.FileName = "SetupRM.exe";
        tempFile.DataMonth = lastModified.Month;
        tempFile.DataDay = lastModified.Day;
        tempFile.DataYear = lastModified.Year;

        if (tempFile.DataMonth < 10)
        {
            tempFile.DataYearMonth = tempFile.DataYear.ToString() + "0" + tempFile.DataMonth.ToString();
        }
        else
        {
            tempFile.DataYearMonth = tempFile.DataYear.ToString() + tempFile.DataMonth.ToString();
        }
    }

    public async Task CheckFile(CancellationToken stoppingToken)
    {
        // Cancellation requested or PullFile failed
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Check if file is unique against the db
        bool fileInDb = context.RoyalFiles.Any(x => (tempFile.FileName == x.FileName) && (tempFile.DataMonth == x.DataMonth) && (tempFile.DataYear == x.DataYear));

        if (fileInDb)
        {
            return;
        }

        // Regardless of file check is unique, add to db
        context.RoyalFiles.Add(tempFile);

        // Check if the folder exists on the disk
        if (!Directory.Exists(Path.Combine(Settings.AddressDataPath, tempFile.DataYearMonth, tempFile.FileName)))
        {
            tempFile.OnDisk = false;
        }
        logger.LogInformation($"Discovered and not on disk: {tempFile.FileName} {tempFile.DataMonth}/{tempFile.DataYear}");

        bool bundleExists = context.RoyalBundles.Any(x => (tempFile.DataMonth == x.DataMonth) && (tempFile.DataYear == x.DataYear));

        if (!bundleExists)
        {
            RoyalBundle newBundle = new()
            {
                DataMonth = tempFile.DataMonth,
                DataYear = tempFile.DataYear,
                DataYearMonth = tempFile.DataYearMonth,
                IsReadyForBuild = false
            };

            newBundle.BuildFiles.Add(tempFile);
            context.RoyalBundles.Add(newBundle);
        }
        else
        {
            RoyalBundle existingBundle = context.RoyalBundles.Where(x => (tempFile.DataMonth == x.DataMonth) && (tempFile.DataYear == x.DataYear)).FirstOrDefault();

            existingBundle.BuildFiles.Add(tempFile);
        }

        await context.SaveChangesAsync(stoppingToken);
    }

    public async Task DownloadFile(CancellationToken stoppingToken)
    {
        List<RoyalFile> offDisk = context.RoyalFiles.Where(x => !x.OnDisk).ToList();

        // Cancellation requested, CheckFile sees that nothing is offDisk, PullFile failed
        if (offDisk.Count == 0 || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        logger.LogInformation($"New files found for download: {offDisk.Count}");

        using WebClient request = new();
        request.Credentials = new NetworkCredential(Settings.UserName, Settings.Password);
        byte[] fileData;

        using (CancellationTokenRegistration registration = stoppingToken.Register(() => request.CancelAsync()))
        {
            logger.LogInformation($"Currently downloading: {tempFile.FileName} {tempFile.DataMonth}/{tempFile.DataYear}");
            // Throws error if request is canceled, caught in catch
            fileData = await request.DownloadDataTaskAsync("ftp://pafdownload.afd.co.uk/SetupRM.exe");
        }

        Directory.CreateDirectory(Path.Combine(Settings.AddressDataPath, tempFile.DataYearMonth));
        Utils.Cleanup(Path.Combine(Settings.AddressDataPath, tempFile.DataYearMonth), stoppingToken);

        using FileStream file = File.Create(Path.Combine(Settings.AddressDataPath, tempFile.DataYearMonth, "SetupRM.exe"));
        file.Write(fileData, 0, fileData.Length);
        file.Close();
        fileData = null;
        // TODO: assign TempFile.Size to fileData.Length / ? before assigning to null

        tempFile.OnDisk = true;
        tempFile.DateDownloaded = DateTime.Now;
        context.RoyalFiles.Update(tempFile);
        await context.SaveChangesAsync(stoppingToken);
    }

    public async Task CheckBuildReady(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        foreach (RoyalBundle bundle in context.RoyalBundles.Include("BuildFiles").ToList())
        {
            if (!bundle.BuildFiles.All(x => x.OnDisk) || bundle.BuildFiles.Count < 1)
            {
                continue;
            }

            bundle.IsReadyForBuild = true;
            bundle.FileCount = bundle.BuildFiles.Count;
            if (string.IsNullOrEmpty(bundle.DownloadDate))
            {
                bundle.DownloadDate = Utils.CalculateDbDate();
            }
            if (string.IsNullOrEmpty(bundle.DownloadTime))
            {
                bundle.DownloadTime = Utils.CalculateDbTime();
            }

            logger.LogInformation($"Bundle ready to build: {bundle.DataMonth}/{bundle.DataYear}");
            await context.SaveChangesAsync(stoppingToken);
        }

        SendDbUpdate = true;
    }
}
