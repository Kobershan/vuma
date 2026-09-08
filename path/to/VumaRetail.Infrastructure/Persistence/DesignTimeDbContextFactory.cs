using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VumaRetail.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VumaRetailDbContext>
{
    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.NewGuid();
    }

    public VumaRetailDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VumaRetailDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=VumaRetail;Trusted_Connection=True;");
        optionsBuilder.UseLazyLoadingProxies();

        return new VumaRetailDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }
}
