using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => Results.Ok("HeatFlow API"));
app.MapControllers();

app.Run();
