using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TenderDeduplication.Models;

namespace TenderDeduplication.Data
{
    /// <summary>
    /// The Entity Framework database context for the application.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        // DbSets for each specific tender type.
        public DbSet<eTender> eTenders { get; set; }
        public DbSet<EskomTender> EskomTenders { get; set; }
        public DbSet<SanralTender> SanralTenders { get; set; }
        public DbSet<TransnetTender> TransnetTenders { get; set; }
        public DbSet<SarsTender> SarsTenders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Table-Per-Type (TPT) inheritance mapping.
            // This ensures EF Core knows which table corresponds to which C# class.
            modelBuilder.Entity<BaseTender>().ToTable("BaseTender");
            modelBuilder.Entity<eTender>().ToTable("eTender");
            modelBuilder.Entity<EskomTender>().ToTable("EskomTender");
            modelBuilder.Entity<SanralTender>().ToTable("SanralTender");
            modelBuilder.Entity<TransnetTender>().ToTable("TransnetTender");
            modelBuilder.Entity<SarsTender>().ToTable("SarsTender");
        }
    }
}
