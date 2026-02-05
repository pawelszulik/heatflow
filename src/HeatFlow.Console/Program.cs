using HeatFlow.Application;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.HomeAssistant;
using HeatFlow.Infrastructure.OpenWeatherMap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HeatFlow.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // Konfiguracja Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/heatflow-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        IServiceProvider? serviceProvider = null;
        try
        {
            Log.Information("Uruchamianie aplikacji HeatFlow");

            // Konfiguracja
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);

            serviceProvider = services.BuildServiceProvider();

            //// Seed bazy danych (jeśli jest skonfigurowana)
            //var connectionString = configuration.GetConnectionString("DefaultConnection")
            //    ?? throw new InvalidOperationException("Brak konfiguracji ConnectionStrings:DefaultConnection");
            //if (!string.IsNullOrWhiteSpace(connectionString))
            //{
            //    using (var scope = serviceProvider.CreateScope())
            //    {
            //        var dbContext = scope.ServiceProvider.GetRequiredService<HeatFlowDbContext>();
            //        await dbContext.Database.MigrateAsync();
            //        await ConfigurationSeed.SeedAsync(dbContext);
            //        Log.Information("Baza danych zaktualizowana i wypełniona danymi domyślnymi");
            //    }
            //}

            // Uruchom główną pętlę
            var orchestrationService = serviceProvider.GetRequiredService<OrchestrationService>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Sprawdź czy uruchomione jako Scheduled Task (bez argumentów) czy ręcznie
            if (args.Length == 0)
            {
                // Tryb Scheduled Task - wykonaj raz i zakończ
                logger.LogInformation("Tryb Scheduled Task - wykonanie jednorazowe");

                // Wykonaj główną pętlę
                var result = await orchestrationService.ExecuteMainLoopAsync();

                if (result.IsSuccess)
                {
                    logger.LogInformation("Wykonanie zakończone sukcesem");
                }
                else if (result.IsSkipped)
                {
                    logger.LogInformation("Wykonanie pominięte: {Reason}", result.SkipReason);
                }
                else
                {
                    logger.LogError("Wykonanie zakończone błędem: {Error}", result.ErrorMessage);
                    await LogErrorToDbAsync(serviceProvider, result.ErrorMessage ?? "Błąd wykonania", null, "Program");
                    Environment.ExitCode = 1;
                }
            }
            else
            {
                // Tryb ciągły (dla testów)
                logger.LogInformation("Tryb ciągły - pętla co 5 minut");

                var cancellationTokenSource = new CancellationTokenSource();
                System.Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cancellationTokenSource.Cancel();
                };

                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        // Wykonaj główną pętlę
                        var result = await orchestrationService.ExecuteMainLoopAsync(cancellationTokenSource.Token);

                        if (result.IsSuccess)
                        {
                            logger.LogInformation("Wykonanie zakończone sukcesem");
                        }
                        else if (result.IsSkipped)
                        {
                            logger.LogInformation("Wykonanie pominięte: {Reason}", result.SkipReason);
                        }
                        else
                        {
                            logger.LogError("Wykonanie zakończone błędem: {Error}", result.ErrorMessage);
                            await LogErrorToDbAsync(serviceProvider, result.ErrorMessage ?? "Błąd wykonania", null, "Program");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Błąd podczas wykonania pętli");
                        await LogExceptionToDbAsync(serviceProvider, ex, "Program");
                    }

                    // Czekaj 5 minut
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationTokenSource.Token);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Aplikacja zakończona błędem");
            if (serviceProvider != null)
                await LogExceptionToDbAsync(serviceProvider, ex, "Program");
            Environment.ExitCode = 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddSerilog();
            builder.AddConfiguration(configuration.GetSection("Logging"));
        });

        // Home Assistant Client
        var haBaseUrl = configuration["HomeAssistant:BaseUrl"] 
            ?? throw new InvalidOperationException("Brak konfiguracji HomeAssistant:BaseUrl");
        var haAccessToken = configuration["HomeAssistant:AccessToken"] 
            ?? throw new InvalidOperationException("Brak konfiguracji HomeAssistant:AccessToken");

        services.AddHttpClient<IHomeAssistantClient, HomeAssistantClient>(client =>
        {
            client.BaseAddress = new Uri(haBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue<int>("HomeAssistant:TimeoutSeconds", 30));
        });

        services.AddSingleton<IHomeAssistantClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(HomeAssistantClient));
            return new HomeAssistantClient(httpClient, haBaseUrl, haAccessToken);
        });

        // OpenWeatherMap Client
        var openWeatherMapApiKey = configuration["OpenWeatherMap:ApiKey"]
            ?? throw new InvalidOperationException("Brak konfiguracji OpenWeatherMap:ApiKey");

        services.AddHttpClient<IOpenWeatherMapClient, OpenWeatherMapClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<IOpenWeatherMapClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenWeatherMapClient));
            var logger = sp.GetRequiredService<ILogger<OpenWeatherMapClient>>();
            return new OpenWeatherMapClient(httpClient, openWeatherMapApiKey, logger);
        });

        // Serwisy faz
        services.AddScoped<IPhaseService, Phase0ForecastService>();
        services.AddScoped<IPhaseService, Phase1DiagnoseService>();
        services.AddScoped<IPhaseService, Phase2ArbitrateService>();
        services.AddScoped<IPhaseService, Phase3ValvesService>();
        services.AddScoped<IPhaseService, Phase4BoilerService>();
        services.AddScoped<IPhaseService, Phase5HysteresisService>();

        // Database (wymagane)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Brak konfiguracji ConnectionStrings:DefaultConnection");
        
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<HeatFlowDbContext>(options =>
                options.UseSqlServer(connectionString, b => b.MigrationsAssembly("HeatFlow.Infrastructure")));

            services.AddScoped<IHeatFlowRepository, HeatFlowRepository>();
            services.AddScoped<IApplicationErrorLogger, ApplicationErrorLogger>();
            services.AddScoped<DataPersistenceService>();
            
            // Configuration Service (wymaga bazy danych)
            services.AddScoped<IConfigurationService, ConfigurationService>();
        }
        else
        {
            throw new InvalidOperationException("Brak konfiguracji połączenia z bazą danych. Baza danych jest wymagana dla ConfigurationService.");
        }

        // Orchestration Service
        services.AddScoped<OrchestrationService>();
    }

    private static async Task LogErrorToDbAsync(IServiceProvider serviceProvider, string message, int? phase, string source)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(message, phase, source, null, "Error", "Console");
        }
        catch
        {
            // Nie rzucamy – log do bazy nie może powalić aplikacji
        }
    }

    private static async Task LogExceptionToDbAsync(IServiceProvider serviceProvider, Exception ex, string source)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(ex, null, source, null, "Error", "Console");
        }
        catch
        {
            // Nie rzucamy – log do bazy nie może powalić aplikacji
        }
    }
}
