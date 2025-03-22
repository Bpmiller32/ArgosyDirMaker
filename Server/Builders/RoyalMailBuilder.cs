using System.Text.RegularExpressions;
using FlaUI.UIA2;
using FlaUI.Core.AutomationElements;
using Server.DataObjects;
using System.Xml;
using System.Diagnostics;

namespace Server.Builders;

public class RoyalMailBuilder : BaseModule
{
    private readonly ILogger<RoyalMailBuilder> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    private string dataYearMonth;
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
        // Avoids lag from client click to server, likely unnessasary.... 
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Builder");
            Status = ModuleStatus.InProgress;
            CurrentTask = dataYearMonth;

            Settings.Validate(config);
            this.dataYearMonth = dataYearMonth;
            dataSourcePath = Path.Combine(Settings.AddressDataPath, dataYearMonth);
            dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth);

            Message = "Verifying PAF Key";
            Progress = 0;
            await CheckKey(key, stoppingToken);

            Message = "Extracing from PAF executable";
            Progress = 1;
            await Extract(key, stoppingToken);

            Message = "Cleaning up from previous builds";
            Progress = 22;
            Cleanup(fullClean: true, stoppingToken);

            Message = "Updating SMi files & dongle list";
            Progress = 23;
            UpdateSmiFiles(stoppingToken);

            Message = "Converting PAF data";
            Progress = 24;
            ConvertPafData(stoppingToken);

            Message = "Compiling database";
            Progress = 47;
            await Compile(stoppingToken);

            Message = "Packaging database";
            Progress = 97;
            await Output(stoppingToken);

            Message = "Cleaning up post build";
            Progress = 98;
            Cleanup(fullClean: false, stoppingToken);

            Message = "Updating packaged directories";
            Progress = 99;
            await CheckBuildComplete(stoppingToken);

            Progress = 100;
            Status = ModuleStatus.Ready;
            Message = "";
            CurrentTask = "";
            logger.LogInformation($"Build Complete: {dataYearMonth}");
        }
        catch (TaskCanceledException e)
        {
            Status = ModuleStatus.Ready;
            CurrentTask = "";
            logger.LogDebug($"{e.Message}");
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"{e.Message}");
        }
    }

    private async Task CheckKey(string royalKey, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (string.IsNullOrEmpty(royalKey))
        {
            return;
        }

        PafKey filteredKey = new()
        {
            DataYear = int.Parse(dataYearMonth[..4]),
            DataMonth = int.Parse(dataYearMonth.Substring(4, 2)),
            Value = royalKey,
        };

        bool keyInDb = context.PafKeys.Any(x => filteredKey.Value == x.Value);

        if (!keyInDb)
        {
            logger.LogInformation("Unique PafKey added: {DataMonth}/{DataYear}", filteredKey.DataMonth, filteredKey.DataYear);
            context.PafKeys.Add(filteredKey);
            await context.SaveChangesAsync(stoppingToken);
        }
    }

    private async Task Extract(string key, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Change so that front end still collects keys for db, but uses what it passes every time in case of mistake
        // PafKey key = context.PafKeys.Where(x => (int.Parse(dataYearMonth.Substring(4, 2)) == x.DataMonth) && (int.Parse(dataYearMonth.Substring(0, 4)) == x.DataYear)).FirstOrDefault() ?? throw new Exception("Key not found in db");

        using UIA2Automation automation = new();
        FlaUI.Core.Application app = FlaUI.Core.Application.Launch(Path.Combine(dataSourcePath, "SetupRM.exe"));
        // Annoyingly have to do this wait because SetupRM is not created correctly, "splash screen" effect causes FlaUI to grab the window before the body is populated with elements
        // Could make better with waiting for Windows control visibility or something, this is good enough though
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        Window[] windows = app.GetAllTopLevelWindows(automation);

        // Check that main window elements can be found
        // TODO: Somehow if key is wrong, look for label maybe?
        AutomationElement keyText = windows[0].FindFirstDescendant(cf => cf.ByClassName("TEdit")) ?? throw new Exception("Could not find the window elements");

        keyText.AsTextBox().Enter(key);

        // 1st page
        AutomationElement beginButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        windows[0].SetForeground();
        beginButton.AsButton().Click();
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // 2nd page
        AutomationElement nextButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        windows[0].SetForeground();
        nextButton.AsButton().Click();
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // 3rd page
        AutomationElement extractText = windows[0].FindFirstDescendant(cf => cf.ByClassName("TEdit"));
        extractText.AsTextBox().Enter(dataSourcePath);
        AutomationElement startButton = windows[0].FindFirstDescendant(cf => cf.ByClassName("TButton"));
        windows[0].SetForeground();
        startButton.AsButton().Click();

        // Annoyingly have to wait because SetupRM hangs before moving to extract causing the WaitForExtract to miss the TProgressBar element is needs to watch
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        await WaitForExtract(windows, stoppingToken);

        windows[0].Close();
    }

    private void Cleanup(bool fullClean, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Kill process that may be running in the background from previous runs
        Utils.KillRmProcs();

        // Ensure working and output directories are created and clear them if they already exist
        Directory.CreateDirectory(Settings.WorkingPath);
        Directory.CreateDirectory(dataOutputPath);

        // Cleanup just working path
        if (!fullClean)
        {
            Utils.Cleanup(Settings.WorkingPath, stoppingToken);
            return;
        }

        // Cleanup working and output path
        Utils.Cleanup(Settings.WorkingPath, stoppingToken);
        Utils.Cleanup(dataOutputPath, stoppingToken);
    }

    private void UpdateSmiFiles(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(Settings.WorkingPath, "Smi"));

        Utils.CopyFiles(Settings.DongleListPath, Path.Combine(Settings.WorkingPath, "Smi"));
        Utils.CopyFiles(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkDirectoryCreationFiles"), Path.Combine(Settings.WorkingPath, "Smi"));

        // Build for one month ahead
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

        // Edit SMi definition xml file with updated date 
        XmlDocument defintionFile = new();
        defintionFile.Load(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.xml"));
        XmlNode root = defintionFile.DocumentElement;
        root.Attributes[1].Value = "Y" + dataYearMonth[..4] + "M" + dataMonthString;
        defintionFile.Save(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.xml"));

        // Edit Uk dongle list with updated date
        using (StreamWriter sw = new(Path.Combine(Settings.WorkingPath, "Smi", "DongleTemp.txt"), true, System.Text.Encoding.Unicode))
        {
            sw.WriteLine("Date=" + dataYearMonth[..4] + dataMonthString + "19");

            using StreamReader sr = new(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.txt"), System.Text.Encoding.Unicode);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                sw.WriteLine(line);
            }
        }

        File.Delete(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.txt"));
        File.Delete(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.lcs"));
        File.Delete(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM_Patterns.exml"));

        File.Move(Path.Combine(Settings.WorkingPath, "Smi", "DongleTemp.txt"), Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.txt"));

        // Encrypt new Uk dongle list, but first wrap the combined paths in quotes to get around spaced directories
        string encryptRepFileName = Utils.WrapQuotes(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptREP.exe"));
        // Perform for LCS
        string encryptRepArgs = "-x lcs " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.txt"));
        Process encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();
        // Perform for ELCS
        encryptRepArgs = "-x elcs " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM.txt"));
        encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();

        // Encrypt patterns, but first wrap the combined paths in quotes to get around spaced directories
        // Perform for LCS
        string encryptPatternsFileName = Utils.WrapQuotes(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptPatterns.exe"));
        string encryptPatternsArgs = "--patterns " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM_Patterns.xml")) + " --clickCharge";
        Process encryptPatterns = Utils.RunProc(encryptPatternsFileName, encryptPatternsArgs);
        encryptPatterns.WaitForExit();

        // If this file wasn't created then EncryptPatterns silently failed, likeliest cause is missing a redistributable
        if (!File.Exists(Path.Combine(Settings.WorkingPath, "Smi", "UK_RM_CM_Patterns.exml")))
        {
            throw new Exception("Missing C++ 2010 x86 redistributable, EncryptPatterns and DirectoryDataCompiler 1.9 won't work. Also check that SQL CE is installed for 1.9");
        }
    }

    private void ConvertPafData(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Move address data files to working folder "Db"
        Utils.CopyFiles(Path.Combine(dataSourcePath, "PAF COMPRESSED STD"), Path.Combine(Settings.WorkingPath, "Db"));
        Utils.CopyFiles(Path.Combine(dataSourcePath, "ALIAS"), Path.Combine(Settings.WorkingPath, "Db"));

        // Start ConvertPafData tool, listen for output
        string convertPafDataFileName = Utils.WrapQuotes(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", "ConvertPafData.exe"));
        string convertPafDataArgs = "--pafPath " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, "Db")) + " --lastPafFileNum 15";
        Process convertPafData = Utils.RunProc(convertPafDataFileName, convertPafDataArgs);

        using (StreamReader sr = convertPafData.StandardOutput)
        {
            string line;
            Regex match = new(@"fpcompst.c\d\d");
            Regex error = new(@"\[E\]");
            while ((line = sr.ReadLine()) != null)
            {
                Match errorFound = error.Match(line);

                if (errorFound.Success)
                {
                    throw new Exception("Error detected in ConvertPafData");
                }

                Match matchFound = match.Match(line);

                if (matchFound.Success)
                {
                    logger.LogDebug($"ConvertPafData processing file: {matchFound.Value}");
                }
            }
        }

        // Copy CovertPafData finished result to SMi build files folder
        File.Copy(Path.Combine(Settings.WorkingPath, "Db", "Uk.txt"), Path.Combine(Settings.WorkingPath, "Smi", "Uk.txt"), true);
    }

    private async Task Compile(CancellationToken stoppingToken)
    {
        List<Task> tasks =
        [
            Task.Run(() => CompileRunner("3.0"), stoppingToken),
            // Task.Run(() => CompileRunner("1.9"), stoppingToken)
        ];

        await Task.WhenAll(tasks);
    }

    private async Task Output(CancellationToken stoppingToken)
    {
        List<Task> tasks =
        [
            Task.Run(() => OutputRunner("3.0"), stoppingToken),
            // Task.Run(() => OutputRunner("1.9"), stoppingToken)
        ];

        await Task.WhenAll(tasks);
    }

    private async Task CheckBuildComplete(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        Bundle bundle = context.RoyalBundles().Where(x => dataYearMonth == x.DataYearMonth).FirstOrDefault();
        bundle.IsBuildComplete = true;
        bundle.CompileTimestamp = DateTime.Now;

        await context.SaveChangesAsync(stoppingToken);
        SendDbUpdate = true;
    }

    private async Task WaitForExtract(Window[] windows, CancellationToken stoppingToken)
    {
        AutomationElement progressbar = windows[0].FindFirstDescendant(cf => cf.ByClassName("TProgressBar"));

        if (progressbar == null)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        await WaitForExtract(windows, stoppingToken);
    }

    private void CompileRunner(string version)
    {
        Directory.CreateDirectory(Path.Combine(Settings.WorkingPath, version));

        foreach (string file in new List<string> { "UK_RM_CM.xml", "UK_RM_CM_Patterns.xml", "UK_RM_CM_Patterns.exml", "UK_RM_CM_Settings.xml", "UK_RM_CM.lcs", "UK_RM_CM.elcs", "BFPO.txt", "UK.txt", "Country.txt", "County.txt", "PostTown.txt", "StreetDescriptor.txt", "StreetName.txt", "PoBoxName.txt", "SubBuildingDesignator.txt", "OrganizationName.txt", "Country_Alias.txt", "UK_IgnorableWordsTable.txt", "UK_WordMatchTable.txt" })
        {
            File.Copy(Path.Combine(Settings.WorkingPath, "Smi", file), Path.Combine(Settings.WorkingPath, version, file), true);
        }

        string directoryDataCompilerFileName = Utils.WrapQuotes(Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", version, "DirectoryDataCompiler.exe"));
        string directoryDataCompilerArgs = "--definition " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, version, "UK_RM_CM.xml")) + " --patterns " + Utils.WrapQuotes(Path.Combine(Settings.WorkingPath, version, "UK_RM_CM_Patterns.xml")) + " --password M0ntyPyth0n --licensed";
        Process directoryDataCompiler = Utils.RunProc(directoryDataCompilerFileName, directoryDataCompilerArgs);

        using StreamReader sr = directoryDataCompiler.StandardOutput;
        string line;
        int linesRead;
        Regex match = new(@"\d\d\d\d\d");
        Regex error = new(@"\[E\]");

        while ((line = sr.ReadLine()) != null)
        {
            Match errorFound = error.Match(line);

            if (errorFound.Success)
            {
                throw new Exception($"Error detected in DirectoryDataCompiler {version}");
            }

            Match matchFound = match.Match(line);

            if (matchFound.Success)
            {
                linesRead = int.Parse(matchFound.Value);
                if (linesRead % 5000 == 0)
                {
                    logger.LogDebug($"DirectoryDataCompiler {version} addresses processed: {matchFound.Value}");
                }
            }
        }
    }

    private void OutputRunner(string version)
    {
        Directory.CreateDirectory(Path.Combine(dataOutputPath, version, "UK_RM_CM"));

        foreach (string file in new List<string> { "UK_IgnorableWordsTable.txt", "UK_RM_CM_Patterns.exml", "UK_WordMatchTable.txt", "UK_RM_CM.lcs", "UK_RM_CM.elcs", "UK_RM_CM.smi" })
        {
            File.Copy(Path.Combine(Settings.WorkingPath, version, file), Path.Combine(dataOutputPath, version, "UK_RM_CM", file), true);
        }

        File.Copy(Path.Combine(Settings.WorkingPath, version, "UK_RM_CM_Settings.xml"), Path.Combine(dataOutputPath, version, "UK_RM_CM_Settings.xml"), true);

        if (version == "1.9")
        {
            File.Delete(Path.Combine(dataOutputPath, version, "UK_RM_CM", "UK_RM_CM.elcs"));
        }
        if (version == "3.0")
        {
            File.Delete(Path.Combine(dataOutputPath, version, "UK_RM_CM", "UK_RM_CM.lcs"));
        }
    }
}
