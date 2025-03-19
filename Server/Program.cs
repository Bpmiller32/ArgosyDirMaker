using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using Server.Builders;
using DataObjects;
using Server.Crawlers;
using Server.ServerMessages;
using Server.Tester;
using Server;

/* ---------------------------- Application setup --------------------------- */
string applicationName = "DirMaker";
using var mutex = new Mutex(false, applicationName);

// Single instance of application check
bool isAnotherInstanceOpen = !mutex.WaitOne(TimeSpan.Zero);
if (isAnotherInstanceOpen)
{
    throw new Exception("Only one instance of the application allowed");
}

// Set exe directory to current directory, important when doing Windows services otherwise runs out of System32
Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure logging
Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .WriteTo.Console(new ExpressionTemplate("[{@t:MM-dd-yyyy HH:mm:ss} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}]  [{@l:u1}] {@m}\n{@x}"))
        // .WriteTo.File(new ExpressionTemplate("[{@t:MM-dd-yyyy HH:mm:ss} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}]  [{@l:u3}] {@m}\n{@x}"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, string.Format("{0}.log", applicationName)))
        .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// CORS
builder.Services.AddCors(options => options.AddPolicy("FrontEnd", pb => pb.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Swagger configuration
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "version 1.0.0",
        Title = "RAF DirMaker",
        Description = "An Asp.Net Core Web API for gathering, building, and testing Argosy Post directories",
        Contact = new OpenApiContact
        {
            Name = "Contact Billy",
            Url = new Uri("https://bpmiller.com")
        },
    });
});

// Database connection
builder.Services.AddDbContext<DatabaseContext>(opt => opt.UseSqlite($"Filename={builder.Configuration.GetValue<string>("DatabaseLocation")}"), ServiceLifetime.Transient);

/* -------------------------- Service registration -------------------------- */
// Crawler services
builder.Services.AddSingleton<SmartMatchCrawler>();
builder.Services.AddSingleton<ParascriptCrawler>();
builder.Services.AddSingleton<RoyalMailCrawler>();

// Builder services
builder.Services.AddSingleton<SmartMatchBuilder>();
builder.Services.AddSingleton<ParascriptBuilder>();
builder.Services.AddSingleton<RoyalMailBuilder>();

// Testing services
builder.Services.AddSingleton<DirTester>();

// Status reporting
builder.Services.AddSingleton<StatusReporter>();

// Build Application
WebApplication app = builder.Build();

// Database build and validation
DatabaseContext context = app.Services.GetService<DatabaseContext>();
context.Database.EnsureCreated();

// Register server address
app.Urls.Add("http://localhost:5000");
IConfiguration config = app.Services.GetService<IConfiguration>();
string serverAddress = config.GetValue<string>("ServerAddress");
if (!string.IsNullOrEmpty(serverAddress))
{
    app.Urls.Add(serverAddress);
}

// Define reverse proxy subdomain for all routes
string reverseProxySubdomain = "/rafdirmaker";

// Register Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Register Middleware
app.UseCors("FrontEnd");
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Module name constants
const string SMARTMATCH_CRAWLER = "SmartMatchCrawler";
const string PARASCRIPT_CRAWLER = "ParascriptCrawler";
const string ROYALMAIL_CRAWLER = "RoyalMailCrawler";
const string SMARTMATCH_BUILDER = "SmartMatchBuilder";
const string PARASCRIPT_BUILDER = "ParascriptBuilder";
const string ROYALMAIL_BUILDER = "RoyalMailBuilder";

// Cancellation tokens
Dictionary<string, CancellationTokenSource> cancelTokens = new()
{
    {SMARTMATCH_CRAWLER, new()},
    {PARASCRIPT_CRAWLER, new()},
    {ROYALMAIL_CRAWLER, new()},

    {SMARTMATCH_BUILDER, new()},
    {PARASCRIPT_BUILDER, new()},
    {ROYALMAIL_BUILDER, new()},
};

/* --------------------------- Register endpoints --------------------------- */
// Status endpoint
app.MapGet($"{reverseProxySubdomain}/status", async (HttpContext context, StatusReporter statusReporter) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");

    for (var i = 0; true; i++)
    {
        string message = await statusReporter.UpdateReport();
        byte[] bytes = Encoding.ASCII.GetBytes($"data: {message}\r\r");

        await context.Response.Body.WriteAsync(bytes);
        await context.Response.Body.FlushAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
});

// Crawler endpoints
app.MapPost($"{reverseProxySubdomain}/smartmatch/crawler", (SmartMatchCrawler crawler, CrawlerMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[SMARTMATCH_CRAWLER] = new();
            Task.Run(() => crawler.Start(cancelTokens[SMARTMATCH_CRAWLER].Token));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[SMARTMATCH_CRAWLER].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost($"{reverseProxySubdomain}/parascript/crawler", (ParascriptCrawler crawler, CrawlerMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[PARASCRIPT_CRAWLER] = new();
            Task.Run(() => crawler.Start(cancelTokens[PARASCRIPT_CRAWLER].Token));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[PARASCRIPT_CRAWLER].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost($"{reverseProxySubdomain}/royalmail/crawler", (RoyalMailCrawler crawler, CrawlerMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[ROYALMAIL_CRAWLER] = new();
            Task.Run(() => crawler.Start(cancelTokens[ROYALMAIL_CRAWLER].Token));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[ROYALMAIL_CRAWLER].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

// Builder endpoints
app.MapPost($"{reverseProxySubdomain}/smartmatch/builder", (SmartMatchBuilder builder, SmartMatchBuilderMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[SMARTMATCH_BUILDER] = new();
            Utils.KillSmProcs();
            if (string.IsNullOrEmpty(message.ExpireDays) || message.ExpireDays == "string")
            {
                message.ExpireDays = "105";
            }
            Task.Run(() => builder.Start(message.Cycle, message.DataYearMonth,
                cancelTokens[SMARTMATCH_BUILDER], message.ExpireDays));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[SMARTMATCH_BUILDER].Cancel();
            Utils.KillSmProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost($"{reverseProxySubdomain}/parascript/builder", (ParascriptBuilder builder, ParascriptBuilderMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[PARASCRIPT_BUILDER] = new();
            Utils.KillPsProcs();
            Task.Run(() => builder.Start(message.DataYearMonth, cancelTokens[PARASCRIPT_BUILDER].Token));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[PARASCRIPT_BUILDER].Cancel();
            Utils.KillPsProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost($"{reverseProxySubdomain}/royalmail/builder", (RoyalMailBuilder builder, RoyalMailBuilderMessage message) =>
{
    switch (message.ModuleCommand)
    {
        case ModuleCommandType.Start:
            cancelTokens[ROYALMAIL_BUILDER] = new();
            Utils.KillRmProcs();
            Task.Run(() => builder.Start(message.DataYearMonth, message.RoyalMailKey,
                cancelTokens[ROYALMAIL_BUILDER].Token));
            return Results.Ok();

        case ModuleCommandType.Stop:
            cancelTokens[ROYALMAIL_BUILDER].Cancel();
            Utils.KillRmProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

// Tester endpoint
app.MapPost($"{reverseProxySubdomain}/dirtester", (DirTester tester, TesterMessage message) =>
{
    switch (message.ModuleCommand)
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
});

/* ---------------------------- Start application --------------------------- */
System.Console.WriteLine("Hello World!");
app.Run();
