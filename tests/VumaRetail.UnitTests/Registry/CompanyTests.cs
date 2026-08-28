using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Business rules for the registry company lifecycle.</summary>
public sealed class CompanyTests
{
    [Fact]
    public void New_company_starts_provisioning_and_cannot_serve()
    {
        Company company = Company.Create(
            Guid.NewGuid(), "hardware", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "SH");

        company.LifecycleState.Should().Be(CompanyLifecycleState.Provisioning);
        company.IsActive.Should().BeFalse();
        company.ConnectionSecretRef.Should().BeNull();
    }

    [Fact]
    public void Active_requires_registered_secret_and_explicit_active_flag()
    {
        Company company = CreateCompany();

        company.SetLifecycle(CompanyLifecycleState.Seeding);
        company.SetLifecycle(CompanyLifecycleState.Registered);
        var act = () => company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        act.Should().Throw<InvalidOperationException>();

        company.SetConnectionSecretRef("secret://tenant/company");
        company.SetLifecycle(CompanyLifecycleState.Active);
        company.IsActive.Should().BeFalse();

        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        company.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Lifecycle_rejects_skipping_provisioning_states()
    {
        Company company = CreateCompany();

        var act = () => company.SetLifecycle(CompanyLifecycleState.Active);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registry_stores_a_secret_reference_not_connection_details()
    {
        Company company = CreateCompany();

        company.SetConnectionSecretRef("secret://tenant/company");

        company.ConnectionSecretRef.Should().Be("secret://tenant/company");
    }

    [Fact]
    public void Business_entity_can_be_assigned_a_company_identity_for_retrofit()
    {
        var entity = new TestEntity(Guid.NewGuid());
        var companyId = Guid.NewGuid();

        entity.AssignCompany(companyId);

        entity.CompanyId.Should().Be(companyId);
    }

    private sealed class TestEntity(Guid tenantId) : VumaRetail.Domain.Entities.Entity(tenantId);

    private static Company CreateCompany()
        => Company.Create(Guid.NewGuid(), "hardware", "Siyaya Hardware", "Siyaya Hardware", "ZAR", "en-ZA", "SH");
}
