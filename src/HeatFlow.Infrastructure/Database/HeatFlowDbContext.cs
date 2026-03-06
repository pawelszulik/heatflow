

using HeatFlow.Domain;
using Microsoft.EntityFrameworkCore;

namespace HeatFlow.Infrastructure.Database;

/// <summary>
/// Kontekst bazy danych Entity Framework Core.
/// </summary>
public class HeatFlowDbContext : DbContext
{
    public HeatFlowDbContext(DbContextOptions<HeatFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<ExecutionHistory> ExecutionHistories { get; set; }
    public DbSet<RoomState> RoomStates { get; set; }
    public DbSet<BoilerStateEntity> BoilerStates { get; set; }
    public DbSet<ValveState> ValveStates { get; set; }

    // Tabele konfiguracji
    public DbSet<RoomConfiguration> RoomConfigurations { get; set; }
    public DbSet<SystemConfiguration> SystemConfigurations { get; set; }
    public DbSet<HeatingParametersEntity> HeatingParameters { get; set; }
    public DbSet<ForecastDataEntity> ForecastDataCache { get; set; }
    public DbSet<ConfigurationChangeLog> ConfigurationChangeLogs { get; set; }
    public DbSet<ApplicationErrorLog> ApplicationErrorLogs { get; set; }
    public DbSet<SummerModeLog> SummerModeLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ExecutionHistory
        modelBuilder.Entity<ExecutionHistory>(entity =>
        {
            entity.ToTable("ExecutionHistory");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ExecutionTime).HasColumnType("datetime2");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Details).HasColumnType("nvarchar(max)");
            entity.HasIndex(e => e.ExecutionTime);
            entity.HasIndex(e => e.Phase);
        });

        // RoomState
        modelBuilder.Entity<RoomState>(entity =>
        {
            entity.ToTable("RoomState");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.RoomName).HasMaxLength(100);
            entity.Property(e => e.TempActual).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempTarget).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempDeficit).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Score).HasColumnType("decimal(10,2)");
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasIndex(e => e.ExecutionId);
            entity.HasIndex(e => e.RoomName);
            entity.HasIndex(e => e.RecordedAt);
        });

        // BoilerStateEntity
        modelBuilder.Entity<BoilerStateEntity>(entity =>
        {
            entity.ToTable("BoilerState");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.TempExternal).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempReturn).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempTarget).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederTime).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Mixer4DPosition).HasColumnType("decimal(5,2)");
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasIndex(e => e.ExecutionId);
            entity.HasIndex(e => e.RecordedAt);
        });

        // ValveState
        modelBuilder.Entity<ValveState>(entity =>
        {
            entity.ToTable("ValveState");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.RoomName).HasMaxLength(100);
            entity.Property(e => e.ValveEntityId).HasMaxLength(200);
            entity.Property(e => e.TempSet).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempActual).HasColumnType("decimal(5,2)");
            entity.Property(e => e.RecordedAt).HasColumnType("datetime2");
            entity.HasIndex(e => e.ExecutionId);
            entity.HasIndex(e => e.RoomName);
            entity.HasIndex(e => e.RecordedAt);
        });

        // RoomConfiguration
        modelBuilder.Entity<RoomConfiguration>(entity =>
        {
            entity.ToTable("RoomConfiguration");
            entity.HasKey(e => e.Name);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.TempTarget).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempTargetActive).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempTargetInactive).HasColumnType("decimal(5,2)");
            entity.Property(e => e.UsageSchedule).HasMaxLength(500);
            entity.Property(e => e.HeatingSchedule).HasMaxLength(500);
            entity.Property(e => e.SensorTemperatureEntityId).HasMaxLength(200);
            entity.Property(e => e.ValveEntityId).HasMaxLength(200);
            entity.HasIndex(e => e.Name);
        });

        // SystemConfiguration
        modelBuilder.Entity<SystemConfiguration>(entity =>
        {
            entity.ToTable("SystemConfiguration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // Zawsze 1
            entity.Property(e => e.RoomsList).HasMaxLength(500);
            entity.Property(e => e.EkoPiecDeviceSn).HasMaxLength(50);
            entity.Property(e => e.TempReturnEntityId).HasMaxLength(200);
            entity.Property(e => e.Mixer4DPositionEntityId).HasMaxLength(200);
            entity.Property(e => e.BoilerTempEntityId).HasMaxLength(200);
            entity.Property(e => e.FeederTimeEntityId).HasMaxLength(200);
            entity.Property(e => e.Latitude).HasColumnType("decimal(9,6)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(9,6)");
        });

        // HeatingParametersEntity
        modelBuilder.Entity<HeatingParametersEntity>(entity =>
        {
            entity.ToTable("HeatingParameters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // Zawsze 1
            entity.Property(e => e.DeficitHighP1).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DeficitHighP2).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DeficitHighP3).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DeficitHighP1Base).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DeficitHighP2Base).HasColumnType("decimal(5,2)");
            entity.Property(e => e.DeficitHighP3Base).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BufferPreparationBase).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BufferPreparation).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastTempDropThreshold).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastTempRiseThreshold).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastPreHeatingP1Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastPreHeatingP2Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastPreHeatingP3Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastPreHeatingBufferMultiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastReductionP1Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastReductionP2Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastReductionP3Multiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastReductionBufferMultiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ValveTolerance).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ValveRetryDelay).HasColumnType("decimal(5,2)");
            entity.Property(e => e.MinReturnTemp).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BoilerNominalTemp).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FrostCompensationFactor).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Mixer4DDefault).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederTimeDefault).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederBoostMultiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederEconomyMultiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederNormalMultiplier).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BoilerTempTolerance).HasColumnType("decimal(5,2)");
            entity.Property(e => e.FeederTimeTolerance).HasColumnType("decimal(5,2)");
            entity.Property(e => e.BoilerRetryDelay).HasColumnType("decimal(5,2)");
            entity.Property(e => e.MinTempDiff).HasColumnType("decimal(5,2)");
            entity.Property(e => e.MinMixer4D).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Hysteresis).HasColumnType("decimal(5,2)");
            entity.Property(e => e.HysteresisSafetyThreshold).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempValidationMin).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempValidationMax).HasColumnType("decimal(5,2)");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2");
        });

        // ForecastDataEntity
        modelBuilder.Entity<ForecastDataEntity>(entity =>
        {
            entity.ToTable("ForecastDataCache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Latitude).HasColumnType("decimal(9,6)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(9,6)");
            entity.Property(e => e.CurrentTemp).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ForecastHoursJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.TempDropThreshold).HasColumnType("decimal(5,2)");
            entity.Property(e => e.TempRiseThreshold).HasColumnType("decimal(5,2)");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2");
            entity.HasIndex(e => new { e.Latitude, e.Longitude });
        });

        // ConfigurationChangeLog
        modelBuilder.Entity<ConfigurationChangeLog>(entity =>
        {
            entity.ToTable("ConfigurationChangeLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Timestamp).HasColumnType("datetime2");
            entity.Property(e => e.EntityType).HasMaxLength(50);
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.FieldName).HasMaxLength(100);
            entity.Property(e => e.OldValue).HasMaxLength(500);
            entity.Property(e => e.NewValue).HasMaxLength(500);
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
        });

        // SummerModeLog
        modelBuilder.Entity<SummerModeLog>(entity =>
        {
            entity.ToTable("SummerModeLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Date).HasColumnType("datetime2");
            entity.Property(e => e.ActivatedAt).HasColumnType("datetime2");
            entity.Property(e => e.DeactivatedAt).HasColumnType("datetime2");
            entity.HasIndex(e => e.Date).IsUnique();
        });

        // ApplicationErrorLog
        modelBuilder.Entity<ApplicationErrorLog>(entity =>
        {
            entity.ToTable("ApplicationErrorLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.OccurredAtUtc).HasColumnType("datetime2");
            entity.Property(e => e.Source).HasMaxLength(200);
            entity.Property(e => e.Message).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ExceptionType).HasMaxLength(500);
            entity.Property(e => e.StackTrace).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ExceptionJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ContextJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Severity).HasMaxLength(20);
            entity.Property(e => e.Origin).HasMaxLength(50);
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.HasIndex(e => e.Phase);
            entity.HasIndex(e => e.Source);
            entity.HasIndex(e => e.Origin);
        });
    }
}
