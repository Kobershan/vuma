#pragma warning disable CS1591
using VumaRetail.Domain.Quality;

namespace VumaRetail.Application.Quality;

public interface IInspectionPlanRepository
{
    Task<InspectionPlan?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InspectionPlan?> FindPublishedAsync(Guid companyId, Guid? itemId, Guid? itemVariantId, CancellationToken cancellationToken = default);
    void Add(InspectionPlan plan);
}

public interface IQualityCertificateRepository
{
    Task<QualityCertificate?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(QualityCertificate certificate);
}

/// <summary>Persistence boundary for tenant/company-scoped quality holds.</summary>
public interface IQualityHoldRepository
{
    Task<QualityHold?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QualityHold?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(QualityHold hold);
}

public interface IInspectionResultRepository
{
    Task<InspectionResult?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(InspectionResult result);
}

public interface INonConformanceRepository
{
    Task<NonConformance?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NonConformance?> FindByOperationIdAsync(Guid operationId, CancellationToken cancellationToken = default);
    void Add(NonConformance nonConformance);
}
