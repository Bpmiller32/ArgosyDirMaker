using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Com.Raf.Utility;

using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

using log4net;

using Server;

namespace Com.Raf.Xtl.Build
{
    public class CycleN2Sha256XtlBuilder : XtlBuilder
    {
        private static ILog log = LogManager.GetLogger(typeof(CycleN2Sha256XtlBuilder));

        private const bool SKIP_BUILD = false;

        public string BuildNumber { get; protected set; } = string.Empty;
        public string Expiration { get; protected set; } = string.Empty;
        public string NetworkPassword { get; protected set; } = string.Empty;
        public string NetworkShare { get; protected set; } = string.Empty;
        public string NetworkUser { get; protected set; } = string.Empty;
        public string OutputFolder { get; protected set; } = string.Empty;
        public string Queries { get; protected set; } = string.Empty;
        public string SourceFolder { get; protected set; } = string.Empty;
        public string TestFile { get; protected set; } = string.Empty;

        private string ToolsDirectory = System.Environment.CurrentDirectory + "\\Tools";
        private string CycleToolsDirectory = System.Environment.CurrentDirectory + "\\Tools\\Cycle-N2-256";


        public CycleN2Sha256XtlBuilder(string buildNum, string sourceFolder, string outputFolder, string networkShare, string networkUser,
            string networkPassword, string expiration, string queries, string testFile)
        {
            BuildNumber = buildNum;
            NetworkPassword = networkPassword;
            NetworkShare = networkShare;
            NetworkUser = networkUser;
            Expiration = expiration;
            OutputFolder = outputFolder;
            Queries = queries;
            SourceFolder = sourceFolder;
            TestFile = testFile;
        }

        // NOTE: cassMass flag is ignored for N2
        public override void Build(bool cassMass, bool runTests)
        {
            try
            {
                Success = false;

                // string uncFolder = @"\\techred.raf.com\directories$";
                string mappedDrive = "Q:";

                if (NetworkDrive.IsDriveMapped(mappedDrive))
                {
                    NetworkDrive.DisconnectNetworkDrive(mappedDrive, true);
                }

                // int e = NetworkDrive.MapNetworkDrive(NetworkShare, mappedDrive, NetworkUser, NetworkPassword);

                // if (e == 0 && NetworkDrive.IsDriveMapped(mappedDrive))
                // {
                //     LogAndUpdateUser($"Successfully mapped {NetworkShare} to {mappedDrive}");
                // }
                // else
                // {
                //     LogAndUpdateUser($"Could not map {NetworkShare} to {mappedDrive} (error code {e})", Logging.LogLevel.Error);
                //     return;
                // }

                string sourceFolder = SourceFolder;

                // do a bunch of stuff
                LogAndUpdateUser("Setting up new build parameters...", Logging.LogLevel.Debug);
                string targetDir = "Xtl Database Creation Cycle-N2 SHA-256";
                string xtlOutput = $"C:\\{targetDir}\\Output\\Build {BuildNumber}";
                string month = BuildNumber.Substring(2, 2);
                string twoDigitYear = BuildNumber.Substring(0, 2);
                string year = $"20{twoDigitYear}";
                string xtlDataMonth = $"{month}/15/{year}";

                // check the expiration
                DateTime expirationDate = DateTime.Parse(xtlDataMonth);
                if (expirationDate.AddDays(Convert.ToDouble(Expiration)) < DateTime.Now)
                {
                    /*if (MessageBox.Show($"Expiration date ({expirationDate.AddDays(Convert.ToDouble(Expiration))}) will be in the past. Continue?", "Expiration Problem", MessageBoxButtons.OKCancel) != DialogResult.OK)
                    {
                        LogAndUpdateUser($"User declined to continue with expiration date in the past", Logging.LogLevel.Warn);
                        return;
                    }*/

                    // string msg = $"Expiration date ({expirationDate.AddDays(Convert.ToDouble(Expiration))}) will be in the past. Click 'Yes' to continue or 'No' to set the expiration to 105 days from today?";
                    // if (MessageBox.Show(msg, "Expiration Problem", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    // {
                    //     LogAndUpdateUser($"User opted to accept an expiration date in the past ({expirationDate.AddDays(Convert.ToDouble(Expiration))})", Logging.LogLevel.Warn);
                    // }
                    // else
                    // {
                    //     // LogAndUpdateUser($"User declined to continue with expiration date in the past", Logging.LogLevel.Warn);
                    //     expirationDate = DateTime.Now.AddDays(105);
                    //     LogAndUpdateUser($"User opted to override expiration date in the past ({expirationDate.AddDays(Convert.ToDouble(Expiration))})", Logging.LogLevel.Warn);
                    // }
                }

                if (Directory.Exists(xtlOutput))
                {
                    FileSystem.DeleteDirectory(xtlOutput);
                }

                Directory.CreateDirectory(xtlOutput);

                string tempDir = $"{Path.GetTempPath()}XtlBuilding";
                string lacsOutput = $"{tempDir}\\LACsLINK", dpvOutput = $"{tempDir}\\DPV_Full", suiteOutput = $"{tempDir}\\Suitelink";
                string extractedFolder = string.Empty;

                string mappedOutput = OutputFolder.Replace(NetworkShare, $"{mappedDrive}\\");
                if (Directory.Exists(mappedOutput))
                {
                    // if (MessageBox.Show("Existing build found, override?", "Override existing build", MessageBoxButtons.OKCancel) == DialogResult.OK)
                    // {
                    //     FileSystem.DeleteDirectory(mappedOutput);
                    // }
                    // else
                    // {
                    //     LogAndUpdateUser($"User declined to overwrite existing build {mappedOutput}", Logging.LogLevel.Warn);
                    //     return;
                    // }

                    FileSystem.DeleteDirectory(mappedOutput);
                }

                if (!SKIP_BUILD)
                {
                    LogAndUpdateUser("Cleaning up from any previous build(s)...", Logging.LogLevel.Debug);
                    string srcData = $"C:\\{targetDir}\\Source Data";
                    try
                    {
                        Array.ForEach(Directory.GetFiles(srcData), delegate (string path) { File.Delete(path); });
                    }
                    catch (Exception) { }

                    try
                    {
                        Array.ForEach(Directory.GetFiles(xtlOutput), delegate (string path) { File.Delete(path); });
                    }
                    catch (Exception) { }

                    if (!CleanupDatabase($"{CycleToolsDirectory}\\CleanupDatabase.exe"))
                    {
                        LogAndUpdateUser("Could not clean up database prior to starting build", Logging.LogLevel.Error);
                        return;
                    }

                    // Copying USPS source data to staging folder (was Stage 1)
                    if (!CopyUspsSourceData(sourceFolder, tempDir, out extractedFolder))
                    {
                        LogAndUpdateUser("Could not copy USPS source data (was Stage 1)", Logging.LogLevel.Error);
                        return;
                    }

                    LogAndUpdateUser("Successfully copied USPS source data (was Stage 1)!");

                    // Create the databases and import USPS data (was Stage 2)
                    string sqlXtlPath = $"C:\\{targetDir}\\Intermediate Database";
                    string sourceDataDir = $"{extractedFolder}";   // supposed to be blank???
                    if (!CreateDatabase($"{CycleToolsDirectory}\\DBCreate.exe", sqlXtlPath, $"{CycleToolsDirectory}\\ImportUsps.exe", sourceDataDir))
                    {
                        LogAndUpdateUser("Could not create database and import source data");
                        return;
                    }

                    LogAndUpdateUser("Successfully imported USPS data (was Stage 2)!");

                    // Generate USPS Xtls (was Stage 3)
                    string xtlSchemaDir = $"{CycleToolsDirectory}\\Schema";
                    if (!GenerateUspsXtls($"{CycleToolsDirectory}\\GenerateUspsXtls.exe", xtlSchemaDir, xtlOutput))
                    {
                        LogAndUpdateUser("Could not generate USPS XTLs (was Stage 3)", Logging.LogLevel.Error);
                        return;
                    }

                    LogAndUpdateUser("Successfully generated USPS XTLs (was Stage 3)!");

                    RestartSqlService();

                    // Generate Key Xtl (was Stage 4)
                    if (!GenerateKeyXtl($"{CycleToolsDirectory}\\GenerateKeyXtl.exe", xtlOutput, Expiration, xtlDataMonth))
                    {
                        LogAndUpdateUser("Could not generate key XTL (was Stage 4)", Logging.LogLevel.Error);
                        return;
                    }

                    LogAndUpdateUser("Successfully generated key XTL (was Stage 4)!");

                    // Create Xtl ID file (was Stage 5)
                    if (!CreateXtlIdFile($"{CycleToolsDirectory}\\DumpKeyXtl.exe", $"{CycleToolsDirectory}\\DumpXtlHeader.exe", $"{xtlOutput}\\xtl-id.txt", year, xtlOutput))
                    {
                        LogAndUpdateUser("Could not create XTL ID file (was Stage 5)", Logging.LogLevel.Error);
                        return;
                    }

                    LogAndUpdateUser("Successfully created XTL ID file (was Stage 5)!");

                    // extract DPV, LACS, SUITE data
                    LogAndUpdateUser("Extracting USPS (non-Zip4) data...");

                    string dpvTar = $"{sourceFolder}\\dpvfl2.tar";
                    if (!Compression.ExtractTar(dpvTar, tempDir))
                    {
                        LogAndUpdateUser($"Could not extract {dpvTar}", Logging.LogLevel.Error);
                        return;
                    }

                    string lacsTar = $"{sourceFolder}\\laclnk2.tar";
                    if (!Compression.ExtractTar(lacsTar, tempDir))
                    {
                        LogAndUpdateUser($"Could not extract {lacsTar}", Logging.LogLevel.Error);
                        return;
                    }

                    string suiteTar = $"{sourceFolder}\\stelnk2.tar";
                    if (!Compression.ExtractTar(suiteTar, tempDir))
                    {
                        LogAndUpdateUser($"Could not extract {suiteTar}", Logging.LogLevel.Error);
                        return;
                    }
                }

                Directory.CreateDirectory(mappedOutput);

                // check out the dongle lists
                string encryptExe = $"{ToolsDirectory}\\EncryptREP.exe";
                if (!ProcessDongleLists(tempDir, $"{year}{month}01", encryptExe, xtlOutput))
                {
                    LogAndUpdateUser($"Could not process dongle lists", Logging.LogLevel.Error);
                    return;
                }

                // process the Suite data
                if (!ProcessSuiteData($"{tempDir}\\SUITELink", xtlDataMonth,
                    DateTime.Parse(xtlDataMonth).AddDays(Convert.ToInt32(Expiration)).ToString("MM/dd/yyyy")))
                {
                    LogAndUpdateUser($"Could not process raw DPV data in {tempDir}", Logging.LogLevel.Error);
                    return;
                }

                if (runTests)
                {
                    string testExe = $"{CycleToolsDirectory}\\TestXtlsN2.exe";
                    if (!RunApcTests(testExe, xtlOutput, lacsOutput, dpvOutput, suiteOutput, TestFile, $"{mappedOutput}\\TestResults.txt"))
                    {
                        LogAndUpdateUser("Could not run APC tests (replaced Stage 6)", Logging.LogLevel.Error);
                        return;
                    }

                    LogAndUpdateUser("Successfully ran APC tests (replaced Stage 6)!");
                }
                else
                {
                    LogAndUpdateUser("Skipped APC tests (was Stage 6)");
                }

                // Clean up the database (was Stage 7)
                if (!CleanupDatabase($"{CycleToolsDirectory}\\CleanupDatabase.exe"))
                {
                    LogAndUpdateUser("Could not clean up database (was Stage 7)", Logging.LogLevel.Error);
                    return;
                }

                LogAndUpdateUser("Updating DPV data...");
                if (!AddDpvHeader($"{CycleToolsDirectory}\\AddDpvHeader.exe", dpvOutput))
                {
                    LogAndUpdateUser($"Could not add DPV header ({dpvOutput})", Logging.LogLevel.Error);
                    return;
                }

                // package all data (DPV, LACS, SUITE, and Zip4)
                if (!PackageDirectoryData(mappedOutput, lacsOutput, suiteOutput, xtlOutput, dpvOutput))
                {
                    LogAndUpdateUser("Could not package directory data!", Logging.LogLevel.Error);
                    return;
                }

                NetworkDrive.DisconnectNetworkDrive(mappedDrive, true);

                // once everything is done, check set "Success"
                LogAndUpdateUser($"Successfully built XTLs to {OutputFolder}");
                Success = true;
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"{ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }
        }

        private bool CopyUspsSourceData(string sourceFolder, string tempDir, out string extractedData)
        {
            if (Directory.Exists(tempDir))
            {
                FileSystem.DeleteDirectory(tempDir);
            }

            Directory.CreateDirectory(tempDir);

            extractedData = $"{tempDir}\\USPS";
            if (Directory.Exists(extractedData))
            {
                FileSystem.DeleteDirectory(extractedData);
            }

            Directory.CreateDirectory(extractedData);

            LogAndUpdateUser("Copying USPS source data to staging folder...");

            LogAndUpdateUser("Extracting AIS ZIPMOVE...");
            string zipMoveTar = $"{sourceFolder}\\zipmovenatl.tar";
            if (!Compression.ExtactTarAndUnzip(zipMoveTar, tempDir, $"\\zipmovenatl\\zipmove\\zipmove.zip", "/MP7IOPE0ZV", $"{extractedData}\\AIS ZIP4 NATIONAL\\zipmove"))
            {
                LogAndUpdateUser("Could not extract AIS ZIPMOVE data", Logging.LogLevel.Error);
                return false;
            }

            LogAndUpdateUser("Extracting AIS ZIP4+NATIONAL...");
            string zip4Tar = $"{sourceFolder}\\zip4natl.tar";
            if (!Compression.ExtactTarAndUnzip(zip4Tar, tempDir, $"\\epf-zip4natl\\zip4\\zip4.zip", "/ZI1APLSZP4", $"{extractedData}\\AIS ZIP4 NATIONAL\\zip4"))
            {
                LogAndUpdateUser("Could not extract AIS ZIP4+NATIONAL data", Logging.LogLevel.Error);
                return false;
            }

            LogAndUpdateUser("Extracting AIS CTYSTATE...");
            string cityStateZip = $"{tempDir}\\epf-zip4natl\\ctystate\\ctystate.zip";
            if (!File.Exists(cityStateZip))
            {
                LogAndUpdateUser($"Could not find CityState archive {cityStateZip}", Logging.LogLevel.Error);
                return false;
            }

            string cityStateSource = $"{extractedData}\\AIS ZIP4 NATIONAL\\ctystate";
            if (!Directory.Exists(cityStateSource))
            {
                Directory.CreateDirectory(cityStateSource);
            }

            if (!Compression.ExtractZip(cityStateZip, "/TI0ZST9ACY", cityStateSource))
            {
                LogAndUpdateUser($"Could not unzip {cityStateZip} to {cityStateSource}");
                return false;
            }

            return true;
        }

        private bool AddDpvHeader(string exe, string dpvDir)
        {
            try
            {
                LogAndUpdateUser("Adding DPV header...");
                string output;
                if (Executable.RunProcess(exe, $" {dpvDir}\\dph.hsa", out output))
                {
                    LogAndUpdateUser("Successfully added DPV header");
                    return true;
                }
                else
                {
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"AddDpvHeader Exception: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }


        private bool CleanupDatabase(string exe, string server = "127.0.0.1", string user = "sa", string pwd = "cry5taL")
        {
            LogAndUpdateUser($"Cleaning up database on {server}...");

            try
            {
                string output;
                if (Executable.RunProcess(exe, $" {server} {user} {pwd}", out output))
                {
                    return true;
                }
                else
                {
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"CleanupDatabase: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private bool CreateDatabase(string dbCreateExe, string sqlXtlPath, string importUspsExe, string sourceDataDir, string server = "127.0.0.1", string user = "sa", string pwd = "cry5taL")
        {
            try
            {
                RestartSqlService();

                // create the database
                LogAndUpdateUser("Creating database...");

                string output;
                if (Executable.RunProcess(dbCreateExe, $" {server} {user} {pwd} \"{sqlXtlPath}\"", out output))
                {
                    // import the USPS data
                    LogAndUpdateUser("Importing USPS data...");

                    if (Executable.RunProcess(importUspsExe, $" \"{sourceDataDir}\" {server} {user} {pwd}", out output))
                    {
                        return true;
                    }
                    else
                    {
                        LogAndUpdateUser(output, Logging.LogLevel.Error);
                    }
                }
                else
                {
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"CreateDatabase: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private bool CreateXtlIdFile(string dumpKeyXtlExe, string dumpXtlHeaderExe, string targetFile, string xtlDataYear, string xtlOuputDir)
        {
            try
            {
                LogAndUpdateUser("Creating XTL ID file...");

                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }

                using (StreamWriter sw = new StreamWriter(targetFile))
                {
                    sw.WriteLine($"Copyright © {xtlDataYear}, RAF Technology, Inc.");
                    sw.WriteLine("");
                    sw.WriteLine($"Cycle-N2");
                    sw.WriteLine("");
                    sw.WriteLine("Xtl Key File : 0.xtl");

                    LogAndUpdateUser("Dumping key XTL...");

                    string output;
                    if (Executable.RunProcess(dumpKeyXtlExe, $" \"{xtlOuputDir}\"", out output))
                    {
                        sw.WriteLine(output);
                        sw.WriteLine("");
                        sw.WriteLine("");

                        LogAndUpdateUser("Dumping XTL header...");

                        if (Executable.RunProcess(dumpXtlHeaderExe, $" \"{xtlOuputDir}\"", out output))
                        {
                            sw.WriteLine(output);
                            sw.WriteLine("");
                            DateTime now = DateTime.Now;
                            sw.WriteLine(now.ToString("ddd MM/dd/yyyy"));
                            sw.WriteLine(now.ToString("hh:mm tt"));
                            return true;
                        }
                        else
                        {
                            LogAndUpdateUser(output, Logging.LogLevel.Error);
                        }
                    }
                    else
                    {
                        LogAndUpdateUser(output, Logging.LogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"GenerateUspsXtls: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private bool GenerateKeyXtl(string exe, string xtlOutputDir, string expirationDays, string xtlDataMonth)
        {
            try
            {
                LogAndUpdateUser("Generating key XTL...");

                string output;
                // NOTE: Parameter '1' is the XTL "format" flag
                // if(Executable.RunProcess(exe, $" 1 \"{xtlOutputDir}\" {expirationDays} {xtlDataMonth} {xtlDataMonth} {Queries}", out output))    \\ old command-line parameters
                DateTime dataMonth = DateTime.Parse(xtlDataMonth);
                string xtlExpirationDate = dataMonth.AddDays(Convert.ToInt32(expirationDays)).ToString("MM/dd/yyyy");
                if (Executable.RunProcess(exe, $" \"{xtlOutputDir}\" {xtlExpirationDate} {xtlDataMonth}", out output))
                {
                    return true;
                }
                else
                {
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"GenerateKeyXtl: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        private bool GenerateUspsXtls(string exe, string xtlSchemaDir, string xtlOutputDir, string server = "127.0.0.1", string user = "sa", string pwd = "cry5taL")
        {
            try
            {
                RestartSqlService();

                LogAndUpdateUser("Generating USPS XTLs...");

                string output;
                string parameters = $" {server} {user} {pwd} {BuildNumber} \"{xtlSchemaDir}\" \"{xtlOutputDir}\"";
                if (Executable.RunProcess(exe, parameters, out output))
                {
                    return true;
                }
                else
                {
                    LogAndUpdateUser($"Parameters: {parameters}", Logging.LogLevel.Error);
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"GenerateUspsXtls: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private void LogAndUpdateUser(string msg, Logging.LogLevel logLevel = Logging.LogLevel.Info)
        {
            switch (logLevel)
            {
                case Logging.LogLevel.Debug:
                    log.Debug(msg);
                    break;
                case Logging.LogLevel.Info:
                    log.Info(msg);
                    break;
                case Logging.LogLevel.Warn:
                    log.Warn(msg);
                    break;
                case Logging.LogLevel.Error:
                    log.Error(msg);
                    break;
            }

            UpdateStatus(msg, logLevel);
        }

        private bool PackageDirectoryData(string mappedOutput, string lacsOutput, string suiteOutput, string xtlOutput, string dpvOutput)
        {
            try
            {
                LogAndUpdateUser("Packaging directory data...");

                string tempFolder = Path.GetTempPath();

                /*string disk1 = $"{mappedOutput}\\Disk1";
                Directory.CreateDirectory(disk1);

                string disk2 = $"{mappedOutput}\\Disk2";
                Directory.CreateDirectory(disk2);*/

                // TS says they don't need them separated anymore
                string disk1 = mappedOutput, disk2 = mappedOutput;

                LogAndUpdateUser("Packaging LACS data...");

                List<string> filesToPackage = new List<string>() { $"{lacsOutput}\\llk.hs1", $"{lacsOutput}\\llk.hs2", $"{lacsOutput}\\llk.hs3",
                    $"{lacsOutput}\\llk.hs4", $"{lacsOutput}\\llk.hs5", $"{lacsOutput}\\llk.hs6", $"{lacsOutput}\\llk.hsl",
                    $"{lacsOutput}\\llk_hint.lst", $"{lacsOutput}\\llk_leftrite.txt", $"{lacsOutput}\\llk_strname.txt",
                    $"{lacsOutput}\\llk_urbx.lst", $"{lacsOutput}\\llk_x11", $"{lacsOutput}\\llkhdr01.dat"};

                // generate the Live.txt file
                string liveFile = tempFolder + "\\Live.txt";
                using (StreamWriter sw = new StreamWriter(liveFile))
                {
                    sw.WriteLine($"20{BuildNumber.Substring(0, 4)}");
                    sw.WriteLine("Cycle-N");
                }

                filesToPackage.Add(liveFile);

                List<string> sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // generate the checksum file
                string checksumFile = $"{tempFolder}\\LACScrcs.txt";
                if (!Crc32.GenerateChecksumFile(sortedFilesToPackage.ToArray(), checksumFile))
                {
                    LogAndUpdateUser("Could not generate LACS checksum file", Logging.LogLevel.Error);
                    return false;
                }

                filesToPackage.Add(checksumFile);

                // sort the files (alphabetically)
                sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // package the files...
                string lacsZipLocal = $"{tempFolder}\\LACS.zip";
                string lacsZip = $"{disk1}\\LACS.zip";
                if (Compression.PackageDirectoryFiles(sortedFilesToPackage.ToArray(), lacsZipLocal))
                {
                    File.Copy(lacsZipLocal, lacsZip);
                    LogAndUpdateUser("Packaged LACS data");
                }
                else
                {
                    LogAndUpdateUser($"Could not package LACS data files in {lacsZip}", Logging.LogLevel.Error);
                    return false;
                }

                LogAndUpdateUser("Packaging SUITE data...");

                filesToPackage = new List<string>() { $"{suiteOutput}\\lcd", liveFile, $"{suiteOutput}\\SLK.dat", $"{suiteOutput}\\SLK.RAF",
                    $"{suiteOutput}\\slkhdr01.dat", $"{suiteOutput}\\slknine.lst", $"{suiteOutput}\\slknoise.lst", $"{suiteOutput}\\slknormal.lst",
                    $"{suiteOutput}\\SLKSecNums.dat", $"{suiteOutput}\\SLKSecNums.RAF"};

                // generate the checksum file
                checksumFile = tempFolder + "\\SUITEcrcs.txt";
                if (!Crc32.GenerateChecksumFile(filesToPackage.ToArray(), checksumFile))
                {
                    LogAndUpdateUser("Could not generate SUITE checksum file", Logging.LogLevel.Error);
                    return false;
                }

                filesToPackage.Add(checksumFile);

                // sort the files (alphabetically)
                sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // package the files...
                string suiteZipLocal = $"{tempFolder}\\SUITE.zip";
                string suiteZip = $"{disk1}\\SUITE.zip";
                if (Compression.PackageDirectoryFiles(sortedFilesToPackage.ToArray(), suiteZipLocal))
                {
                    File.Copy(suiteZipLocal, suiteZip);
                    LogAndUpdateUser("Packaged SUITE data");
                }
                else
                {
                    LogAndUpdateUser($"Could not package SUITE data files in {suiteZip}", Logging.LogLevel.Error);
                    return false;
                }

                LogAndUpdateUser("Packaging XTL (Zip4) data...");

                filesToPackage = new List<string>() { $"{xtlOutput}\\0.xtl", $"{xtlOutput}\\51.xtl", $"{xtlOutput}\\55.xtl",
                    $"{xtlOutput}\\56.xtl", $"{xtlOutput}\\200.xtl", $"{xtlOutput}\\201.xtl", $"{xtlOutput}\\202.xtl",
                    $"{xtlOutput}\\203.xtl", $"{xtlOutput}\\204.xtl", $"{xtlOutput}\\206.xtl", $"{xtlOutput}\\207.xtl",
                    $"{xtlOutput}\\208.xtl", $"{xtlOutput}\\209.xtl", $"{xtlOutput}\\210.xtl", $"{xtlOutput}\\211.xtl",
                    $"{xtlOutput}\\212.xtl", $"{xtlOutput}\\213.xtl", $"{xtlOutput}\\ArgosyMonthly.lcs",
                    $"{xtlOutput}\\SmSdkMonthly.lcs", $"{xtlOutput}\\xtlcrcs.txt", $"{xtlOutput}\\xtl-id.txt"};

                // generate the LiveN2.txt file
                string liveN2File = tempFolder + "\\LiveN2.txt";
                using (StreamWriter sw = new StreamWriter(liveN2File))
                {
                    sw.WriteLine("LIVE");
                }

                filesToPackage.Add(liveN2File);

                sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // generate the checksum file
                checksumFile = tempFolder + "\\ZIP4crcs.txt";
                if (!Crc32.GenerateChecksumFile(sortedFilesToPackage.ToArray(), checksumFile))
                {
                    LogAndUpdateUser("Could not generate ZIP4 checksum file", Logging.LogLevel.Error);
                    return false;
                }

                filesToPackage.Add(checksumFile);

                // sort the files (alphabetically)
                sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // package the files...
                string zip4ZipLocal = $"{tempFolder}\\Zip4.zip";
                string zip4Zip = $"{disk1}\\Zip4.zip";
                if (Compression.PackageDirectoryFiles(sortedFilesToPackage.ToArray(), zip4ZipLocal))
                {
                    File.Copy(zip4ZipLocal, zip4Zip);
                    LogAndUpdateUser("Packaged ZIP4 data");
                }
                else
                {
                    LogAndUpdateUser($"Could not package ZIP4 data files in {zip4Zip}", Logging.LogLevel.Error);
                    return false;
                }

                LogAndUpdateUser("Packaging DPV data...");

                filesToPackage = new List<string>() { $"{dpvOutput}\\dph.hsa", $"{dpvOutput}\\dph.hsc", $"{dpvOutput}\\dph.hsf",
                    $"{dpvOutput}\\dvdhdr01.dat", $"{dpvOutput}\\lcd", liveFile, $"{lacsOutput}\\llk.hsa", $"{dpvOutput}\\month.dat"};

                // generate the checksum file
                checksumFile = tempFolder + "\\DPVcrcs.txt";
                if (!Crc32.GenerateChecksumFile(filesToPackage.ToArray(), checksumFile))
                {
                    LogAndUpdateUser("Could not generate DPV checksum file", Logging.LogLevel.Error);
                    return false;
                }

                filesToPackage.Add(checksumFile);

                // sort the files (alphabetically)
                sortedFilesToPackage = filesToPackage.OrderBy(s => Path.GetFileName(s)).ToList();

                // package the files...
                string dpvZipLocal = $"{tempFolder}\\DPV.zip";
                string dpvZip = $"{disk2}\\DPV.zip";
                if (Compression.PackageDirectoryFiles(sortedFilesToPackage.ToArray(), dpvZipLocal))
                {
                    File.Copy(dpvZipLocal, dpvZip);
                    LogAndUpdateUser("Packaged DPV data");
                }
                else
                {
                    LogAndUpdateUser($"Could not package DPV data files in {dpvZip}", Logging.LogLevel.Error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"PackageDirectoryData: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private bool ProcessDongleLists(string tempFolder, string dateYYYYMMDD, string encryptExe, string xtlOutput)
        {
            try
            {
                LogAndUpdateUser("Processing dongle lists...");

                string dongleListsFolder = $"{tempFolder}\\dongleLists";
                if (Directory.Exists(dongleListsFolder))
                {
                    FileSystem.DeleteDirectory(dongleListsFolder);
                }

                Directory.CreateDirectory(dongleListsFolder);
                Utils.CopyFiles("./DongleLists", dongleListsFolder);

                // https://scm.raf.com/repo/ArgosyPost/trunk/Code/data/ (ArgosyDefault.txt)
                // https://scm.raf.com/repo/APC/trunk/Code/SMLicense/ (SmSdkMonthly.txt)
                // if (SourceControl.CheckoutFolder("https://scm.raf.com/svn/repo/ArgosyPost/trunk/Code/data/", NetworkUser, NetworkPassword, adFolder) &&
                //     SourceControl.CheckoutFolder("https://scm.raf.com/svn/repo/APC/trunk/Code/SMLicense/", NetworkUser, NetworkPassword, smSdkFolder))
                if (true)
                {
                    string adText = $"ArgosyDefault.txt";
                    string smSdkText = $"SmSdkMonthly.txt";

                    // prepend the month to both files
                    // Line 1: Date=YYYYMMDD
                    // Line 2: Dongles:
                    // Line 3: First dongle ID

                    // ArgosyDefault.txt
                    string prependDate = $"Date={dateYYYYMMDD}{System.Environment.NewLine}";
                    string newContents = prependDate + File.ReadAllText($"{dongleListsFolder}\\{adText}");
                    File.WriteAllText($"{dongleListsFolder}\\{adText}", newContents);

                    // SmSdkMonthly.txt
                    newContents = prependDate + File.ReadAllText($"{dongleListsFolder}\\{smSdkText}");
                    File.WriteAllText($"{dongleListsFolder}\\{smSdkText}", newContents);

                    // encrypt both files (EncryptREP.exe)
                    string output;
                    if (Executable.RunProcess(encryptExe, $" -x lcs \"{dongleListsFolder}\\{adText}\"", out output) &&
                        Executable.RunProcess(encryptExe, $" -x lcs \"{dongleListsFolder}\\{smSdkText}\"", out output))
                    {
                        // copy both .lcs files to xtlOutput
                        string adLcs = adText.Replace(".txt", ".lcs");
                        string smSdkLcs = smSdkText.Replace(".txt", ".lcs");
                        File.Copy($"{dongleListsFolder}\\{adLcs}", $"{xtlOutput}\\ArgosyMonthly.lcs");
                        File.Copy($"{dongleListsFolder}\\{smSdkLcs}", $"{xtlOutput}\\{smSdkLcs}");
                        return true;
                    }
                    else
                    {
                        LogAndUpdateUser("Could not encrypt dongle lists", Logging.LogLevel.Error);
                    }
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"ProcessDongleLists: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private bool ProcessSuiteData(string suiteDir, string dataMonth, string expirationDate)
        {
            try
            {
                LogAndUpdateUser("Processing SUITE data...");

                string output;
                string datExe = ToolsDirectory + "\\RafatizeDAT\\rafatizeSLK.exe";
                if (Executable.RunProcess(datExe, $" \"{suiteDir}\" {dataMonth} {expirationDate}", out output))
                {
                    File.Move($"{suiteDir}\\SLK.RAF", $"{suiteDir}\\SLK.dat");
                    File.Move($"{suiteDir}\\SLKSecNums.RAF", $"{suiteDir}\\SLKSecNums.dat");
                    string slkExe = ToolsDirectory + "\\RafatizeSLK\\rafatizeSLK.exe";
                    if (Executable.RunProcess(slkExe, $" \"{suiteDir}\" {dataMonth} {expirationDate}", out output))
                    {
                        return true;
                    }
                }

                log.Error(output);
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"ProcessDpvData: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }

        private void RestartSqlService()
        {
            LogAndUpdateUser("Restarting SQL Server service...", Logging.LogLevel.Debug);
            if (!Service.RestartService("MSSQLSERVER", 60000))
            {
                LogAndUpdateUser("Could not restart SQL Server service!", Logging.LogLevel.Error);
            }
        }

        private bool RunApcTests(string testExe, string xtlDir, string lacsDir, string dpvDir, string suiteDir, string testFile, string resultsFile)
        {
            try
            {
                LogAndUpdateUser("Running APC tests...");
                string output;
                if (Executable.RunProcess(testExe, $" \"{xtlDir}\" \"{lacsDir}\" \"{dpvDir}\" \"{suiteDir}\" \"{testFile}\" \"{resultsFile}\"", out output))
                {
                    return true;
                }
                else
                {
                    LogAndUpdateUser(output, Logging.LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                LogAndUpdateUser($"RunApcTests: {ex.Message} - {ex.StackTrace}", Logging.LogLevel.Error);
            }

            return false;
        }
    }
}
