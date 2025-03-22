using Server.Builders;
using Server.Crawlers;
using Server.DataObjects;
using Server.Tester;
using Microsoft.AspNetCore.Http;

namespace Server.ModuleControl;

// Helper class for handling module commands
public static class ModuleCommandHandler
{
    // Handle SmartMatch crawler commands
    public static IResult HandleSmartMatchCrawlerCommand(
        SmartMatchCrawler crawler,
        ServerMessages.ModuleMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "SmartMatchCrawler";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Task.Run(() => crawler.Start(cancelTokens[moduleKey].Token));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle Parascript crawler commands
    public static IResult HandleParascriptCrawlerCommand(
        ParascriptCrawler crawler,
        ServerMessages.ModuleMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "ParascriptCrawler";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Task.Run(() => crawler.Start(cancelTokens[moduleKey].Token));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle RoyalMail crawler commands
    public static IResult HandleRoyalMailCrawlerCommand(
        RoyalMailCrawler crawler,
        ServerMessages.ModuleMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "RoyalMailCrawler";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Task.Run(() => crawler.Start(cancelTokens[moduleKey].Token));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle SmartMatch builder commands
    public static IResult HandleSmartMatchBuilderCommand(
        SmartMatchBuilder builder,
        ServerMessages.SmartMatchBuilderMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "SmartMatchBuilder";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Utils.KillSmProcs();
                string expireDays = string.IsNullOrEmpty(message.ExpireDays) || message.ExpireDays == "string"
                    ? "105"
                    : message.ExpireDays;
                Task.Run(() => builder.Start(message.Cycle, message.DataYearMonth, cancelTokens[moduleKey], expireDays));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                Utils.KillSmProcs();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle Parascript builder commands
    public static IResult HandleParascriptBuilderCommand(
        ParascriptBuilder builder,
        ServerMessages.ParascriptBuilderMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "ParascriptBuilder";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Utils.KillPsProcs();
                Task.Run(() => builder.Start(message.DataYearMonth, cancelTokens[moduleKey].Token));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                Utils.KillPsProcs();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle RoyalMail builder commands
    public static IResult HandleRoyalMailBuilderCommand(
        RoyalMailBuilder builder,
        ServerMessages.RoyalMailBuilderMessage message,
        Dictionary<string, CancellationTokenSource> cancelTokens)
    {
        const string moduleKey = "RoyalMailBuilder";

        switch (message.Command)
        {
            case ModuleCommandType.Start:
                cancelTokens[moduleKey] = new CancellationTokenSource();
                Utils.KillRmProcs();
                Task.Run(() => builder.Start(message.DataYearMonth, message.RoyalMailKey, cancelTokens[moduleKey].Token));
                return Results.Ok();

            case ModuleCommandType.Stop:
                cancelTokens[moduleKey].Cancel();
                Utils.KillRmProcs();
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }

    // Handle tester commands
    public static IResult HandleTesterCommand(
        DirTester tester,
        ServerMessages.TesterMessage message)
    {
        switch (message.Command)
        {
            case ModuleCommandType.Start:
                Task.Run(() => tester.Start(message.TestDirectoryType, message.DataYearMonth));
                return Results.Ok();

            case ModuleCommandType.Stop:
                tester.Status = ModuleStatus.Ready;
                return Results.Ok();

            default:
                return Results.BadRequest();
        }
    }
}
