namespace NaryadAi.Models
{
    public class WorkOrder
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = "Обычный"; 
        public string Status { get; set; } = "Выдан"; 
        public int EquipmentId { get; set; }
        public Equipment? Equipment { get; set; }
        public int? ExecutorId { get; set; } 
        public Employee? Executor { get; set; }
    }
}
