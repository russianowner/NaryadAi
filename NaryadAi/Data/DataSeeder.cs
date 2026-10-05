using NaryadAi.Models;

namespace NaryadAi.Data;

public static class DataSeeder
{
    public static void Initialize(AppDbContext context)
    {
        if (context.Employees.Any())
            return;

        var admin = new Employee
        {
            FullName = "Системный Администратор",
            Role = "Admin",
            Login = "admin",
            PasswordHash = "admin",
            Specialty = "ИТ",
            Status = "В сети"
        };

        context.Employees.Add(admin);
        context.SaveChanges();
    }
}