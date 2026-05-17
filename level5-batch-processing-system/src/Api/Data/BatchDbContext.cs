using Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Entity Framework DbContext — the bridge between C# objects and PostgreSQL tables.
/// 
/// INTERVIEW: EF Core is an ORM (Object-Relational Mapper). Instead of writing
/// raw SQL, you work with C# objects and EF translates to SQL:
///   C#:  context.Reports.Where(r => r.PanelId == "AU").ToListAsync()
///   SQL: SELECT * FROM report WHERE panel_id = 'AU'
/// 
/// DbSet<T> = a table. Each DbSet maps to one PostgreSQL table.
/// OnModelCreating = where you configure relationships, indexes, constraints.
/// </summary>
public class BatchDbContext : DbContext
{
    public BatchDbContext(DbContextOptions<BatchDbContext> options) : base(options) { }

    public DbSet<Report> Reports => Set<Report>();
    public DbSet<DataTriggerDefinition> DataTriggerDefinitions => Set<DataTriggerDefinition>();
    public DbSet<DataTriggerExecution> DataTriggerExecutions => Set<DataTriggerExecution>();
    public DbSet<JobQueue> JobQueues => Set<JobQueue>();
    public DbSet<Dataset> Datasets => Set<Dataset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Index on JobQueue for fast dequeue: find rows where execution hasn't started
        modelBuilder.Entity<JobQueue>()
            .HasIndex(j => j.DateExecutionStarted)
            .HasFilter("\"DateExecutionStarted\" IS NULL")
            .HasDatabaseName("ix_job_queue_pending");

        // Index on executions by trigger for fast history lookups
        modelBuilder.Entity<DataTriggerExecution>()
            .HasIndex(e => new { e.DataTriggerId, e.DateCreated })
            .HasDatabaseName("ix_execution_trigger_date");

        // Index on executions by status for result orchestrator
        modelBuilder.Entity<DataTriggerExecution>()
            .HasIndex(e => e.ResultStatus)
            .HasDatabaseName("ix_execution_status");

        // Soft delete filter: exclude deleted triggers from default queries
        modelBuilder.Entity<DataTriggerDefinition>()
            .HasQueryFilter(d => d.DateDeleted == null);

        modelBuilder.Entity<Report>()
            .HasQueryFilter(r => r.DateDeleted == null);

        // Seed data so the system works out of the box
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var now = DateTime.UtcNow;

        var report1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var report2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var trigger1Id = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
        var trigger2Id = Guid.Parse("aaaa2222-2222-2222-2222-222222222222");
        var trigger3Id = Guid.Parse("aaaa3333-3333-3333-3333-333333333333");

        modelBuilder.Entity<Report>().HasData(
            new Report
            {
                ReportId = report1Id,
                DisplayName = "Weekly Audience Summary",
                InternalName = "weekly_audience_summary",
                PanelId = "AU",
                AverageExecutionTimeSeconds = 30,
                DateCreated = now
            },
            new Report
            {
                ReportId = report2Id,
                DisplayName = "Monthly Ratings Export",
                InternalName = "monthly_ratings_export",
                PanelId = "AU",
                AverageExecutionTimeSeconds = 120,
                DateCreated = now
            }
        );

        modelBuilder.Entity<DataTriggerDefinition>().HasData(
            new DataTriggerDefinition
            {
                DataTriggerId = trigger1Id,
                ReportId = report1Id,
                TriggerName = "Finance Team Weekly Report",
                PanelId = "AU",
                IsActive = true,
                OutputFormat = "CSV",
                Frequency = "daily",
                EmailEnabled = true,
                EmailTo = "rizul1309@gmail.com",
                Priority = 100,
                DateCreated = now,
                DateLastModified = now
            },
            new DataTriggerDefinition
            {
                DataTriggerId = trigger2Id,
                ReportId = report1Id,
                TriggerName = "Marketing Dashboard Feed",
                PanelId = "AU",
                IsActive = true,
                OutputFormat = "JSON",
                Frequency = "daily",
                EmailEnabled = false,
                Priority = 500,
                DateCreated = now,
                DateLastModified = now
            },
            new DataTriggerDefinition
            {
                DataTriggerId = trigger3Id,
                ReportId = report2Id,
                TriggerName = "Monthly Client Export",
                PanelId = "AU",
                IsActive = true,
                OutputFormat = "Excel",
                Frequency = "monthly",
                EmailEnabled = true,
                EmailTo = "rizul1309@gmail.com",
                Priority = 200,
                DateCreated = now,
                DateLastModified = now
            }
        );
    }
}
