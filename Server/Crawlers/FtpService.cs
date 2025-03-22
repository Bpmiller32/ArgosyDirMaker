using System.Net;

namespace Server.Crawlers;

// Service for handling FTP operations using FtpWebRequest and WebClient
public class FtpService
{
    private readonly ILogger logger;

    // Fields
    private const int MaxRetries = 3;
    private readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public FtpService(ILogger logger)
    {
        this.logger = logger;
    }

    // Gets the last modified date of a file on an FTP server
    public async Task<DateTime> GetFileLastModifiedDate(string url, string username, string password, CancellationToken cancellationToken)
    {
        // Create FTP request
#pragma warning disable SYSLIB0014 // Type or member is obsolete
        FtpWebRequest request = (FtpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014 // Type or member is obsolete
        request.Method = WebRequestMethods.Ftp.GetFileSize; // Use GetFileSize as it also returns the timestamp
        request.Credentials = new NetworkCredential(username, password);
        request.UsePassive = true;
        request.UseBinary = true;
        request.KeepAlive = false;

        // Implement retry logic for transient failures
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using FtpWebResponse response = (FtpWebResponse)await request.GetResponseAsync();
                DateTime lastModified = response.LastModified;

                if (lastModified != DateTime.MinValue)
                {
                    return lastModified;
                }

                // If we can't get the last modified date, fall back to current time
                logger.LogWarning($"Could not determine last modified date for {url}, using current time");
                return DateTime.Now;
            }
            catch (Exception ex) when (attempt < MaxRetries &&
                (ex is WebException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning($"Attempt {attempt} failed to get last modified date: {ex.Message}. Retrying...");
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException("Operation was canceled", ex);
            }
        }

        // If we've exhausted all retries, throw the exception
        throw new WebException($"Failed to get last modified date for {url} after {MaxRetries} attempts");
    }

    // Downloads a file from an FTP server to a local path, returns the size of the downloaded file in bytes
    public async Task<long> DownloadFile(string url, string username, string password, string destinationPath, CancellationToken cancellationToken)
    {
        logger.LogInformation($"Downloading file from {url} to {destinationPath}");

        // Create destination directory if it doesn't exist
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

        // Implement retry logic for transient failures
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
#pragma warning disable SYSLIB0014 // Type or member is obsolete
                using WebClient client = new WebClient();
#pragma warning restore SYSLIB0014 // Type or member is obsolete
                client.Credentials = new NetworkCredential(username, password);

                // Register cancellation token
                using CancellationTokenRegistration registration = cancellationToken.Register(() => client.CancelAsync());

                // Download the file
                await client.DownloadFileTaskAsync(new Uri(url), destinationPath);

                // Get the file size
                FileInfo fileInfo = new FileInfo(destinationPath);
                long fileSize = fileInfo.Length;

                logger.LogInformation($"Successfully downloaded {FormatFileSize(fileSize)} to {destinationPath}");
                return fileSize;
            }
            catch (Exception ex) when (attempt < MaxRetries &&
                (ex is WebException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning($"Attempt {attempt} failed to download file: {ex.Message}. Retrying...");
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                // Clean up partial download if canceled
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { /* Ignore cleanup errors */ }
                }
                throw new TaskCanceledException("Download operation was canceled", ex);
            }
        }

        // If we've exhausted all retries, throw the exception
        throw new WebException($"Failed to download file from {url} after {MaxRetries} attempts");
    }

    // Formats a file size in bytes to a human-readable string (KB, MB, GB)
    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }
}
