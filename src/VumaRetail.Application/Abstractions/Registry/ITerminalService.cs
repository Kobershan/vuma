using VumaRetail.Domain.Registry;

namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Registers terminals and maintains the companies each till may sell for.
/// </summary>
public interface ITerminalService
{
    /// <summary>Registers a terminal at a premises.</summary>
    Task<RegistryTerminal> RegisterAsync(Guid tenantId, Guid premisesId, string terminalId, string deviceCertThumbprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the companies a till may sell for. Refused unless every named company exists in this
    /// tenant and all of them sit under one Operator ID — a till never spans operators.
    /// </summary>
    Task SetCompaniesAsync(Guid terminalId, IReadOnlyList<Guid> companyIds, CancellationToken cancellationToken = default);
}
