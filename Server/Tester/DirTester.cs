using System.Diagnostics;
using System.IO.Compression;
using Server.DataObjects;

#pragma warning disable CS4014 //ignore that not awaiting Task, by design

namespace Server.Tester;

public class DirTester : BaseModule
{
    private readonly ILogger<DirTester> logger;
    private readonly IConfiguration config;

    public DirTester(ILogger<DirTester> logger, IConfiguration config)
    {
        this.logger = logger;
        this.config = config;
    }

    public async Task Start(string directoryType, string dataYearMonth)
    {
        // Avoids lag from client click to server, likely unnessasary.... 
        if (Status == ModuleStatus.InProgress)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting Tester");
            Status = ModuleStatus.InProgress;

            Settings.DirectoryName = directoryType;
            Settings.Validate(config);

            switch (directoryType)
            {
                case "Zip4":
                    CurrentTask = "Zip4";

                    Message = "Checking disc contents";
                    Progress = 50;
                    Zip4CheckDisc();

                    break;
                case "SmartMatch":
                    CurrentTask = "Cycle-O";

                    Message = "Checking disc contents";
                    Progress = 15;
                    SmartMatchCheckDisc();

                    Message = "Installing directory to Argosy Post";
                    Progress = 30;
                    await SmartMatchInstallDirectory();

                    Message = "Checking directory license file";
                    Progress = 45;
                    if (await CheckLicense(directoryType))
                    {
                        Message = "Changed to known working configuration, dongle on list";
                    }
                    else
                    {
                        Message = "Unable to change to known working configuration, donglelist likely performing correctly";
                    }

                    Message = "Adding dongle to directory license file";
                    Progress = 60;
                    AddSmartMatchLicense(dataYearMonth);

                    Message = "Injecting test images";
                    Progress = 75;
                    await InjectImages(directoryType);
                    break;
                case "Parascript":
                    CurrentTask = "Parascript";

                    Message = "Checking disc contents";
                    Progress = 25;
                    ParascriptCheckDisc();

                    Message = "Installing directory to Argosy Post";
                    Progress = 50;
                    await ParascriptInstallDirectory();

                    Message = "Injecting test images";
                    Progress = 75;
                    await InjectImages(directoryType);
                    break;
                case "RoyalMail":
                    CurrentTask = "RoyalMail";

                    Message = "Checking disc contents";
                    Progress = 15;
                    RoyalMailCheckDisc();

                    Message = "Installing directory to Argosy Post";
                    Progress = 30;
                    await RoyalMailInstallDirectory();

                    Message = "Checking directory license file";
                    Progress = 45;
                    if (await CheckLicense(directoryType))
                    {
                        Message = "Changed to known working configuration, dongle on list";
                    }
                    else
                    {
                        Message = "Unable to change to known working configuration, donglelist likely performing correctly";
                    }

                    Message = "Adding dongle to directory license file";
                    Progress = 60;
                    AddRoyalMailLicense(dataYearMonth);

                    Message = "Injecting test images";
                    Progress = 75;
                    await InjectImages(directoryType);
                    break;
            }

            Message = "Completed testing";
            Progress = 100;
            logger.LogInformation("Test Complete");
            Status = ModuleStatus.Ready;
        }
        catch (Exception e)
        {
            Status = ModuleStatus.Error;
            Message = $"{e.Message}";
            logger.LogError($"{e.Message}");
        }
    }

    private async Task InjectImages(string directoryName)
    {
        // AP warmup....
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Utils.StartService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Set config with ControlPort, wait for Recmodule to initialize after setting
        ControlPort controlPort = new(logger, "127.0.0.1", 1069);
        controlPort.RequestConfigChange(directoryName);

        BackEndSockC beSockC = new("127.0.0.1");
        CancellationTokenSource stoppingTokenSource = new();

        _ = Task.Run(async () =>
        {
            // Wait for AP initialize
            await Task.Delay(TimeSpan.FromSeconds(15));

            // Inject known good test deck
            string testDeckPath = Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", directoryName);
            Process feSockC = Utils.RunProc(@"C:\Program Files\Argosy Post\FE_SOCK_C_Test.exe", $"-f {testDeckPath}\\*.tif");
            feSockC.WaitForExit();

            // Wait for inject to finish through AP
            await Task.Delay(TimeSpan.FromSeconds(15));
            stoppingTokenSource.Cancel();
        });

        await beSockC.ExecuteAsync(stoppingTokenSource);

        if (beSockC.FinalCount < 5)
        {
            throw new Exception("Directory did not pass injection test");
        }
    }

    private async Task<bool> CheckLicense(string directoryName)
    {
        ControlPort controlPort = new(logger, "127.0.0.1", 1069);

        Task.Run(async () =>
        {
            // AP Warmup
            await Task.Delay(TimeSpan.FromSeconds(2));
            controlPort.RequestStatusAlerts();

            // Set to known baseline config (Silver, no product)
            await Task.Delay(TimeSpan.FromSeconds(10));
            controlPort.RequestConfigChange("Baseline");

            // Set to config for testing
            await Task.Delay(TimeSpan.FromSeconds(10));
            controlPort.RequestConfigChange(directoryName);
        });

        if (await controlPort.RecieveMessage())
        {
            return true;
        }
        else
        {
            return false;
        }
    }


    private void Zip4CheckDisc()
    {
        List<string> smFiles =
        [
            "Zip4.zip"
        ];

        string missingFiles = "";

        foreach (string file in smFiles)
        {
            if (!File.Exists(Path.Combine(Settings.DiscDrivePath, file)))
            {
                missingFiles += file + ", ";
            }
        }

        if (!string.IsNullOrEmpty(missingFiles))
        {
            throw new Exception("Missing files (may have disc in wrong drive): " + missingFiles);
        }
    }

    private void SmartMatchCheckDisc()
    {
        List<string> smFiles =
        [
            "DPV.zip",
            "LACS.zip",
            "SUITE.zip",
            "Zip4.zip"
        ];

        string missingFiles = "";

        foreach (string file in smFiles)
        {
            if (!File.Exists(Path.Combine(Settings.DiscDrivePath, file)))
            {
                missingFiles += file + ", ";
            }
        }

        if (!string.IsNullOrEmpty(missingFiles))
        {
            throw new Exception("Missing files (may have disc in wrong drive): " + missingFiles);
        }
    }

    private void ParascriptCheckDisc()
    {
        const string dpvFile = "DPV.zip";
        const string suiteFile = "SUITE.zip";
        const string zip4File = "Zip4.zip";
        List<string> lacsFiles =
        [
            "fileinfo_log.txt",
            "live.txt",
            "llk.hs1",
            "llk.hs2",
            "llk.hs3",
            "llk.hs4",
            "llk.hs5",
            "llk.hs6",
            "llk.hsa",
            "llk.hsl",
            "llkhdr01.dat",
            "llk_cln.dat",
            "llk_cln.txt",
            "llk_crd.dat",
            "llk_czp.dat",
            "llk_czp.txt",
            "llk_dsc.dat",
            "llk_hint.lst",
            "llk_lcd",
            "llk_leftrite.txt",
            "llk_lln.dat",
            "llk_nam.dat",
            "llk_pno.dat",
            "llk_rv9.dat",
            "llk_rv9.esd",
            "llk_rv9.idx",
            "llk_sno.dat",
            "llk_strname.txt",
            "llk_suf.dat",
            "llk_urbx.lst",
            "llk_x11"
        ];

        string missingFiles = "";

        if (!File.Exists(Path.Combine(Settings.DiscDrivePath, "DPV", dpvFile)))
        {
            missingFiles += dpvFile + ", ";
        }
        if (!File.Exists(Path.Combine(Settings.DiscDrivePath, "Suite", suiteFile)))
        {
            missingFiles += suiteFile + ", ";
        }
        if (!File.Exists(Path.Combine(Settings.DiscDrivePath, "Zip4", zip4File)))
        {
            missingFiles += zip4File + ", ";
        }
        foreach (string file in lacsFiles)
        {
            if (!File.Exists(Path.Combine(Settings.DiscDrivePath, "LACS", file)))
            {
                missingFiles += file + ", ";
            }
        }

        if (!string.IsNullOrEmpty(missingFiles))
        {
            throw new Exception("Missing files (may have disc in wrong drive): " + missingFiles);
        }
    }

    private void RoyalMailCheckDisc()
    {
        const string rmSettingsFile = "UK_RM_CM_Settings.xml";
        List<string> rmFiles =
        [
            "UK_IgnorableWordsTable.txt",
            "UK_RM_CM.lcs",
            "UK_RM_CM.elcs",
            "UK_RM_CM.smi",
            "UK_RM_CM_Patterns.exml",
            "UK_WordMatchTable.txt",
        ];

        string missingFiles = "";

        if (!File.Exists(Path.Combine(Settings.DiscDrivePath, rmSettingsFile)))
        {
            missingFiles += rmSettingsFile + ", ";
        }
        foreach (string file in rmFiles)
        {
            if (!File.Exists(Path.Combine(Settings.DiscDrivePath, "UK_RM_CM", file)))
            {
                missingFiles += file + ", ";
            }
        }

        if (!string.IsNullOrEmpty(missingFiles))
        {
            throw new Exception("Missing files (may have disc in wrong drive): " + missingFiles);
        }
    }

    private async Task SmartMatchInstallDirectory()
    {
        // Stop RAFMaster
        await Utils.StopService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));


        // Extract zipped files to temp folder (not all files are copied from DVD)
        string tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "SmartMatch");
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        Directory.CreateDirectory(Path.Combine(tempFolder, "DPV"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "Suite"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "Zip4"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "LACS"));

        // Make sure these are here, in case of 1st install
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match", "DPV"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match", "LACSLink"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match", "SuiteLink"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match", "Zip4"));

        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "Zip4.zip"), Path.Combine(tempFolder, "Zip4"));
        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "SUITE.zip"), Path.Combine(tempFolder, "Suite"));
        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "DPV.zip"), Path.Combine(tempFolder, "DPV"));
        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "LACS.zip"), Path.Combine(tempFolder, "LACS"));

        // Copy Files to Argosy
        Utils.CopyFiles(Path.Combine(tempFolder, "DPV"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match\DPV");
        Utils.CopyFiles(Path.Combine(tempFolder, "LACS"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match\LACSLink");
        Utils.CopyFiles(Path.Combine(tempFolder, "Suite"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match\SuiteLink");
        Utils.CopyFiles(Path.Combine(tempFolder, "Zip4"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match\Zip4");

        // Cleanup
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        // Start RAFMaster
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Utils.StartService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));

    }

    private async Task ParascriptInstallDirectory()
    {
        // Stop RAFMaster
        await Utils.StopService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Extract zipped files to temp folder (not all files are copied from DVD)
        string tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "Parascript");
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        Directory.CreateDirectory(Path.Combine(tempFolder, "DPV"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "Suite"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "Zip4"));
        Directory.CreateDirectory(Path.Combine(tempFolder, "LACS"));

        // Make sure these are here, in case of 1st install
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript", "DPV"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript", "LACSLink"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript", "SuiteLink"));
        Directory.CreateDirectory(Path.Combine(@"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript", "Zip4"));

        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "Zip4", "Zip4.zip"), Path.Combine(tempFolder, "Zip4"));
        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "Suite", "SUITE.zip"), Path.Combine(tempFolder, "Suite"));
        ZipFile.ExtractToDirectory(Path.Combine(Settings.DiscDrivePath, "DPV", "DPV.zip"), Path.Combine(tempFolder, "DPV"));
        Utils.CopyFiles(Path.Combine(Settings.DiscDrivePath, "LACS"), Path.Combine(tempFolder, "LACS"));

        // Copy Files to Argosy
        Utils.CopyFiles(Path.Combine(tempFolder, "DPV"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript\DPV");
        Utils.CopyFiles(Path.Combine(tempFolder, "LACS"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript\LACSLink");
        Utils.CopyFiles(Path.Combine(tempFolder, "Suite"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript\SuiteLink");
        Utils.CopyFiles(Path.Combine(tempFolder, "Zip4"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\AddressScript\Zip4");

        // Cleanup
        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        // Start RAFMaster
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Utils.StartService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    private async Task RoyalMailInstallDirectory()
    {
        // Stop RAFMaster
        await Utils.StopService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));

        Utils.CopyFiles(Settings.DiscDrivePath, @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match-i");

        // Start RAFMaster
        await Task.Delay(TimeSpan.FromSeconds(10));
        await Utils.StartService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    private async Task AddSmartMatchLicense(string dataYearMonth)
    {
        // Cleanup
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.txt")))
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.txt"));
        }
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.elcs")))
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.elcs"));
        }

        // Set dataYearMonth to year and month (day is always the 1st)
        dataYearMonth += "01";

        // Create txt version of ArgosyMonthly that includes dongle
        string rawDongleListPath = Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.txt");
        using (StreamWriter sw = new(rawDongleListPath, true))
        {
            sw.WriteLine("Date=" + dataYearMonth);
            sw.WriteLine("Dongles:");
            sw.WriteLine(config.GetValue<string>("DongleId"));
        }

        string encryptRepFileName = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptREP.exe");
        string encryptRepArgs = $"-x elcs {rawDongleListPath}";

        // Remember to add EncryptREP to Windows Defender exclusion list
        Process encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();

        // Check that LCS file was actually created
        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.elcs")))
        {
            throw new Exception("ELCS file was not created, something likely wrong with EncryptREP");
        }

        // Overwrite old LCS, start RAFArgosyMaster
        await Utils.StopService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));
        File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "argosymonthly.elcs"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match\Zip4\argosymonthly.elcs", true);
    }

    private async Task AddRoyalMailLicense(string dataYearMonth)
    {
        // Cleanup
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.txt")))
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.txt"));
        }
        if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.elcs")))
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.elcs"));
        }

        // Set dataYearMonth to year and month (day is always the 19th)
        dataYearMonth += "19";

        // Create txt version of UK_RM_CM that includes dongle
        string rawDongleListPath = Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.txt");
        using (StreamWriter sw = new(rawDongleListPath, true))
        {
            sw.WriteLine($"Date={dataYearMonth}");
            sw.WriteLine("Directory=UK_RM_CM");
            sw.WriteLine("Dongles:");
            sw.WriteLine(config.GetValue<string>("DongleId"));
        }

        string encryptRepFileName = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptREP.exe");
        string encryptRepArgs = $"-x elcs {rawDongleListPath}";

        // Remember to add EncryptREP to Windows Defender exclusion list
        Process encryptRep = Utils.RunProc(encryptRepFileName, encryptRepArgs);
        encryptRep.WaitForExit();

        // Check that LCS file was actually created
        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.elcs")))
        {
            throw new Exception("ELCS file was not created, something likely wrong with EncryptREP");
        }

        // Overwrite old LCS, start RAFArgosyMaster
        await Utils.StopService("RAFArgosyMaster");
        await Task.Delay(TimeSpan.FromSeconds(10));
        File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "TestDecks", "TesterTemp", "UK_RM_CM.elcs"), @"C:\ProgramData\RAF\ArgosyPost\Sync\Directories\RAF Smart Match-i\UK_RM_CM\UK_RM_CM.elcs", true);
    }
}
