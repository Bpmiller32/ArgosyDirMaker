using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Force.Crc32;

using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;

using log4net;

using SharpSvn;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Com.Raf.Utility
{
    public class Crc32
    {
        private static ILog log = LogManager.GetLogger(typeof(Crc32));

        public static string LastError { get; protected set; } = string.Empty;

        public static bool GenerateChecksumFile(string[] files, string checksumFile)
        {
            LastError = string.Empty;
            try
            {
                // CHECKSUM_CRC32 {file name} {bytes} {CRC value}
                // E.g. CHECKSUM_CRC32 Live.txt 19 3683194789
                using (StreamWriter sw = new StreamWriter(checksumFile))
                {
                    foreach (string file in files)
                    {
                        if (File.Exists(file))
                        {
                            sw.WriteLine($"CHECKSUM_CRC32 {Path.GetFileName(file)} {new FileInfo(file).Length} {ComputeCrc32(file)}");
                        }
                        else
                        {
                            log.Error($"Cannot generate checksume for {file}: file does not exist");
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = $"GenerateChecksumFile Exception: {ex.Message} - {ex.StackTrace}";
            }

            return false;
        }

        private static string ComputeCrc32(string file)
        {
            byte[] fileBytes = File.ReadAllBytes(file);
            return Convert.ToString(Force.Crc32.Crc32Algorithm.Compute(fileBytes, 0, fileBytes.Length));
        }
    }

    public class Compression
    {
        private static ILog log = LogManager.GetLogger(typeof(Compression));

        public static string LastError { get; protected set; } = string.Empty;

        public static bool CreateTar(string sourceFolder, string destinationFile, bool recurse, string stripLeadingFolders = "")
        {
            try
            {
                using (var tarFile = File.OpenWrite(destinationFile))
                {
                    using (TarArchive tarArchive = TarArchive.CreateOutputTarArchive(tarFile))
                    {
                        AddDirectoryFilesToTar(tarArchive, sourceFolder, recurse, stripLeadingFolders);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                log.Error($"CreateTar Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        private static void AddDirectoryFilesToTar(TarArchive tarArchive, string sourceFolder, bool recurse, string stripLeadingFolders)
        {
            if (recurse)
            {
                foreach (string directory in Directory.GetDirectories(sourceFolder))
                {
                    AddDirectoryFilesToTar(tarArchive, directory, recurse, stripLeadingFolders);
                }
            }

            foreach (FileInfo file in new DirectoryInfo(sourceFolder).GetFiles())
            {
                TarEntry entry = TarEntry.CreateEntryFromFile(file.FullName);
                if (stripLeadingFolders.Length == 0)
                {
                    entry.Name = file.Name;
                }
                else
                {
                    entry.Name = file.FullName.Replace(stripLeadingFolders, "");
                }

                tarArchive.WriteEntry(entry, false);
            }
        }

        public static bool ExtractTar(string sourceFile, string destinationFolder)
        {
            try
            {
                using (var tarFile = File.OpenRead(sourceFile))
                {
                    using (var tarArchive = TarArchive.CreateInputTarArchive(tarFile))
                    {
                        tarArchive.ExtractContents(destinationFolder);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                log.Error($"ExtractTar Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        public static bool ExtactTarAndUnzip(string tarPath, string tempDir, string zipSubPath, string zipPassword, string zipOutputPath)
        {
            LastError = string.Empty;

            try
            {
                if (!File.Exists(tarPath))
                {
                    // UpdateStatus($"Could not find TAR file ({tarPath})");
                    LastError = $"Could not find TAR file ({tarPath})";
                    return false;
                }

                if (!ExtractTar(tarPath, tempDir))
                {
                    // UpdateStatus($"Could not extract TAR file ({tarPath}) to {tempDir}");
                    LastError = $"Could not extract TAR file ({tarPath}) to {tempDir}";
                    return false;
                }

                string zipPath = $"{tempDir}{zipSubPath}";
                if (!File.Exists(zipPath))
                {
                    // UpdateStatus($"Could not find extracted ZIP file {zipPath}");
                    LastError = $"Could not find extracted ZIP file {zipPath}";
                    return false;
                }

                if (!Directory.Exists(zipOutputPath))
                {
                    Directory.CreateDirectory(zipOutputPath);
                }

                if (!ExtractZip(zipPath, zipPassword, zipOutputPath))
                {
                    // UpdateStatus($"Could not unzip {zipPath} to {zipOutputPath}");
                    LastError = $"Could not unzip {zipPath} to {zipOutputPath}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // UpdateStatus($"ExtactTarAndUnzip Exception: {ex.Message} - {ex.StackTrace}");
                LastError = $"ExtactTarAndUnzip Exception: {ex.Message} - {ex.StackTrace}";
            }

            return false;
        }

        public static bool ExtractZip(string FileZipPath, string password, string OutputFolder)
        {
            ZipFile file = null;
            try
            {
                FileStream fs = File.OpenRead(FileZipPath);
                file = new ZipFile(fs);

                if (!String.IsNullOrEmpty(password))
                {
                    // AES encrypted entries are handled automatically
                    file.Password = password;
                }

                foreach (ZipEntry zipEntry in file)
                {
                    if (!zipEntry.IsFile)
                    {
                        // Ignore directories
                        continue;
                    }

                    String entryFileName = zipEntry.Name;
                    // to remove the folder from the entry:- entryFileName = Path.GetFileName(entryFileName);
                    // Optionally match entrynames against a selection list here to skip as desired.
                    // The unpacked length is available in the zipEntry.Size property.

                    // 4K is optimum
                    byte[] buffer = new byte[4096];
                    Stream zipStream = file.GetInputStream(zipEntry);

                    // Manipulate the output filename here as desired.
                    String fullZipToPath = Path.Combine(OutputFolder, entryFileName);
                    string directoryName = Path.GetDirectoryName(fullZipToPath);

                    if (directoryName.Length > 0)
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
                    // of the file, but does not waste memory.
                    // The "using" will close the stream even if an exception occurs.
                    using (FileStream streamWriter = File.Create(fullZipToPath))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LastError = $"ExtactZip Exception: {ex.Message} - {ex.StackTrace}";
            }
            finally
            {
                if (file != null)
                {
                    file.IsStreamOwner = true; // Makes close also shut the underlying stream
                    file.Close(); // Ensure we release resources
                }
            }

            return false;
        }

        public static bool PackageDirectoryFiles(string[] files, string zipPath)
        {
            try
            {
                using (ZipFile zipFile = ZipFile.Create(zipPath))
                {
                    zipFile.BeginUpdate();
                    foreach (string file in files)
                    {
                        zipFile.Add(file, Path.GetFileName(file));
                    }

                    zipFile.CommitUpdate();
                }

                return File.Exists(zipPath);
            }
            catch (Exception ex)
            {
                log.Error($"PackageDirectoryFiles Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        public static bool ZipFiles(string[] files, string zipPath, string password = "", bool fullPath = true)
        {
            try
            {
                using (ZipFile zipFile = ZipFile.Create(zipPath))
                {
                    zipFile.BeginUpdate();
                    foreach (string file in files)
                    {
                        if (fullPath)
                        {
                            zipFile.Add(file);
                        }
                        else
                        {
                            zipFile.Add(file, Path.GetFileName(file));
                        }
                    }

                    if (password.Length > 0)
                    {
                        zipFile.Password = password;
                        // zipFile.UseZip64 = UseZip64.On;
                    }

                    zipFile.CommitUpdate();
                }

                return File.Exists(zipPath);
            }
            catch (Exception ex)
            {
                log.Error($"ZipFiles Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }
    }

    public class Executable
    {
        private static ILog log = LogManager.GetLogger(typeof(Executable));

        public static bool RunProcess(string exe, string arguments)
        {
            try
            {
                log.Debug($"{exe} {arguments}");
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                // p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = arguments;
                p.Start();
                // output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    return true;
                }
                else
                {
                    log.Error($"Process failed (error code {p.ExitCode}) and console output is unavailable. Try the above command from a DOS prompt for more information...");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error running process '{exe}': {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        public static bool RunProcess(string exe, string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                log.Debug($"{exe} {arguments}");
                Process p = new Process();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = arguments;
                p.Start();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    return true;
                }

                log.Error($"Process failed (error code {p.ExitCode})");
            }
            catch (Exception ex)
            {
                log.Error($"Error running process '{exe}': {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }
    }

    public class FileSystem
    {
        private static ILog log = LogManager.GetLogger(typeof(FileSystem));

        // recursive, remove read-only properties
        public static bool DeleteDirectory(string parentDirectory)
        {
            try
            {
                ClearReadOnly(new DirectoryInfo(parentDirectory));
                Directory.Delete(parentDirectory, true);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"DeleteDirectory Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        private static void ClearReadOnly(DirectoryInfo parentDirectory)
        {
            if (parentDirectory != null)
            {
                parentDirectory.Attributes = FileAttributes.Normal;
                foreach (FileInfo fi in parentDirectory.GetFiles())
                {
                    fi.Attributes = FileAttributes.Normal;
                }
                foreach (DirectoryInfo di in parentDirectory.GetDirectories())
                {
                    ClearReadOnly(di);
                }
            }
        }
    }

    public class Logging
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warn,
            Error
        }
    }

    public class NetworkDrive
    {
        private static ILog log = LogManager.GetLogger(typeof(NetworkDrive));

        public enum ResourceScope
        {
            RESOURCE_CONNECTED = 1,
            RESOURCE_GLOBALNET,
            RESOURCE_REMEMBERED,
            RESOURCE_RECENT,
            RESOURCE_CONTEXT
        }

        public enum ResourceType
        {
            RESOURCETYPE_ANY,
            RESOURCETYPE_DISK,
            RESOURCETYPE_PRINT,
            RESOURCETYPE_RESERVED
        }

        public enum ResourceUsage
        {
            RESOURCEUSAGE_CONNECTABLE = 0x00000001,
            RESOURCEUSAGE_CONTAINER = 0x00000002,
            RESOURCEUSAGE_NOLOCALDEVICE = 0x00000004,
            RESOURCEUSAGE_SIBLING = 0x00000008,
            RESOURCEUSAGE_ATTACHED = 0x00000010,
            RESOURCEUSAGE_ALL = (RESOURCEUSAGE_CONNECTABLE | RESOURCEUSAGE_CONTAINER | RESOURCEUSAGE_ATTACHED),
        }

        public enum ResourceDisplayType
        {
            RESOURCEDISPLAYTYPE_GENERIC,
            RESOURCEDISPLAYTYPE_DOMAIN,
            RESOURCEDISPLAYTYPE_SERVER,
            RESOURCEDISPLAYTYPE_SHARE,
            RESOURCEDISPLAYTYPE_FILE,
            RESOURCEDISPLAYTYPE_GROUP,
            RESOURCEDISPLAYTYPE_NETWORK,
            RESOURCEDISPLAYTYPE_ROOT,
            RESOURCEDISPLAYTYPE_SHAREADMIN,
            RESOURCEDISPLAYTYPE_DIRECTORY,
            RESOURCEDISPLAYTYPE_TREE,
            RESOURCEDISPLAYTYPE_NDSCONTAINER
        }

        [System.Flags]
        public enum AddConnectionOptions
        {
            CONNECT_UPDATE_PROFILE = 0x00000001,
            CONNECT_UPDATE_RECENT = 0x00000002,
            CONNECT_TEMPORARY = 0x00000004,
            CONNECT_INTERACTIVE = 0x00000008,
            CONNECT_PROMPT = 0x00000010,
            CONNECT_NEED_DRIVE = 0x00000020,
            CONNECT_REFCOUNT = 0x00000040,
            CONNECT_REDIRECT = 0x00000080,
            CONNECT_LOCALDRIVE = 0x00000100,
            CONNECT_CURRENT_MEDIA = 0x00000200,
            CONNECT_DEFERRED = 0x00000400,
            CONNECT_RESERVED = unchecked((int)0xFF000000),
            CONNECT_COMMANDLINE = 0x00000800,
            CONNECT_CMD_SAVECRED = 0x00001000,
            CONNECT_CRED_RESET = 0x00002000
        }

        [StructLayout(LayoutKind.Sequential)]
        private class NETRESOURCE
        {
            public ResourceScope dwScope = 0;
            //  change resource type as required
            public ResourceType dwType = ResourceType.RESOURCETYPE_DISK;
            public ResourceDisplayType dwDisplayType = 0;
            public ResourceUsage dwUsage = 0;
            public string lpLocalName = null;
            public string lpRemoteName = null;
            public string lpComment = null;
            public string lpProvider = null;
        }

        [DllImport("mpr.dll")]
        private static extern int WNetAddConnection2(NETRESOURCE lpNetResource, string lpPassword, string lpUsername, int dwFlags);

        [DllImport("mpr.dll")]
        private static extern int WNetCancelConnection2(string sLocalName, uint iFlags, int iForce);

        // <summary>
        // Map network drive 'unc' to local Windows drive 'drive'
        // </summary>
        // <param name="unc">network path (example: @"\\servername\shardrive")</param>
        // <param name="drive">local Windows drive (example: "Q:")</param>
        // <param name="user">username (null, if not specified)</param>
        // <param name="password">password (null, if not specified)</param>
        // <returns></returns>
        public static int MapNetworkDrive(string unc, string drive, string user, string password)
        {
            NETRESOURCE myNetResource = new NETRESOURCE();
            myNetResource.lpLocalName = drive;
            myNetResource.lpRemoteName = unc;
            myNetResource.lpProvider = null;

            //  change dwFlags parameter as required
            int result = WNetAddConnection2(myNetResource, password, user,
                                            (int)AddConnectionOptions.CONNECT_TEMPORARY);
            return result;
        }

        public static int DisconnectNetworkDrive(string drive, bool bForceDisconnect)
        {
            if (bForceDisconnect)
            {
                return WNetCancelConnection2(drive, 0, 1);
            }
            else
            {
                return WNetCancelConnection2(drive, 0, 0);
            }
        }

        public static bool IsDriveMapped(string drive)
        {
            string[] DriveList = Environment.GetLogicalDrives();
            for (int i = 0; i < DriveList.Length; i++)
            {
                if (DriveList[i].ToString().StartsWith(drive))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class Service
    {
        private static ILog log = LogManager.GetLogger(typeof(Service));

        public static bool RestartService(string serviceName, int timeoutMilliseconds)
        {
            ServiceController service = new ServiceController(serviceName);
            try
            {
                int millisec1 = Environment.TickCount;
                TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);

                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);

                // count the rest of the timeout
                int millisec2 = Environment.TickCount;
                timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds - (millisec2 - millisec1));

                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, timeout);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($" Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        private bool StartService(string serviceName, int timeoutMilliseconds)
        {
            ServiceController service = new ServiceController(serviceName);
            try
            {
                TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, timeout);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"StartService Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }

        private bool StopService(string serviceName, int timeoutMilliseconds)
        {
            ServiceController service = new ServiceController(serviceName);
            try
            {
                TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"StopService Exception: {ex.Message} - {ex.StackTrace}");
            }

            return false;
        }
    }

    public class SourceControl
    {
        public static string LastError { get; protected set; } = string.Empty;

        public static bool CheckoutFolder(string svnUri, string user, string password, string outputFolder)
        {
            LastError = string.Empty;
            try
            {
                using (SvnClient client = new SvnClient())
                {
                    client.Authentication.Clear(); // Clear a previous authentication
                    client.Authentication.DefaultCredentials = new System.Net.NetworkCredential(user, password);
                    client.Authentication.SslServerTrustHandlers += delegate (object sender, SharpSvn.Security.SvnSslServerTrustEventArgs e)
                    {
                        e.AcceptedFailures = e.Failures;
                        e.Save = true;
                    };

                    // Checkout the code to the specified directory
                    return client.CheckOut(new Uri(svnUri), outputFolder);
                }
            }
            catch (Exception ex)
            {
                LastError = $"CheckoutFile Exception: {ex.Message} - {ex.StackTrace}";
            }

            return false;
        }
    }
}
