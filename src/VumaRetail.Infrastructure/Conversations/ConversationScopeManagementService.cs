using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Conversations;
using VumaRetail.Domain.Conversations;
using VumaRetail.Infrastructure.Persistence;
using VumaRetail.Infrastructure.Registry;

namespace VumaRetail.Infrastructure.Conversations;

/// <summary>Persists explicit account/company boundaries for conversation bindings.</summary>
public sealed class ConversationScopeManagementService(
    VumaRegistryDbContext registry,
    IServiceScopeFactory scopes,
    ITenantContext tenant) : IConversationScopeManagementService, IConversationScopeReader
{
    public async Task<ConversationAccountScope> AddAsync(ConversationAccountScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        bool bindingExists = await registry.ContactBindings.AnyAsync(x => x.Id == scope.BindingId, cancellationToken).ConfigureAwait(false);
        if (!bindingExists)
        {
            throw new InvalidOperationException("Conversation binding not found.");
        }

        bool companyExists = await registry.Companies
            .AnyAsync(x => x.Id == scope.OperatingCompanyId && x.TenantId == tenant.TenantId && x.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (!companyExists)
        {
            throw new InvalidOperationException("Conversation scope company was not found or is inactive.");
        }

        // The registry deliberately has no foreign key into company databases. Validate the pair at
        // the company boundary before persisting it; otherwise an administrator could grant a binding
        // an arbitrary account id under an unrelated company and the document handlers would mint a
        // token for a scope that was never real.
        using IServiceScope companyScope = scopes.CreateScope();
        companyScope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenant.TenantId);
        companyScope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(scope.OperatingCompanyId);
        ICompanyDbContextFactory factory = companyScope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await factory
            .CreateAsync(CompanyAccessMode.Read, cancellationToken).ConfigureAwait(false);
        bool accountExists = await companyDb.CustomerAccounts
            .AsNoTracking()
            .AnyAsync(x => x.Id == scope.CustomerAccountId && x.TenantId == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!accountExists)
        {
            throw new InvalidOperationException("Conversation scope account was not found in the selected company.");
        }

        registry.ConversationAccountScopes.Add(scope);
        await registry.CommitAsync(cancellationToken).ConfigureAwait(false);
        return scope;
    }

    public async Task<IReadOnlyList<ConversationAccountScope>> ListAsync(Guid bindingId, CancellationToken cancellationToken = default)
    {
        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("A binding is required.", nameof(bindingId));
        }

        return await registry.ConversationAccountScopes
            .AsNoTracking()
            .Where(x => x.BindingId == bindingId)
            .OrderBy(x => x.OperatingCompanyId)
            .ThenBy(x => x.CustomerAccountId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
