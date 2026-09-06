using VumaRetail.Domain.Registry;

namespace VumaRetail.UnitTests.Registry;

/// <summary>Stable Stage 06c company inputs shared by acceptance tests.</summary>
internal static class Stage06cCompanyFixture
{
    public static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-0000000006c0");

    public static IReadOnlyList<CompanySeed> Companies { get; } =
    [
        new("hardware", "Mahlangu Hardware (Pty) Ltd", "Mahlangu Hardware", "ZAR", "en-ZA", "HW"),
        new("distribution", "Vuma Distribution (Pty) Ltd", "Vuma Distribution", "ZAR", "en-ZA", "DC"),
        new("groceries", "Ubuntu Groceries (Pty) Ltd", "Ubuntu Groceries", "ZAR", "en-ZA", "GR"),
    ];

    public static IReadOnlyList<Company> CreateCompanies()
        => Companies
            .Select(seed => Company.Create(
                TenantId,
                seed.Code,
                seed.LegalName,
                seed.TradingName,
                seed.BaseCurrency,
                seed.Locale,
                seed.DocumentPrefix))
            .ToArray();

    internal sealed record CompanySeed(
        string Code,
        string LegalName,
        string TradingName,
        string BaseCurrency,
        string Locale,
        string DocumentPrefix);
}
