using System.Collections;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

namespace Server;

public static class Utils
{
    public static void Cleanup(string path, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        // Cleanup from previous run
        DirectoryInfo cleanupPath = new(path);

        foreach (FileInfo file in cleanupPath.GetFiles())
        {
            file.Delete();
        }
        foreach (DirectoryInfo dir in cleanupPath.GetDirectories())
        {
            dir.Delete(true);
        }
    }

    public static string CalculateDbDate()
    {
        DateTime timestamp = DateTime.Now;
        return $"{timestamp.Month}/{timestamp.Day}/{timestamp.Year}";
    }

    public static string CalculateDbTime()
    {
        DateTime timestamp = DateTime.Now;
        string hour;
        string minute;
        string ampm;
        if (timestamp.Minute < 10)
        {
            minute = timestamp.Minute.ToString().PadLeft(2, '0');
        }
        else
        {
            minute = timestamp.Minute.ToString();
        }
        if (timestamp.Hour > 12)
        {
            hour = (timestamp.Hour - 12).ToString();
            ampm = "pm";
        }
        else
        {
            hour = timestamp.Hour.ToString();
            ampm = "am";
        }

        return $"{hour}:{minute} {ampm}";
    }

    public static string WrapQuotes(string input)
    {
        StringBuilder sb = new();
        _ = sb.Append('"').Append(input).Append('"');
        return sb.ToString();
    }

    public static void CopyFiles(string sourceDirectory, string destDirectory)
    {
        DirectoryInfo source = new(sourceDirectory);
        DirectoryInfo dest = new(destDirectory);

        CopyFilesHelper(source, dest);
    }

    public static void CopyFilesHelper(DirectoryInfo source, DirectoryInfo dest)
    {
        Directory.CreateDirectory(dest.FullName);

        foreach (FileInfo file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(dest.FullName, file.Name), true);
        }

        foreach (DirectoryInfo subDir in source.GetDirectories())
        {
            DirectoryInfo nextSubDir = dest.CreateSubdirectory(subDir.Name);
            CopyFilesHelper(subDir, nextSubDir);
        }
    }

    public static Process RunProc(string fileName, string args)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        Process proc = new()
        {
            StartInfo = startInfo
        };

        proc.Start();

        return proc;
    }

    public static void KillSmProcs()
    {
        foreach (Process process in Process.GetProcessesByName("CleanupDatabase"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("DBCreate"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("ImportUsps"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("GenerateUspsXtls"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("GenerateKeyXtl"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("DumpKeyXtl"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("DumpXtlHeader"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("EncryptREP"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("TestXtlsN2"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("AddDpvHeader"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("rafatizeSLK"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("XtlBuildingWizard"))
        {
            process.Kill(true);
        }
    }

    public static void KillPsProcs()
    {
        foreach (Process process in Process.GetProcessesByName("PDBIntegrity"))
        {
            process.Kill(true);
        }
    }

    public static void KillRmProcs()
    {
        foreach (Process process in Process.GetProcessesByName("ConvertPafData"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("DirectoryDataCompiler"))
        {
            process.Kill(true);
        }
        foreach (Process process in Process.GetProcessesByName("SetupRM"))
        {
            process.Kill(true);
        }
    }

    public static async Task StopService(string serviceName)
    {
        ServiceController service = new(serviceName);

        // Check that service is stopped, if not attempt to stop it
        if (!service.Status.Equals(ServiceControllerStatus.Stopped))
        {
            service.Stop(true);
        }

        // With timeout wait until service actually stops. ServiceController annoyingly returns control immediately, also doesn't allow SC.Stop() on a stopped/stopping service without throwing Exception
        int timeOut = 0;
        while (true)
        {
            service.Refresh();

            if (timeOut > 20)
            {
                throw new Exception("Unable to stop service");
            }

            if (!service.Status.Equals(ServiceControllerStatus.Stopped))
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                timeOut++;
                continue;
            }

            break;
        }
    }

    public static async Task StartService(string serviceName)
    {
        ServiceController service = new(serviceName);

        // Check that service is running, if not attempt to start it
        if (!service.Status.Equals(ServiceControllerStatus.Running))
        {
            service.Start();
        }

        // With timeout wait until service actually stops. ServiceController annoyingly returns control immediately, also doesn't allow SC.Stop() on a stopped/stopping service without throwing Exception
        int timeOut = 0;
        while (true)
        {
            service.Refresh();

            if (timeOut > 20)
            {
                throw new Exception("Unable to start service");
            }

            if (!service.Status.Equals(ServiceControllerStatus.Running))
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                timeOut++;
                continue;
            }

            break;
        }
    }

    public static int ConvertIntBytes(byte[] bytes)
    {
        Array.Reverse(bytes);
        uint value = BitConverter.ToUInt32(bytes);

        return Convert.ToInt32(value);
    }

    public static string ConvertStringBytes(byte[] bytes)
    {
        Array.Reverse(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    public static bool ConvertBoolBytes(byte[] bytes)
    {
        Array.Reverse(bytes);
        return BitConverter.ToBoolean(bytes);
    }

    public static BitArray ConvertBitBytes(byte[] bytes)
    {
        Array.Reverse(bytes);
        return new(bytes);
    }
}