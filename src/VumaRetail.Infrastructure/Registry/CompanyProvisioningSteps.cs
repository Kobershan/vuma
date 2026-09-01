using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Provisioning step that creates the isolated company database.</summary>
public sealed class CreateCompanyDatabaseStep(ICompanyDatabaseCreator creator) : ICompanyProvisioningStep
{
    public string Name => "create-database";
    public CompanyLifecycleState CompletedState => CompanyLifecycleState.Provisioning;

    public Task ExecuteAsync(Company company, CancellationToken cancellationToken = default)
        => creator.CreateAsync(company, cancellationToken);
}

/// <summary>Provisioning step that migrates the isolated company database.</summary>
public sealed class MigrateCompanyDatabaseStep(ICompanyDatabaseMigrator migrator) : ICompanyProvisioningStep
{
    public string Name => "migrate";
    public CompanyLifecycleState CompletedState => CompanyLifecycleState.Seeding;

    public Task ExecuteAsync(Company company, CancellationToken cancellationToken = default)
        => migrator.MigrateAsync(company, cancellationToken);
}

/// <summary>Provisioning step that seeds company-owned accounting and operating defaults.</summary>
public sealed class SeedCompanyDataStep(ICompanyDataSeeder seeder) : ICompanyProvisioningStep
{
    public string Name => "seed";
    public CompanyLifecycleState CompletedState => CompanyLifecycleState.Seeding;

    public Task ExecuteAsync(Company company, CancellationToken cancellationToken = default)
        => seeder.SeedAsync(company, cancellationToken);
}

/// <summary>Provisioning step that registers the encrypted connection reference in the registry.</summary>
public sealed class RegisterCompanyConnectionStep(ICompanyConnectionRegistrar registrar) : ICompanyProvisioningStep
{
    public string Name => "register-connection";
    public CompanyLifecycleState CompletedState => CompanyLifecycleState.Registered;

    public async Task ExecuteAsync(Company company, CancellationToken cancellationToken = default)
    {
        string secretReference = await registrar.RegisterAsync(company, cancellationToken).ConfigureAwait(false);
        company.SetConnectionSecretRef(secretReference);
    }
}
