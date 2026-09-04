using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Business rules for the Operator ID on a company (ADR-121).</summary>
public sealed class CompanyOperatorTests
{
    [Fact]
    public void New_company_has_no_operator_until_the_vendor_assigns_one()
    {
        Company company = Company.Create(Guid.NewGuid(), "hw", "Hardware", "Hardware", "ZAR", "en-ZA", "HW");

        company.OperatorId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AssignOperator_sets_it_once_and_rejects_empties()
    {
        Company company = Company.Create(Guid.NewGuid(), "hw", "Hardware", "Hardware", "ZAR", "en-ZA", "HW");
        Guid operatorId = UuidV7.NewGuid();

        company.AssignOperator(operatorId);

        company.OperatorId.Should().Be(operatorId);

        var sameAgain = () => company.AssignOperator(operatorId);
        sameAgain.Should().NotThrow();

        var empty = () => company.AssignOperator(Guid.Empty);
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Changing_ownership_is_refused_because_it_is_a_vendor_operation()
    {
        Company company = Company.Create(Guid.NewGuid(), "hw", "Hardware", "Hardware", "ZAR", "en-ZA", "HW");
        company.AssignOperator(UuidV7.NewGuid());

        var act = () => company.AssignOperator(UuidV7.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Registry_factories_carry_tenant_and_caller_time()
    {
        Guid tenantId = UuidV7.NewGuid();
        DateTimeOffset now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

        PremisesOccupancy occupancy = PremisesOccupancy.Create(tenantId, UuidV7.NewGuid(), UuidV7.NewGuid(), UuidV7.NewGuid(), now);
        PremisesBinLayout layout = PremisesBinLayout.Create(tenantId, UuidV7.NewGuid(), "A1", "A1-01", "Shelf", isShared: true);
        RegistryUserCompanyAccess access = RegistryUserCompanyAccess.Create(tenantId, UuidV7.NewGuid(), UuidV7.NewGuid(), "Cashier", "user:owner", now);

        occupancy.TenantId.Should().Be(tenantId);
        occupancy.OccupiesFrom.Should().Be(now);
        layout.TenantId.Should().Be(tenantId);
        layout.IsShared.Should().BeTrue();
        access.TenantId.Should().Be(tenantId);
        access.GrantedAt.Should().Be(now);
    }
}
