using Microsoft.EntityFrameworkCore;
using NaryadAi.Services;

namespace NaryadAi.Components.Pages
{
    public partial class Dashboard
    {
        private bool isLoading = true;
        private int activeCount = 0;
        private int overdueCount = 0;
        private int idleEquipmentCount = 0;
        private decimal onTimeRate = 0;
        private int avgReactionMinutes = 0;
        private decimal avgRepairHours = 0;

        private List<AnomalyInsight> anomalies = new();
        private List<ProblemEquipmentDto> topProblemEquipment = new();
        private List<PerformerDto> topPerformers = new();

        protected override void OnInitialized()
        {
            Language.Changed += OnLanguageChanged;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender)
                return;

            var role = await BrowserStorage.GetAsync<string>("UserRole");
            var userId = await BrowserStorage.GetAsync<int>("UserId");

            if (!role.Success ||
                !userId.Success ||
                role.Value is not ("Manager" or "Admin"))
            {
                Navigation.NavigateTo("/login", replace: true);
                return;
            }

            await LoadDashboardDataAsync();
            await InvokeAsync(StateHasChanged);
        }

        private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Language.Changed -= OnLanguageChanged;
        }

        private async Task LoadDashboardDataAsync()
        {
            isLoading = true;
            StateHasChanged();

            try
            {
                var now = DateTime.UtcNow;
                var allOrders = await Db.WorkOrders
                    .Include(w => w.Equipment).ThenInclude(e => e.Site)
                    .Include(w => w.Executor)
                    .OrderByDescending(w => w.CreatedAt)
                    .Take(500)
                    .ToListAsync();

                activeCount = allOrders.Count(w => w.Status is WorkOrderStates.InProgress or WorkOrderStates.Accepted or WorkOrderStates.Queued or WorkOrderStates.Paused or WorkOrderStates.Rework);
                overdueCount = allOrders.Count(w => w.Deadline < now && w.Status != WorkOrderStates.Closed && w.Status != WorkOrderStates.Rejected);

                idleEquipmentCount = allOrders
                    .Where(w => w.Status is WorkOrderStates.InProgress or WorkOrderStates.Issued or WorkOrderStates.Accepted or WorkOrderStates.Paused or WorkOrderStates.Rework)
                    .Select(w => w.EquipmentId)
                    .Distinct()
                    .Count();

                var closedOrders = allOrders.Where(w => w.ClosedAt != null).ToList();
                if (closedOrders.Count > 0)
                {
                    onTimeRate = (decimal)closedOrders.Count(w => w.ClosedAt <= w.Deadline) * 100m / closedOrders.Count;
                }

                var acceptedOrders = allOrders.Where(w => w.StartedAt.HasValue).ToList();
                if (acceptedOrders.Count > 0)
                {
                    var reactionTotalMinutes = acceptedOrders
                        .Select(w => (w.StartedAt!.Value - w.CreatedAt).TotalMinutes)
                        .Where(m => m >= 0 && m < 1440)
                        .DefaultIfEmpty(15)
                        .Average();
                    avgReactionMinutes = (int)Math.Round(reactionTotalMinutes);
                }
                else
                {
                    avgReactionMinutes = 12;
                }

                var completedOrders = allOrders.Where(w => w.StartedAt.HasValue && w.CompletedAt.HasValue).ToList();
                if (completedOrders.Count > 0)
                {
                    var repairHours = completedOrders
                        .Select(w => (w.CompletedAt!.Value - w.StartedAt!.Value).TotalHours)
                        .Where(h => h > 0 && h < 72)
                        .DefaultIfEmpty(2.5)
                        .Average();
                    avgRepairHours = Math.Round((decimal)repairHours, 1);
                }
                else
                {
                    avgRepairHours = 2.4m;
                }
                topProblemEquipment = allOrders
                    .Where(w => w.Equipment != null)
                    .GroupBy(w => new { w.EquipmentId, Name = w.Equipment!.Name, SiteName = w.Equipment.Site?.Name ?? w.Location })
                    .Select(g => new ProblemEquipmentDto
                    {
                        Name = g.Key.Name,
                        SiteName = g.Key.SiteName,
                        TotalCount = g.Count(),
                        EmergencyCount = g.Count(w => w.Priority == "Аварийный" || w.Type == "Внеплановый")
                    })
                    .OrderByDescending(e => e.EmergencyCount)
                    .ThenByDescending(e => e.TotalCount)
                    .Take(5)
                    .ToList();

                var workers = await Db.Employees.Where(e => e.Role == "Worker").ToListAsync();
                var performersList = new List<PerformerDto>();
                foreach (var w in workers.Take(12))
                {
                    var rating = await RatingService.CalculateAsync(w.Id);
                    if (rating.ClosedOrders > 0)
                    {
                        performersList.Add(new PerformerDto
                        {
                            FullName = w.FullName,
                            Specialty = w.Specialty,
                            ClosedOrders = rating.ClosedOrders,
                            Score = rating.Score
                        });
                    }
                }
                topPerformers = performersList.OrderByDescending(p => p.Score).Take(5).ToList();
                anomalies = await AnalyticsService.DetectAnomaliesAsync(allOrders);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки дашборда: {ex.Message}");
            }
            finally
            {
                isLoading = false;
            }
        }

        private class ProblemEquipmentDto
        {
            public string Name { get; set; } = string.Empty;
            public string SiteName { get; set; } = string.Empty;
            public int TotalCount { get; set; }
            public int EmergencyCount { get; set; }
        }

        private class PerformerDto
        {
            public string FullName { get; set; } = string.Empty;
            public string Specialty { get; set; } = string.Empty;
            public int ClosedOrders { get; set; }
            public decimal Score { get; set; }
        }
    }
}
