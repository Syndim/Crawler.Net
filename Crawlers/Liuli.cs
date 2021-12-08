using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Abot2.Crawler;
using Abot2.Poco;
using Newtonsoft.Json;

namespace Crawler.Net.Crawlers;

internal class PageData
{
    public string? Category { get; set; }

    public List<string>? Tags { get; set; }

    public string? Title { get; set; }

    public string? Content { get; set; }

    public Dictionary<string, string>? Images { get; set; }

    public string? Published { get; set; }

    public string? ExternalId { get; set; }

    public string? OriginalUrl { get; set; }

    public string? Cover { get; set; }
}

internal class Liuli
{
    private static readonly Regex ArticleIdRegex = new Regex(@".*?(\d+)\.html");
    private readonly LiuliArgs _args;
    private readonly string _authority;
    private readonly HttpClient _client = new HttpClient();

    public Liuli(LiuliArgs args)
    {
        _args = args;
        _authority = new Uri(_args.Url!).Authority;
    }

    public async Task<CrawlResult> Crawl()
    {
        Directory.CreateDirectory(_args.Path!);
        var config = new CrawlConfiguration
        {
            MinCrawlDelayPerDomainMilliSeconds = 1000
        };

        var crawler = new PoliteWebCrawler(config);
        crawler.ShouldCrawlPageDecisionMaker = ShouldCrawlPageDecisionMaker;
        crawler.PageCrawlStarting += PageCrawlStarting;
        crawler.PageCrawlCompleted += ProcessPageCrawlCompleted;
        crawler.PageCrawlDisallowed += PageCrawlDisallowed;

        return await crawler.CrawlAsync(new Uri(_args.Url!));
    }

    private void PageCrawlDisallowed(object? sender, PageCrawlDisallowedArgs args)
    {
        // Console.WriteLine($"Page crawl disallowed: {args.PageToCrawl.Uri}, reason: {args.DisallowedReason}");
    }

    private void PageCrawlStarting(object? sender, PageCrawlStartingArgs args)
    {
        Console.WriteLine($"About to crawl link {args.PageToCrawl.Uri}");
    }

    private async void ProcessPageCrawlCompleted(object? sender, PageCrawlCompletedArgs args)
    {
        var page = args.CrawledPage;
        Console.WriteLine($"Page crawled: {page.Uri}");

        if (!TryGetArticleIdFromUrl(page.Uri, out int articleId))
        {
            return;
        }

        var document = page.AngleSharpHtmlDocument;

        var article = document.QuerySelector("#content article");
        if (article == null)
        {
            Console.WriteLine("Failed to get article from page, skip");
            return;
        }

        var titleTag = article.QuerySelector("h1.entry-title");
        if (titleTag == null)
        {
            Console.WriteLine("Failed to get title tag, skip");
            return;
        }

        var title = titleTag.TextContent;
        var contentTag = article.QuerySelector("div.entry-content");
        if (contentTag == null)
        {
            Console.WriteLine("Failed to get content tag, skip");
            return;
        }

        var path = Path.Combine(_args.Path!, articleId.ToString());
        var indexFilePath = GetIndexFilePath(articleId);
        if (File.Exists(indexFilePath))
        {
            Console.WriteLine("Already fetched, return");
            return;
        }

        Directory.CreateDirectory(path);

        var content = contentTag.InnerHtml;
        var coverImgTag = contentTag.QuerySelector("img");
        if (coverImgTag == null)
        {
            Console.WriteLine("Faild to get coverImgTag");
        }

        var imageTags = contentTag.QuerySelectorAll("img");
        string cover = string.Empty;
        bool isFirst = true;
        Dictionary<string, string> images = new();
        if (imageTags == null)
        {
            Console.WriteLine("No image found");
        }
        else
        {
            foreach (var imageTag in imageTags)
            {
                var url = imageTag.GetAttribute("src");
                if (string.IsNullOrEmpty(url))
                {
                    Console.WriteLine($"Invalid image url: {url}");
                    continue;
                }

                try
                {
                    var stream = await _client.GetStreamAsync(url);
                    var extension = Path.GetExtension(url);
                    var fileName = CreateMD5Hash(url);
                    var fullFileName = $"{fileName}{extension}";
                    var imgFilePath = Path.Combine(path, fullFileName);
                    var imgFile = File.Create(imgFilePath);
                    if (imgFile != null)
                    {
                        await stream.CopyToAsync(imgFile);
                        images.Add(url, fullFileName);
                    }

                    if (isFirst)
                    {
                        cover = fullFileName;
                        isFirst = false;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to get image: {e.Message}");
                }
            }
        }

        var dateElement = document.QuerySelector("time.entry-date");
        var date = dateElement.GetAttribute("datetime");

        var categoryElement = document.QuerySelector("*[rel='category tag']");
        var category = categoryElement.TextContent;

        var tagElements = document.QuerySelectorAll("*[rel='tag']");
        var tags = new List<string>();
        foreach (var tagElement in tagElements)
        {
            tags.Add(tagElement.TextContent);
        }

        var pageData = new PageData
        {
            Tags = tags,
            Cover = cover,
            Title = title,
            Images = images,
            Content = content,
            Category = category,
            Published = date,
            ExternalId = articleId.ToString(),
            OriginalUrl = page.Uri.ToString()
        };

        var jsonResult = JsonConvert.SerializeObject(pageData, Formatting.Indented);
        var outputFile = File.Create(indexFilePath);
        using var sw = new StreamWriter(outputFile);
        await sw.WriteAsync(jsonResult);
    }

    private CrawlDecision ShouldCrawlPageDecisionMaker(PageToCrawl page, CrawlContext context)
    {
        if (page.Uri.Authority != _authority)
        {
            return new CrawlDecision { Allow = false, Reason = "Invalid authority" };
        }
        else if (page.Uri.PathAndQuery.Contains("tag"))
        {
            return new CrawlDecision { Allow = false, Reason = "Tag page" };
        }
        else if (page.Uri.PathAndQuery.Contains("lang="))
        {
            return new CrawlDecision { Allow = false, Reason = "Lang page" };
        }
        else if (page.Uri.PathAndQuery.Contains("/community") || page.Uri.PathAndQuery.Contains("/bbs"))
        {
            return new CrawlDecision { Allow = false, Reason = "BBS page" };
        }
        else if (page.Uri.PathAndQuery.Contains("/wp2"))
        {
            return new CrawlDecision { Allow = false, Reason = "Ad page" };
        }
        else if (page.Uri.PathAndQuery.Contains("/author"))
        {
            return new CrawlDecision { Allow = false, Reason = "Author page" };
        }
        else if (page.Uri.PathAndQuery.Contains("/about.html"))
        {
            return new CrawlDecision { Allow = false, Reason = "About page" };
        }

        if (TryGetArticleIdFromUrl(page.Uri, out int articleId))
        {
            if (File.Exists(GetIndexFilePath(articleId)))
            {
                return new CrawlDecision { Allow = false, Reason = "Already crawlered" };
            }
        }

        return new CrawlDecision { Allow = true };
    }

    private string GetIndexFilePath(int articleId)
    {
        return Path.Combine(_args.Path!, articleId.ToString(), "index.json");
    }

    private static bool TryGetArticleIdFromUrl(Uri uri, out int articleId)
    {
        articleId = 0;
        var articleIdMatch = ArticleIdRegex.Match(uri.PathAndQuery);
        if (articleIdMatch == null || articleIdMatch.Groups.Count < 2)
        {
            Console.WriteLine($"Failed to parse id url: {uri}, skip");
            return false;
        }

        var articleIdString = articleIdMatch.Groups[1];
        if (!int.TryParse(articleIdString.Value, out articleId))
        {
            Console.WriteLine($"Failed to parse id from article tag: {articleIdString}, skip");
            return false;
        }

        return true;
    }

    private string CreateMD5Hash(string input)
    {
        // Step 1, calculate MD5 hash from input
        MD5 md5 = System.Security.Cryptography.MD5.Create();
        byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);

        // Step 2, convert byte array to hex string
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < hashBytes.Length; i++)
        {
            sb.Append(hashBytes[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
