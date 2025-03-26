using System.Text.RegularExpressions;
using FlaUI.UIA2;
using FlaUI.Core.AutomationElements;
using Server.DataObjects;
using System.Xml;
using System.Diagnostics;

namespace Server.Builders;

public class RoyalMailBuilder : BaseModule
{
    // Progress constants for better readability and maintenance
    private static class ProgressSteps
    {
        public const int VerifyKey = 0;
        public const int ExtractPaf = 1;
        public const int InitialCleanup = 22;
        public const int UpdateSmi = 23;
        public const int ConvertData = 24;
        public const int Compilation = 47;
        public const int Packaging = 97;
        public const int FinalCleanup = 98;
        public const int DatabaseUpdate = 99;
        public const int Complete = 100;
    }

    private readonly ILogger<RoyalMailBuilder> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private string dataYearMonth;
    private string key;
    private string dataSourcePath;
    private string dataOutputPath;

    public RoyalMailBuilder(ILogger<RoyalMailBuilder> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        Settings.DirectoryName = "RoyalMail";
    }

    public async Task Start(string dataYearMonth, string key, CancellationToken stoppingToken)
    {
        // Only start if the module is in Ready state
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting RoyalMail Builder");
            Status = ModuleStatus.InProgress;
            CurrentTask = dataYearMonth;

            Settings.Validate(config);
            this.dataYearMonth = dataYearMonth;
            this.key = key;
            dataSourcePath = Path.Combine(Settings.AddressDataPath, dataYearMonth);
            dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth);

            Message = "Verifying PAF Key";
            Progress = ProgressSteps.VerifyKey;
            await VerifyAndStoreKey(stoppingToken);

            Message = "Extracting from PAF executable";
            Progress = ProgressSteps.ExtractPaf;
            await ExtractPafData(stoppingToken);

            Message = "Cleaning up from previous builds";
            Progress = ProgressSteps.InitialCleanup;
            CleanupDirectories(fullClean: true, stoppingToken);

            Message = "Updating SMi files & dongle list";
            Progress = ProgressSteps.UpdateSmi;
            UpdateSmiFiles(stoppingToken);

            Message = "Converting PAF data";
            Progress = ProgressSteps.ConvertData;
            ConvertPafData(stoppingToken);

            Message = "Compiling database";
            Progress = ProgressSteps.Compilation;
            await CompileDatabase(stoppingToken);

            Message = "Packaging database";
            Progress = ProgressSteps.Packaging;
            await PackageOutput(stoppingToken);

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
        catch (IOException e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"File system error: {e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"Build failed: {e.Message}");
        }
    }

    // Verifies and stores the PAF key in the database if it's unique
    private async Task VerifyAndStoreKey(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            logger.LogWarning("No PAF key provided");
            return;
        }

        PafKey filteredKey = new()
        {
            DataYear = int.Parse(dataYearMonth[..4]),
            DataMonth = int.Parse(dataYearMonth.Substring(4, 2)),
            Value = key,
        };

        bool keyInDb = context.PafKeys.Any(x => filteredKey.Value == x.Value);

        if (!keyInDb)
        {
            logger.LogInformation("Unique PafKey added: {DataMonth}/{DataYear}", filteredKey.DataMonth, filteredKey.DataYear);
            context.PafKeys.Add(filteredKey);
            await context.SaveChangesAsync(stoppingToken);
        }
        else
        {
            logger.LogInformation("Using existing PAF key from database");
        }
    }

    // Extracts data from the PAF executable using the provided key
    private async Task ExtractPafData(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        string setupRmPath = Path.Combine(dataSourcePath, "SetupRM.exe");
        if (!File.Exists(setupRmPath))
        {
            throw new FileNotFoundException($"SetupRM.exe not found at: {setupRmPath}");
        }

        using UIA2Automation automation = new();
        FlaUI.Core.Application app = FlaUI.Core.Application.Launch(setupRmPath);

        // Wait for the application to initialize
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        Window[] windows = app.GetAllTopLevelWindows(automation);

        if (windows.Length == 0)
        {
            throw new Exception("Could not find the SetupRM application window");
        }

        // Enter the PAF key
        AutomationElement keyText = windows[0].FindFirstDescendant(cf => cf.ByClassName("TEdit"));
        if (keyText == null)
        {
            throw new Exception("Could not find the key input field");
        }
        keyText.AsTextBox().Enter(key);

        // Navigate through the setup wizard
        // First page - Begin button
        AutomationElement beginButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        if (beginButton == null)
        {
            throw new Exception("Could not find the Begin button");
        }
        windows[0].SetForeground();
        beginButton.AsButton().Click();
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // Second page - Next button
        AutomationElement nextButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        if (nextButton == null)
        {
            throw new Exception("Could not find the Next button");
        }
        windows[0].SetForeground();
        nextButton.AsButton().Click();
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // Third page - Set extract path and start
        AutomationElement extractText = windows[0].FindFirstDescendant(cf => cf.ByClassName("TEdit"));
        if (extractText == null)
        {
            throw new Exception("Could not find the extract path input field");
        }
        extractText.AsTextBox().Enter(dataSourcePath);

        AutomationElement startButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        if (startButton == null)
        {
            throw new Exception("Could not find the Start button");
        }
        windows[0].SetForeground();
        startButton.AsButton().Click();

        // Wait for extraction to complete
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        await WaitForExtraction(windows, stoppingToken);

        windows[0].Close();
    }

    // Cleans up directories before or after the build process
    // fullClean: If true, cleans both working and output directories; otherwise, only cleans working directory
    private void CleanupDirectories(bool fullClean, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Terminate any running RoyalMail processes
        Utils.KillRmProcs();

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

    // Updates SMi files and dongle list with the current data period
    private void UpdateSmiFiles(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        string smiPath = Path.Combine(Settings.WorkingPath, "Smi");
        Directory.CreateDirectory(smiPath);

        // Copy necessary files
        Utils.CopyFiles(Settings.DongleListPath, smiPath);

        string ukDirectoryCreationFilesPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkDirectoryCreationFiles");
        if (!Directory.Exists(ukDirectoryCreationFilesPath))
        {
            throw new DirectoryNotFoundException($"UkDirectoryCreationFiles directory not found at: {ukDirectoryCreationFilesPath}");
        }
        Utils.CopyFiles(ukDirectoryCreationFilesPath, smiPath);

        // Calculate the next month for build
        int dataMonthInt = int.Parse(dataYearMonth.Substring(4, 2));
        string dataMonthString;

        if (dataMonthInt < 10)
        {
            dataMonthInt++;
            dataMonthString = $"0{dataMonthInt}";
        }
        else if (dataMonthInt > 9 && dataMonthInt < 12)
        {
            dataMonthInt++;
            dataMonthString = dataMonthInt.ToString();
        }
        else
        {
            dataMonthString = "01";
        }

        // Update XML definition file with new date
        string xmlFilePath = Path.Combine(smiPath, "UK_RM_CM.xml");
        if (!File.Exists(xmlFilePath))
        {
            throw new FileNotFoundException($"UK_RM_CM.xml not found at: {xmlFilePath}");
        }

        XmlDocument definitionFile = new();
        definitionFile.Load(xmlFilePath);
        XmlNode root = definitionFile.DocumentElement;
        root.Attributes[1].Value = "Y" + dataYearMonth[..4] + "M" + dataMonthString;
        definitionFile.Save(xmlFilePath);

        // Update UK dongle list with new date
        string dongleListPath = Path.Combine(smiPath, "UK_RM_CM.txt");
        string tempDongleListPath = Path.Combine(smiPath, "DongleTemp.txt");

        if (!File.Exists(dongleListPath))
        {
            throw new FileNotFoundException($"UK_RM_CM.txt not found at: {dongleListPath}");
        }

        using (StreamWriter sw = new(tempDongleListPath, true, System.Text.Encoding.Unicode))
        {
            sw.WriteLine("Date=" + dataYearMonth[..4] + dataMonthString + "19");

            using StreamReader sr = new(dongleListPath, System.Text.Encoding.Unicode);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                sw.WriteLine(line);
            }
        }

        // Replace original files with updated ones
        File.Delete(dongleListPath);
        File.Delete(Path.Combine(smiPath, "UK_RM_CM.lcs"));
        File.Delete(Path.Combine(smiPath, "UK_RM_CM_Patterns.exml"));
        File.Move(tempDongleListPath, dongleListPath);

        // Encrypt the dongle list
        string encryptRepPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptREP.exe");
        string encryptRepFileName = Utils.WrapQuotes(encryptRepPath);

        // Encrypt for LCS format
        string encryptRepArgs = "-x lcs " + Utils.WrapQuotes(dongleListPath);
        Process encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();

        // Encrypt for ELCS format
        encryptRepArgs = "-x elcs " + Utils.WrapQuotes(dongleListPath);
        encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();

        // Encrypt patterns
        string encryptPatternsPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptPatterns.exe");
        string encryptPatternsFileName = Utils.WrapQuotes(encryptPatternsPath);
        string patternsFilePath = Path.Combine(smiPath, "UK_RM_CM_Patterns.xml");

        if (!File.Exists(patternsFilePath))
        {
            throw new FileNotFoundException($"UK_RM_CM_Patterns.xml not found at: {patternsFilePath}");
        }

        string encryptPatternsArgs = "--patterns " + Utils.WrapQuotes(patternsFilePath) + " --clickCharge";
        Process encryptPatterns = Utils.RunProc(encryptPatternsFileName, encryptPatternsArgs);
        encryptPatterns.WaitForExit();

        // Verify encryption was successful
        string encryptedPatternsPath = Path.Combine(smiPath, "UK_RM_CM_Patterns.exml");
        if (!File.Exists(encryptedPatternsPath))
        {
            throw new Exception("Missing C++ 2010 x86 redistributable, EncryptPatterns and DirectoryDataCompiler 1.9 won't work. Also check that SQL CE is installed for 1.9");
        }
    }

    // Converts PAF data to the required format
    private void ConvertPafData(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        string dbPath = Path.Combine(Settings.WorkingPath, "Db");
        Directory.CreateDirectory(dbPath);

        // Copy address data files to working folder
        string pafCompressedPath = Path.Combine(dataSourcePath, "PAF COMPRESSED STD");
        if (!Directory.Exists(pafCompressedPath))
        {
            throw new DirectoryNotFoundException($"PAF COMPRESSED STD directory not found at: {pafCompressedPath}");
        }
        Utils.CopyFiles(pafCompressedPath, dbPath);

        string aliasPath = Path.Combine(dataSourcePath, "ALIAS");
        if (!Directory.Exists(aliasPath))
        {
            throw new DirectoryNotFoundException($"ALIAS directory not found at: {aliasPath}");
        }
        Utils.CopyFiles(aliasPath, dbPath);

        // Run ConvertPafData tool
        string convertPafDataPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", "ConvertPafData.exe");
        string convertPafDataFileName = Utils.WrapQuotes(convertPafDataPath);
        string convertPafDataArgs = "--pafPath " + Utils.WrapQuotes(dbPath) + " --lastPafFileNum 15";
        Process convertPafData = Utils.RunProc(convertPafDataFileName, convertPafDataArgs);

        // Monitor the conversion process
        using (StreamReader sr = convertPafData.StandardOutput)
        {
            string line;
            Regex matchPattern = new(@"fpcompst.c\d\d");
            Regex errorPattern = new(@"\[E\]");

            while ((line = sr.ReadLine()) != null)
            {
                // Check for errors
                Match errorFound = errorPattern.Match(line);
                if (errorFound.Success)
                {
                    throw new Exception("Error detected in ConvertPafData: " + line);
                }

                // Log progress
                Match matchFound = matchPattern.Match(line);
                if (matchFound.Success)
                {
                    logger.LogDebug($"ConvertPafData processing file: {matchFound.Value}");
                }
            }
        }

        // Copy the conversion result to SMi build files folder
        string ukTextPath = Path.Combine(dbPath, "Uk.txt");
        string smiUkTextPath = Path.Combine(Settings.WorkingPath, "Smi", "Uk.txt");

        if (!File.Exists(ukTextPath))
        {
            throw new FileNotFoundException($"Uk.txt not found at: {ukTextPath}");
        }

        File.Copy(ukTextPath, smiUkTextPath, true);
    }

    // Compiles the database for different versions
    private async Task CompileDatabase(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        List<Task> tasks =
        [
            Task.Run(() => CompileVersion("3.0"), stoppingToken),
            // Task.Run(() => CompileVersion("1.9"), stoppingToken)
        ];

        await Task.WhenAll(tasks);
    }

    // Packages the compiled output for different versions
    private async Task PackageOutput(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        List<Task> tasks =
        [
            Task.Run(() => PackageVersion("3.0"), stoppingToken),
            // Task.Run(() => PackageVersion("1.9"), stoppingToken)
        ];

        await Task.WhenAll(tasks);
    }

    // Updates the database to mark the build as complete
    private async Task UpdateBuildStatus(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Find the bundle record for this data period
        Bundle bundle = context.RoyalBundles().Where(x => dataYearMonth == x.DataYearMonth).FirstOrDefault();

        // Only update if bundle exists
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

    // Waits for the extraction process to complete
    private async Task WaitForExtraction(Window[] windows, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Non-recursive implementation to avoid stack overflow
        int maxAttempts = 30; // Maximum number of attempts (15 minutes total)
        int attempt = 0;

        while (attempt < maxAttempts)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            AutomationElement progressbar = windows[0].FindFirstDescendant(cf => cf.ByClassName("TProgressBar"));

            // If progress bar is no longer present, extraction is complete
            if (progressbar == null)
            {
                logger.LogInformation("Extraction completed");
                return;
            }

            logger.LogDebug($"Waiting for extraction to complete (attempt {attempt + 1}/{maxAttempts})");
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            attempt++;
        }

        logger.LogWarning("Extraction wait time exceeded, continuing with process");
    }

    // Compiles the database for a specific version
    private void CompileVersion(string version)
    {
        string versionPath = Path.Combine(Settings.WorkingPath, version);
        Directory.CreateDirectory(versionPath);

        // Copy required files to version directory
        string smiPath = Path.Combine(Settings.WorkingPath, "Smi");
        List<string> filesToCopy = new List<string>
        {
            "UK_RM_CM.xml",
            "UK_RM_CM_Patterns.xml",
            "UK_RM_CM_Patterns.exml",
            "UK_RM_CM_Settings.xml",
            "UK_RM_CM.lcs",
            "UK_RM_CM.elcs",
            "BFPO.txt",
            "UK.txt",
            "Country.txt",
            "County.txt",
            "PostTown.txt",
            "StreetDescriptor.txt",
            "StreetName.txt",
            "PoBoxName.txt",
            "SubBuildingDesignator.txt",
            "OrganizationName.txt",
            "Country_Alias.txt",
            "UK_IgnorableWordsTable.txt",
            "UK_WordMatchTable.txt"
        };

        foreach (string file in filesToCopy)
        {
            string sourcePath = Path.Combine(smiPath, file);
            string destPath = Path.Combine(versionPath, file);

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath, true);
            }
            else
            {
                logger.LogWarning($"File not found for compilation: {sourcePath}");
            }
        }

        // Run DirectoryDataCompiler for this version
        string compilerPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", version, "DirectoryDataCompiler.exe");
        string compilerFileName = Utils.WrapQuotes(compilerPath);
        string definitionPath = Path.Combine(versionPath, "UK_RM_CM.xml");
        string patternsPath = Path.Combine(versionPath, "UK_RM_CM_Patterns.xml");

        string compilerArgs = "--definition " + Utils.WrapQuotes(definitionPath) +
                              " --patterns " + Utils.WrapQuotes(patternsPath) +
                              " --password M0ntyPyth0n --licensed";

        Process compiler = Utils.RunProc(compilerFileName, compilerArgs);

        // Monitor the compilation process
        using StreamReader sr = compiler.StandardOutput;
        string line;
        Regex addressCountPattern = new(@"\d\d\d\d\d");
        Regex errorPattern = new(@"\[E\]");
        int linesRead = 0;

        while ((line = sr.ReadLine()) != null)
        {
            // Check for errors
            Match errorFound = errorPattern.Match(line);
            if (errorFound.Success)
            {
                throw new Exception($"Error detected in DirectoryDataCompiler {version}: {line}");
            }

            // Log progress
            Match matchFound = addressCountPattern.Match(line);
            if (matchFound.Success)
            {
                linesRead = int.Parse(matchFound.Value);
                if (linesRead % 5000 == 0)
                {
                    logger.LogDebug($"DirectoryDataCompiler {version} addresses processed: {linesRead}");
                }
            }
        }

        logger.LogInformation($"Compilation completed for version {version}, processed {linesRead} addresses");
    }

    // Packages the output for a specific version
    private void PackageVersion(string version)
    {
        string versionOutputPath = Path.Combine(dataOutputPath, version, "UK_RM_CM");
        Directory.CreateDirectory(versionOutputPath);

        // Copy required files to output directory
        string versionPath = Path.Combine(Settings.WorkingPath, version);
        List<string> filesToCopy = new List<string>
        {
            "UK_IgnorableWordsTable.txt",
            "UK_RM_CM_Patterns.exml",
            "UK_WordMatchTable.txt",
            "UK_RM_CM.lcs",
            "UK_RM_CM.elcs",
            "UK_RM_CM.smi"
        };

        foreach (string file in filesToCopy)
        {
            string sourcePath = Path.Combine(versionPath, file);
            string destPath = Path.Combine(versionOutputPath, file);

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath, true);
            }
            else
            {
                logger.LogWarning($"File not found for packaging: {sourcePath}");
            }
        }

        // Copy settings file to version directory
        string settingsSourcePath = Path.Combine(versionPath, "UK_RM_CM_Settings.xml");
        string settingsDestPath = Path.Combine(dataOutputPath, version, "UK_RM_CM_Settings.xml");

        if (File.Exists(settingsSourcePath))
        {
            File.Copy(settingsSourcePath, settingsDestPath, true);
        }
        else
        {
            logger.LogWarning($"Settings file not found for packaging: {settingsSourcePath}");
        }

        // Remove version-specific files
        if (version == "1.9")
        {
            string elcsPath = Path.Combine(versionOutputPath, "UK_RM_CM.elcs");
            if (File.Exists(elcsPath))
            {
                File.Delete(elcsPath);
            }
        }
        else if (version == "3.0")
        {
            string lcsPath = Path.Combine(versionOutputPath, "UK_RM_CM.lcs");
            if (File.Exists(lcsPath))
            {
                File.Delete(lcsPath);
            }
        }

        logger.LogInformation($"Packaging completed for version {version}");
    }
}
