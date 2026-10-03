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
        public int? SiteId { get; set; }
        public Site? Site { get; set; }

        public DateTime? AcceptedAt { get; set; }
        public DateTime? QueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? AiReviewStartedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public DateTime? ReworkRequestedAt { get; set; }

        public ICollection<WorkOrderEvent> Events { get; set; } = new List<WorkOrderEvent>();
        public ICollection<WorkOrderPhoto> Photos { get; set; } = new List<WorkOrderPhoto>();
        public ICollection<MaterialWriteOff> Materials { get; set; } = new List<MaterialWriteOff>();
        public ICollection<AiEvaluation> AiEvaluations { get; set; } = new List<AiEvaluation>();
    }
}
