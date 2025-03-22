using System.Net;

namespace Server.Crawlers;

// Service for handling FTP operations using modern HttpClient
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
        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password)
        };
        using var client = new HttpClient(handler);

        // Create a custom request to get the file's timestamp
        var request = new HttpRequestMessage(HttpMethod.Head, url);

        // Implement retry logic for transient failures
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.LastModified.HasValue)
                {
                    return response.Content.Headers.LastModified.Value.DateTime;
                }

                // If we can't get the last modified date from headers, fall back to current time
                logger.LogWarning($"Could not determine last modified date for {url}, using current time");
                return DateTime.Now;
            }
            catch (Exception ex) when (attempt < MaxRetries && (ex is HttpRequestException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning($"Attempt {attempt} failed to get last modified date: {ex.Message}. Retrying...");
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        // If we've exhausted all retries, throw the exception
        throw new HttpRequestException($"Failed to get last modified date for {url} after {MaxRetries} attempts");
    }

    // Downloads a file from an FTP server to a local path, returns the size of the downloaded file in bytes
    public async Task<long> DownloadFile(string url, string username, string password, string destinationPath, CancellationToken cancellationToken)
    {
        logger.LogInformation($"Downloading file from {url} to {destinationPath}");

        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password)
        };
        using var client = new HttpClient(handler);

        // Implement retry logic for transient failures
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Download as stream to avoid loading entire file into memory
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                // Get file size from headers if available
                long fileSize = response.Content.Headers.ContentLength ?? -1;

                // Create destination directory if it doesn't exist
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                // Stream directly to file
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                await response.Content.CopyToAsync(fileStream, cancellationToken);

                // If we couldn't get the file size from headers, get it from the file
                if (fileSize < 0)
                {
                    fileSize = fileStream.Length;
                }

                logger.LogInformation($"Successfully downloaded {FormatFileSize(fileSize)} to {destinationPath}");
                return fileSize;
            }
            catch (Exception ex) when (attempt < MaxRetries && (ex is HttpRequestException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning($"Attempt {attempt} failed to download file: {ex.Message}. Retrying...");
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        // If we've exhausted all retries, throw the exception
        throw new HttpRequestException($"Failed to download file from {url} after {MaxRetries} attempts");
    }

    // Formats a file size in bytes to a human-readable string (KB, MB, GB)
    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
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
