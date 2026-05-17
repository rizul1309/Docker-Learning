using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Entities;

/// <summary>
/// A dataset = a snapshot of data that arrived and triggered executions.
/// 
/// INTERVIEW: When new audience data lands (e.g., "January 2026 ratings"),
/// a Dataset row is created. All triggers for that panel fire against
/// this specific dataset. This gives you traceability: "execution X
/// ran against dataset Y."
/// </summary>
[Table("dataset")]
public class Dataset
{
    [Key]
    public Guid DatasetId { get; set; }

    [Required]
    [MaxLength(64)]
    public string PanelId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ExternalDatasetId { get; set; }

    public DateTime DateCreated { get; set; }
    public DateTime? DateCompleted { get; set; }
}
