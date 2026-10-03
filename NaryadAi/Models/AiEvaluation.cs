namespace NaryadAi.Models;

public class AiEvaluation
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool? MasterApproved { get; set; }
    public string? MasterComment { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}
