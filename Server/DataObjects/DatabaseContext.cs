using Microsoft.EntityFrameworkCore;

namespace Server.DataObjects;

// Entity Framework database context for the application. Provides access to all database tables/entities
public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }
    
    public DbSet<Bundle> Bundles { get; set; }
    public DbSet<DataFile> Files { get; set; }
    public DbSet<PafKey> PafKeys { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure relationships
        modelBuilder.Entity<DataFile>()
            .HasOne(f => f.Bundle)
            .WithMany(b => b.Files)
            .HasForeignKey(f => f.BundleId);
    }
}
