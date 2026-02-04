using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Factory dla tworzenia DbContext w czasie projektowania (design-time).
/// Używane przez narzędzia EF Core do tworzenia migracji.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HeatFlowDbContext>
{
    public HeatFlowDbContext CreateDbContext(string[] args)
    {
        // Ścieżka do appsettings.json w projekcie startup (HeatFlow.Console)
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "HeatFlow.Console");
        
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Brak konfiguracji ConnectionStrings:DefaultConnection w appsettings.json");

        var optionsBuilder = new DbContextOptionsBuilder<HeatFlowDbContext>();
        optionsBuilder.UseSqlServer(connectionString, b => b.MigrationsAssembly("HeatFlow.Infrastructure"));

        return new HeatFlowDbContext(optionsBuilder.Options);
    }
}
