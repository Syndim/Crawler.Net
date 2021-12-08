using System.CommandLine;
using System.CommandLine.Invocation;

namespace Crawler.Net;

interface ICommandProperty
{
    void AddToCommand(Command c);
}

internal class CommandArgs
{
    static List<ICommandProperty> Commands = new List<ICommandProperty>
    {
        new LiuliArgs()
    };

    public static async Task InvokeAsync(string[] args)
    {
        var rootCommand = new RootCommand();
        Commands.ForEach(c => c.AddToCommand(rootCommand));
        await rootCommand.InvokeAsync(args);
    }
}

internal class LiuliArgs : ICommandProperty
{
    public string? Url { get; set; }

    public string? Path { get; set; }
 
    public string? Proxy { get; set; }

    public void AddToCommand(Command c)
    {
        var options = new List<Option>
        {
            new Option<string>(new string[] { "-u", "--url" }, "Root Url")
            {
                IsRequired = true
            },
            new Option<string>(new string[] { "-p", "--path" }, "Path to save the result")
            {
                IsRequired = true
            },
            new Option<string>(new string[] { "-h", "--proxy" }, "Proxy used for fetching images")
        };

        var command = new Command("liuli", "Fetch Liuli pages")
        {
            Handler = CommandHandler.Create<LiuliArgs>(async options =>
                    {
                        var result = await new Crawlers.Liuli(options).Crawl();
                        if (result.ErrorOccurred)
                        {
                            throw result.ErrorException;
                        }

                        Console.WriteLine($"Crawling for {result.RootUri} completed, time elapsed: {result.Elapsed}");
                    })
        };

        options.ForEach(o => command.Add(o));
        c.AddCommand(command);
    }
}
