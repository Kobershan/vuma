using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Conversations;

/// <summary>One customer-account/company boundary a verified conversation binding may reach.</summary>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class ConversationAccountScope : Entity
{
    private ConversationAccountScope() { }

    /// <summary>Creates a tenant-owned binding scope.</summary>
    public ConversationAccountScope(Guid tenantId, Guid bindingId, Guid companyId, Guid customerAccountId)
        : base(tenantId)
    {
        if (bindingId == Guid.Empty)
        {
            throw new ArgumentException("A binding is required.", nameof(bindingId));
        }
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }
        if (customerAccountId == Guid.Empty)
        {
            throw new ArgumentException("A customer account is required.", nameof(customerAccountId));
        }
        BindingId = bindingId;
        OperatingCompanyId = companyId;
        CustomerAccountId = customerAccountId;
    }

    /// <summary>The contact binding this scope belongs to.</summary>
    public Guid BindingId { get; private set; }
    /// <summary>The company whose records are reachable.</summary>
    public Guid OperatingCompanyId { get; private set; }
    /// <summary>The customer account whose records are reachable.</summary>
    public Guid CustomerAccountId { get; private set; }
}
