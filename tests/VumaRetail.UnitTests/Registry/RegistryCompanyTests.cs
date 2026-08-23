using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class RegistryCompanyTests
{
    private static readonly Guid TenantId = UuidV7.NewGuid();

    private static RegistryCompany NewCompany()
        => RegistryCompany.BeginProvisioning(TenantId, "sh", "Siyaya Hardware (Pty) Ltd", "Siyaya Hardware", "ZAR", "en-ZA", "sh");

    [Fact]
    public void Provisioning_begins_at_Provisioning_and_is_not_yet_active()
    {
        RegistryCompany company = NewCompany();

        company.LifecycleState.Should().Be(CompanyLifecycleState.Provisioning);
        company.IsActive.Should().BeFalse();
        company.IsVisibleToBusinessOperations.Should().BeFalse();
        // Codes and prefixes are upper-cased so a mixed-case operator input never produces two
        // effectively-identical companies that the unique index cannot see are the same.
        company.Code.Should().Be("SH");
        company.DocumentPrefix.Should().Be("SH");
    }

    [Fact]
    public void Advancing_through_every_step_reaches_Active_and_sets_IsActive()
    {
        RegistryCompany company = NewCompany();

        company.AdvanceTo(CompanyLifecycleState.Seeding);
        company.AdvanceTo(CompanyLifecycleState.Registered);
        company.IsActive.Should().BeFalse("Registered is not yet visible to business operations");

        company.AdvanceTo(CompanyLifecycleState.Active);

        company.LifecycleState.Should().Be(CompanyLifecycleState.Active);
        company.IsActive.Should().BeTrue();
        company.IsVisibleToBusinessOperations.Should().BeTrue();
    }

    [Fact]
    public void Provisioning_is_re_runnable_from_the_step_it_reached()
    {
        RegistryCompany company = NewCompany();
        company.AdvanceTo(CompanyLifecycleState.Seeding);

        company.RecordFailure("Seeding", "chart of accounts template not found");

        company.FailedStep.Should().Be("Seeding");
        company.FailureReason.Should().NotBeNullOrEmpty();

        // Re-running from where it stopped clears the failure without moving the state backward.
        company.AdvanceTo(CompanyLifecycleState.Seeding);

        company.FailedStep.Should().BeNull();
        company.FailureReason.Should().BeNull();
        company.LifecycleState.Should().Be(CompanyLifecycleState.Seeding);
    }

    [Fact]
    public void The_lifecycle_never_moves_backward()
    {
        RegistryCompany company = NewCompany();
        company.AdvanceTo(CompanyLifecycleState.Registered);

        Action moveBack = () => company.AdvanceTo(CompanyLifecycleState.Provisioning);

        moveBack.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deactivating_an_active_company_hides_it_from_business_operations_without_losing_its_history()
    {
        RegistryCompany company = NewCompany();
        company.AdvanceTo(CompanyLifecycleState.Seeding);
        company.AdvanceTo(CompanyLifecycleState.Registered);
        company.AdvanceTo(CompanyLifecycleState.Active);

        company.Deactivate();

        company.LifecycleState.Should().Be(CompanyLifecycleState.Active, "deactivation removes visibility, not the fact that provisioning completed");
        company.IsActive.Should().BeFalse();
        company.IsVisibleToBusinessOperations.Should().BeFalse();

        company.Reactivate();

        company.IsActive.Should().BeTrue();
        company.IsVisibleToBusinessOperations.Should().BeTrue();
    }

    [Fact]
    public void A_company_that_has_not_completed_provisioning_cannot_be_deactivated()
    {
        RegistryCompany company = NewCompany();

        Action deactivate = () => company.Deactivate();

        deactivate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_company_that_has_never_been_active_cannot_be_reactivated()
    {
        RegistryCompany company = NewCompany();

        Action reactivate = () => company.Reactivate();

        reactivate.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_company_must_belong_to_a_tenant()
    {
        Action create = () => RegistryCompany.BeginProvisioning(Guid.Empty, "sh", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "sh");

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Migration_state_is_recorded_against_the_company_it_was_run_for()
    {
        RegistryCompany company = NewCompany();

        company.RecordMigration("20260823195115_InitialRegistry", CompanyMigrationState.UpToDate);

        company.SchemaVersion.Should().Be("20260823195115_InitialRegistry");
        company.MigrationState.Should().Be(CompanyMigrationState.UpToDate);
    }
}
