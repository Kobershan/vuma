using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Stage06c;

[Trait("Category", "Unit")]
[Trait("Stage", "06C")]
[Trait("Requirement", "06C-01")]
public sealed class _06C01_RegistryFoundationTests
{
[Fact]
    public void Company_requires_tenant_scoping()
    {
        var action = () => Company.Create(
            Guid.Empty, 
            "test", 
            "Test Company", 
            "Test Company", 
            "ZAR", 
            "en-ZA", 
            "TC-");
             
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Group_requires_tenant_scoping()
    {
        var action = () => CompanyGroup.Create(
            Guid.Empty, 
            "test-group");
             
        action.Should().Throw<ArgumentException>();
    }
}