#pragma warning disable CS1591, IDE0011
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Connect;

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PriceProposal : Entity
{
    private readonly List<PriceProposalLine> _lines = [];
    private PriceProposal(Guid tenantId, Guid connectionId, DateTimeOffset effectiveFrom, DateTimeOffset? expiresAt, string? note)
        : base(tenantId) { ConnectionId = connectionId; EffectiveFrom = effectiveFrom; ExpiresAt = expiresAt; VersionNote = note?.Trim(); }
    private PriceProposal() { }
    public Guid ConnectionId { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? VersionNote { get; private set; }
    public ConnectProposalStatus Status { get; private set; } = ConnectProposalStatus.Pending;
    public DateTimeOffset? DecidedAt { get; private set; }
    public IReadOnlyList<PriceProposalLine> Lines => _lines;
    public static PriceProposal Publish(Guid supplierTenantId, Guid connectionId, DateTimeOffset effectiveFrom, DateTimeOffset? expiresAt, string? note)
    {
        if (supplierTenantId == Guid.Empty || connectionId == Guid.Empty || (expiresAt is not null && expiresAt <= effectiveFrom)) throw new ArgumentException("Invalid price proposal.");
        return new PriceProposal(supplierTenantId, connectionId, effectiveFrom, expiresAt, note);
    }
    public PriceProposalLine AddLine(string supplierSku, decimal unitPrice, string currency, decimal? moq, int? leadTimeDays)
    {
        if (Status != ConnectProposalStatus.Pending) throw new InvalidOperationException("A decided proposal cannot be changed.");
        if (string.IsNullOrWhiteSpace(supplierSku) || unitPrice < 0 || string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3) throw new ArgumentException("Price proposal line is invalid.");
        var line = new PriceProposalLine(TenantId, Id, supplierSku, unitPrice, currency, moq, leadTimeDays); _lines.Add(line); return line;
    }
    public void Accept(IEnumerable<Guid>? lineIds, DateTimeOffset at)
    {
        if (Status is not (ConnectProposalStatus.Pending or ConnectProposalStatus.PartiallyAccepted)) throw new InvalidOperationException("Proposal is already decided.");
        var selected = lineIds?.ToHashSet() ?? _lines.Select(x => x.Id).ToHashSet();
        if (selected.Count == 0 || selected.Except(_lines.Select(x => x.Id)).Any()) throw new ArgumentException("Unknown proposal line.");
        Status = selected.Count == _lines.Count ? ConnectProposalStatus.Accepted : ConnectProposalStatus.PartiallyAccepted; DecidedAt = at;
    }
    public void Reject(DateTimeOffset at) { if (Status != ConnectProposalStatus.Pending) throw new InvalidOperationException("Proposal is already decided."); Status = ConnectProposalStatus.Rejected; DecidedAt = at; }
    public void Rollback(DateTimeOffset at) { if (Status != ConnectProposalStatus.Accepted) throw new InvalidOperationException("Only an accepted proposal can be rolled back."); Status = ConnectProposalStatus.RolledBack; DecidedAt = at; }
}

[Replicated(ReplicationScope.Bidirectional, ConflictPolicy.CloudWins)]
public sealed class PriceProposalLine : Entity
{
    internal PriceProposalLine(Guid tenantId, Guid proposalId, string sku, decimal price, string currency, decimal? moq, int? leadTimeDays) : base(tenantId)
    { ProposalId = proposalId; SupplierSku = sku.Trim(); UnitPrice = price; Currency = currency.Trim().ToUpperInvariant(); MinimumOrderQuantity = moq; LeadTimeDays = leadTimeDays; }
    private PriceProposalLine() { }
    public Guid ProposalId { get; private set; }
    public string SupplierSku { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal? MinimumOrderQuantity { get; private set; }
    public int? LeadTimeDays { get; private set; }
}
