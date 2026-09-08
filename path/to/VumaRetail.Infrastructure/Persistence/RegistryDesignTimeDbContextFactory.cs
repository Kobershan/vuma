using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VumaRetail.Infrastructure.Persistence;

public sealed class RegistryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<VumaRegistryDbContext>
{
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.NewGuid();
    }

    public VumaRegistryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VumaRegistryDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=VumaRegistry;Trusted_Connection=True;");
        optionsBuilder.UseLazyLoadingProxies();

        return new VumaRegistryDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }
}
