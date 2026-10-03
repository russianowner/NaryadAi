using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;

namespace NaryadAi.Services;

public record EmployeeRating(int EmployeeId, decimal Score, int ClosedOrders, decimal OnTimeRate, decimal ReworkRate, decimal RejectionRate);

public class EmployeeRatingService(AppDbContext db)
{
    public async Task<EmployeeRating> CalculateAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        var assignedOrders = await db.WorkOrders.Where(x => x.ExecutorId == employeeId)
            .Include(x => x.Events).ToListAsync(cancellationToken);
        var orders = await db.WorkOrders.Where(x => x.ExecutorId == employeeId && x.ClosedAt != null)
            .Include(x => x.AiEvaluations).Include(x => x.Events).ToListAsync(cancellationToken);
        if (assignedOrders.Count == 0 || orders.Count == 0) return new EmployeeRating(employeeId, 0, orders.Count, 0, 0, 0);

        var aiScores = orders.SelectMany(x => x.AiEvaluations).Select(x => x.Score).ToList();
        var masterScores = orders.SelectMany(x => x.AiEvaluations).Where(x => x.MasterApproved == true).Select(x => x.Score).ToList();
        var quality = aiScores.Count > 0 ? aiScores.Average() : 70m;
        if (masterScores.Count > 0) quality = (quality + masterScores.Average()) / 2m;
        var onTimeRate = (decimal)orders.Count(x => x.ClosedAt <= x.Deadline) / orders.Count;
        var reworkRate = (decimal)orders.Count(x => x.Events.Any(e => e.Action == WorkOrderStates.Rework)) / orders.Count;
        var rejectionRate = (decimal)assignedOrders.Count(x => x.RejectedAt != null) / assignedOrders.Count;
        var score = Math.Clamp(decimal.Round(quality * .6m + onTimeRate * 100m * .25m
            - reworkRate * 100m * .1m - rejectionRate * 100m * .05m, 1), 0, 100);
        return new EmployeeRating(employeeId, score, orders.Count, decimal.Round(onTimeRate * 100, 1),
            decimal.Round(reworkRate * 100, 1), decimal.Round(rejectionRate * 100, 1));
    }
}
