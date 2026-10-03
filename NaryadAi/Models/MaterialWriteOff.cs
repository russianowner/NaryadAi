namespace NaryadAi.Models;

public class MaterialWriteOff
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public string Material { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
}
