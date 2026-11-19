using AntesDaChuva.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentUpdate> IncidentUpdates => Set<IncidentUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>();
            e.Property(x => x.Neighborhood).HasMaxLength(80);
        });
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });
        modelBuilder.Entity<Incident>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1500).IsRequired();
            e.Property(x => x.Address).HasMaxLength(180).IsRequired();
            e.Property(x => x.Neighborhood).HasMaxLength(80).IsRequired();
            e.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.OwnerId, x.IdempotencyKey }).IsUnique();
            e.HasOne(x => x.Owner).WithMany(x => x.Incidents)
                .HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany(x => x.Incidents)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<IncidentUpdate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PreviousStatus).HasConversion<string>();
            e.Property(x => x.NewStatus).HasConversion<string>();
            e.Property(x => x.Note).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Incident).WithMany(x => x.Updates)
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Actor).WithMany()
                .HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
