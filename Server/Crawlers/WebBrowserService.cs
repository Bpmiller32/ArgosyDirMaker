using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace Server.Crawlers;

// Service for handling web browser interactions using Puppeteer
public class WebBrowserService
{
    private readonly ILogger logger;

    // Constants
    private const string PARASCRIPT_URL = "https://parascript.sharefile.com/share/view/s80765117d4441b88";

    public WebBrowserService(ILogger logger)
    {
        this.logger = logger;
    }

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
            await page.Client.SendAsync("Page.setDownloadBehavior", new { behavior = "allow", downloadPath });
        }

        return (browser, page);
    }

    // Navigates to the Parascript download portal
    public async Task NavigateToParascriptPortal(Page page)
    {
        await page.GoToAsync(PARASCRIPT_URL);
    }

    /// Finds an element on the page that contains the specified text using multiple search strategies
    public async Task<IElementHandle> FindElementByText(Page page, string text, int timeout = 5000, CancellationToken stoppingToken = default)
    {
        // Try XPath with text content
        try
        {
            if (stoppingToken.IsCancellationRequested) return null;

            string xpathExpression = $"//*[contains(translate(text(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), '{text.ToLower()}')]";
            await page.WaitForXPathAsync(xpathExpression, new WaitForSelectorOptions { Timeout = timeout });
            var elements = await page.XPathAsync(xpathExpression);

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

            var element = await page.EvaluateFunctionHandleAsync(jsSelector);
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
                        var specificElements = await page.XPathAsync(specificXPath);
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

    // Waits for an element containing the specified text to appear and remain stable on the page
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
                var element = await FindElementByText(page, text, 10000, stoppingToken);

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

    // Extracts file information from the page content
    public async Task<(string month, string year)> ExtractFileInfo(Page page)
    {
        HtmlAgilityPack.HtmlDocument doc = new();
        doc.LoadHtml(await page.GetContentAsync());

        // Search for nodes containing the pattern
        HtmlAgilityPack.HtmlNode node = null;

        // Try different search strategies
        node = doc.DocumentNode.SelectSingleNode("//span[contains(text(), 'ads_zip_09_')] | //div[contains(text(), 'ads_zip_09_')]");

        if (node == null)
        {
            var allTextNodes = doc.DocumentNode.SelectNodes("//text()[contains(., 'ads_zip_09_')]");

            if (allTextNodes != null && allTextNodes.Count > 0)
            {
                node = allTextNodes[0].ParentNode;
            }
        }

        if (node == null)
        {
            var allNodes = doc.DocumentNode.SelectNodes("//*");
            if (allNodes != null)
            {
                foreach (var n in allNodes)
                {
                    if (n.InnerText.Contains("ads_zip_09_"))
                    {
                        node = n;
                        break;
                    }
                }
            }
        }

        if (node == null)
        {
            throw new Exception("Could not find node containing 'ads_zip_09_' pattern");
        }

        // Extract the year/month information using regex
        string nodeText = node.InnerText;
        var match = Regex.Match(nodeText, @"ads_zip_09_(\d{2})(\d{2})");

        if (!match.Success)
        {
            throw new Exception($"Could not extract date from text: {nodeText}");
        }

        string month = match.Groups[1].Value;
        string year = match.Groups[2].Value;

        return (month, year);
    }

    // Waits for downloads to complete
    public async Task<bool> WaitForDownloadsToComplete(string downloadPath, CancellationToken stoppingToken)
    {
        const int maxAttempts = 60; // 10 minutes maximum (60 * 10 seconds)
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Download monitoring was stopped due to cancellation");
                return false;
            }

            string[] files = Directory.GetFiles(downloadPath, "*.CRDOWNLOAD");

            if (files.Length < 1)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            attempts++;
        }

        logger.LogWarning("Download wait timeout reached");
        return false;
    }
}
