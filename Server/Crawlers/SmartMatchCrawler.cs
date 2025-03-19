using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;
using Server.DataObjects;

namespace Server.Crawlers;

public class SmartMatchCrawler : BaseModule
{
    private readonly ILogger<SmartMatchCrawler> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private readonly List<UspsFile> tempFiles = [];

    public SmartMatchCrawler(ILogger<SmartMatchCrawler> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

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

            Settings.Validate(config);

            Message = "Searching for available new files";
            await PullFiles(stoppingToken);

            Message = "Veifying files against database";
            await CheckFiles(stoppingToken);

            Message = "Downloading new files";
            await DownloadFiles(stoppingToken);

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

    private async Task PullFiles(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Clear tempfiles in case of leftovers from last pass
        tempFiles.Clear();

        // Download local chromium binary to launch browser
        BrowserFetcher fetcher = new();
        await fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        // Set launchoptions, create browser instance
        LaunchOptions options = new() { Headless = true };

        // Create a browser instance, page instance, register stoppingToken to browser event
        using Browser browser = (Browser)await Puppeteer.LaunchAsync(options);
        using (stoppingToken.Register(async () => await browser.CloseAsync()))
        using (Page page = (Page)await browser.NewPageAsync())
        {
            // Navigate to download portal page
            await page.GoToAsync("https://epf.usps.gov/");

            await page.WaitForSelectorAsync("#email");
            await page.FocusAsync("#email");
            await page.Keyboard.TypeAsync(Settings.UserName);
            await page.FocusAsync("#password");
            await page.Keyboard.TypeAsync(Settings.Password);

            await page.ClickAsync("#login");

            await page.WaitForSelectorAsync("#r1");
            await page.ClickAsync("#r1");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await page.WaitForSelectorAsync("#r2");
            await page.ClickAsync("#r2");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // Has a 30s timeout, should throw exception if tbody/filelist is not found
            await page.WaitForSelectorAsync("#tblFileList > tbody");

            // Arrrived at download portal page, pull page HTML
            HtmlAgilityPack.HtmlDocument doc = new();
            doc.LoadHtml(page.GetContentAsync().Result);

            // Format downloadables into list
            foreach (HtmlAgilityPack.HtmlNode fileRow in doc.DocumentNode.SelectNodes("/html/body/div[2]/table/tbody/tr/td/div[3]/table/tbody/tr/td/div/table/tbody/tr"))
            {
                UspsFile file = new()
                {
                    FileName = fileRow.ChildNodes[5].InnerText.Trim(),
                    UploadDate = DateTime.Parse(fileRow.ChildNodes[3].InnerText.Trim()),
                    Size = fileRow.ChildNodes[6].InnerText.Trim(),
                    OnDisk = true,

                    ProductKey = fileRow.Attributes[0].Value.Trim().Substring(19, 5),
                    FileId = fileRow.Attributes[1].Value.Trim().Substring(3, 7),

                    DataMonth = DateTime.Parse(fileRow.ChildNodes[3].InnerText.Trim()).Month,
                    DataYear = DateTime.Parse(fileRow.ChildNodes[3].InnerText.Trim()).Year
                };

                if (file.DataMonth < 10)
                {
                    file.DataYearMonth = file.DataYear.ToString() + "0" + file.DataMonth.ToString();
                }
                else
                {
                    file.DataYearMonth = file.DataYear.ToString() + file.DataMonth.ToString();
                }

                if (fileRow.ChildNodes[1].InnerText.Trim() == "Downloaded")
                {
                    file.PreviouslyDownloaded = true;
                }

                // New logic needed because epf behavior change, cannot open G00424KL files because of access change + Cycle O added
                string productDescription = fileRow.ChildNodes[2].InnerText.Trim();
                if (productDescription.Contains("Cycle O"))
                {
                    file.Cycle = "Cycle-O";
                }
                else
                {
                    file.Cycle = "Cycle-N";
                }

                if (file.FileName.Contains(".zip", StringComparison.OrdinalIgnoreCase) || file.FileName.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tempFiles.Add(file);
            }
        }
    }

    private async Task CheckFiles(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        foreach (UspsFile file in tempFiles)
        {
            // Check if file is unique against the db
            bool fileInDb = context.UspsFiles.Any(x => file.FileId == x.FileId);

            if (fileInDb)
            {
                continue;
            }

            // Regardless of file check is unique, add to db
            context.UspsFiles.Add(file);

            // Check if file exists on the disk 
            if (!File.Exists(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle, file.FileName)))
            {
                file.OnDisk = false;
            }
            logger.LogDebug($"Discovered and not on disk: {file.FileName} {file.DataMonth}/{file.DataYear} {file.Cycle}");

            bool bundleExists = context.UspsBundles.Any(x => (file.DataMonth == x.DataMonth) && (file.DataYear == x.DataYear) && (file.Cycle == x.Cycle));

            if (!bundleExists)
            {
                UspsBundle newBundle = new()
                {
                    DataMonth = file.DataMonth,
                    DataYear = file.DataYear,
                    DataYearMonth = file.DataYearMonth,
                    Cycle = file.Cycle,
                    IsReadyForBuild = false
                };

                newBundle.BuildFiles.Add(file);
                context.UspsBundles.Add(newBundle);
            }
            else
            {
                UspsBundle existingBundle = context.UspsBundles.FirstOrDefault(x => (file.DataMonth == x.DataMonth) && (file.DataYear == x.DataYear) && (file.Cycle == x.Cycle));

                existingBundle.BuildFiles.Add(file);
            }

            await context.SaveChangesAsync(stoppingToken);
        }
    }

    private async Task DownloadFiles(CancellationToken stoppingToken)
    {
        List<UspsFile> offDisk = [.. context.UspsFiles.Where(x => !x.OnDisk)];

        // If all files are downloaded, no need to kick open new browser
        if (offDisk.Count == 0 || stoppingToken.IsCancellationRequested)
        {
            return;
        }

        logger.LogInformation($"New files found for download: {offDisk.Count}");

        foreach (UspsFile file in offDisk)
        {
            // Ensure there is a folder to land in (this will punch through recursively btw, Downloads gets created as well if does not exist)
            Directory.CreateDirectory(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle));
            // Don't cleaup the folder, if some files appear on different days/times then they get overridden
            // Utils.Cleanup(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle), stoppingToken);
        }

        // Download local chromium binary to launch browser
        BrowserFetcher fetcher = new();
        await fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        // Set launchoptions, extras
        LaunchOptions options = new() { Headless = true };
        // PuppeteerExtra extra = new();
        // extra.Use(new StealthPlugin());

        // Create a browser instance, page instance
        using Browser browser = (Browser)await Puppeteer.LaunchAsync(options);
        using (stoppingToken.Register(async () => await browser.CloseAsync()))
        using (Page page = (Page)await browser.NewPageAsync())
        {
            // Navigate to download portal page
            await page.GoToAsync("https://epf.usps.gov/");

            await page.WaitForSelectorAsync("#email");
            await page.FocusAsync("#email");
            await page.Keyboard.TypeAsync(Settings.UserName);
            await page.FocusAsync("#password");
            await page.Keyboard.TypeAsync(Settings.Password);

            await page.ClickAsync("#login");

            await page.WaitForSelectorAsync("#r1");
            await page.ClickAsync("#r1");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await page.WaitForSelectorAsync("#r2");
            await page.ClickAsync("#r2");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await page.WaitForSelectorAsync("#tblFileList > tbody");

            // Changed behavior, start download and wait for indivdual file to download. USPS website corrupts downloads if you do them all at once sometimes
            foreach (UspsFile file in offDisk)
            {
                string path = Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle);
                await page.Client.SendAsync("Page.setDownloadBehavior", new { behavior = "allow", downloadPath = path });

                await page.EvaluateExpressionAsync($"document.querySelector('#td_{file.FileId}').childNodes[0].click()");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                logger.LogInformation($"Currently downloading: {file.FileName} {file.DataMonth}/{file.DataYear} {file.Cycle}");
                await WaitForDownload(file, stoppingToken);

                if (file.FileName.Contains("zip4natl") || file.FileName.Contains("zipmovenatl"))
                {
                    // Since Zip data is the same for N and O, make sure in both folders
                    Directory.CreateDirectory(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-O"));
                    File.Copy(Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-N", file.FileName), Path.Combine(Settings.AddressDataPath, file.DataYearMonth, "Cycle-O", file.FileName), true);
                }
            }
        }
    }

    private async Task CheckBuildReady(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        foreach (UspsBundle bundle in context.UspsBundles.Include("BuildFiles").ToList())
        {
            if (bundle.Cycle == "Cycle-N" && (!bundle.BuildFiles.All(x => x.OnDisk) || bundle.BuildFiles.Count < 6))
            {
                continue;
            }
            if (bundle.Cycle == "Cycle-O" && (!bundle.BuildFiles.All(x => x.OnDisk) || bundle.BuildFiles.Count < 4))
            {
                continue;
            }

            UspsBundle cycleNEquivalent = context.UspsBundles.Where(x => x.DataYearMonth == bundle.DataYearMonth && x.Cycle == "Cycle-N").Include("BuildFiles").FirstOrDefault();
            if (bundle.Cycle == "Cycle-O" && !cycleNEquivalent.BuildFiles.Any(x => x.FileName == "zip4natl.tar") && !cycleNEquivalent.BuildFiles.Any(x => x.FileName == "zipmovenatl.tar"))
            {
                continue;
            }


            bundle.IsReadyForBuild = true;
            bundle.FileCount = bundle.BuildFiles.Count;
            if (!bundle.DownloadTimestamp.HasValue)
            {
                bundle.DownloadTimestamp = DateTime.Now;
            }

            logger.LogInformation($"Bundle ready to build: {bundle.DataMonth}/{bundle.DataYear} {bundle.Cycle}");
            await context.SaveChangesAsync(stoppingToken);
        }

        SendDbUpdate = true;
    }

    private async Task WaitForDownload(UspsFile file, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Download in progress was stopped due to cancellation");
            return;
        }

        string path = Path.Combine(Settings.AddressDataPath, file.DataYearMonth, file.Cycle);
        if (!File.Exists(Path.Combine(path, file.FileName + ".CRDOWNLOAD")))
        {
            logger.LogDebug("Finished downloading");
            file.OnDisk = true;
            file.DateDownloaded = DateTime.Now;
            context.UspsFiles.Update(file);
            await context.SaveChangesAsync(stoppingToken);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        await WaitForDownload(file, stoppingToken);
    }
}
