namespace Crawler.Net;

public class Program
{
    public static async Task Main(string[] args)
    {
        await CommandArgs.InvokeAsync(args);
    }
}
