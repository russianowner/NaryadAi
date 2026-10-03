using Microsoft.EntityFrameworkCore;
using NaryadAi.Models;

namespace NaryadAi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Equipment> Equipments { get; set; }
        public DbSet<WorkOrder> WorkOrders { get; set; }

        public DbSet<ReferenceItem> ReferenceItems { get; set; }
    }
}