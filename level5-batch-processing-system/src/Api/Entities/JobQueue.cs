using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Api.Entities;

/// <summary>
/// A job queue entry = a unit of work waiting to be picked up by a runner.
/// 
/// INTERVIEW: This is the WORK QUEUE table. It's the bridge between
/// "we decided to run this report" (execution) and "a runner is actually
/// processing it." The lifecycle:
///   1. Execution created → JobQueue row inserted (DateExecutionStarted = null)
///   2. Runner calls GET /jobs/next → row locked (FOR UPDATE SKIP LOCKED),
///      DateExecutionStarted set to now
///   3. Runner finishes → POST /jobs/{id} → row DELETED, execution updated
/// 
/// WHY DELETE instead of soft-delete? The JobQueue table is a HOT table —
/// runners poll it constantly. Keeping completed rows would slow down the
/// dequeue query. Completed state lives in DataTriggerExecution, not here.
/// 
/// INTERVIEW: "FOR UPDATE SKIP LOCKED" is the key pattern here.
/// It's PostgreSQL's way of implementing a fair, concurrent work queue:
///   - FOR UPDATE: lock the row so no other runner can grab it
///   - SKIP LOCKED: if a row is already locked, skip it and grab the next one
///   - This means 10 runners can dequeue simultaneously without conflicts
/// </summary>
[Table("job_queue")]
public class JobQueue
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long JobQueueId { get; set; }

    public Guid DataTriggerExecutionId { get; set; }

    [MaxLength(64)]
    public string PanelId { get; set; } = string.Empty;

    public DateTime DateCreated { get; set; }
    public DateTime? DateExecutionStarted { get; set; }

    [ForeignKey(nameof(DataTriggerExecutionId))]
    public DataTriggerExecution DataTriggerExecution { get; set; } = null!;
}
