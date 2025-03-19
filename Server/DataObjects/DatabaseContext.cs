using Microsoft.EntityFrameworkCore;

namespace Server.DataObjects;

// Entity Framework database context for the application
// Provides access to all database tables/entities
public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }

    // USPS (United States Postal Service) data
    public DbSet<UspsBundle> UspsBundles { get; set; }
    public DbSet<UspsFile> UspsFiles { get; set; }

    // Parascript data
    public DbSet<ParaBundle> ParaBundles { get; set; }
    public DbSet<ParaFile> ParaFiles { get; set; }

    // Royal Mail data
    public DbSet<RoyalBundle> RoyalBundles { get; set; }
    public DbSet<RoyalFile> RoyalFiles { get; set; }

    // PAF (Postcode Address File) keys
    public DbSet<PafKey> PafKeys { get; set; }
}
