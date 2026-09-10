using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// An unqualified prospect. A lead is not a customer; it is a candidate that may convert into
/// one through a documented, one-way process (Stage 19).
/// </summary>
/// <remarks>
/// <para>
/// CRM never owns the customer identity (Stage 06's partner record). A lead carries its own
/// captured details; conversion links it to the partner it became via <see cref="CustomerId"/> —
/// a plain <see cref="Guid"/>, never a cross-schema foreign key (CONVENTIONS.md §2).
/// </para>
/// </remarks>
[Replicated(ReplicationScope.StoreToCloud, ConflictPolicy.CloudWins)]
public sealed class Lead : Entity
{
    private Lead()
    {
    }

    /// <summary>Captures a lead.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The company capturing the lead.</param>
    /// <param name="firstName">First name.</param>
    /// <param name="lastName">Last name.</param>
    /// <param name="email">Email address. Lower-cased; the natural key with the store.</param>
    /// <param name="phone">Phone number, if known.</param>
    /// <param name="company">Prospect's company, if known.</param>
    /// <param name="source">Where the lead came from.</param>
    /// <param name="storeId">The store that captured it, if any.</param>
    public Lead(
        Guid tenantId,
        Guid companyId,
        string firstName,
        string lastName,
        string email,
        string? phone,
        string? company,
        LeadSource source,
        Guid? storeId = null)
        : base(tenantId, storeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        AssignCompany(companyId);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email.Trim().ToLowerInvariant();
        Phone = phone;
        Company = company;
        Source = source;
        Status = LeadStatus.New;
    }

    /// <summary>First name.</summary>
    public string FirstName { get; private set; } = string.Empty;

    /// <summary>Last name.</summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>Email address, lower-cased.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Phone number, if known.</summary>
    public string? Phone { get; private set; }

    /// <summary>Prospect's company, if known.</summary>
    public string? Company { get; private set; }

    /// <summary>Where the lead came from.</summary>
    public LeadSource Source { get; private set; }

    /// <summary>Pipeline position.</summary>
    public LeadStatus Status { get; private set; }

    /// <summary>The user working the lead, if assigned.</summary>
    public Guid? AssignedTo { get; private set; }

    /// <summary>The partner this lead converted into, once converted.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>When conversion happened, UTC. Set only on conversion.</summary>
    public DateTimeOffset? ConvertedAt { get; private set; }

    /// <summary>Assigns the lead to a user.</summary>
    /// <param name="userId">The user taking the lead.</param>
    /// <exception cref="LeadTerminalStateException">The lead is terminal.</exception>
    public void AssignTo(Guid userId)
    {
        RefuseIfTerminal();
        AssignedTo = userId;
    }

    /// <summary>Updates captured details. Never touches pipeline state.</summary>
    /// <param name="firstName">First name.</param>
    /// <param name="lastName">Last name.</param>
    /// <param name="phone">Phone number.</param>
    /// <param name="company">Prospect's company.</param>
    /// <exception cref="LeadTerminalStateException">The lead is terminal.</exception>
    public void UpdateDetails(string firstName, string lastName, string? phone, string? company)
    {
        RefuseIfTerminal();
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Phone = phone;
        Company = company;
    }

    /// <summary>Moves a new lead to contacted.</summary>
    /// <exception cref="LeadTerminalStateException">The lead is terminal or not new.</exception>
    public void MarkContacted()
    {
        RefuseIfTerminal();
        if (Status is not LeadStatus.New)
        {
            throw new LeadTerminalStateException($"A {Status} lead cannot move back to Contacted.");
        }

        Status = LeadStatus.Contacted;
    }

    /// <summary>Qualifies a contacted lead.</summary>
    /// <exception cref="LeadTerminalStateException">The lead is terminal or not contacted.</exception>
    public void Qualify()
    {
        RefuseIfTerminal();
        if (Status is not LeadStatus.Contacted)
        {
            throw new LeadTerminalStateException($"A {Status} lead cannot be qualified.");
        }

        Status = LeadStatus.Qualified;
    }

    /// <summary>Qualifies a lead out. Terminal.</summary>
    /// <param name="dead">True for gone-cold (<c>Dead</c>), false for qualified-out.</param>
    /// <exception cref="LeadTerminalStateException">The lead is already terminal.</exception>
    public void Disqualify(bool dead = false)
    {
        RefuseIfTerminal();
        Status = dead ? LeadStatus.Dead : LeadStatus.Disqualified;
    }

    /// <summary>
    /// Converts the lead into an existing partner. Atomic and one-way: sets the link, stamps the
    /// conversion and closes the pipeline in one step.
    /// </summary>
    /// <param name="customerId">The partner this lead became (Stage 06 identity).</param>
    /// <param name="convertedAt">When conversion happened, UTC. From <c>IClock</c>, never the wall clock.</param>
    /// <returns>The conversion result, carrying the customer UUID downstream.</returns>
    /// <exception cref="LeadAlreadyConvertedException">Already converted.</exception>
    /// <exception cref="LeadTerminalStateException">Terminal in another state.</exception>
    public ConvertLeadResult Convert(Guid customerId, DateTimeOffset convertedAt)
    {
        if (Status == LeadStatus.Converted)
        {
            throw new LeadAlreadyConvertedException();
        }

        RefuseIfTerminal();
        CustomerId = customerId;
        Status = LeadStatus.Converted;
        ConvertedAt = convertedAt;
        return new ConvertLeadResult(customerId, Status, ConvertedAt);
    }

    private void RefuseIfTerminal()
    {
        if (Status is LeadStatus.Converted or LeadStatus.Disqualified or LeadStatus.Dead)
        {
            throw new LeadTerminalStateException($"A {Status} lead cannot be changed.");
        }
    }
}

/// <summary>What converting a lead produced: the customer UUID everything downstream references.</summary>
/// <param name="CustomerId">The partner the lead became.</param>
/// <param name="LeadStatus">The lead's status after conversion.</param>
/// <param name="ConvertedAt">When conversion happened, UTC.</param>
public sealed record ConvertLeadResult(Guid CustomerId, LeadStatus LeadStatus, DateTimeOffset? ConvertedAt);
