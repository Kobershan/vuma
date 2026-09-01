namespace VumaRetail.Contracts.Registry;

public sealed record ProvisionCompanyRequest(string Code, string LegalName, string TradingName, string BaseCurrency, string Locale, string DocumentPrefix);
public sealed record CompanyResponse(Guid Id, string Code, string LegalName, string TradingName, string BaseCurrency, string Locale, string DocumentPrefix, string LifecycleState, bool IsActive, long SchemaVersion, string MigrationState, string ProvisioningStep, string? ProvisioningError, DateTimeOffset? DeactivatedAt, string? DeactivatedBy, string? DeactivationReason);
public sealed record CompanyMigrationStatusResponse(Guid CompanyId, string Code, string LifecycleState, long SchemaVersion, string MigrationState, string? PendingAction, string? Error);
public sealed record SelectCompanyResponse(Guid CompanyId, string SelectionSource);
