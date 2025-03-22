using PuppeteerSharp;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Server.DataObjects;

namespace Server.Crawlers;

// Service for handling web browser interactions using Puppeteer
public class WebBrowserService
{
    // DI
    private readonly ILogger logger;

    // Fields
    private const string PARASCRIPT_URL = "https://parascript.sharefile.com/share/view/s80765117d4441b88";
    private const string SMARTMATCH_URL = "https://epf.usps.gov/";

    public WebBrowserService(ILogger logger)
    {
        this.logger = logger;
    }

    /* ----------------------- Generic Browser Methods ---------------------- */

    // Initializes a browser instance with the specified options
    public async Task<(Browser browser, Page page)> InitializeBrowser(bool headless = true, string downloadPath = null)
    {
        // Download local chromium binary to launch browser
        BrowserFetcher fetcher = new();
        await fetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        // Set launch options
        LaunchOptions options = new() { Headless = headless };

        // Create browser instance
        Browser browser = (Browser)await Puppeteer.LaunchAsync(options);
        Page page = (Page)await browser.NewPageAsync();

        // Configure download behavior if path is provided
        if (!string.IsNullOrEmpty(downloadPath))
        {
            await ConfigureDownloadPath(page, downloadPath);
        }

        return (browser, page);
    }

    // Generic method to navigate to any URL
    public async Task NavigateToUrl(Page page, string url)
    {
        await page.GoToAsync(url);
    }

    // Configure download path for a page
    public async Task ConfigureDownloadPath(Page page, string downloadPath)
    {
        await page.Client.SendAsync("Page.setDownloadBehavior", new { behavior = "allow", downloadPath });
    }

    // Generic method to find an element by text
    public async Task<IElementHandle> FindElementByText(Page page, string text, int timeout = 5000, CancellationToken stoppingToken = default)
    {
        // Try XPath with text content
        try
        {
            if (stoppingToken.IsCancellationRequested) return null;

            string xpathExpression = $"//*[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{text.ToLower()}')]";
            await page.WaitForXPathAsync(xpathExpression, new WaitForSelectorOptions { Timeout = timeout });
            IElementHandle[] elements = await page.XPathAsync(xpathExpression);

            if (elements.Length > 0)
            {
                return elements[0];
            }
        }
        catch { }

        if (stoppingToken.IsCancellationRequested)
        {
            return null;
        }

        // Try JavaScript evaluation as a fallback
        try
        {
            string jsSelector = $@"
                Array.from(document.querySelectorAll('button, div, span, a, label, input')).find(el => {{
                    const text = el.textContent || el.innerText || el.value || '';
                    return text.toLowerCase().includes('{text.ToLower()}');
                }})
            ";

            IJSHandle element = await page.EvaluateFunctionHandleAsync(jsSelector);
            if (element != null)
            {
                // Verify it's a valid element
                bool isValid = await page.EvaluateFunctionAsync<bool>($@"
                    () => {{
                        const el = {jsSelector};
                        return el ? true : false;
                    }}
                ");

                if (isValid)
                {
                    // Get the element text to create a more specific XPath
                    string elementText = await page.EvaluateFunctionAsync<string>($@"
                        () => {{
                            const el = {jsSelector};
                            return el ? (el.textContent || el.innerText || el.value || '') : '';
                        }}
                    ");

                    if (!string.IsNullOrEmpty(elementText))
                    {
                        // Create a more specific XPath with the exact text
                        string specificXPath = $"//*[contains(text(), '{elementText.Replace("'", "\\'").Substring(0, Math.Min(elementText.Length, 20))}')]";
                        IElementHandle[] specificElements = await page.XPathAsync(specificXPath);
                        if (specificElements.Length > 0)
                        {
                            return specificElements[0];
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    // Generic method to wait for an element with text to appear
    public async Task<IElementHandle> WaitForTextElement(Page page, string text, int maxAttempts = 5, int stabilityDelay = 1000, CancellationToken stoppingToken = default)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                // Find the element
                IElementHandle element = await FindElementByText(page, text, 10000, stoppingToken);

                if (element == null)
                {
                    // Element with text not found
                    await Task.Delay(2000, stoppingToken);
                    continue;
                }

                // Add a small delay to ensure element is stable
                await Task.Delay(stabilityDelay, stoppingToken);

                // Verify element is still present and visible
                bool isVisible = await element.EvaluateFunctionAsync<bool>(@"e => {
                    const rect = e.getBoundingClientRect();
                    return rect.width > 0 && 
                           rect.height > 0 && 
                           window.getComputedStyle(e).visibility !== 'hidden' &&
                           window.getComputedStyle(e).display !== 'none';
                }");

                if (isVisible)
                {
                    return element;
                }
            }
            catch
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return null;
                }
            }

            // Wait before retry
            await Task.Delay(2000, stoppingToken);
        }

        logger.LogWarning($"Failed to find stable element with text '{text}' after {maxAttempts} attempts");
        return null;
    }

    // Generic method to wait for downloads to complete
    public async Task<bool> WaitForDownloadsToComplete(string downloadPath, string fileExtension = ".CRDOWNLOAD", int maxAttempts = 60, int delaySeconds = 10, CancellationToken stoppingToken = default)
    {
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Download monitoring was stopped due to cancellation");
                return false;
            }

            string[] files = Directory.GetFiles(downloadPath, $"*{fileExtension}");

            if (files.Length < 1)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            attempts++;
        }

        logger.LogWarning("Download wait timeout reached");
        return false;
    }

    // Generic method to wait for a specific file to download
    public async Task<bool> WaitForFileDownload(string filePath, int maxAttempts = 30, int delaySeconds = 10, CancellationToken stoppingToken = default)
    {
        int attempts = 0;
        string crdownloadPath = filePath + ".CRDOWNLOAD";

        while (attempts < maxAttempts)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Download monitoring was stopped due to cancellation");
                return false;
            }

            // Check if download is complete (CRDOWNLOAD file no longer exists)
            if (!File.Exists(crdownloadPath))
            {
                // Verify the actual file exists
                if (File.Exists(filePath))
                {
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            attempts++;
        }

        logger.LogWarning($"Download timeout for file: {Path.GetFileName(filePath)}");
        return false;
    }

    // Generic method to click an element
    public async Task ClickElement(Page page, string selector)
    {
        await page.ClickAsync(selector);
    }

    // Generic method to type text
    public async Task TypeText(Page page, string selector, string text)
    {
        await page.FocusAsync(selector);
        await page.Keyboard.TypeAsync(text);
    }

    /* ----------------------- Crawler-Specific Methods ---------------------- */

    /* --- Parascript Methods --- */

    // Navigate to Parascript portal (convenience method using generic NavigateToUrl)
    public async Task NavigateToParascriptPortal(Page page)
    {
        await NavigateToUrl(page, PARASCRIPT_URL);
    }

    // Extract Parascript file information using a regex pattern
    public async Task<(string month, string year)> ExtractParascriptFileInfo(Page page, string pattern = "ads_zip_09_")
    {
        HtmlAgilityPack.HtmlDocument doc = new();
        doc.LoadHtml(await page.GetContentAsync());

        // Search for nodes containing the pattern
        HtmlNode node = null;

        // Try different search strategies
        node = doc.DocumentNode.SelectSingleNode($"//span[contains(text(), '{pattern}')] | //div[contains(text(), '{pattern}')]");

        if (node == null)
        {
            HtmlNodeCollection allTextNodes = doc.DocumentNode.SelectNodes($"//text()[contains(., '{pattern}')]");

            if (allTextNodes != null && allTextNodes.Count > 0)
            {
                node = allTextNodes[0].ParentNode;
            }
        }

        if (node == null)
        {
            HtmlNodeCollection allNodes = doc.DocumentNode.SelectNodes("//*");
            if (allNodes != null)
            {
                foreach (var n in allNodes)
                {
                    if (n.InnerText.Contains(pattern))
                    {
                        node = n;
                        break;
                    }
                }
            }
        }

        if (node == null)
        {
            throw new Exception($"Could not find node containing '{pattern}' pattern");
        }

        // Extract the year/month information using regex
        string nodeText = node.InnerText;
        Match match = Regex.Match(nodeText, $@"{pattern}(\d{{2}})(\d{{2}})");

        if (!match.Success)
        {
            throw new Exception($"Could not extract date from text: {nodeText}");
        }

        string month = match.Groups[1].Value;
        string year = match.Groups[2].Value;

        return (month, year);
    }

    /* --- SmartMatch Methods --- */

    // Navigate to SmartMatch portal (convenience method using generic NavigateToUrl)
    public async Task NavigateToSmartMatchPortal(Page page)
    {
        await NavigateToUrl(page, SMARTMATCH_URL);
    }

    // Login to SmartMatch portal
    public async Task LoginToSmartMatchPortal(Page page, string username, string password, CancellationToken stoppingToken)
    {
        try
        {
            // Wait for login form elements
            await page.WaitForSelectorAsync("#email");
            await TypeText(page, "#email", username);
            await TypeText(page, "#password", password);

            // Click login button
            await ClickElement(page, "#login");

            // Wait for post-login elements
            await page.WaitForSelectorAsync("#r1");
            await ClickElement(page, "#r1");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await page.WaitForSelectorAsync("#r2");
            await ClickElement(page, "#r2");
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // Wait for file list to load
            await page.WaitForSelectorAsync("#tblFileList > tbody");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error logging in to SmartMatch portal: {ex.Message}");
            throw;
        }
    }

    // Extract SmartMatch file information
    public async Task<List<DataFile>> ExtractSmartMatchFileInfo(Page page)
    {
        // Create a list to hold the file information
        List<DataFile> fileInfoList = new List<DataFile>();

        try
        {
            // Get page HTML content
            HtmlAgilityPack.HtmlDocument doc = new();
            doc.LoadHtml(await page.GetContentAsync());

            // Extract file rows
            HtmlNodeCollection fileRows = doc.DocumentNode.SelectNodes("/html/body/div[2]/table/tbody/tr/td/div[3]/table/tbody/tr/td/div/table/tbody/tr");

            if (fileRows == null || fileRows.Count == 0)
            {
                logger.LogWarning("No file rows found in SmartMatch portal");
                return fileInfoList;
            }

            foreach (HtmlNode fileRow in fileRows)
            {
                // Skip non-data files
                string fileName = fileRow.ChildNodes[5].InnerText.Trim();
                if (fileName.Contains(".zip", StringComparison.OrdinalIgnoreCase) || fileName.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Extract upload date
                DateTime uploadDate = DateTime.Parse(fileRow.ChildNodes[3].InnerText.Trim());

                // Extract product description to determine cycle
                string productDescription = fileRow.ChildNodes[2].InnerText.Trim();
                string cycle = productDescription.Contains("Cycle O") ? "Cycle-O" : "Cycle-N";

                // Extract other file information
                string size = fileRow.ChildNodes[6].InnerText.Trim();
                string productKey = fileRow.Attributes[0].Value.Trim().Substring(19, 5);
                string fileId = fileRow.Attributes[1].Value.Trim().Substring(3, 7);

                // Create a new DataFile object and add it to the list
                DataFile file = DatabaseExtensions.CreateUspsFile(
                    fileName,
                    uploadDate.Month,
                    uploadDate.Year,
                    uploadDate.Month < 10
                        ? uploadDate.Year.ToString() + "0" + uploadDate.Month.ToString()
                        : uploadDate.Year.ToString() + uploadDate.Month.ToString(),
                    cycle);

                // Set USPS-specific properties
                file.UploadDate = uploadDate;
                file.Size = size;
                file.ProductKey = productKey;
                file.FileId = fileId;
                file.OnDisk = true;

                fileInfoList.Add(file);
            }

            return fileInfoList;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting SmartMatch file info: {ex.Message}");
            throw;
        }
    }

    // Download a SmartMatch file
    public async Task DownloadSmartMatchFile(Page page, string fileId, string downloadPath, CancellationToken stoppingToken)
    {
        try
        {
            // Set download path
            await ConfigureDownloadPath(page, downloadPath);

            // Click download button for specific file
            await page.EvaluateExpressionAsync($"document.querySelector('#td_{fileId}').childNodes[0].click()");

            // Wait for download to start
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError($"Error downloading SmartMatch file {fileId}: {ex.Message}");
            throw;
        }
    }
}
