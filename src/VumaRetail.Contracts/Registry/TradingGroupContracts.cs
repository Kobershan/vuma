namespace VumaRetail.Contracts.Registry;

/// <summary>The acting Operator ID and the companies it owns in this tenant.</summary>
/// <param name="OperatorId">The vendor-issued operator identity.</param>
/// <param name="DisplayName">The operator's display name.</param>
/// <param name="IsActive">Whether the operator is currently active.</param>
/// <param name="Companies">The operator's companies in this tenant.</param>
public sealed record OperatorResponse(Guid OperatorId, string DisplayName, bool IsActive, IReadOnlyList<OperatorCompanyResponse> Companies);

/// <summary>One company under the acting operator.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">The company's short code.</param>
/// <param name="IsActive">Whether the company may be served.</param>
public sealed record OperatorCompanyResponse(Guid CompanyId, string Code, bool IsActive);

/// <summary>Proposes a company link.</summary>
/// <param name="CompanyAId">One side of the pair.</param>
/// <param name="CompanyBId">The other side of the pair.</param>
/// <param name="Scopes">The link scopes as combined <c>CompanyLinkScope</c> flags.</param>
public sealed record ProposeCompanyLinkRequest(Guid CompanyAId, Guid CompanyBId, int Scopes);

/// <summary>A company link row.</summary>
/// <param name="Id">The link.</param>
/// <param name="CompanyAId">The smaller of the two company ids.</param>
/// <param name="CompanyBId">The larger of the two company ids.</param>
/// <param name="Scopes">The granted scopes as combined flags.</param>
/// <param name="Status">Where the link sits: <c>Proposed</c>, <c>Accepted</c>, <c>Active</c>, <c>Suspended</c> or <c>Revoked</c>.</param>
/// <param name="EffectiveFrom">When the link becomes effective.</param>
/// <param name="EffectiveTo">When the link ends, if it has.</param>
public sealed record CompanyLinkResponse(Guid Id, Guid CompanyAId, Guid CompanyBId, int Scopes, string Status, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);

/// <summary>Suspends a link; the dispute mechanism's pause.</summary>
/// <param name="Reason">Why the link is suspended.</param>
public sealed record SuspendCompanyLinkRequest(string Reason);

/// <summary>Revokes a link. Final; the reason needs at least ten characters.</summary>
/// <param name="Reason">Why the link is revoked.</param>
public sealed record RevokeCompanyLinkRequest(string Reason);

/// <summary>Creates a physical premises.</summary>
/// <param name="Code">The short code.</param>
/// <param name="Name">The human-readable name.</param>
/// <param name="Address">The physical address.</param>
/// <param name="GeoLocation">Geographic coordinates.</param>
/// <param name="TradingHours">Trading hours, free text.</param>
public sealed record CreatePremisesRequest(string Code, string Name, string Address, string GeoLocation, string TradingHours);

/// <summary>A premises row.</summary>
/// <param name="Id">The premises.</param>
/// <param name="Code">The short code.</param>
/// <param name="Name">The human-readable name.</param>
/// <param name="IsActive">Whether the premises is currently active.</param>
public sealed record PremisesResponse(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Adds a company as an occupant of a premises.</summary>
/// <param name="CompanyId">The occupying company.</param>
/// <param name="StoreId">The store row inside that company's database.</param>
public sealed record AddPremisesOccupancyRequest(Guid CompanyId, Guid StoreId);

/// <summary>Creates a registry user.</summary>
/// <param name="Login">The one login for this human.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="ContactDetails">Contact details.</param>
/// <param name="OperatorId">The operator that owns this login.</param>
public sealed record CreateRegistryUserRequest(string Login, string DisplayName, string ContactDetails, Guid OperatorId);

/// <summary>A registry user row.</summary>
/// <param name="Id">The user.</param>
/// <param name="Login">The one login.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="IsEnabled">Whether the login may sign in.</param>
public sealed record RegistryUserResponse(Guid Id, string Login, string DisplayName, bool IsEnabled);

/// <summary>Grants a user access to a company.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Roles">Comma-separated role names in that company.</param>
public sealed record GrantCompanyAccessRequest(Guid CompanyId, string Roles);

/// <summary>Registers a terminal at a premises.</summary>
/// <param name="PremisesId">The premises the till stands on.</param>
/// <param name="TerminalId">The terminal identifier.</param>
/// <param name="DeviceCertThumbprint">The device certificate thumbprint.</param>
public sealed record RegisterTerminalRequest(Guid PremisesId, string TerminalId, string DeviceCertThumbprint);

/// <summary>Sets the companies a till may sell for.</summary>
/// <param name="CompanyIds">The authorised companies, all under one Operator ID.</param>
public sealed record SetTerminalCompaniesRequest(List<Guid> CompanyIds);
