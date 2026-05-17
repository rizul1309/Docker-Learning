using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Entities;

/// <summary>
/// A report template = the definition of what kind of report can be generated.
/// 
/// INTERVIEW: Reports are uploaded by users (as .aqz files in the real system).
/// A DataTriggerDefinition references a Report — "when new data arrives,
/// run THIS report." Reports belong to ReportContainers (folders).
/// </summary>
[Table("report")]
public class Report
{
    [Key]
    public Guid ReportId { get; set; }

    [Required]
    [MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string InternalName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? PanelId { get; set; }

    public int? AverageExecutionTimeSeconds { get; set; }

    public DateTime DateCreated { get; set; }
    public DateTime? DateDeleted { get; set; }

    public ICollection<DataTriggerDefinition> TriggerDefinitions { get; set; } = new List<DataTriggerDefinition>();
}
