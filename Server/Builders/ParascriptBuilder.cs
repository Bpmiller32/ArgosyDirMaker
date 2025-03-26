using System.Diagnostics;
using System.IO.Compression;
using Server.DataObjects;

namespace Server.Builders;

public class ParascriptBuilder : BaseModule
{
    // Progress constants for better readability and maintenance
    private static class ProgressSteps
    {
        public const int ExtractDownload = 0;
        public const int InitialCleanup = 1;
        public const int Extraction = 3;
        public const int Packaging = 21;
        public const int FinalCleanup = 98;
        public const int DatabaseUpdate = 99;
        public const int Complete = 100;
    }

    private readonly ILogger<ParascriptBuilder> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private string dataYearMonth;
    private string dataSourcePath;
    private string dataOutputPath;

    public ParascriptBuilder(ILogger<ParascriptBuilder> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        Settings.DirectoryName = "Parascript";
    }

    // Starts the Parascript build process for the specified data period
    // dataYearMonth: The year and month of the data to process in YYYYMM format
    // stoppingToken: Cancellation token to stop the operation
    public async Task Start(string dataYearMonth, CancellationToken stoppingToken)
    {
        // Only start if the module is in Ready state
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Parascript Builder");
            Status = ModuleStatus.InProgress;
            CurrentTask = dataYearMonth;

            Settings.Validate(config);
            this.dataYearMonth = dataYearMonth;
            dataSourcePath = Path.Combine(Settings.AddressDataPath, dataYearMonth);
            dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth);

            Message = "Extracting files from download";
            Progress = ProgressSteps.ExtractDownload;
            ExtractDownload(stoppingToken);

            Message = "Cleaning up from previous builds";
            Progress = ProgressSteps.InitialCleanup;
            CleanupDirectories(fullClean: true, stoppingToken);

            Message = "Compiling database";
            Progress = ProgressSteps.Extraction;
            await ExtractComponents(stoppingToken);

            Message = "Packaging database";
            Progress = ProgressSteps.Packaging;
            await ArchiveComponents(stoppingToken);

            Message = "Cleaning up post build";
            Progress = ProgressSteps.FinalCleanup;
            CleanupDirectories(fullClean: false, stoppingToken);

            Message = "Updating packaged directories";
            Progress = ProgressSteps.DatabaseUpdate;
            await UpdateBuildStatus(stoppingToken);

            Progress = ProgressSteps.Complete;
            Status = ModuleStatus.Ready;
            Message = "";
            CurrentTask = "";
            logger.LogInformation($"Build Complete: {dataYearMonth}");
        }
        catch (TaskCanceledException e)
        {
            Status = ModuleStatus.Ready;
            CurrentTask = "";
            logger.LogDebug($"Build cancelled: {e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"Build failed: {e.Message}");
        }
    }

    // Extracts the downloaded zip file to the source directory
    private void ExtractDownload(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        DirectoryInfo inputPath = new(dataSourcePath);
        foreach (DirectoryInfo dir in inputPath.GetDirectories())
        {
            dir.Attributes &= ~FileAttributes.ReadOnly;
            dir.Delete(true);
        }

        string zipFilePath = Path.Combine(dataSourcePath, "Files.zip");
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException($"Source file not found: {zipFilePath}");
        }

        ZipFile.ExtractToDirectory(zipFilePath, dataSourcePath);
    }

    // Cleans up directories before or after the build process
    // fullClean: If true, cleans both working and output directories; otherwise, only cleans working directory
    private void CleanupDirectories(bool fullClean, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Terminate any running Parascript processes
        Utils.KillPsProcs();

        // Ensure directories exist
        Directory.CreateDirectory(Settings.WorkingPath);
        Directory.CreateDirectory(dataOutputPath);

        // Clean working directory
        Utils.Cleanup(Settings.WorkingPath, stoppingToken);

        // Clean output directory if full clean requested
        if (fullClean)
        {
            Utils.Cleanup(dataOutputPath, stoppingToken);
        }
    }

    // Extracts component files in parallel
    private async Task ExtractComponents(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Format month and year for file naming
        string monthYear = $"{dataYearMonth.Substring(4, 2)}{dataYearMonth.Substring(2, 2)}";

        List<Task> buildTasks =
        [
            // Extract Zip4 component
            CreateExtractionTask("zip", Path.Combine(dataSourcePath, "ads6", $"ads_zip_09_{monthYear}.exe"), stoppingToken),
            
            // Extract LACS component
            CreateExtractionTask("lacs", Path.Combine(dataSourcePath, "DPVandLACS", "LACSLink", $"ads_lac_09_{monthYear}.exe"), stoppingToken),
            
            // Extract Suite component
            CreateExtractionTask("suite", Path.Combine(dataSourcePath, "DPVandLACS", "SuiteLink", $"ads_slk_09_{monthYear}.exe"), stoppingToken),
            
            // Extract DPV component and verify integrity
            Task.Run(() =>
            {
                string dpvPath = Path.Combine(Settings.WorkingPath, "dpv");
                string dpvSourceFile = Path.Combine(dataSourcePath, "DPVandLACS", "DPVfull", $"ads_dpv_09_{monthYear}.exe");

                if (!File.Exists(dpvSourceFile))
                {
                    throw new FileNotFoundException($"DPV source file not found: {dpvSourceFile}");
                }

                ZipFile.ExtractToDirectory(dpvSourceFile, dpvPath);
                File.Create(Path.Combine(dpvPath, "live.txt")).Close();

                // Verify database integrity
                string integrityToolPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "PDBIntegrity.exe");
                string logFilePath = Path.Combine(dpvPath, "fileinfo_log.txt");
                
                // Check if the integrity tool exists before trying to run it
                if (!Utils.VerifyRequiredExecutable(integrityToolPath, logger))
                {
                    throw new FileNotFoundException($"Required integrity tool not found: {integrityToolPath}");
                }

                Process proc = Utils.RunProc(integrityToolPath, logFilePath);

                using StreamReader sr = proc.StandardOutput;
                string procOutput = sr.ReadToEnd();

                if (!procOutput.Contains("Database files are consistent"))
                {
                    throw new Exception("DPV database files integrity check failed");
                }
            }, stoppingToken)
        ];

        await Task.WhenAll(buildTasks);
    }

    // Creates a task to extract a component to its working directory
    private Task CreateExtractionTask(string componentName, string sourceFilePath, CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            string componentPath = Path.Combine(Settings.WorkingPath, componentName);

            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException($"{componentName} source file not found: {sourceFilePath}");
            }

            ZipFile.ExtractToDirectory(sourceFilePath, componentPath);
            File.Create(Path.Combine(componentPath, "live.txt")).Close();
        }, stoppingToken);
    }

    // Archives the extracted components in parallel
    private async Task ArchiveComponents(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        List<Task> buildTasks =
        [
            // Archive Zip4 component
            CreateArchiveTask("zip", "Zip4", "Zip4.zip", stoppingToken),
            
            // Archive DPV component
            CreateArchiveTask("dpv", "DPV", "DPV.zip", stoppingToken),
            
            // Archive Suite component
            CreateArchiveTask("suite", "Suite", "SUITE.zip", stoppingToken),
            
            // Copy LACS files (special case - individual files)
            Task.Run(() =>
            {
                string lacsSourcePath = Path.Combine(Settings.WorkingPath, "lacs");
                string lacsOutputPath = Path.Combine(dataOutputPath, "LACS");

                Directory.CreateDirectory(lacsOutputPath);

                foreach (string file in Directory.GetFiles(lacsSourcePath))
                {
                    File.Copy(file, Path.Combine(lacsOutputPath, Path.GetFileName(file)), overwrite: true);
                }
            }, stoppingToken)
        ];

        await Task.WhenAll(buildTasks);
    }

    // Creates a task to archive a component to its output directory
    private Task CreateArchiveTask(string componentName, string outputDirName, string zipFileName, CancellationToken stoppingToken)
    {
        return Task.Run(() =>
        {
            string sourcePath = Path.Combine(Settings.WorkingPath, componentName);
            string outputDir = Path.Combine(dataOutputPath, outputDirName);
            string outputFile = Path.Combine(outputDir, zipFileName);

            Directory.CreateDirectory(outputDir);

            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }

            ZipFile.CreateFromDirectory(sourcePath, outputFile);
        }, stoppingToken);
    }

    // Updates the database to mark the build as complete
    private async Task UpdateBuildStatus(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Find the bundle record for this data period
        Bundle bundle = context.ParaBundles().Where(x => dataYearMonth == x.DataYearMonth).FirstOrDefault();

        // Only update if bundle exists (it may not if running standalone without a crawler)
        if (bundle != null)
        {
            bundle.IsBuildComplete = true;
            bundle.CompileTimestamp = DateTime.Now;

            await context.SaveChangesAsync(stoppingToken);
            SendDbUpdate = true;
        }
        else
        {
            logger.LogWarning($"No bundle record found for {dataYearMonth}. Database not updated.");
        }
    }
}
