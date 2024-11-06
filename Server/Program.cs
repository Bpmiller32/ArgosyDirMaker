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
using System.Text.Json;

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

// Crawlers, Builders, Testers registration
builder.Services.AddSingleton<SmartMatchCrawler>();
builder.Services.AddSingleton<ParascriptCrawler>();
builder.Services.AddSingleton<RoyalMailCrawler>();

builder.Services.AddSingleton<SmartMatchBuilder>();
builder.Services.AddSingleton<ParascriptBuilder>();
builder.Services.AddSingleton<RoyalMailBuilder>();

builder.Services.AddSingleton<DirTester>();

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

// Register Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Register Middleware
app.UseCors("FrontEnd");
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Cancellation tokens
Dictionary<string, CancellationTokenSource> cancelTokens = new()
{
    {"SmartMatchCrawler", new()},
    {"ParascriptCrawler", new()},
    {"RoyalMailCrawler", new()},

    {"SmartMatchBuilder", new()},
    {"ParascriptBuilder", new()},
    {"RoyalMailBuilder", new()},
};

// Status
app.MapGet("/status", async (HttpContext context, StatusReporter statusReporter) =>
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

// Crawler Endpoints
app.MapPost("/smartmatch/crawler", (SmartMatchCrawler smartMatchCrawler, CrawlerMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["SmartMatchCrawler"] = new();
            Task.Run(() => smartMatchCrawler.Start(cancelTokens["SmartMatchCrawler"].Token));
            return Results.Ok();

        case "stop":
            cancelTokens["SmartMatchCrawler"].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost("/parascript/crawler", (ParascriptCrawler parascriptCrawler, CrawlerMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["ParascriptCrawler"] = new();
            Task.Run(() => parascriptCrawler.Start(cancelTokens["ParascriptCrawler"].Token));
            return Results.Ok();

        case "stop":
            cancelTokens["ParascriptCrawler"].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost("/royalmail/crawler", (RoyalMailCrawler royalMailCrawler, CrawlerMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["RoyalMailCrawler"] = new();
            Task.Run(() => royalMailCrawler.Start(cancelTokens["RoyalMailCrawler"].Token));
            return Results.Ok();

        case "stop":
            cancelTokens["RoyalMailCrawler"].Cancel();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

// Builder Endpoints
app.MapPost("/smartmatch/builder", (SmartMatchBuilder smartMatchBuilder, SmartMatchBuilderMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["SmartMatchBuilder"] = new();
            Utils.KillSmProcs();
            if (string.IsNullOrEmpty(serverMessage.ExpireDays) || serverMessage.ExpireDays == "string")
            {
                serverMessage.ExpireDays = "105";
            }
            Task.Run(() => smartMatchBuilder.Start(serverMessage.Cycle, serverMessage.DataYearMonth, cancelTokens["SmartMatchBuilder"], serverMessage.ExpireDays));
            return Results.Ok();

        case "stop":
            cancelTokens["SmartMatchBuilder"].Cancel();
            Utils.KillSmProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost("/parascript/builder", (ParascriptBuilder parascriptBuilder, ParascriptBuilderMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["ParascriptBuilder"] = new();
            Utils.KillPsProcs();
            Task.Run(() => parascriptBuilder.Start(serverMessage.DataYearMonth, cancelTokens["ParascriptBuilder"].Token));
            return Results.Ok();

        case "stop":
            cancelTokens["ParascriptBuilder"].Cancel();
            Utils.KillPsProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.MapPost("/royalmail/builder", (RoyalMailBuilder royalMailBuilder, RoyalMailBuilderMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            cancelTokens["RoyalMailBuilder"] = new();
            Utils.KillRmProcs();
            Task.Run(() => royalMailBuilder.Start(serverMessage.DataYearMonth, serverMessage.RoyalMailKey, cancelTokens["RoyalMailBuilder"].Token));
            return Results.Ok();

        case "stop":
            cancelTokens["RoyalMailBuilder"].Cancel();
            Utils.KillRmProcs();
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

// Tester
app.MapPost("/dirtester", (DirTester dirTester, TesterMessage serverMessage) =>
{
    switch (serverMessage.ModuleCommand)
    {
        case "start":
            Task.Run(() => dirTester.Start(serverMessage.TestDirectoryType, serverMessage.DataYearMonth));
            return Results.Ok();

        case "stop":
            dirTester.Status = ModuleStatus.Ready;
            return Results.Ok();

        default:
            return Results.BadRequest();
    }
});

app.Run();
