using HeatFlow.Application;
using HeatFlow.Core.Dose;
using HeatFlow.Core.Phases;
using HeatFlow.Domain;
using HeatFlow.Infrastructure.Configuration;
using HeatFlow.Infrastructure.Database;
using HeatFlow.Infrastructure.EsterownikMapper;
using HeatFlow.Infrastructure.HomeAssistant;
using HeatFlow.Infrastructure.OpenWeatherMap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MSSqlServer;

namespace HeatFlow.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // Konfiguracja Serilog
        var earlyConfig = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        var earlyConnectionString = earlyConfig.GetConnectionString("DefaultConnection");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File("logs/heatflow-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.MSSqlServer(
                connectionString: earlyConnectionString,
                sinkOptions: new MSSqlServerSinkOptions
                {
                    TableName = "SerilogLog",
                    AutoCreateSqlTable = true,
                    BatchPostingLimit = 50,
                    BatchPeriod = TimeSpan.FromSeconds(5)
                },
                restrictedToMinimumLevel: LogEventLevel.Information)
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

            // Test zapisu nastaw podajnika przed pierwszym włączeniem automatu dawki (Faza 5)
            if (args.Length > 0 && args[0] == "--dawka-test-zapisu")
            {
                Environment.ExitCode = await RunDoseWriteTestAsync(serviceProvider, args.Skip(1).ToArray());
                return;
            }

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

        services.AddScoped<IHomeAssistantClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(HomeAssistantClient));
            var logger = sp.GetRequiredService<ILogger<HomeAssistantClient>>();
            var errorLogger = sp.GetRequiredService<IApplicationErrorLogger>();
            return new HomeAssistantClient(httpClient, haBaseUrl, haAccessToken, logger, errorLogger);
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
        services.AddScoped<IPhaseService, Phase4SummerModeService>();
        services.AddScoped<ISummerModeRepository, SummerModeRepository>();
        services.AddScoped<ISummerBoilerSetpoint, SummerBoilerSetpoint>();
        services.AddScoped<IPhaseService, Phase5DoseService>();
        services.AddScoped<IDoseRepository, DoseRepository>();
        services.AddScoped<DoseControllerWriter>();
        services.AddScoped<IDoseNotifier, HomeAssistantDiscordNotifier>();
        services.AddSingleton(new DoseWriteOptions());
        services.AddSingleton(TimeProvider.System);

        // eSterownikMapper - potwierdzenie zapisu nastaw kotła (Faza 5), adres z appsettings
        // (EsterownikMapper:BaseUrl, u nas http://172.17.137.33:5000/). Bez klucza nie zgadujemy adresu:
        // ostrzeżenie przy starcie, mapper „nie odpowiada”, automat dawki się nie włączy.
        var mapperBaseUrl = configuration["EsterownikMapper:BaseUrl"];
        if (string.IsNullOrWhiteSpace(mapperBaseUrl))
        {
            Log.Warning("Brak EsterownikMapper:BaseUrl w appsettings - Faza 5 nie odczyta nastaw kotła z mappera, " +
                        "automat dawki się nie włączy (tryb cienia działa)");
        }
        services.AddHttpClient<IEsterownikMapperClient, EsterownikMapperClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(mapperBaseUrl))
                client.BaseAddress = new Uri(mapperBaseUrl.EndsWith('/') ? mapperBaseUrl : mapperBaseUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

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
            services.AddScoped<IConfigurationAuditService, ConfigurationAuditService>();
        }
        else
        {
            throw new InvalidOperationException("Brak konfiguracji połączenia z bazą danych. Baza danych jest wymagana dla ConfigurationService.");
        }

        // Orchestration Service
        services.AddScoped<OrchestrationService>();
    }

    /// <summary>
    /// Test „aktualna ± 1” zapisu nastaw z HA do sterownika (analiza-kociol-nastawy.md § 9 „Zapis do
    /// sterownika”): dla każdego wybranego rejestru zapis wartości o 1 obok aktualnej, potwierdzenie
    /// w eSterownikMapperze, przywrócenie i ponowne potwierdzenie. Ta sama ścieżka co automat
    /// (DoseControllerWriter). Robić z Pawłem przy kotle; nie działa przy włączonym automacie.
    /// Użycie: HeatFlow.Console --dawka-test-zapisu [praca] [postoj] [dmuchawa]  (domyślnie praca postoj)
    /// </summary>
    private static async Task<int> RunDoseWriteTestAsync(IServiceProvider serviceProvider, string[] args)
    {
        using var scope = serviceProvider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IEsterownikMapperClient>();
        var writer = scope.ServiceProvider.GetRequiredService<DoseControllerWriter>();
        var doseRepository = scope.ServiceProvider.GetRequiredService<IDoseRepository>();
        var entities = DoseEntities.From(await scope.ServiceProvider.GetRequiredService<IConfigurationService>()
            .GetSystemConfigurationAsync());
        var configErrors = entities.Validate();
        if (configErrors.Count > 0)
        {
            System.Console.WriteLine($"Błędna konfiguracja encji (SystemConfiguration): {string.Join("; ", configErrors)}");
            return 6;
        }

        var registers = new List<DoseRegister>();
        foreach (var a in args.Length == 0 ? new[] { "praca", "postoj" } : args)
        {
            switch (a.ToLowerInvariant())
            {
                case "praca": registers.Add(DoseRegister.Work); break;
                case "postoj": case "postój": registers.Add(DoseRegister.Pause); break;
                case "dmuchawa": registers.Add(DoseRegister.Blower); break;
                default:
                    System.Console.WriteLine($"Nieznany rejestr: {a} (praca / postoj / dmuchawa)");
                    return 2;
            }
        }

        var automat = await doseRepository.GetAutomatSwitchAsync();
        var doseState = await doseRepository.GetStateAsync();
        if (automat.Enabled || doseState?.AutomatActive == true)
        {
            System.Console.WriteLine("Automat dawki jest włączony - najpierw wyłącz switch.heatflow_dawka_automat.");
            return 3;
        }

        var start = await mapper.GetFeedSettingsAsync();
        if (start == null)
        {
            System.Console.WriteLine("eSterownikMapper nie odpowiada (GetAllValues) - test przerwany, nic nie zapisano.");
            return 4;
        }

        System.Console.WriteLine($"Sterownik teraz (mapper): praca {start.FeederWork} s, postój {start.FeederPause} s, dmuchawa {start.Blower} %");
        var plan = registers.Select(r =>
        {
            var current = DoseControllerWriter.Value(start, r);
            var test = DoseControllerWriter.WithinHardLimits(r, current + 1) ? current + 1 : current - 1;
            return (Register: r, Current: current, Test: test);
        }).ToList();
        foreach (var (r, current, test) in plan)
            System.Console.WriteLine($"  {DoseControllerWriter.EntityId(entities, r)}: {current} → {test} → z powrotem {current}");
        System.Console.Write("Wpisz 'tak', żeby zapisać: ");
        if (!string.Equals(System.Console.ReadLine()?.Trim(), "tak", StringComparison.OrdinalIgnoreCase))
        {
            System.Console.WriteLine("Przerwane, nic nie zapisano.");
            return 1;
        }

        var allOk = true;
        foreach (var (r, current, test) in plan)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var there = await writer.WriteAsync(entities, r, test);
            System.Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F0} s] {there.Report}");
            sw.Restart();
            var back = await writer.WriteAsync(entities, r, current);
            System.Console.WriteLine($"[{sw.Elapsed.TotalSeconds:F0} s] przywrócenie: {back.Report}");
            allOk &= there.Confirmed && back.Confirmed;
            if (!back.Confirmed)
            {
                System.Console.WriteLine($"UWAGA: {DoseControllerWriter.EntityId(entities, r)} nie wrócił na {current} - ustaw ręcznie na panelu.");
                break;
            }
        }

        var end = await mapper.GetFeedSettingsAsync();
        System.Console.WriteLine(end == null
            ? "Mapper nie odpowiada na koniec - sprawdź nastawy na panelu."
            : $"Sterownik na koniec (mapper): praca {end.FeederWork} s, postój {end.FeederPause} s, dmuchawa {end.Blower} %");
        System.Console.WriteLine(allOk ? "WYNIK: zapis i przywrócenie potwierdzone w mapperze." : "WYNIK: test NIEUDANY.");
        return allOk ? 0 : 5;
    }

    private static async Task LogErrorToDbAsync(IServiceProvider serviceProvider, string message, int? phase, string source)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var errorLogger = scope.ServiceProvider.GetRequiredService<IApplicationErrorLogger>();
            await errorLogger.LogAsync(message, phase, source, null, "Error", "Console");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Nie udało się zapisać błędu do ApplicationErrorLog (Console)");
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
        catch (Exception innerEx)
        {
            Log.Warning(innerEx, "Nie udało się zapisać wyjątku do ApplicationErrorLog (Console)");
            // Nie rzucamy – log do bazy nie może powalić aplikacji
        }
    }
}
