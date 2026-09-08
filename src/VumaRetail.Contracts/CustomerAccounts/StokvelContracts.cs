namespace VumaRetail.Contracts.CustomerAccounts;

/// <summary>Opens a stokvel group.</summary>
/// <param name="Name">What the members call it.</param>
/// <param name="Type">Savings, GroceryHamper, Burial or InvestmentBuying.</param>
/// <param name="Constitution">Payout rules and the visibility rule.</param>
/// <param name="CycleStart">The first day of the cycle.</param>
/// <param name="CycleEnd">The last day of the cycle.</param>
/// <param name="StoreId">The store the group belongs to.</param>
/// <param name="CompanyId">The company, or null for the acting company.</param>
public sealed record CreateStokvelRequest(
    string Name,
    string Type,
    string Constitution,
    DateOnly CycleStart,
    DateOnly CycleEnd,
    Guid StoreId,
    Guid? CompanyId = null);

/// <summary>A stokvel group, as returned by the API.</summary>
/// <param name="Id">The group.</param>
/// <param name="GroupNumber">The human-readable number.</param>
/// <param name="Name">What the members call it.</param>
/// <param name="Type">What it saves toward.</param>
/// <param name="Status">Forming, Active, PayingOut or Closed.</param>
/// <param name="CycleStart">The first day of the cycle.</param>
/// <param name="CycleEnd">The last day of the cycle.</param>
public sealed record StokvelResponse(
    Guid Id,
    string GroupNumber,
    string Name,
    string Type,
    string Status,
    DateOnly CycleStart,
    DateOnly CycleEnd);

/// <summary>Joins a member to a group.</summary>
/// <param name="PartnerId">The customer partner.</param>
/// <param name="Role">Member, Chairperson, Treasurer or Secretary.</param>
/// <param name="ObligationAmount">What they owe per cycle.</param>
/// <param name="ObligationCurrency">The ISO 4217 currency.</param>
public sealed record AddMemberRequest(
    Guid PartnerId,
    string Role,
    decimal ObligationAmount,
    string ObligationCurrency);

/// <summary>A member, as returned by the API.</summary>
/// <param name="Id">The member row.</param>
/// <param name="PartnerId">The customer partner.</param>
/// <param name="Role">What this member may do.</param>
/// <param name="JoinedAt">When they joined, UTC.</param>
/// <param name="LeftAt">When they left, UTC, if they did.</param>
/// <param name="Obligation">What they owe per cycle.</param>
public sealed record MemberResponse(
    Guid Id,
    Guid PartnerId,
    string Role,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt,
    decimal Obligation);

/// <summary>Records a contribution.</summary>
/// <param name="MemberId">The member who paid.</param>
/// <param name="Amount">What was paid.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="Channel">Till, EFT, debit order, storefront.</param>
/// <param name="ReceiptReference">The caller's receipt reference (idempotency key).</param>
/// <param name="TakenOffline">Whether captured offline.</param>
public sealed record RecordContributionRequest(
    Guid MemberId,
    decimal Amount,
    string Currency,
    string Channel,
    string ReceiptReference,
    bool TakenOffline = false);

/// <summary>A contribution, as returned by the API.</summary>
/// <param name="Id">The contribution row.</param>
/// <param name="MemberId">The member who paid.</param>
/// <param name="Amount">What was paid.</param>
/// <param name="ReceiptReference">The receipt.</param>
/// <param name="PaidAt">When, UTC.</param>
/// <param name="RunningBalance">The member's balance after this row.</param>
public sealed record ContributionResponse(
    Guid Id,
    Guid MemberId,
    decimal Amount,
    string ReceiptReference,
    DateTimeOffset PaidAt,
    decimal RunningBalance);

/// <summary>Allocates a bonus pool across the group's members.</summary>
/// <param name="BonusPoolAmount">The distributable pool.</param>
/// <param name="BonusPoolCurrency">The ISO 4217 currency.</param>
/// <param name="AsAt">The date weights are measured to, UTC. Defaults to now.</param>
public sealed record AllocateBenefitsRequest(
    decimal BonusPoolAmount,
    string BonusPoolCurrency,
    DateTimeOffset? AsAt = null);

/// <summary>One member's share, as returned by the API.</summary>
/// <param name="Id">The allocation row.</param>
/// <param name="MemberId">The member.</param>
/// <param name="Amount">The share.</param>
/// <param name="Basis">How it was computed.</param>
public sealed record BenefitAllocationResponse(
    Guid Id,
    Guid MemberId,
    decimal Amount,
    string Basis);

/// <summary>Requests a payout.</summary>
/// <param name="MemberId">The member drawing down.</param>
/// <param name="Kind">Goods, Hamper, Cash or StoreCredit.</param>
/// <param name="Amount">How much.</param>
/// <param name="Currency">The ISO 4217 currency.</param>
/// <param name="HamperBasketId">The basket, for hamper payouts.</param>
/// <param name="CapturedOffline">Whether requested offline.</param>
public sealed record RequestPayoutRequest(
    Guid MemberId,
    string Kind,
    decimal Amount,
    string Currency,
    Guid? HamperBasketId = null,
    bool CapturedOffline = false);

/// <summary>A payout, as returned by the API.</summary>
/// <param name="Id">The payout.</param>
/// <param name="MemberId">The member drawing down.</param>
/// <param name="Kind">What the balance turns into.</param>
/// <param name="Amount">How much.</param>
/// <param name="Status">Requested, Approved or Settled.</param>
/// <param name="SaleId">The sale it settled as, for goods/hamper payouts.</param>
public sealed record PayoutResponse(
    Guid Id,
    Guid MemberId,
    string Kind,
    decimal Amount,
    string Status,
    Guid? SaleId);

/// <summary>One statement row.</summary>
/// <param name="When">When, UTC.</param>
/// <param name="Description">What it is.</param>
/// <param name="Debit">What left the balance.</param>
/// <param name="Credit">What entered it.</param>
/// <param name="RunningBalance">The balance after this row.</param>
public sealed record StokvelStatementLineResponse(
    DateTimeOffset When,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>A member's statement: every contribution, benefit and settled payout.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="MemberId">The member.</param>
/// <param name="Available">What the member has.</param>
/// <param name="Lines">Ordered by time.</param>
public sealed record MemberStatementResponse(
    Guid GroupId,
    Guid MemberId,
    decimal Available,
    IReadOnlyList<StokvelStatementLineResponse> Lines);

/// <summary>One member's position on the group statement.</summary>
/// <param name="MemberId">The member row.</param>
/// <param name="PartnerId">The customer partner.</param>
/// <param name="Role">What this member may do.</param>
/// <param name="Available">What the member has.</param>
public sealed record GroupMemberResponse(
    Guid MemberId,
    Guid PartnerId,
    string Role,
    decimal Available);

/// <summary>The group's statement: the projected balance with every member's position.</summary>
/// <param name="GroupId">The group.</param>
/// <param name="GroupBalance">Contributions less settled payouts plus benefits.</param>
/// <param name="Members">One row per member.</param>
public sealed record GroupStatementResponse(
    Guid GroupId,
    decimal GroupBalance,
    IReadOnlyList<GroupMemberResponse> Members);

/// <summary>One hamper basket line to freeze.</summary>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Quantity">How much. Must be positive.</param>
/// <param name="Uom">The unit the quantity is counted in.</param>
/// <param name="SubstitutionItemId">The substitute item, when the line item is unavailable.</param>
/// <param name="SubstitutionItemVariantId">The substitute variant.</param>
public sealed record HamperLineRequest(
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string Uom,
    Guid? SubstitutionItemId = null,
    Guid? SubstitutionItemVariantId = null);

/// <summary>Creates a hamper basket.</summary>
/// <param name="Name">What the members call it.</param>
/// <param name="GroupPriceAmount">The frozen group price members pay.</param>
/// <param name="GroupPriceCurrency">The ISO 4217 currency.</param>
/// <param name="ValidFrom">The first day it may be taken.</param>
/// <param name="ValidTo">The last day it may be taken.</param>
/// <param name="LocationCode">The stock location its reservations hold at.</param>
/// <param name="Lines">At least one.</param>
/// <param name="CompanyId">The company, or null for the acting company.</param>
public sealed record CreateHamperRequest(
    string Name,
    decimal GroupPriceAmount,
    string GroupPriceCurrency,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    string LocationCode,
    IReadOnlyList<HamperLineRequest> Lines,
    Guid? CompanyId = null);

/// <summary>A hamper basket, as returned by the API.</summary>
/// <param name="Id">The basket.</param>
/// <param name="Name">What the members call it.</param>
/// <param name="GroupPrice">The frozen group price.</param>
/// <param name="ValidFrom">The first day it may be taken.</param>
/// <param name="ValidTo">The last day it may be taken.</param>
/// <param name="Lines">The frozen contents.</param>
public sealed record HamperResponse(
    Guid Id,
    string Name,
    decimal GroupPrice,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    IReadOnlyList<HamperLineRequest> Lines);
