using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Entities;

/// <summary>
/// One execution = one RUN of a trigger definition.
/// 
/// INTERVIEW: This is the HISTORY table. Every time a trigger fires,
/// a new execution row is created with status Pending. It transitions:
///   Pending → (runner picks it up) → Success or Failure
/// 
/// The execution tracks timing, result paths, and export status.
/// One DataTriggerDefinition has MANY DataTriggerExecutions (1:N).
/// 
/// Real-world analogy: The trigger says "run weekly sales report."
/// Each Monday's actual run is an execution. Monday Jan 6 = execution 1,
/// Monday Jan 13 = execution 2, etc.
/// </summary>
[Table("data_trigger_execution")]
public class DataTriggerExecution
{
    [Key]
    public Guid DataTriggerExecutionId { get; set; }

    /// <summary>
    /// Groups multiple executions that were triggered by the same event.
    /// E.g., new dataset arrives → 50 triggers fire → all share one InvocationId.
    /// </summary>
    public Guid InvocationId { get; set; }

    public Guid DataTriggerId { get; set; }

    public Guid DatasetId { get; set; }

    public ResultStatus ResultStatus { get; set; } = ResultStatus.Pending;

    public DateTime DateCreated { get; set; }
    public DateTime? DateExecutionStarted { get; set; }
    public DateTime? DateExecutionCompleted { get; set; }
    public int? ExecutionTimeSeconds { get; set; }

    [MaxLength(1024)]
    public string? ResultPath { get; set; }

    [MaxLength(1024)]
    public string? LogPath { get; set; }

    public bool EmailSent { get; set; }

    [ForeignKey(nameof(DataTriggerId))]
    public DataTriggerDefinition DataTriggerDefinition { get; set; } = null!;

    [ForeignKey(nameof(DatasetId))]
    public Dataset Dataset { get; set; } = null!;

    public ICollection<JobQueue> Jobs { get; set; } = new List<JobQueue>();
}

public enum ResultStatus
{
    Pending = 0,
    Success = 1,
    Failure = 2
}
