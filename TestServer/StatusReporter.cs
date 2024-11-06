using System.Text.Json;

public class StatusReporter
{
    private readonly Dictionary<string, BaseModule> modules = new();

    private string testDataYearMonth = "202312|202401|202402";
    private string testFileCount = "4|4|4";
    private string testDownloadDate = "2/14/2024|2/15/2024|2/16/2024";
    private string testDownloadTime = "3:45 pm|4:45 pm|5:45 pm";

    public StatusReporter()
    {
        modules.Add("smartMatchCrawler", new BaseModule()
        {
            Status = ModuleStatus.Ready,
        });
        modules.Add("smartMatchBuilder", new BaseModule()
        {
            Status = ModuleStatus.Ready,
        });

        modules.Add("parascriptCrawler", new BaseModule()
        {
            Status = ModuleStatus.Ready,
        });
        modules.Add("parascriptBuilder", new BaseModule()
        {
            Status = ModuleStatus.Ready,
            Message = "Compiling directories test"
        });

        modules.Add("royalMailCrawler", new BaseModule()
        {
            Status = ModuleStatus.Ready,
        });
        modules.Add("royalMailBuilder", new BaseModule()
        {
            Status = ModuleStatus.Ready,
        });
    }

    public void ToggleStatus()
    {
        // SmartMatch
        if (modules["smartMatchCrawler"].Status == ModuleStatus.InProgress)
        {
            modules["smartMatchCrawler"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["smartMatchCrawler"].Status = ModuleStatus.InProgress;
        }
        if (modules["smartMatchBuilder"].Status == ModuleStatus.InProgress)
        {
            modules["smartMatchBuilder"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["smartMatchBuilder"].Status = ModuleStatus.InProgress;
        }

        // Parascript
        if (modules["parascriptCrawler"].Status == ModuleStatus.InProgress)
        {
            modules["parascriptCrawler"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["parascriptCrawler"].Status = ModuleStatus.InProgress;
        }
        if (modules["parascriptBuilder"].Status == ModuleStatus.InProgress)
        {
            modules["parascriptBuilder"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["parascriptBuilder"].Status = ModuleStatus.InProgress;
        }

        // RoyalMail
        if (modules["royalMailCrawler"].Status == ModuleStatus.InProgress)
        {
            modules["royalMailCrawler"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["royalMailCrawler"].Status = ModuleStatus.InProgress;
        }
        if (modules["royalMailBuilder"].Status == ModuleStatus.InProgress)
        {
            modules["royalMailBuilder"].Status = ModuleStatus.Ready;
        }
        else
        {
            modules["royalMailBuilder"].Status = ModuleStatus.InProgress;
        }
    }

    public void ToggleProgress()
    {
        modules["parascriptBuilder"].Progress += 10;
    }

    public void AddDirectory()
    {
        testDataYearMonth += "|202501";
        testFileCount += "|4";
        testDownloadDate += "|10/14/2029";
        testDownloadTime += "|69:69 pm";

    }

    public void ResetDirectory()
    {
        testDataYearMonth = "202312|202401|202402";
        testFileCount = "4|4|4";
        testDownloadDate = "2/14/2024|2/15/2024|2/16/2024";
        testDownloadTime = "3:45 pm|4:45 pm|5:45 pm";
    }

    public string UpdateReport()
    {
        // Construct JSON object to send to client
        var jsonObject = new
        {
            SmartMatch = new
            {
                Crawler = new
                {
                    modules["smartMatchCrawler"].Status,
                    modules["smartMatchCrawler"].Progress,
                    modules["smartMatchCrawler"].Message,
                    ReadyToBuild = new
                    {
                        DataYearMonth = testDataYearMonth,
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
                Builder = new
                {
                    modules["parascriptBuilder"].Status,
                    modules["parascriptBuilder"].Progress,
                    modules["parascriptBuilder"].Message,
                    BuildComplete = new
                    {
                        DataYearMonth = testDataYearMonth,
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
            },
            Parascript = new
            {
                Crawler = new
                {
                    modules["parascriptCrawler"].Status,
                    modules["parascriptCrawler"].Progress,
                    modules["parascriptCrawler"].Message,
                    ReadyToBuild = new
                    {
                        DataYearMonth = "",
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
                Builder = new
                {
                    modules["parascriptBuilder"].Status,
                    modules["parascriptBuilder"].Progress,
                    modules["parascriptBuilder"].Message,
                    BuildComplete = new
                    {
                        DataYearMonth = "202312|202401",
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
            },
            RoyalMail = new
            {
                Crawler = new
                {
                    modules["royalMailCrawler"].Status,
                    modules["royalMailCrawler"].Progress,
                    modules["royalMailCrawler"].Message,
                    ReadyToBuild = new
                    {
                        DataYearMonth = testDataYearMonth,
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
                Builder = new
                {
                    modules["royalMailBuilder"].Status,
                    modules["royalMailBuilder"].Progress,
                    modules["royalMailBuilder"].Message,
                    BuildComplete = new
                    {
                        DataYearMonth = testDataYearMonth,
                        FileCount = testFileCount,
                        DownloadDate = testDownloadDate,
                        DownloadTime = testDownloadTime,
                    }
                },
            },
        };

        return JsonSerializer.Serialize(jsonObject);
    }
}
