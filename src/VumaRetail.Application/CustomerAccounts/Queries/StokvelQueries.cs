#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.CustomerAccounts;
using VumaRetail.Domain.CustomerAccounts;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.CustomerAccounts.Queries;

public sealed record StokvelStatementLine(
    DateTimeOffset When,
    string Description,
    Money Debit,
    Money Credit,
    Money RunningBalance);

public sealed record MemberStatementResult(
    Guid GroupId,
    Guid MemberId,
    Money Available,
    IReadOnlyList<StokvelStatementLine> Lines);

public sealed record GetMemberStatementQuery(
    Guid GroupId,
    Guid MemberId,
    Guid CallerPartnerId,
    MemberRole CallerRole) : IQuery<MemberStatementResult>;

public sealed class GetMemberStatementQueryHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IStokvelPayoutRepository payouts)
    : IQueryHandler<GetMemberStatementQuery, MemberStatementResult>
{
    public async Task<MemberStatementResult> HandleAsync(GetMemberStatementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var group = await groups.FindAsync(query.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(query.GroupId);
        var member = await groups.FindMemberAsync(query.MemberId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.MemberNotFound(query.MemberId);
        if (member.GroupId != group.Id)
        {
            throw StokvelExceptions.MemberNotFound(query.MemberId);
        }

        // The visibility wall: a Member reads only their own rows unless the constitution says
        // otherwise. Governance roles read any member's rows. Declaring is not enforcing.
        if (query.CallerRole == MemberRole.Member
            && query.CallerPartnerId != member.PartnerId
            && !group.MembersSeeAll)
        {
            throw new StokvelVisibilityException("Members may read only their own statement.");
        }

        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelBenefitAllocation> benefits =
            await ledger.ListBenefitsForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelPayout> history =
            await payouts.ListForMemberAsync(member.Id, cancellationToken).ConfigureAwait(false);

        string currency = contributions.FirstOrDefault()?.Amount.Currency
            ?? benefits.FirstOrDefault()?.Amount.Currency
            ?? history.FirstOrDefault()?.Amount.Currency
            ?? "ZAR";

        var events = new List<(DateTimeOffset When, string Description, Money Debit, Money Credit)>();
        foreach (var c in contributions)
        {
            events.Add((c.PaidAt, $"Contribution {c.ReceiptReference}",
                Money.Zero(currency), c.Amount));
        }

        foreach (var b in benefits)
        {
            events.Add((b.AllocatedAt, $"Benefit {b.Basis}",
                Money.Zero(currency), b.Amount));
        }

        foreach (var p in history.Where(p => p.Status == StokvelPayoutStatus.Settled))
        {
            events.Add((p.SettledAt ?? p.RequestedAt, $"Payout {p.Kind} {p.Amount}",
                p.Amount, Money.Zero(currency)));
        }

        Money running = Money.Zero(currency);
        var lines = new List<StokvelStatementLine>();
        foreach (var e in events.OrderBy(e => e.When))
        {
            running += e.Credit - e.Debit;
            lines.Add(new StokvelStatementLine(e.When, e.Description, e.Debit, e.Credit, running));
        }

        return new MemberStatementResult(group.Id, member.Id, running, lines);
    }
}

public sealed record GroupMemberSummary(Guid MemberId, Guid PartnerId, string Role, Money Available);

public sealed record GroupStatementResult(
    Guid GroupId,
    Money GroupBalance,
    IReadOnlyList<GroupMemberSummary> Members);

public sealed record GetGroupStatementQuery(Guid GroupId, MemberRole CallerRole)
    : IQuery<GroupStatementResult>;

public sealed class GetGroupStatementQueryHandler(
    IStokvelGroupRepository groups,
    IStokvelContributionRepository ledger,
    IStokvelPayoutRepository payouts)
    : IQueryHandler<GetGroupStatementQuery, GroupStatementResult>
{
    public async Task<GroupStatementResult> HandleAsync(GetGroupStatementQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var group = await groups.FindAsync(query.GroupId, cancellationToken).ConfigureAwait(false)
            ?? throw StokvelExceptions.GroupNotFound(query.GroupId);

        // Treasurer, chair and secretary read the group. A plain member does not.
        if (query.CallerRole == MemberRole.Member)
        {
            throw new StokvelVisibilityException("Only the treasurer, chair or secretary may read the group statement.");
        }

        IReadOnlyList<StokvelMember> members =
            await groups.ListMembersAsync(group.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelContribution> contributions =
            await ledger.ListForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelBenefitAllocation> benefits =
            await ledger.ListBenefitsForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<StokvelPayout> history =
            await payouts.ListForGroupAsync(group.Id, cancellationToken).ConfigureAwait(false);

        string currency = contributions.FirstOrDefault()?.Amount.Currency
            ?? benefits.FirstOrDefault()?.Amount.Currency
            ?? history.FirstOrDefault()?.Amount.Currency
            ?? "ZAR";

        Money balance = group.Balance(contributions, history, benefits, currency);
        var summaries = members.Select(member => new GroupMemberSummary(
            member.Id,
            member.PartnerId,
            member.Role.ToString(),
            member.Available(
                contributions.Where(c => c.MemberId == member.Id),
                history.Where(p => p.MemberId == member.Id),
                benefits.Where(b => b.MemberId == member.Id),
                currency))).ToList();

        return new GroupStatementResult(group.Id, balance, summaries);
    }
}
