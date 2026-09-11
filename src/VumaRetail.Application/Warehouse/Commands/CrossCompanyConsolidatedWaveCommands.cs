using System.Text.Json;
using FluentValidation;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Abstractions.Sync;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Application.Warehouse.Commands;

/// <summary>One company-local demand line in a cross-company consolidated wave.</summary>
public sealed record CrossCompanyWaveLine(
    Guid CompanyId,
    Guid LocationId,
    Guid OrderId,
    Guid OrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    decimal Quantity,
    string UnitOfMeasure,
    string PackSize,
    string GeographyValue);

/// <summary>Builds a consolidated wave coordinated by a registry saga.</summary>
[CommandSideEffect(SideEffect.Write)]
public sealed record BuildCrossCompanyConsolidatedWaveCommand(
    Guid TenantId,
    Guid? StoreId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string GeographyLevel,
    string GeographyValue,
    IReadOnlyList<CrossCompanyWaveLine> Lines,
    string IdempotencyKey) : ICommand<Guid>;

/// <summary>Stable saga type shared by the application command and company dispatcher.</summary>
public static class CrossCompanyConsolidatedWaveSaga
{
    /// <summary>Registry saga intent type for cross-company warehouse waves.</summary>
    public const string IntentType = "warehouse-cross-company-wave";
}

/// <summary>Validates the cross-company wave boundary before a registry intent is created.</summary>
public sealed class BuildCrossCompanyConsolidatedWaveCommandValidator
    : AbstractValidator<BuildCrossCompanyConsolidatedWaveCommand>
{
    /// <summary>Builds the validation rules.</summary>
    public BuildCrossCompanyConsolidatedWaveCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PeriodFrom).NotEmpty();
        RuleFor(x => x.PeriodTo).GreaterThanOrEqualTo(x => x.PeriodFrom);
        RuleFor(x => x.GeographyLevel).Must(x => x is "Province" or "City" or "Suburb");
        RuleFor(x => x.GeographyValue).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

/// <summary>Creates one durable registry intent with one independent company leg per company.</summary>
public sealed class BuildCrossCompanyConsolidatedWaveCommandHandler(
    ISagaCoordinator coordinator,
    IClock clock,
    IHybridClock hybridClock,
    ITenantContext tenantContext,
    IOperatorContext operatorContext,
    IPrincipalAccessor principal)
    : ICommandHandler<BuildCrossCompanyConsolidatedWaveCommand, Guid>
{
    /// <summary>Creates and executes the registry saga.</summary>
    public async Task<Guid> HandleAsync(
        BuildCrossCompanyConsolidatedWaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.TenantId != tenantContext.TenantId)
        {
            throw new UnauthorizedAccessException("The wave tenant does not match the authenticated tenant.");
        }
        ValidateLines(command);

        var companies = command.Lines.Select(x => x.CompanyId).Distinct().OrderBy(x => x).ToArray();
        if (companies.Length < 2)
        {
            throw new InvalidOperationException("A cross-company wave requires at least two companies.");
        }

        var localWaves = companies.ToDictionary(
            companyId => companyId,
            _ => UuidV7.NewGuid());

        var payload = new CrossCompanyConsolidatedWavePayload(
            command.TenantId, command.StoreId, command.PeriodFrom, command.PeriodTo,
            command.GeographyLevel.Trim(), command.GeographyValue.Trim(),
            command.Lines, localWaves);

        SagaIntent intent = SagaIntent.Create(
            command.TenantId,
            CrossCompanyConsolidatedWaveSaga.IntentType,
            command.IdempotencyKey,
            clock.UtcNow,
            JsonSerializer.Serialize(payload));
        intent.Authorize(
            operatorContext.RequireOperatorId(),
            principal.Principal,
            hybridClock.Next().ToString());

        foreach (Guid companyId in companies)
        {
            intent.AddLeg(companyId);
        }

        SagaResult result = await coordinator.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The cross-company wave remains in progress; failed legs are retryable.");
        }

        return result.IntentId;
    }

    private static void ValidateLines(BuildCrossCompanyConsolidatedWaveCommand command)
    {
        HashSet<Guid> lineIds = [];
        foreach (CrossCompanyWaveLine line in command.Lines)
        {
            if (line.CompanyId == Guid.Empty || line.LocationId == Guid.Empty || line.OrderId == Guid.Empty
                || line.OrderLineId == Guid.Empty || !lineIds.Add(line.OrderLineId))
            {
                throw new InvalidOperationException("Cross-company wave lines must have unique order-line identities and valid ownership.");
            }

            if (line.Quantity <= 0m || string.IsNullOrWhiteSpace(line.UnitOfMeasure))
            {
                throw new InvalidOperationException($"Cross-company wave line {line.OrderLineId} has invalid quantity or unit.");
            }

            if (!string.Equals(line.GeographyValue.Trim(), command.GeographyValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Cross-company wave line {line.OrderLineId} is outside the requested geography.");
            }
        }

        foreach (var companyLines in command.Lines.GroupBy(x => x.CompanyId))
        {
            if (companyLines.Select(x => x.LocationId).Distinct().Count() != 1)
            {
                throw new InvalidOperationException($"Company {companyLines.Key} cannot span multiple pick locations in one wave leg.");
            }
        }
    }
}

/// <summary>Sanitised durable payload consumed independently by each company leg.</summary>
public sealed record CrossCompanyConsolidatedWavePayload(
    Guid TenantId,
    Guid? StoreId,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string GeographyLevel,
    string GeographyValue,
    IReadOnlyList<CrossCompanyWaveLine> Lines,
    IReadOnlyDictionary<Guid, Guid> LocalWaveIds);
