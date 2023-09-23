using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SanBot.Database.Models;

namespace SanBot.Database.Data
{
    public class ApplicationDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectionString = new SqliteConnectionStringBuilder()
            {
                DataSource = "Sanbot.Database.sqlite",
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            optionsBuilder.UseSqlite(connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Persona>()
                .HasIndex(n => n.Handle);
            modelBuilder.Entity<Persona>()
                .HasIndex(n => n.Name);
        }

        public DbSet<Persona> Personas { get; set; } = default!;

    }
}
