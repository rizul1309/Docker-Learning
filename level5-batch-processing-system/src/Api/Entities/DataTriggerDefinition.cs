using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Entities;

/// <summary>
/// A trigger definition = "run THIS report whenever new data arrives."
/// 
/// INTERVIEW: This is the CONFIG table. Users create these once and they
/// fire automatically. Think of it like a cron job definition — it describes
/// WHAT to run and WHEN, but doesn't represent a single run.
/// 
/// Real-world analogy: "Every Monday, generate the weekly sales report
/// and email it to the finance team." That's a DataTriggerDefinition.
/// </summary>
[Table("data_trigger_definition")]
public class DataTriggerDefinition
{
    [Key]
    public Guid DataTriggerId { get; set; }

    public Guid ReportId { get; set; }

    [Required]
    [MaxLength(256)]
    public string TriggerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string PanelId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [MaxLength(32)]
    public string OutputFormat { get; set; } = "CSV";

    [MaxLength(32)]
    public string Frequency { get; set; } = "daily";

    public bool EmailEnabled { get; set; }

    [MaxLength(512)]
    public string? EmailTo { get; set; }

    public int Priority { get; set; } = 1000;

    public DateTime DateCreated { get; set; }
    public DateTime DateLastModified { get; set; }
    public DateTime? DateDeleted { get; set; }

    [ForeignKey(nameof(ReportId))]
    public Report Report { get; set; } = null!;

    public ICollection<DataTriggerExecution> Executions { get; set; } = new List<DataTriggerExecution>();
}
