using Microsoft.EntityFrameworkCore;

namespace DataObjects;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }

    public DbSet<UspsBundle> UspsBundles { get; set; }
    public DbSet<UspsFile> UspsFiles { get; set; }

    public DbSet<ParaBundle> ParaBundles { get; set; }
    public DbSet<ParaFile> ParaFiles { get; set; }

    public DbSet<RoyalBundle> RoyalBundles { get; set; }
    public DbSet<RoyalFile> RoyalFiles { get; set; }

    public DbSet<PafKey> PafKeys { get; set; }
}
