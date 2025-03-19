using Com.Raf.Utility;
using Com.Raf.Xtl.Build;
using Server.DataObjects;

namespace Server.Builders;

public class SmartMatchBuilder : BaseModule
{
    private readonly ILogger<SmartMatchBuilder> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private CancellationTokenSource cancellationTokenSource;

    public SmartMatchBuilder(ILogger<SmartMatchBuilder> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        Settings.DirectoryName = "SmartMatch";
    }

    public async Task Start(string cycle, string dataYearMonth, CancellationTokenSource stoppingTokenSource, string expireDays)
    {
        // Avoids lag from client click to server, likely unnessasary.... 
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        logger.LogInformation("Starting Builder");
        Status = ModuleStatus.InProgress;
        Message = "Starting Builder";
        CurrentTask = dataYearMonth;

        Settings.Validate(config);

        Task builderTask = Task.CompletedTask;
        cancellationTokenSource = stoppingTokenSource;
        string dataSourcePath = Path.Combine(Settings.AddressDataPath, dataYearMonth);

        await Utils.StopService("MSSQLSERVER");
        await Utils.StartService("MSSQLSERVER");

        if (cycle == "N")
        {
            string sourceFolder = Path.Combine(dataSourcePath, "Cycle-N");
            string dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth, "Cycle-N");

            Progress = 1;

            CycleN2Sha256XtlBuilder smartMatchBuilder = new(dataYearMonth.Substring(2, 4) + "1", sourceFolder, dataOutputPath, Settings.AddressDataPath, "user", "password", expireDays, "14 15 16 19 20 21 22 23 24 25 26 27 28 29 30 31", "TestFile.Placeholder");
            smartMatchBuilder.UpdateStatus += UpdateStatus;
            builderTask = Task.Run(() =>
            {
                using (stoppingTokenSource.Token.Register(Thread.CurrentThread.Interrupt))
                {
                    smartMatchBuilder.Build(false, false);
                }
            });
        }
        else if (cycle == "O")
        {
            string sourceFolder = Path.Combine(dataSourcePath, "Cycle-O");
            string dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth, "Cycle-O");

            Progress = 1;

            CycleOSha256XtlBuilder smartMatchBuilder = new(dataYearMonth.Substring(2, 4) + "1", sourceFolder, dataOutputPath, Settings.AddressDataPath, "user", "password", expireDays, "14 15 16 19 20 21 22 23 24 25 26 27 28 29 30 31", "TestFile.Placeholder");
            smartMatchBuilder.UpdateStatus += UpdateStatus;
            builderTask = Task.Run(() =>
            {
                using (stoppingTokenSource.Token.Register(Thread.CurrentThread.Interrupt))
                {
                    smartMatchBuilder.Build(false, false);
                }
            });
        }
        else if (cycle == "OtoN")
        {
            string sourceFolder = Path.Combine(dataSourcePath, "Cycle-O");
            string dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth, "Cycle-N-Using-O");

            Progress = 1;

            CycleN2Sha256XtlBuilder smartMatchBuilder = new(dataYearMonth.Substring(2, 4) + "1", sourceFolder, dataOutputPath, Settings.AddressDataPath, "user", "password", expireDays, "14 15 16 19 20 21 22 23 24 25 26 27 28 29 30 31", "TestFile.Placeholder");
            smartMatchBuilder.UpdateStatus += UpdateStatus;
            builderTask = Task.Run(() =>
            {
                using (stoppingTokenSource.Token.Register(Thread.CurrentThread.Interrupt))
                {
                    smartMatchBuilder.Build(false, false);
                }
            });
        }
        else if (cycle == "MASSN")
        {
            string sourceFolder = Path.Combine(Settings.AddressDataPath, "MASS-N");
            string dataOutputPath = Path.Combine(Settings.OutputPath, "MASS-N");

            Progress = 1;

            CycleN2Sha256XtlBuilder smartMatchBuilder = new(dataYearMonth.Substring(2, 4) + "1", sourceFolder, dataOutputPath, Settings.AddressDataPath, "user", "password", expireDays, "14 15 16 19 20 21 22 23 24 25 26 27 28 29 30 31", "TestFile.Placeholder");
            smartMatchBuilder.UpdateStatus += UpdateStatus;
            builderTask = Task.Run(() =>
            {
                using (stoppingTokenSource.Token.Register(Thread.CurrentThread.Interrupt))
                {
                    smartMatchBuilder.Build(true, false);
                }
            });
        }
        else if (cycle == "MASSO")
        {
            string sourceFolder = Path.Combine(Settings.AddressDataPath, "MASS-O");
            string dataOutputPath = Path.Combine(Settings.OutputPath, "MASS-O");

            Progress = 1;

            CycleOSha256XtlBuilder smartMatchBuilder = new(dataYearMonth.Substring(2, 4) + "1", sourceFolder, dataOutputPath, Settings.AddressDataPath, "user", "password", expireDays, "14 15 16 19 20 21 22 23 24 25 26 27 28 29 30 31", "TestFile.Placeholder");
            smartMatchBuilder.UpdateStatus += UpdateStatus;
            builderTask = Task.Run(() =>
            {
                using (stoppingTokenSource.Token.Register(Thread.CurrentThread.Interrupt))
                {
                    smartMatchBuilder.Build(true, false);
                }
            });
        }

        while (!stoppingTokenSource.Token.IsCancellationRequested)
        {
            if (builderTask.Status == TaskStatus.RanToCompletion)
            {
                Message = "";
                Progress = 100;
                logger.LogInformation("XtlBuilder finished running");
                await CheckBuildComplete(dataYearMonth, cycle, stoppingTokenSource.Token);
                Status = ModuleStatus.Ready;
                CurrentTask = "";
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        // Set back to ready here instead of Error, otherwise no chance to set
        Status = ModuleStatus.Ready;
        CurrentTask = "";
    }

    private void UpdateStatus(string status, Logging.LogLevel logLevel)
    {
        logger.LogInformation(status);

        if (status.Contains("(was Stage ", StringComparison.CurrentCulture))
        {
            int stageNumberIndex = status.IndexOf("(was Stage ");
            int stageNumber = int.Parse(status.Substring(stageNumberIndex + 11, 1));
            Message = $"Stage {stageNumber + 1}";

            switch (stageNumber)
            {
                case 1:
                    Progress = 2;
                    break;
                case 2:
                    Progress = 24;
                    break;
                case 3:
                    Progress = 60;
                    break;
                case 4:
                    Progress = 61;
                    break;
                case 5:
                    Progress = 62;
                    break;
                case 6:
                    Progress = 64;
                    break;
                default:
                    break;
            }
        }

        if (logLevel == Logging.LogLevel.Error)
        {
            cancellationTokenSource.Cancel();
            Status = ModuleStatus.Error;
        }
    }

    private async Task CheckBuildComplete(string dataYearMonth, string cycle, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (cycle == "OtoN")
        {
            cycle = "N";
        }

        // Will be null if Crawler never made a record for it, watch out if running standalone
        UspsBundle bundle = context.UspsBundles.Where(x => dataYearMonth == x.DataYearMonth && $"Cycle-{cycle}" == x.Cycle).FirstOrDefault();
        bundle.IsBuildComplete = true;
        bundle.CompileTimestamp = DateTime.Now;

        await context.SaveChangesAsync(stoppingToken);
        SendDbUpdate = true;
    }
}
