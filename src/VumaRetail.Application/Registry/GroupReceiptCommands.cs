using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Primitives;

#pragma warning disable CS1591
#pragma warning disable CA1062

namespace VumaRetail.Application.Registry;

/// <summary>Captures a group receipt in the registry. Posts nothing (ADR-104).</summary>
public sealed class CaptureGroupReceiptCommand
{
    public Guid TenantId { get; init; }
    public Guid CapturingCompanyId { get; init; }
    public Guid BankAccountId { get; init; }
    public Money Amount { get; init; }
    public string TenderType { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public DateTimeOffset CapturedAt { get; init; }
}

/// <summary>Allocates part of a group receipt to one company. Dispatches a saga leg.</summary>
public sealed class AllocateGroupReceiptCommand
{
    public Guid TenantId { get; init; }
    public Guid GroupReceiptId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? CustomerPartnerId { get; init; }
    public Money Amount { get; init; }
    public IReadOnlyList<Guid>? TargetInvoiceIds { get; init; }
}

/// <summary>Reverses the entire group receipt. Dispatches reversing legs.</summary>
public sealed class ReverseGroupReceiptCommand
{
    public Guid TenantId { get; init; }
    public Guid GroupReceiptId { get; init; }
}

/// <summary>Handler for <see cref="CaptureGroupReceiptCommand"/>.</summary>
public sealed class CaptureGroupReceiptCommandHandler
{
    private readonly IGroupReceiptService _service;

    public CaptureGroupReceiptCommandHandler(IGroupReceiptService service)
    {
        _service = service;
    }

    public Task<Guid> HandleAsync(CaptureGroupReceiptCommand command, CancellationToken cancellationToken = default)
    {
        return _service.CaptureAsync(
            command.TenantId, command.CapturingCompanyId, command.BankAccountId,
            command.Amount, command.TenderType, command.Reference, command.CapturedAt,
            cancellationToken);
    }
}

/// <summary>Handler for <see cref="AllocateGroupReceiptCommand"/>.</summary>
public sealed class AllocateGroupReceiptCommandHandler
{
    private readonly IGroupReceiptService _service;

    public AllocateGroupReceiptCommandHandler(IGroupReceiptService service)
    {
        _service = service;
    }

    public Task HandleAsync(AllocateGroupReceiptCommand command, CancellationToken cancellationToken = default)
    {
        return _service.AllocateAsync(
            command.TenantId, command.GroupReceiptId, command.CompanyId,
            command.CustomerPartnerId, command.Amount, command.TargetInvoiceIds,
            cancellationToken);
    }
}

/// <summary>Handler for <see cref="ReverseGroupReceiptCommand"/>.</summary>
public sealed class ReverseGroupReceiptCommandHandler
{
    private readonly IGroupReceiptService _service;

    public ReverseGroupReceiptCommandHandler(IGroupReceiptService service)
    {
        _service = service;
    }

    public Task HandleAsync(ReverseGroupReceiptCommand command, CancellationToken cancellationToken = default)
    {
        return _service.ReverseAsync(command.TenantId, command.GroupReceiptId, cancellationToken);
    }
}

/// <summary>Queries unallocated group receipts for the ageing report.</summary>
public sealed class GetUnallocatedGroupReceiptsQuery
{
    public Guid TenantId { get; init; }
}

/// <summary>Handler for <see cref="GetUnallocatedGroupReceiptsQuery"/>.</summary>
public sealed class GetUnallocatedGroupReceiptsQueryHandler
{
    private readonly IGroupReceiptRepository _repository;

    public GetUnallocatedGroupReceiptsQueryHandler(IGroupReceiptRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<Domain.Registry.GroupReceipt>> HandleAsync(
        GetUnallocatedGroupReceiptsQuery query, CancellationToken cancellationToken = default)
    {
        return await _repository.GetUnallocatedAsync(query.TenantId, cancellationToken);
    }
}
