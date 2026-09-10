using VumaRetail.Domain.Conversations;

namespace VumaRetail.UnitTests.Conversations;

public sealed class ConversationScopeTests
{
    [Fact]
    public void Scope_requires_binding_company_and_account()
    {
        Guid tenant = Guid.NewGuid();
        Guid binding = Guid.NewGuid();
        Guid company = Guid.NewGuid();
        Guid account = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new ConversationAccountScope(tenant, Guid.Empty, company, account));
        Assert.Throws<ArgumentException>(() => new ConversationAccountScope(tenant, binding, Guid.Empty, account));
        Assert.Throws<ArgumentException>(() => new ConversationAccountScope(tenant, binding, company, Guid.Empty));
    }

    [Fact]
    public void Scope_preserves_the_structural_boundary()
    {
        Guid tenant = Guid.NewGuid();
        Guid binding = Guid.NewGuid();
        Guid company = Guid.NewGuid();
        Guid account = Guid.NewGuid();

        ConversationAccountScope scope = new(tenant, binding, company, account);

        Assert.Equal(tenant, scope.TenantId);
        Assert.Equal(binding, scope.BindingId);
        Assert.Equal(company, scope.OperatingCompanyId);
        Assert.Equal(account, scope.CustomerAccountId);
    }
}
