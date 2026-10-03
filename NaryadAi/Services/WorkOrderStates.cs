namespace NaryadAi.Services;

public static class WorkOrderStates
{
    public const string Issued = "Выдан";
    public const string Accepted = "Принят в работу";
    public const string Queued = "В очереди";
    public const string Rejected = "Отклонён";
    public const string InProgress = "В работе";
    public const string Paused = "Приостановлен";
    public const string Completed = "Исполнено";
    public const string AiReview = "Проверка ИИ";
    public const string Rework = "На доработку";
    public const string Closed = "Закрыт";
}
