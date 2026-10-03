namespace NaryadAi.Models
{
    public class WorkOrder
    {
        public int Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Type { get; set; } = "Плановый";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime Deadline { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Priority { get; set; } = "Обычный";
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = "Выдан";
        public int EquipmentId { get; set; }
        public Equipment? Equipment { get; set; }
        public int? ExecutorId { get; set; }
        public Employee? Executor { get; set; }
        public int? MasterId { get; set; }
        public Employee? Master { get; set; }
        public string? CreationComment { get; set; }
        public string? PhotoBeforePath { get; set; }
        public string? CloseWorksDone { get; set; }
        public string? CloseFaultCode { get; set; }
        public string? CloseComment { get; set; }
        public string? PhotoAfterPath { get; set; }
    }
}