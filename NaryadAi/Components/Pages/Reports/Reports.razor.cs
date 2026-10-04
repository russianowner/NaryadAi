using ClosedXML.Excel;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using MudBlazor;
using NaryadAi.Data;
using NaryadAi.Models;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages.Reports;

public partial class Reports : ComponentBase
{
    [Inject] private AppDbContext DbContext { get; set; } = default!;
    [Inject] private Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage.ProtectedLocalStorage BrowserStorage { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime JavaScript { get; set; } = default!;
    [Inject] private EmployeeRatingService RatingService { get; set; } = default!;
    [Inject] private AiAnalyticsService AiAnalytics { get; set; } = default!;
    [Inject] private AppLanguageService Language { get; set; } = default!;

    private bool isAuthorized;
    private bool isBusy;
    private bool reportLoaded;
    private DateTime? startDate = DateTime.Today.AddDays(-7);
    private DateTime? endDate = DateTime.Today;
    private List<WorkOrder> reportOrders = [];
    private List<Equipment> reportEquipment = [];
    private List<(Employee Worker, EmployeeRating Rating)> reportRatings = [];
    private string aiSummary = "Генерация сводки...";
    private int IdleEquipmentCount => reportEquipment.Count(e =>
        !reportOrders.Any(o => o.EquipmentId == e.Id && IsActive(o.Status)));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        var role = await BrowserStorage.GetAsync<string>("UserRole");
        var userId = await BrowserStorage.GetAsync<int>("UserId");
        if (!role.Success || !userId.Success || role.Value is not ("Master" or "Admin" or "Manager"))
        {
            Navigation.NavigateTo("/login");
            return;
        }

        isAuthorized = true;
        await LoadReport();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadReport()
    {
        if (startDate is null || endDate is null || startDate.Value.Date > endDate.Value.Date)
        {
            Snackbar.Add(Language.T("Проверьте период отчёта"), Severity.Warning);
            return;
        }

        isBusy = true;
        try
        {
            var fromUtc = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
            var untilUtc = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            reportOrders = await DbContext.WorkOrders.AsNoTracking()
                .AsSplitQuery()
                .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt < untilUtc)
                .Include(x => x.Site)
                .Include(x => x.Equipment)
                .Include(x => x.Executor)
                .Include(x => x.Master)
                .Include(x => x.Materials)
                .Include(x => x.AiEvaluations)
                .Include(x => x.Events)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
            reportEquipment = await DbContext.Equipments.AsNoTracking().ToListAsync();
            
            var workers = await DbContext.Employees.AsNoTracking().Where(x => x.Role == "Worker").OrderBy(x => x.FullName).ToListAsync();
            reportRatings.Clear();
            foreach (var worker in workers)
            {
                var r = await RatingService.CalculateAsync(worker.Id);
                reportRatings.Add((worker, r));
            }
            reportRatings = reportRatings.OrderByDescending(r => r.Rating.Score).ToList();
            
            aiSummary = "Генерация сводки...";
            reportLoaded = true;
            _ = InvokeAsync(StateHasChanged);
            aiSummary = await AiAnalytics.GenerateShiftSummaryAsync(reportOrders);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"{Language.T("Не удалось загрузить отчёт")}: {ex.Message}", Severity.Error);
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task ExportExcel()
    {
        if (reportOrders.Count == 0) return;

        isBusy = true;
        try
        {
            using var workbook = new XLWorkbook();
            AddOrdersSheet(workbook);
            AddShiftSummarySheet(workbook);
            await AddRatingsSheetAsync(workbook);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"NaryadAI_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
            await JavaScript.InvokeVoidAsync("naryadAiDownload", fileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Convert.ToBase64String(stream.ToArray()));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"{Language.T("Не удалось сформировать Excel")}: {ex.Message}", Severity.Error);
        }
        finally
        {
            isBusy = false;
        }
    }

    private void AddOrdersSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Наряды");
        var headers = new[]
        {
            "Номер", "Создан", "Тип", "Статус", "Приоритет", "Участок", "Оборудование", "Инв. номер",
            "Исполнитель", "Бригада", "Мастер", "Срок", "Принят", "Начат", "Исполнен", "Закрыт",
            "Просрочен", "Шифр неисправности", "Выполненные работы", "Вердикт ИИ", "Балл ИИ",
            "Материалы", "Комментарий", "Причина отказа/отмены"
        };
        WriteHeader(sheet, headers);

        for (var i = 0; i < reportOrders.Count; i++)
        {
            var order = reportOrders[i];
            var row = i + 2;
            var evaluation = order.AiEvaluations.OrderByDescending(x => x.EvaluatedAt).FirstOrDefault();
            var materials = string.Join("; ", order.Materials.Select(x => $"{x.Material}: {x.Quantity} {x.Unit}"));
            var reason = order.Events.OrderByDescending(x => x.OccurredAt)
                .FirstOrDefault(x => x.Action is "Отклонён" or "Отменён")?.Comment;
            object?[] values =
            [
                order.Number, order.CreatedAt.ToLocalTime(), order.Type, order.Status, order.Priority,
                order.Site?.Name ?? order.Location, order.Equipment?.Name, order.Equipment?.InventoryNumber,
                order.Executor?.FullName, order.Executor?.Brigade, order.Master?.FullName,
                order.Deadline.ToLocalTime(), Local(order.AcceptedAt), Local(order.StartedAt), Local(order.CompletedAt),
                Local(order.ClosedAt), IsOverdue(order) ? "Да" : "Нет", order.CloseFaultCode, order.CloseWorksDone,
                evaluation?.Verdict, evaluation?.Score, materials, order.CloseComment ?? order.CreationComment, reason
            ];
            WriteRow(sheet, row, values);
        }

        FormatSheet(sheet, headers.Length, reportOrders.Count + 1);
    }

    private void AddShiftSummarySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Сводка смен");
        var headers = new[] { "Дата", "Мастер", "Выдано нарядов", "Закрыто", "Просрочено", "Активно", "Оборудование в простое" };
        WriteHeader(sheet, headers);

        var groups = reportOrders.GroupBy(x => new
        {
            Date = x.CreatedAt.ToLocalTime().Date,
            Master = x.Master?.FullName ?? "Не указан"
        }).OrderBy(x => x.Key.Date).ThenBy(x => x.Key.Master).ToList();

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            object?[] values =
            [
                group.Key.Date, group.Key.Master, group.Count(), group.Count(x => x.ClosedAt != null),
                group.Count(IsOverdue), group.Count(x => IsActive(x.Status)),
                IdleEquipmentCount
            ];
            WriteRow(sheet, i + 2, values);
        }

        FormatSheet(sheet, headers.Length, groups.Count + 1);
    }

    private async Task AddRatingsSheetAsync(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("Рейтинг работников");
        var headers = new[] { "Работник", "Бригада", "Рейтинг", "Закрыто нарядов", "В срок, %", "Доработки, %", "Отказы, %" };
        WriteHeader(sheet, headers);

        var workers = await DbContext.Employees.AsNoTracking().Where(x => x.Role == "Worker")
            .OrderBy(x => x.FullName).ToListAsync();
        for (var i = 0; i < workers.Count; i++)
        {
            var worker = workers[i];
            var rating = await RatingService.CalculateAsync(worker.Id);
            object?[] values =
            [worker.FullName, worker.Brigade, rating.Score, rating.ClosedOrders,
                rating.OnTimeRate, rating.ReworkRate, rating.RejectionRate];
            WriteRow(sheet, i + 2, values);
        }

        FormatSheet(sheet, headers.Length, workers.Count + 1);
    }

    private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) sheet.Cell(1, i + 1).Value = headers[i];
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<object?> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is DateTime date) sheet.Cell(row, i + 1).Value = date;
            else if (values[i] is decimal number) sheet.Cell(row, i + 1).Value = number;
            else if (values[i] is int integer) sheet.Cell(row, i + 1).Value = integer;
            else sheet.Cell(row, i + 1).Value = values[i]?.ToString() ?? string.Empty;
        }
    }

    private static void FormatSheet(IXLWorksheet sheet, int columnCount, int rowCount)
    {
        var used = sheet.Range(1, 1, Math.Max(rowCount, 1), columnCount);
        used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        used.Style.Alignment.WrapText = true;
        var header = sheet.Range(1, 1, 1, columnCount);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#176B62");
        sheet.SheetView.FreezeRows(1);
        if (rowCount > 1) sheet.Range(1, 1, rowCount, columnCount).SetAutoFilter();
        sheet.Columns(1, columnCount).AdjustToContents(1, Math.Min(rowCount, 300));
        sheet.Column(1).Width = Math.Min(sheet.Column(1).Width, 24);
    }

    private static DateTime? Local(DateTime? value) => value?.ToLocalTime();

    private static bool IsActive(string status) => status is "Выдан" or "Принят в работу" or "В очереди"
        or "В работе" or "Приостановлен" or "Проверка ИИ" or "На доработку";

    private static bool IsOverdue(WorkOrder order) => order.Deadline < DateTime.UtcNow
        && order.ClosedAt is null && order.CancelledAt is null && order.RejectedAt is null;
}
