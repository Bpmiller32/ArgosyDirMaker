using System.Diagnostics;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using Server.DataObjects;

namespace Server.Builders;

public class SmartMatchBuilder : BaseModule
{
    // Progress constants for better readability and maintenance
    private static class ProgressSteps
    {
        public const int InitialSetup = 0;
        public const int DatabaseCleanup = 1;
        public const int DataPreparation = 2;
        public const int StageOne = 10;   // Copy USPS source data
        public const int StageTwo = 24;   // Create database and import USPS data
        public const int StageThree = 60; // Generate USPS XTLs
        public const int StageFour = 61;  // Generate Key XTL
        public const int StageFive = 62;  // Create XTL ID file
        public const int StageSix = 64;   // Run APC tests
        public const int Packaging = 90;  // Package directory data
        public const int FinalCleanup = 98;
        public const int DatabaseUpdate = 99;
        public const int Complete = 100;
    }

    // DI
    private readonly ILogger<SmartMatchBuilder> logger;
    private readonly IConfiguration config;
    private readonly DatabaseContext context;

    // Fields
    private CancellationToken stoppingToken;
    private string dataYearMonth;
    private string cycle;
    private string expireDays;
    private string dataSourcePath;
    private string dataOutputPath;
    private string tempDir;
    private string extractedFolder;
    private string lacsOutput;
    private string dpvOutput;
    private string suiteOutput;
    private string xtlOutput;
    private string buildNumber;
    private string toolsDirectory;
    private string cycleToolsDirectory;
    private bool isMassData;
    private bool runTests;
    private string TestFile = "TestFile.Placeholder";

    public SmartMatchBuilder(ILogger<SmartMatchBuilder> logger, IConfiguration config, DatabaseContext context)
    {
        this.logger = logger;
        this.config = config;
        this.context = context;

        Settings.DirectoryName = "SmartMatch";
    }

    public async Task Start(string cycle, string dataYearMonth, CancellationTokenSource stoppingTokenSource, string expireDays)
    {
        // Avoid lag from client click to server
        if (Status != ModuleStatus.Ready)
        {
            return;
        }

        try
        {
            logger.LogInformation("Starting SmartMatch Builder");
            Status = ModuleStatus.InProgress;
            Message = "Starting Builder";
            CurrentTask = dataYearMonth;

            Settings.Validate(config);
            stoppingToken = stoppingTokenSource.Token;
            this.dataYearMonth = dataYearMonth;
            this.cycle = cycle;
            this.expireDays = expireDays;
            dataSourcePath = Path.Combine(Settings.AddressDataPath, dataYearMonth);
            dataOutputPath = Path.Combine(Settings.OutputPath, dataYearMonth);
            tempDir = $"{Path.GetTempPath()}XtlBuilding";
            lacsOutput = $"{tempDir}\\LACsLINK";
            dpvOutput = $"{tempDir}\\DPV_Full";
            suiteOutput = $"{tempDir}\\Suitelink";
            toolsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tools");
            buildNumber = dataYearMonth.Substring(2, 4) + "1";
            isMassData = false;
            runTests = false;

            // Restart SQL Server to ensure a clean state
            await Utils.StopService("MSSQLSERVER");
            await Utils.StartService("MSSQLSERVER");

            // Initialize progress
            Progress = ProgressSteps.InitialSetup;

            // Determine cycle type and set appropriate paths and flags
            SetupCycleSpecificParameters();

            // Clean up directories and prepare for build
            Message = "Cleaning up from previous builds";
            Progress = ProgressSteps.DatabaseCleanup;
            CleanupDirectories();

            /* ----------------- Main build process - build the database ---------------- */
            // Copy USPS source data
            Message = "Copying USPS source data";
            Progress = ProgressSteps.StageOne;
            if (!CopyUspsSourceData())
            {
                throw new Exception("Failed to copy USPS source data");
            }

            // Create database and import USPS data
            Message = "Creating database and importing USPS data";
            Progress = ProgressSteps.StageTwo;
            if (!CreateAndImportDatabase())
            {
                throw new Exception("Failed to create database and import USPS data");
            }

            // Generate USPS XTLs
            Message = "Generating USPS XTLs";
            Progress = ProgressSteps.StageThree;
            if (!GenerateUspsXtls())
            {
                throw new Exception("Failed to generate USPS XTLs");
            }

            // Restart SQL Server before generating key XTL
            await RestartSqlServer();

            // Generate Key XTL
            Message = "Generating Key XTL";
            Progress = ProgressSteps.StageFour;
            if (!GenerateKeyXtl())
            {
                throw new Exception("Failed to generate Key XTL");
            }

            // Create XTL ID file
            Message = "Creating XTL ID file";
            Progress = ProgressSteps.StageFive;
            if (!CreateXtlIdFile())
            {
                throw new Exception("Failed to create XTL ID file");
            }

            // Extract non-Zip4 data
            Message = "Extracting non-Zip4 data";
            if (!ExtractNonZip4Data())
            {
                throw new Exception("Failed to extract non-Zip4 data");
            }

            // Process dongle lists
            Message = "Processing dongle lists";
            if (!ProcessDongleLists())
            {
                throw new Exception("Failed to process dongle lists");
            }

            // Process Suite data
            Message = "Processing Suite data";
            if (!ProcessSuiteData())
            {
                throw new Exception("Failed to process Suite data");
            }

            // Run APC tests if needed
            if (runTests)
            {
                Message = "Running APC tests";
                Progress = ProgressSteps.StageSix;
                if (!RunApcTests())
                {
                    throw new Exception("Failed to run APC tests");
                }
            }

            // Clean up database
            Message = "Cleaning up database";
            Progress = ProgressSteps.FinalCleanup;
            CleanupDatabase();

            // Add DPV header
            Message = "Adding DPV header";
            if (!AddDpvHeader())
            {
                throw new Exception("Failed to add DPV header");
            }

            // Package directory data
            Message = "Packaging directory data";
            Progress = ProgressSteps.Packaging;
            if (!PackageDirectoryData())
            {
                throw new Exception("Failed to package directory data");
            }

            // Update database status
            Message = "Updating database status";
            Progress = ProgressSteps.DatabaseUpdate;
            await CheckBuildComplete(stoppingToken);

            // Build complete
            Message = "";
            Progress = ProgressSteps.Complete;
            logger.LogInformation("SmartMatch Builder finished running");
            Status = ModuleStatus.Ready;
            CurrentTask = "";
        }
        catch (TaskCanceledException)
        {
            Status = ModuleStatus.Ready;
            CurrentTask = "";
            logger.LogDebug("Build cancelled");
        }
        catch (Exception ex)
        {
            Status = ModuleStatus.Error;
            logger.LogError($"Build failed: {ex.Message}");
        }
    }

    // Sets up cycle-specific parameters based on the cycle type
    private void SetupCycleSpecificParameters()
    {
        switch (cycle)
        {
            case "N":
                cycleToolsDirectory = Path.Combine(toolsDirectory, "Cycle-N2-256");
                dataSourcePath = Path.Combine(dataSourcePath, "Cycle-N");
                dataOutputPath = Path.Combine(dataOutputPath, "Cycle-N");
                break;
            case "O":
                cycleToolsDirectory = Path.Combine(toolsDirectory, "Cycle-O-256");
                dataSourcePath = Path.Combine(dataSourcePath, "Cycle-O");
                dataOutputPath = Path.Combine(dataOutputPath, "Cycle-O");
                break;
            case "OtoN":
                cycleToolsDirectory = Path.Combine(toolsDirectory, "Cycle-N2-256");
                dataSourcePath = Path.Combine(dataSourcePath, "Cycle-O");
                dataOutputPath = Path.Combine(dataOutputPath, "Cycle-N-Using-O");
                break;
            case "MASSN":
                cycleToolsDirectory = Path.Combine(toolsDirectory, "Cycle-N2-256");
                dataSourcePath = Path.Combine(Settings.AddressDataPath, "MASS-N");
                dataOutputPath = Path.Combine(Settings.OutputPath, "MASS-N");
                isMassData = true;
                break;
            case "MASSO":
                cycleToolsDirectory = Path.Combine(toolsDirectory, "Cycle-O-256");
                dataSourcePath = Path.Combine(Settings.AddressDataPath, "MASS-O");
                dataOutputPath = Path.Combine(Settings.OutputPath, "MASS-O");
                isMassData = true;
                break;
            default:
                throw new ArgumentException($"Unknown cycle type: {cycle}");
        }

        // Set the xtlOutput path
        string targetDir;
        if (cycle.StartsWith('O'))
        {
            targetDir = "Xtl Database Creation Cycle-O SHA-256";
        }
        else
        {
            targetDir = "Xtl Database Creation Cycle-N2 SHA-256";
        }
        xtlOutput = $"C:\\{targetDir}\\Output\\Build {buildNumber}";

        logger.LogInformation($"Setting up build for cycle {cycle} with data from {dataSourcePath}");
        logger.LogInformation($"Output will be written to {dataOutputPath}");
    }

    // Cleans up directories before the build process
    private void CleanupDirectories()
    {
        // Clean up temp directory
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
        Directory.CreateDirectory(tempDir);

        // Create output directories
        Directory.CreateDirectory(dataOutputPath);
        Directory.CreateDirectory(lacsOutput);
        Directory.CreateDirectory(dpvOutput);
        Directory.CreateDirectory(suiteOutput);

        // Clean up xtlOutput directory
        if (Directory.Exists(xtlOutput))
        {
            Directory.Delete(xtlOutput, true);
        }
        Directory.CreateDirectory(xtlOutput);

        // Clean up database
        CleanupDatabase();
    }

    // Copies USPS source data to the staging folder
    private bool CopyUspsSourceData()
    {
        try
        {
            logger.LogInformation("Copying USPS source data to staging folder");

            // Create extracted data directory
            extractedFolder = $"{tempDir}\\USPS";
            Directory.CreateDirectory(extractedFolder);

            // Extract AIS ZIPMOVE data
            logger.LogInformation("Extracting AIS ZIPMOVE data");
            string zipMoveDir = $"{extractedFolder}\\AIS ZIP4 NATIONAL\\zipmove";
            Directory.CreateDirectory(zipMoveDir);

            if (isMassData)
            {
                // For MASS data, extract from zip file
                string zipMoveZip = $"{dataSourcePath}\\zipmovenatl.zip";
                if (!File.Exists(zipMoveZip))
                {
                    logger.LogError($"ZIPMOVE file not found: {zipMoveZip}");
                    return false;
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(zipMoveZip, zipMoveDir);
            }
            else
            {
                // For regular data, extract from tar file
                string zipMoveTar = $"{dataSourcePath}\\zipmovenatl.tar";
                if (!File.Exists(zipMoveTar))
                {
                    logger.LogError($"ZIPMOVE file not found: {zipMoveTar}");
                    return false;
                }

                if (!ExtractTarAndUnzip(zipMoveTar, tempDir, "\\zipmovenatl\\zipmove\\zipmove.zip",
                    "/MP7IOPE0ZV", zipMoveDir))
                {
                    logger.LogError("Failed to extract ZIPMOVE data");
                    return false;
                }
            }

            // Extract AIS ZIP4+NATIONAL data
            logger.LogInformation("Extracting AIS ZIP4+NATIONAL data");
            string zip4Dir = $"{extractedFolder}\\AIS ZIP4 NATIONAL\\zip4";
            Directory.CreateDirectory(zip4Dir);

            if (isMassData)
            {
                // For MASS data, extract from zip file
                string zip4Zip = $"{dataSourcePath}\\zip4natl.zip";
                if (!File.Exists(zip4Zip))
                {
                    logger.LogError($"ZIP4 file not found: {zip4Zip}");
                    return false;
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(zip4Zip, zip4Dir);
            }
            else
            {
                // For regular data, extract from tar file
                string zip4Tar = $"{dataSourcePath}\\zip4natl.tar";
                if (!File.Exists(zip4Tar))
                {
                    logger.LogError($"ZIP4 file not found: {zip4Tar}");
                    return false;
                }

                if (!ExtractTarAndUnzip(zip4Tar, tempDir, "\\epf-zip4natl\\zip4\\zip4.zip",
                    "/ZI1APLSZP4", zip4Dir))
                {
                    logger.LogError("Failed to extract ZIP4 data");
                    return false;
                }
            }

            // Extract AIS CTYSTATE data
            logger.LogInformation("Extracting AIS CTYSTATE data");
            string cityStateSource = $"{extractedFolder}\\AIS ZIP4 NATIONAL\\ctystate";
            Directory.CreateDirectory(cityStateSource);

            string cityStateZip;
            if (isMassData)
            {
                cityStateZip = $"{dataSourcePath}\\ctystatenatl.zip";
            }
            else
            {
                cityStateZip = $"{tempDir}\\epf-zip4natl\\ctystate\\ctystate.zip";
            }

            if (!File.Exists(cityStateZip))
            {
                logger.LogError($"CTYSTATE file not found: {cityStateZip}");
                return false;
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(cityStateZip, cityStateSource);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error copying USPS source data: {ex.Message}");
            return false;
        }
    }

    // Creates the database and imports USPS data
    private bool CreateAndImportDatabase()
    {
        try
        {
            // Restart SQL Server
            RestartSqlServer().Wait();

            // Create the database
            logger.LogInformation("Creating database");
            string targetDir = cycle.StartsWith("O") ?
                "Xtl Database Creation Cycle-O SHA-256" :
                "Xtl Database Creation Cycle-N2 SHA-256";
            string sqlXtlPath = $"C:\\{targetDir}\\Intermediate Database";

            // Create intermediate database directory if it doesn't exist
            if (!Directory.Exists($"{sqlXtlPath}\\Intermediate Database"))
            {
                Directory.CreateDirectory($"{sqlXtlPath}\\Intermediate Database");
            }

            // Run database creation tool
            string dbCreateExe = $"{cycleToolsDirectory}\\DBCreate.exe";
            string server = "127.0.0.1";
            string user = "sa";
            string pwd = "cry5taL";

            if (!RunProcess(dbCreateExe, $" {server} {user} {pwd} \"{sqlXtlPath}\"", out string output))
            {
                logger.LogError($"Database creation failed: {output}");
                return false;
            }

            // Import USPS data
            logger.LogInformation("Importing USPS data");
            string importUspsExe = $"{cycleToolsDirectory}\\ImportUsps.exe";

            if (!RunProcess(importUspsExe, $" \"{extractedFolder}\" {server} {user} {pwd}", out output))
            {
                logger.LogError($"USPS data import failed: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error creating database: {ex.Message}");
            return false;
        }
    }

    // Generates USPS XTLs
    private bool GenerateUspsXtls()
    {
        try
        {
            // Restart SQL Server
            RestartSqlServer().Wait();

            logger.LogInformation("Generating USPS XTLs");

            string generateUspsXtlsExe = $"{cycleToolsDirectory}\\GenerateUspsXtls.exe";
            string xtlSchemaDir = $"{cycleToolsDirectory}\\Schema";
            string server = "127.0.0.1";
            string user = "sa";
            string pwd = "cry5taL";

            string parameters = $" {server} {user} {pwd} {buildNumber} \"{xtlSchemaDir}\" \"{xtlOutput}\"";

            if (!RunProcess(generateUspsXtlsExe, parameters, out string output))
            {
                logger.LogError($"USPS XTL generation failed: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error generating USPS XTLs: {ex.Message}");
            return false;
        }
    }

    // Generates Key XTL
    private bool GenerateKeyXtl()
    {
        try
        {
            logger.LogInformation("Generating Key XTL");

            string generateKeyXtlExe = $"{cycleToolsDirectory}\\GenerateKeyXtl.exe";
            string month = buildNumber.Substring(2, 2);
            string twoDigitYear = buildNumber.Substring(0, 2);
            string year = $"20{twoDigitYear}";
            string xtlDataMonth;

            if (cycle.StartsWith("O"))
            {
                // For Cycle-O, use the 1st of the month
                xtlDataMonth = $"{month}/1/{year}";

                if (!RunProcess(generateKeyXtlExe, $" \"{xtlOutput}\" {xtlDataMonth}", out string output))
                {
                    logger.LogError($"Key XTL generation failed: {output}");
                    return false;
                }
            }
            else
            {
                // For Cycle-N, use the 15th of the month
                xtlDataMonth = $"{month}/15/{year}";
                DateTime dataMonth = DateTime.Parse(xtlDataMonth);
                string xtlExpirationDate = dataMonth.AddDays(Convert.ToInt32(expireDays)).ToString("MM/dd/yyyy");

                if (!RunProcess(generateKeyXtlExe, $" \"{xtlOutput}\" {xtlExpirationDate} {xtlDataMonth}", out string output))
                {
                    logger.LogError($"Key XTL generation failed: {output}");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error generating Key XTL: {ex.Message}");
            return false;
        }
    }

    // Creates XTL ID file
    private bool CreateXtlIdFile()
    {
        try
        {
            logger.LogInformation("Creating XTL ID file");

            string dumpKeyXtlExe = $"{cycleToolsDirectory}\\DumpKeyXtl.exe";
            string dumpXtlHeaderExe = $"{cycleToolsDirectory}\\DumpXtlHeader.exe";
            string targetFile = $"{xtlOutput}\\xtl-id.txt";
            string twoDigitYear = buildNumber.Substring(0, 2);
            string year = $"20{twoDigitYear}";

            // Delete the file if it exists
            if (File.Exists(targetFile))
            {
                File.Delete(targetFile);
            }

            // Create the XTL ID file
            using (StreamWriter sw = new StreamWriter(targetFile))
            {
                sw.WriteLine($"Copyright © {year}, RAF Technology, Inc.");
                sw.WriteLine("");
                sw.WriteLine(cycle.StartsWith("O") ? "Cycle-O" : "Cycle-N2");
                sw.WriteLine("");
                sw.WriteLine("Xtl Key File : 0.xtl");

                // Dump key XTL
                logger.LogInformation("Dumping key XTL");
                if (!RunProcess(dumpKeyXtlExe, $" \"{xtlOutput}\"", out string output))
                {
                    logger.LogError($"Key XTL dump failed: {output}");
                    return false;
                }

                sw.WriteLine(output);
                sw.WriteLine("");
                sw.WriteLine("");

                // Dump XTL header
                logger.LogInformation("Dumping XTL header");
                if (!RunProcess(dumpXtlHeaderExe, $" \"{xtlOutput}\"", out output))
                {
                    logger.LogError($"XTL header dump failed: {output}");
                    return false;
                }

                sw.WriteLine(output);
                sw.WriteLine("");
                DateTime now = DateTime.Now;
                sw.WriteLine(now.ToString("ddd MM/dd/yyyy"));
                sw.WriteLine(now.ToString("hh:mm tt"));
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error creating XTL ID file: {ex.Message}");
            return false;
        }
    }

    // Extracts non-Zip4 data (DPV, LACS, SUITE)
    private bool ExtractNonZip4Data()
    {
        try
        {
            logger.LogInformation("Extracting non-Zip4 data (DPV, LACS, SUITE)");

            if (isMassData)
            {
                // For MASS data, extract from zip files

                // Extract DPV data
                string dpvZip = $"{dataSourcePath}\\dpvfl.zip";
                if (!File.Exists(dpvZip))
                {
                    logger.LogError($"DPV file not found: {dpvZip}");
                    return false;
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(dpvZip, dpvOutput);

                // Extract LACS data
                string lacsZip = $"{dataSourcePath}\\laclnk.zip";
                if (!File.Exists(lacsZip))
                {
                    logger.LogError($"LACS file not found: {lacsZip}");
                    return false;
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(lacsZip, lacsOutput);

                // Extract SUITE data
                string suiteZip = $"{dataSourcePath}\\stelnk.zip";
                if (!File.Exists(suiteZip))
                {
                    logger.LogError($"SUITE file not found: {suiteZip}");
                    return false;
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(suiteZip, suiteOutput);
            }
            else
            {
                // For regular data, extract from tar files

                // Extract DPV data
                string dpvTar = $"{dataSourcePath}\\dpvfl2.tar";
                if (!File.Exists(dpvTar))
                {
                    logger.LogError($"DPV file not found: {dpvTar}");
                    return false;
                }
                if (!ExtractTar(dpvTar, tempDir))
                {
                    logger.LogError($"Failed to extract DPV data from {dpvTar}");
                    return false;
                }

                // Extract LACS data
                string lacsTar = $"{dataSourcePath}\\laclnk2.tar";
                if (!File.Exists(lacsTar))
                {
                    logger.LogError($"LACS file not found: {lacsTar}");
                    return false;
                }
                if (!ExtractTar(lacsTar, tempDir))
                {
                    logger.LogError($"Failed to extract LACS data from {lacsTar}");
                    return false;
                }

                // Extract SUITE data
                string suiteTar = $"{dataSourcePath}\\stelnk2.tar";
                if (!File.Exists(suiteTar))
                {
                    logger.LogError($"SUITE file not found: {suiteTar}");
                    return false;
                }
                if (!ExtractTar(suiteTar, tempDir))
                {
                    logger.LogError($"Failed to extract SUITE data from {suiteTar}");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting non-Zip4 data: {ex.Message}");
            return false;
        }
    }

    // Processes dongle lists
    private bool ProcessDongleLists()
    {
        try
        {
            logger.LogInformation("Processing dongle lists");

            string dongleListsFolder = $"{tempDir}\\dongleLists";
            if (Directory.Exists(dongleListsFolder))
            {
                Directory.Delete(dongleListsFolder, true);
            }
            Directory.CreateDirectory(dongleListsFolder);

            // Copy dongle list files
            CopyFiles("./DongleLists", dongleListsFolder);

            string month = buildNumber.Substring(2, 2);
            string twoDigitYear = buildNumber.Substring(0, 2);
            string year = $"20{twoDigitYear}";
            string dateYYYYMMDD = $"{year}{month}01";

            // Process each dongle list file
            string[] dongleFiles = { "ArgosyDefault.txt", "SmSdkMonthly.txt" };
            if (cycle.StartsWith("O"))
            {
                // Add SS.txt for Cycle-O
                dongleFiles = new[] { "ArgosyDefault.txt", "SmSdkMonthly.txt", "SS.txt" };
            }

            foreach (string file in dongleFiles)
            {
                // Prepend date to dongle list
                string prependDate = $"Date={dateYYYYMMDD}{Environment.NewLine}";
                string newContents = prependDate + File.ReadAllText($"{dongleListsFolder}\\{file}");
                File.WriteAllText($"{dongleListsFolder}\\{file}", newContents);

                // Encrypt dongle list
                string encryptExe = $"{toolsDirectory}\\EncryptREP.exe";
                string encryptFormat = cycle.StartsWith("O") ? "elcs" : "lcs";

                if (!RunProcess(encryptExe, $" -x {encryptFormat} \"{dongleListsFolder}\\{file}\"", out string output))
                {
                    logger.LogError($"Failed to encrypt dongle list {file}: {output}");
                    return false;
                }
            }

            // Copy encrypted files to xtlOutput
            string adLcs = dongleFiles[0].Replace(".txt", cycle.StartsWith("O") ? ".elcs" : ".lcs");
            string smSdkLcs = dongleFiles[1].Replace(".txt", cycle.StartsWith("O") ? ".elcs" : ".lcs");

            File.Copy($"{dongleListsFolder}\\{adLcs}", $"{xtlOutput}\\ArgosyMonthly.{(cycle.StartsWith("O") ? "elcs" : "lcs")}");
            File.Copy($"{dongleListsFolder}\\{smSdkLcs}", $"{xtlOutput}\\{smSdkLcs}");

            if (cycle.StartsWith("O") && dongleFiles.Length > 2)
            {
                string ssLcs = dongleFiles[2].Replace(".txt", ".elcs");
                File.Copy($"{dongleListsFolder}\\{ssLcs}", $"{xtlOutput}\\{ssLcs}");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error processing dongle lists: {ex.Message}");
            return false;
        }
    }

    // Processes Suite data
    private bool ProcessSuiteData()
    {
        try
        {
            logger.LogInformation("Processing Suite data");

            string month = buildNumber.Substring(2, 2);
            string twoDigitYear = buildNumber.Substring(0, 2);
            string year = $"20{twoDigitYear}";
            string dataMonth = cycle.StartsWith("O") ? $"{month}/1/{year}" : $"{month}/15/{year}";
            string expirationDate = DateTime.Parse(dataMonth).AddDays(Convert.ToInt32(expireDays)).ToString("MM/dd/yyyy");

            // Run RafatizeDAT
            string datExe = $"{toolsDirectory}\\RafatizeDAT\\rafatizeSLK.exe";
            if (!RunProcess(datExe, $" \"{suiteOutput}\" {dataMonth} {expirationDate}", out string output))
            {
                logger.LogError($"Failed to run RafatizeDAT: {output}");
                return false;
            }

            // Move files
            File.Move($"{suiteOutput}\\SLK.RAF", $"{suiteOutput}\\SLK.dat");
            File.Move($"{suiteOutput}\\SLKSecNums.RAF", $"{suiteOutput}\\SLKSecNums.dat");

            // Run RafatizeSLK
            string slkExe = $"{toolsDirectory}\\RafatizeSLK\\rafatizeSLK.exe";
            if (!RunProcess(slkExe, $" \"{suiteOutput}\" {dataMonth} {expirationDate}", out output))
            {
                logger.LogError($"Failed to run RafatizeSLK: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error processing Suite data: {ex.Message}");
            return false;
        }
    }

    // Adds DPV header
    private bool AddDpvHeader()
    {
        try
        {
            logger.LogInformation("Adding DPV header");

            string addDpvHeaderExe = $"{cycleToolsDirectory}\\AddDpvHeader.exe";

            if (!RunProcess(addDpvHeaderExe, $" {dpvOutput}\\dph.hsa", out string output))
            {
                logger.LogError($"Failed to add DPV header: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error adding DPV header: {ex.Message}");
            return false;
        }
    }

    // Packages directory data (DPV, LACS, SUITE, and Zip4)
    private bool PackageDirectoryData()
    {
        try
        {
            logger.LogInformation("Packaging directory data");

            // Create output directory
            Directory.CreateDirectory(dataOutputPath);

            // Package LACS data
            logger.LogInformation("Packaging LACS data");
            string lacsZipPath = Path.Combine(dataOutputPath, "LACS.zip");
            if (!PackageLacsData(lacsZipPath))
            {
                return false;
            }

            // Package SUITE data
            logger.LogInformation("Packaging SUITE data");
            string suiteZipPath = Path.Combine(dataOutputPath, "SUITE.zip");
            if (!PackageSuiteData(suiteZipPath))
            {
                return false;
            }

            // Package ZIP4 data
            logger.LogInformation("Packaging ZIP4 data");
            string zip4ZipPath = Path.Combine(dataOutputPath, "Zip4.zip");
            if (!PackageZip4Data(zip4ZipPath))
            {
                return false;
            }

            // Package DPV data
            logger.LogInformation("Packaging DPV data");
            string dpvZipPath = Path.Combine(dataOutputPath, "DPV.zip");
            if (!PackageDpvData(dpvZipPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error packaging directory data: {ex.Message}");
            return false;
        }
    }

    // Packages LACS data
    private bool PackageLacsData(string lacsZipPath)
    {
        try
        {
            // Create list of files to package
            List<string> filesToPackage = new List<string>
            {
                $"{lacsOutput}\\llk.hs1",
                $"{lacsOutput}\\llk.hs2",
                $"{lacsOutput}\\llk.hs3",
                $"{lacsOutput}\\llk.hs4",
                $"{lacsOutput}\\llk.hs5",
                $"{lacsOutput}\\llk.hs6",
                $"{lacsOutput}\\llk.hsl",
                $"{lacsOutput}\\llk_hint.lst",
                $"{lacsOutput}\\llk_leftrite.txt",
                $"{lacsOutput}\\llk_strname.txt",
                $"{lacsOutput}\\llk_urbx.lst",
                $"{lacsOutput}\\llk_x11",
                $"{lacsOutput}\\llkhdr01.dat"
            };

            // Generate Live.txt file
            string liveFilePath = Path.Combine(tempDir, "Live.txt");
            using (StreamWriter sw = new StreamWriter(liveFilePath))
            {
                string twoDigitYear = buildNumber.Substring(0, 2);
                string year = $"20{twoDigitYear}";
                sw.WriteLine(year + buildNumber.Substring(2, 2));
                sw.WriteLine("Cycle-N");
            }
            filesToPackage.Add(liveFilePath);

            // Generate checksum file
            string checksumFilePath = Path.Combine(tempDir, "LACScrcs.txt");
            if (!GenerateChecksumFile(filesToPackage, checksumFilePath))
            {
                return false;
            }
            filesToPackage.Add(checksumFilePath);

            // Create zip file
            if (!CreateZipFile(filesToPackage, lacsZipPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error packaging LACS data: {ex.Message}");
            return false;
        }
    }

    // Packages SUITE data
    private bool PackageSuiteData(string suiteZipPath)
    {
        try
        {
            // Create list of files to package
            List<string> filesToPackage = new List<string>
            {
                $"{suiteOutput}\\lcd",
                Path.Combine(tempDir, "Live.txt"),
                $"{suiteOutput}\\SLK.dat",
                $"{suiteOutput}\\slkhdr01.dat",
                $"{suiteOutput}\\slknine.lst",
                $"{suiteOutput}\\slknoise.lst",
                $"{suiteOutput}\\slknormal.lst",
                $"{suiteOutput}\\SLKSecNums.dat"
            };

            // Generate checksum file
            string checksumFilePath = Path.Combine(tempDir, "SUITEcrcs.txt");
            if (!GenerateChecksumFile(filesToPackage, checksumFilePath))
            {
                return false;
            }
            filesToPackage.Add(checksumFilePath);

            // Create zip file
            if (!CreateZipFile(filesToPackage, suiteZipPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error packaging SUITE data: {ex.Message}");
            return false;
        }
    }

    // Packages ZIP4 data
    private bool PackageZip4Data(string zip4ZipPath)
    {
        try
        {
            // Create list of files to package
            List<string> filesToPackage = new List<string>
            {
                $"{xtlOutput}\\0.xtl",
                $"{xtlOutput}\\51.xtl",
                $"{xtlOutput}\\55.xtl",
                $"{xtlOutput}\\56.xtl",
                $"{xtlOutput}\\200.xtl",
                $"{xtlOutput}\\201.xtl",
                $"{xtlOutput}\\202.xtl",
                $"{xtlOutput}\\203.xtl",
                $"{xtlOutput}\\204.xtl",
                $"{xtlOutput}\\206.xtl",
                $"{xtlOutput}\\207.xtl",
                $"{xtlOutput}\\208.xtl",
                $"{xtlOutput}\\209.xtl",
                $"{xtlOutput}\\210.xtl",
                $"{xtlOutput}\\211.xtl",
                $"{xtlOutput}\\212.xtl",
                $"{xtlOutput}\\213.xtl"
            };

            // Add cycle-specific files
            if (cycle.StartsWith("O"))
            {
                filesToPackage.Add($"{xtlOutput}\\205.xtl");
                filesToPackage.Add($"{xtlOutput}\\ArgosyMonthly.elcs");
                filesToPackage.Add($"{xtlOutput}\\SmSdkMonthly.elcs");
                filesToPackage.Add($"{xtlOutput}\\SS.elcs");

                // Generate LiveO.txt file
                string liveOFilePath = Path.Combine(tempDir, "LiveO.txt");
                using (StreamWriter sw = new StreamWriter(liveOFilePath))
                {
                    sw.WriteLine("LIVE");
                }
                filesToPackage.Add(liveOFilePath);
            }
            else
            {
                filesToPackage.Add($"{xtlOutput}\\ArgosyMonthly.lcs");
                filesToPackage.Add($"{xtlOutput}\\SmSdkMonthly.lcs");

                // Generate LiveN2.txt file
                string liveN2FilePath = Path.Combine(tempDir, "LiveN2.txt");
                using (StreamWriter sw = new StreamWriter(liveN2FilePath))
                {
                    sw.WriteLine("LIVE");
                }
                filesToPackage.Add(liveN2FilePath);
            }

            // Add common files
            filesToPackage.Add($"{xtlOutput}\\xtlcrcs.txt");
            filesToPackage.Add($"{xtlOutput}\\xtl-id.txt");

            // Generate checksum file
            string checksumFilePath = Path.Combine(tempDir, "ZIP4crcs.txt");
            if (!GenerateChecksumFile(filesToPackage, checksumFilePath))
            {
                return false;
            }
            filesToPackage.Add(checksumFilePath);

            // Create zip file
            if (!CreateZipFile(filesToPackage, zip4ZipPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error packaging ZIP4 data: {ex.Message}");
            return false;
        }
    }

    // Packages DPV data
    private bool PackageDpvData(string dpvZipPath)
    {
        try
        {
            // Create list of files to package
            List<string> filesToPackage = new List<string>
            {
                $"{dpvOutput}\\dph.hsa",
                $"{dpvOutput}\\dph.hsc",
                $"{dpvOutput}\\dph.hsf",
                $"{dpvOutput}\\dvdhdr01.dat",
                $"{dpvOutput}\\lcd",
                Path.Combine(tempDir, "Live.txt"),
                $"{lacsOutput}\\llk.hsa"
            };

            // Add month.dat if not MASS data
            if (!isMassData)
            {
                filesToPackage.Add($"{dpvOutput}\\month.dat");
            }

            // Add optional files if they exist
            string[] optionalFiles = { "dph.hsp", "dph.hsr", "dph.hsx" };
            foreach (string file in optionalFiles)
            {
                string filePath = Path.Combine(dpvOutput, file);
                if (File.Exists(filePath))
                {
                    filesToPackage.Add(filePath);
                }
                else
                {
                    logger.LogWarning($"Optional DPV file not found: {file}");
                }
            }

            // Add additional files for Cycle-O
            if (cycle.StartsWith("O"))
            {
                string[] additionalFiles = { "dph.hsd", "dph.hsn", "dph.hst", "dph.hsu", "dph.hsv", "dph.hsy", "dph.hsz" };
                foreach (string file in additionalFiles)
                {
                    string filePath = Path.Combine(dpvOutput, file);
                    if (File.Exists(filePath))
                    {
                        filesToPackage.Add(filePath);
                    }
                }
            }

            // Generate checksum file
            string checksumFilePath = Path.Combine(tempDir, "DPVcrcs.txt");
            if (!GenerateChecksumFile(filesToPackage, checksumFilePath))
            {
                return false;
            }
            filesToPackage.Add(checksumFilePath);

            // Create zip file
            if (!CreateZipFile(filesToPackage, dpvZipPath))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error packaging DPV data: {ex.Message}");
            return false;
        }
    }

    // Runs APC tests
    private bool RunApcTests()
    {
        try
        {
            logger.LogInformation("Running APC tests");

            string testExe = $"{cycleToolsDirectory}\\TestXtls{(cycle.StartsWith("O") ? "O" : "N2")}.exe";
            string resultsFile = Path.Combine(dataOutputPath, "TestResults.txt");

            if (!RunProcess(testExe, $" \"{xtlOutput}\" \"{lacsOutput}\" \"{dpvOutput}\" \"{suiteOutput}\" \"{TestFile}\" \"{resultsFile}\"", out string output))
            {
                logger.LogError($"APC tests failed: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error running APC tests: {ex.Message}");
            return false;
        }
    }

    // Cleans up the database
    private bool CleanupDatabase()
    {
        try
        {
            logger.LogInformation("Cleaning up database");

            string cleanupDatabaseExe = $"{cycleToolsDirectory}\\CleanupDatabase.exe";
            string server = "127.0.0.1";
            string user = "sa";
            string pwd = "cry5taL";

            if (!RunProcess(cleanupDatabaseExe, $" {server} {user} {pwd}", out string output))
            {
                logger.LogError($"Database cleanup failed: {output}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error cleaning up database: {ex.Message}");
            return false;
        }
    }

    // Updates the database to mark the build as complete
    private async Task CheckBuildComplete(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        string actualCycle = cycle;
        if (cycle == "OtoN")
        {
            actualCycle = "N";
        }

        // Will be null if Crawler never made a record for it, watch out if running standalone
        Bundle bundle = context.UspsBundles().Where(x => dataYearMonth == x.DataYearMonth && $"Cycle-{actualCycle}" == x.Cycle).FirstOrDefault();

        if (bundle != null)
        {
            bundle.IsBuildComplete = true;
            bundle.CompileTimestamp = DateTime.Now;

            await context.SaveChangesAsync(stoppingToken);
            SendDbUpdate = true;
        }
        else
        {
            logger.LogWarning($"No bundle record found for {dataYearMonth} Cycle-{actualCycle}. Database not updated.");
        }
    }

    // Restarts the SQL Server
    private async Task RestartSqlServer()
    {
        logger.LogInformation("Restarting SQL Server");

        await Utils.StopService("MSSQLSERVER");
        await Utils.StartService("MSSQLSERVER");
    }

    // Extracts a tar file and then unzips a file within it
    private bool ExtractTarAndUnzip(string tarPath, string tempDir, string zipSubPath, string zipPassword, string zipOutputPath)
    {
        try
        {
            logger.LogInformation($"Extracting tar file: {tarPath}");

            if (!File.Exists(tarPath))
            {
                logger.LogError($"Tar file not found: {tarPath}");
                return false;
            }

            if (!ExtractTar(tarPath, tempDir))
            {
                logger.LogError($"Failed to extract tar file: {tarPath}");
                return false;
            }

            string zipPath = $"{tempDir}{zipSubPath}";
            if (!File.Exists(zipPath))
            {
                logger.LogError($"Zip file not found: {zipPath}");
                return false;
            }

            if (!Directory.Exists(zipOutputPath))
            {
                Directory.CreateDirectory(zipOutputPath);
            }

            logger.LogInformation($"Extracting zip file: {zipPath}");

            using (ICSharpCode.SharpZipLib.Zip.ZipFile zipFile = new ICSharpCode.SharpZipLib.Zip.ZipFile(zipPath))
            {
                if (!string.IsNullOrEmpty(zipPassword))
                {
                    zipFile.Password = zipPassword;
                }

                foreach (ZipEntry entry in zipFile)
                {
                    if (!entry.IsFile)
                    {
                        continue;
                    }

                    string entryFileName = entry.Name;
                    string fullZipToPath = Path.Combine(zipOutputPath, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);

                    if (!string.IsNullOrEmpty(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    byte[] buffer = new byte[4096];
                    using (Stream zipStream = zipFile.GetInputStream(entry))
                    using (FileStream streamWriter = File.Create(fullZipToPath))
                    {
                        int bytesRead;
                        while ((bytesRead = zipStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            streamWriter.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting tar and unzipping: {ex.Message}");
            return false;
        }
    }

    // Extracts a tar file
    private bool ExtractTar(string tarPath, string outputPath)
    {
        try
        {
            logger.LogInformation($"Extracting tar file: {tarPath}");

            if (!File.Exists(tarPath))
            {
                logger.LogError($"Tar file not found: {tarPath}");
                return false;
            }

            using (FileStream tarFile = File.OpenRead(tarPath))
#pragma warning disable CS0618 // Type or member is obsolete
            using (TarInputStream tarInputStream = new(tarFile))
            {
                TarEntry tarEntry;
                while ((tarEntry = tarInputStream.GetNextEntry()) != null)
                {
                    if (tarEntry.IsDirectory)
                    {
                        Directory.CreateDirectory(Path.Combine(outputPath, tarEntry.Name));
                    }
                    else
                    {
                        string outPath = Path.Combine(outputPath, tarEntry.Name);
                        string directoryName = Path.GetDirectoryName(outPath);

                        if (!string.IsNullOrEmpty(directoryName))
                        {
                            Directory.CreateDirectory(directoryName);
                        }

                        using (FileStream outStream = File.Create(outPath))
                        {
                            tarInputStream.CopyEntryContents(outStream);
                        }
                    }
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting tar file: {ex.Message}");
            return false;
        }
    }

    // Copies files from one directory to another
    private void CopyFiles(string sourceDir, string targetDir)
    {
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, true);
        }
    }

    // Runs a process and captures its output
    private bool RunProcess(string exe, string arguments, out string output)
    {
        output = string.Empty;

        try
        {
            logger.LogDebug($"Running process: {exe} {arguments}");

            using (Process process = new Process())
            {
                process.StartInfo.FileName = exe;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            output = ex.Message;
            logger.LogError($"Error running process {exe}: {ex.Message}");
            return false;
        }
    }

    // Generates a checksum file for a list of files
    private bool GenerateChecksumFile(List<string> files, string checksumFile)
    {
        try
        {
            logger.LogInformation($"Generating checksum file: {checksumFile}");

            using (StreamWriter sw = new StreamWriter(checksumFile))
            {
                foreach (string file in files)
                {
                    if (File.Exists(file))
                    {
                        long fileSize = new FileInfo(file).Length;
                        uint checksum = ComputeCrc32(file);
                        sw.WriteLine($"CHECKSUM_CRC32 {Path.GetFileName(file)} {fileSize} {checksum}");
                    }
                    else
                    {
                        logger.LogWarning($"File not found for checksum: {file}");
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error generating checksum file: {ex.Message}");
            return false;
        }
    }

    // Computes CRC32 checksum for a file
    private uint ComputeCrc32(string file)
    {
        using (FileStream fs = File.OpenRead(file))
        {
            byte[] buffer = new byte[4096];
            uint crc = 0xFFFFFFFF;
            int bytesRead;

            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    crc = ((crc >> 8) & 0x00FFFFFF) ^ Crc32Table[(crc ^ buffer[i]) & 0xFF];
                }
            }

            return ~crc;
        }
    }

    // CRC32 lookup table
    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    // Generates CRC32 lookup table
    private static uint[] GenerateCrc32Table()
    {
        uint[] table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) == 1)
                {
                    crc = (crc >> 1) ^ 0xEDB88320;
                }
                else
                {
                    crc >>= 1;
                }
            }
            table[i] = crc;
        }

        return table;
    }

    // Creates a zip file from a list of files
    private bool CreateZipFile(List<string> files, string zipPath)
    {
        try
        {
            logger.LogInformation($"Creating zip file: {zipPath}");

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (ZipOutputStream zipStream = new ZipOutputStream(File.Create(zipPath)))
            {
                zipStream.SetLevel(9); // Maximum compression

                byte[] buffer = new byte[4096];

                foreach (string file in files)
                {
                    if (File.Exists(file))
                    {
                        ZipEntry entry = new ZipEntry(Path.GetFileName(file));
                        entry.DateTime = DateTime.Now;
                        zipStream.PutNextEntry(entry);

                        using (FileStream fs = File.OpenRead(file))
                        {
                            int bytesRead;
                            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                zipStream.Write(buffer, 0, bytesRead);
                            }
                        }
                    }
                    else
                    {
                        logger.LogWarning($"File not found for zip: {file}");
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error creating zip file: {ex.Message}");
            return false;
        }
    }
}