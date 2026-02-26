using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/heatflow-api-.log", rollingInterval: RollingInterval.Day));

if (OperatingSystem.IsWindows())
    builder.Host.UseWindowsService();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

builder.Services.AddDbContext<HeatFlowDbContext>(options =>
{
    if (useInMemory)
        options.UseInMemoryDatabase("HeatFlow");
    else
        options.UseSqlServer(connectionString ?? throw new InvalidOperationException("Brak konfiguracji ConnectionStrings:DefaultConnection"), b => b.MigrationsAssembly("HeatFlow.Infrastructure"));
});

builder.Services.AddScoped<IHeatFlowRepository, HeatFlowRepository>();
builder.Services.AddScoped<IApplicationErrorLogger, ApplicationErrorLogger>();
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IConfigurationAuditService, ConfigurationAuditService>();

builder.Services.AddControllers();

var port = builder.Configuration.GetValue<int>("Kestrel:Port", 5000);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(serverOptions => serverOptions.ListenAnyIP(port));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins);
        else policy.AllowAnyOrigin();
        policy.AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("X-API-Key");
    });
});

var app = builder.Build();

var apiKey = builder.Configuration["HeatFlow:ApiKey"] ?? builder.Configuration["ApiKey"] ?? "";
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) { await next(context); return; }
    var key = context.Request.Headers["X-API-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(apiKey) || key == apiKey) { await next(context); return; }
    context.Response.StatusCode = 401;
    await context.Response.CompleteAsync();
});

app.UseCors();

// Globalny obsługiwacz wyjątków – zapis do ApplicationErrorLog z origin "Api"
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        try
        {
            using var scope = context.RequestServices.CreateScope();
            var errorLogger = scope.ServiceProvider.GetService<IApplicationErrorLogger>();
            if (errorLogger != null)
            {
                var ctx = new { Path = context.Request.Path.Value, Method = context.Request.Method };
                await errorLogger.LogAsync(ex, null, "Api.Unhandled", ctx, "Error", "Api");
            }
        }
        catch (Exception innerEx)
        {
            var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("HeatFlow.Api");
            logger?.LogError(innerEx, "Nie udało się zapisać błędu do ApplicationErrorLog");
        }
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Wystąpił błąd wewnętrzny." });
    }
});

app.MapGet("/", () => Results.Ok("HeatFlow API"));
app.MapControllers();

app.Run();
