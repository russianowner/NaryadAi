namespace NaryadAi.Models;

public class WorkOrderPhoto
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public string Type { get; set; } = "После";
    public string FilePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public int? AuthorId { get; set; }
    public Employee? Author { get; set; }
}
