using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NaryadAi.Data;
using NaryadAi.Models;

namespace NaryadAi.Services;

public sealed record ApiAccessToken(string AccessToken, DateTime ExpiresAtUtc, string TokenType = "Bearer");

public sealed class ApiAccessTokenService
{
    private readonly AppDbContext db;
    private readonly IDataProtector protector;

    public ApiAccessTokenService(AppDbContext db, IDataProtectionProvider protectionProvider)
    {
        this.db = db;
        protector = protectionProvider.CreateProtector("NaryadAi.ApiAccessToken.v1");
    }

    public async Task<(Employee? Employee, ApiAccessToken? Token)> AuthenticateAsync(
        string? login, string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret)) return (null, null);

        var employee = string.IsNullOrWhiteSpace(login)
            ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.PinCode == secret, cancellationToken)
            : await db.Employees.AsNoTracking().FirstOrDefaultAsync(
                x => x.Login == login && x.PasswordHash == secret, cancellationToken);
        if (employee is null) return (null, null);

        var expiresAtUtc = DateTime.UtcNow.AddHours(8);
        var ticket = new Ticket(employee.Id, expiresAtUtc);
        var token = protector.Protect(JsonSerializer.Serialize(ticket));
        return (employee, new ApiAccessToken(token, expiresAtUtc));
    }

    public async Task<Employee?> ResolveAsync(string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var protectedTicket = authorizationHeader[7..].Trim();
            var ticketJson = protector.Unprotect(protectedTicket);
            var ticket = JsonSerializer.Deserialize<Ticket>(ticketJson);
            if (ticket is null || ticket.EmployeeId <= 0 || ticket.ExpiresAtUtc <= DateTime.UtcNow) return null;
            return await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticket.EmployeeId, cancellationToken);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private sealed record Ticket(int EmployeeId, DateTime ExpiresAtUtc);
}
