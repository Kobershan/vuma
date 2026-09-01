using NSubstitute;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.UnitTests.Registry;

public sealed class CompanyContextTests
{
    [Fact]
    public void Missing_company_is_rejected()
    {
        var context = new AmbientCompanyContext();

        var act = () => context.RequireCompany();

        act.Should().Throw<InvalidOperationException>().WithMessage("*acting company*");
    }

    [Fact]
    public void Empty_company_is_rejected()
    {
        var context = new AmbientCompanyContext();

        var act = () => context.SetCompany(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_bound_company_cannot_be_replaced_or_nested()
    {
        var context = new AmbientCompanyContext();
        var companyId = Guid.NewGuid();
        context.SetCompany(companyId);

        var mismatch = () => context.SetCompany(Guid.NewGuid());
        var nested = () => context.SetCompany(companyId);

        mismatch.Should().Throw<InvalidOperationException>().WithMessage("*cannot change*");
        nested.Should().Throw<InvalidOperationException>().WithMessage("*already bound*");
        context.RequireCompany().Should().Be(companyId);
    }

    [Fact]
    public async Task Factory_rejects_missing_tenant_before_database_resolution()
    {
        var context = new AmbientCompanyContext();
        context.SetCompany(Guid.NewGuid());
        var resolver = Substitute.For<ICompanyConnectionResolver>();
        var guard = Substitute.For<ICompanyServingGuard>();
        var factory = new CompanyDbContextFactory(
            context,
            resolver,
            Substitute.For<ICompanyConnectionSecretStore>(),
            Tenant(Guid.Empty),
            guard);

        Func<Task> act = () => factory.CreateAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*authenticated tenant*");
        await resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default, default);
        await guard.DidNotReceiveWithAnyArgs().EnsureServableAsync(default, default);
    }

    [Fact]
    public async Task Factory_allows_exactly_one_context_and_rejects_the_second()
    {
        var companyId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var context = new AmbientCompanyContext();
        context.SetCompany(companyId);
        var resolver = Substitute.For<ICompanyConnectionResolver>();
        resolver.ResolveAsync(tenantId, companyId, Arg.Any<CancellationToken>())
            .Returns(new CompanyConnection(companyId, tenantId, "secret://company", 1));
        var secrets = Substitute.For<ICompanyConnectionSecretStore>();
        secrets.ResolveAsync("secret://company", Arg.Any<CancellationToken>()).Returns("Host=unused;Database=company");
        var factory = new CompanyDbContextFactory(
            context,
            resolver,
            secrets,
            Tenant(tenantId),
            Substitute.For<ICompanyServingGuard>());

        await using var first = await factory.CreateAsync();
        Func<Task> second = () => factory.CreateAsync();

        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only one company DbContext*");
        await resolver.Received(1).ResolveAsync(tenantId, companyId, Arg.Any<CancellationToken>());
    }

    private static ITenantContext Tenant(Guid tenantId)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        return tenant;
    }
}
