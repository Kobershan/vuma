using VumaRetail.Domain.Primitives;
using VumaRetail.Domain.Registry;
using Xunit;
using FluentAssertions;

namespace VumaRetail.UnitTests.Stage06c;

[Trait("Category", "Unit")]
[Trait("Stage", "06C")]
[Trait("Requirement", "06C-01")]
public sealed class _06C01_RegistryTests
{
    [Fact]
    public void Company_lifecycle_transitions_enforce_correct_sequence()
    {
        var company = Company.Create(
            Guid.NewGuid(), 
            "test", 
            "Test Company", 
            "Test Company", 
            "ZAR", 
            "en-ZA", 
            "TC-");
            
        company.SetConnectionSecretRef("test-secret");
        
        company.LifecycleState.Should().Be(CompanyLifecycleState.Provisioning);
        
        company.SetLifecycle(CompanyLifecycleState.Seeding);
        company.LifecycleState.Should().Be(CompanyLifecycleState.Seeding);
        
        company.SetLifecycle(CompanyLifecycleState.Registered);
        company.LifecycleState.Should().Be(CompanyLifecycleState.Registered);
        
        Action invalid = () => company.SetLifecycle(CompanyLifecycleState.Provisioning);
        invalid.Should().Throw<InvalidOperationException>();
        
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        company.LifecycleState.Should().Be(CompanyLifecycleState.Active);
        company.IsActive.Should().BeTrue();
        
        company.SetLifecycle(CompanyLifecycleState.Deactivated, isActive: false);
        company.LifecycleState.Should().Be(CompanyLifecycleState.Deactivated);
        company.IsActive.Should().BeFalse();
    }
    
    [Fact]
    public void Company_must_have_connection_secret_before_activation()
    {
        var company = Company.Create(
            Guid.NewGuid(), 
            "no-secret", 
            "No Secret Co", 
            "No Secret Co", 
            "ZAR", 
            "en-ZA", 
            "NS-");
            
        Action activate = () => company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        activate.Should().Throw<InvalidOperationException>();
    }
    
[Fact]
    public void Group_link_scope_requires_active_member()
    {
        var tenantId = Guid.NewGuid();
        var group = CompanyGroup.Create(tenantId, "test-group");
        var company = Company.Create(
            tenantId, 
            "test", 
            "Test Company", 
            "Test Company", 
            "ZAR", 
            "en-ZA", 
            "TC-");
        
        company.SetConnectionSecretRef("test-secret");
        company.SetLifecycle(CompanyLifecycleState.Seeding);
        company.SetLifecycle(CompanyLifecycleState.Registered);
        company.SetLifecycle(CompanyLifecycleState.Active, isActive: true);
        
        group.AddMember(company.Id);
        group.Members.Should().ContainSingle(m => m.CompanyId == company.Id);
    }
}