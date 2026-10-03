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
        public DbSet<Site> Sites { get; set; }
        public DbSet<WorkOrderEvent> WorkOrderEvents { get; set; }
        public DbSet<WorkOrderPhoto> WorkOrderPhotos { get; set; }
        public DbSet<MaterialWriteOff> MaterialWriteOffs { get; set; }
        public DbSet<AiEvaluation> AiEvaluations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<WorkOrder>().HasIndex(x => x.Number).IsUnique();
            modelBuilder.Entity<WorkOrder>().HasIndex(x => new { x.Status, x.Deadline });
            modelBuilder.Entity<Site>().HasIndex(x => x.Name).IsUnique();
            modelBuilder.Entity<Equipment>().HasOne(x => x.Site).WithMany(x => x.Equipments)
                .HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<WorkOrder>().HasOne(x => x.Site).WithMany()
                .HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<WorkOrderEvent>().HasIndex(x => new { x.WorkOrderId, x.OccurredAt });
            modelBuilder.Entity<WorkOrderEvent>().HasOne(x => x.Actor).WithMany()
                .HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<WorkOrderEvent>().HasOne(x => x.WorkOrder).WithMany(x => x.Events)
                .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WorkOrderPhoto>().HasOne(x => x.Author).WithMany()
                .HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<WorkOrderPhoto>().HasOne(x => x.WorkOrder).WithMany(x => x.Photos)
                .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<MaterialWriteOff>().HasOne(x => x.WorkOrder).WithMany(x => x.Materials)
                .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<MaterialWriteOff>().Property(x => x.Quantity).HasPrecision(12, 3);
            modelBuilder.Entity<AiEvaluation>().HasOne(x => x.WorkOrder).WithMany(x => x.AiEvaluations)
                .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AiEvaluation>().Property(x => x.Score).HasPrecision(5, 2);
        }
    }
}
