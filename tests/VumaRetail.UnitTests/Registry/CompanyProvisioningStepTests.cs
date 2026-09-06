using NSubstitute;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Registry;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class CompanyProvisioningStepTests
{
    [Fact]
    public async Task Ordered_steps_delegate_to_their_idempotent_adapters()
    {
        var company = Company.Create(Guid.NewGuid(), "one", "One", "One", "ZAR", "en-ZA", "ONE");
        var creator = Substitute.For<ICompanyDatabaseCreator>();
        var migrator = Substitute.For<ICompanyDatabaseMigrator>();
        var seeder = Substitute.For<ICompanyDataSeeder>();
        var registrar = Substitute.For<ICompanyConnectionRegistrar>();
        registrar.RegisterAsync(company, Arg.Any<CancellationToken>()).Returns("secret://one");

        var steps = new ICompanyProvisioningStep[]
        {
            new CreateCompanyDatabaseStep(creator),
            new MigrateCompanyDatabaseStep(migrator),
            new SeedCompanyDataStep(seeder),
            new RegisterCompanyConnectionStep(registrar),
        };

        steps.Select(step => step.Name).Should().Equal("create-database", "migrate", "seed", "register-connection");
        steps.Select(step => step.CompletedState).Should().Equal(
            CompanyLifecycleState.Provisioning,
            CompanyLifecycleState.Seeding,
            CompanyLifecycleState.Seeding,
            CompanyLifecycleState.Registered);

        foreach (var step in steps)
        {
            await step.ExecuteAsync(company);
        }

        await creator.Received(1).CreateAsync(company, Arg.Any<CancellationToken>());
        await migrator.Received(1).MigrateAsync(company, Arg.Any<CancellationToken>());
        await seeder.Received(1).SeedAsync(company, Arg.Any<CancellationToken>());
        await registrar.Received(1).RegisterAsync(company, Arg.Any<CancellationToken>());
        company.ConnectionSecretRef.Should().Be("secret://one");
    }

    [Fact]
    public async Task Registration_rejects_an_empty_secret_reference()
    {
        var company = Company.Create(Guid.NewGuid(), "one", "One", "One", "ZAR", "en-ZA", "ONE");
        var registrar = Substitute.For<ICompanyConnectionRegistrar>();
        registrar.RegisterAsync(company, Arg.Any<CancellationToken>()).Returns(" ");

        var act = () => new RegisterCompanyConnectionStep(registrar).ExecuteAsync(company);

        await act.Should().ThrowAsync<ArgumentException>();
        company.ConnectionSecretRef.Should().BeNull();
    }
}
