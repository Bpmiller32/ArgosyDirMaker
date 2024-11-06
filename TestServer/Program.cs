using System.Text;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;

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

// Crawlers, Builders, Testers registration
builder.Services.AddSingleton<StatusReporter>();

// Build Application
WebApplication app = builder.Build();

// Register server address
IConfiguration config = app.Services.GetService<IConfiguration>();
string serverAddress = config.GetValue<string>("ServerAddress");
app.Urls.Add("http://localhost:5000");
app.Urls.Add(serverAddress);


// Register Swagger
app.UseSwagger();
app.UseSwaggerUI();

// Register Middleware
app.UseCors("FrontEnd");
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Toggle
app.MapGet("/toggleStatus", (StatusReporter statusReporter) =>
{
    statusReporter.ToggleStatus();
    return "Toggled Status";
});
app.MapGet("/ToggleProgress", (StatusReporter statusReporter) =>
{
    statusReporter.ToggleProgress();
    return "Toggled Progress";
});

// AddDirectory
app.MapGet("/addDirectory", (StatusReporter statusReporter) =>
{
    statusReporter.AddDirectory();
    return "Added Directory";
});

// ResetDirectory
app.MapGet("/resetDirectory", (StatusReporter statusReporter) =>
{
    statusReporter.ResetDirectory();
    return "Reset Directory";
});

// Status
app.MapGet("/status", async (HttpContext context, StatusReporter statusReporter) =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");

    for (var i = 0; true; i++)
    {
        string message = statusReporter.UpdateReport();
        byte[] bytes = Encoding.ASCII.GetBytes($"data: {message}\r\r");

        await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
        await context.Response.Body.FlushAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
});

app.Run();