using Microsoft.EntityFrameworkCore;
using Revisu.Domain.Entities;

namespace Revisu.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Obras> Obras { get; set; }
        public DbSet<Generos> Generos { get; set; }
        public DbSet<Avaliacoes> Avaliacoes { get; set; }
        public DbSet<Biblioteca> Biblioteca { get; set; }
        public DbSet<Elenco> Elencos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Obras>()
                .HasMany(o => o.Elenco)
                .WithMany(e => e.Obras)
                .UsingEntity(j => j.ToTable("ElencoObras"));
        }

    }
}
