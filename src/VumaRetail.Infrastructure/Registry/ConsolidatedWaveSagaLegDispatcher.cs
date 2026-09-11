using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Application.Warehouse.Commands;
using VumaRetail.Domain.Registry;
using VumaRetail.Domain.Warehouse;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>
/// Applies one cross-company consolidated-wave leg in one company database. The registry saga
/// carries the immutable grouped demand; this dispatcher creates only that company's wave/tasks.
/// </summary>
public sealed class ConsolidatedWaveSagaLegDispatcher(
    IServiceScopeFactory scopes) : ISagaLegDispatcher
{
    public bool CanDispatch(string intentType) => intentType == CrossCompanyConsolidatedWaveSaga.IntentType;

    public async Task DispatchAsync(
        SagaIntent intent,
        SagaLeg leg,
        CancellationToken cancellationToken = default)
    {
        CrossCompanyConsolidatedWavePayload payload = ReadPayload(intent);
        if (payload.TenantId != intent.TenantId || !payload.LocalWaveIds.TryGetValue(leg.CompanyId, out Guid waveId))
        {
            throw new InvalidOperationException("The wave saga payload does not match its company leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);

        if (await companyDb.PickWaves.AnyAsync(x => x.Id == waveId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        IReadOnlyList<CrossCompanyWaveLine> lines = payload.Lines
            .Where(x => x.CompanyId == leg.CompanyId)
            .ToArray();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A company wave leg must contain at least one line.");
        }

        await using var transaction = await companyDb.Database
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        PickWave wave = PickWave.OpenConsolidated(
            waveId, intent.TenantId, payload.StoreId, lines[0].LocationId,
            payload.GeographyLevel, payload.GeographyValue,
            payload.PeriodFrom, payload.PeriodTo, leg.CompanyId);
        companyDb.PickWaves.Add(wave);

        foreach (var group in lines.GroupBy(x => new { x.ItemId, x.ItemVariantId, x.UnitOfMeasure, x.PackSize }))
        {
            Guid taskId = DeterministicId(leg.LegId, group.Key.ItemId, group.Key.ItemVariantId, group.Key.UnitOfMeasure, group.Key.PackSize);
            PickTask task = PickTask.Create(
                intent.TenantId, payload.StoreId, wave.Id,
                group.Key.ItemId, group.Key.ItemVariantId,
                new Domain.Primitives.Quantity(group.Sum(x => x.Quantity), group.Key.UnitOfMeasure),
                $"saga:{intent.Id:N}:{group.Key.PackSize}", taskId);
            companyDb.PickTasks.Add(task);

            foreach (CrossCompanyWaveLine line in group)
            {
                companyDb.PickWaveLineBreakdowns.Add(PickWaveLineBreakdown.Create(
                    intent.TenantId, payload.StoreId, task.Id,
                    line.OrderId, line.OrderLineId, line.Quantity));
            }
        }

        await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompensateAsync(
        SagaIntent intent,
        SagaLeg leg,
        CancellationToken cancellationToken = default)
    {
        CrossCompanyConsolidatedWavePayload payload = ReadPayload(intent);
        if (!payload.LocalWaveIds.TryGetValue(leg.CompanyId, out Guid waveId))
        {
            throw new InvalidOperationException("The wave saga payload does not contain this company leg.");
        }

        using IServiceScope scope = scopes.CreateScope();
        Bind(scope, intent.TenantId, leg.CompanyId);
        ICompanyDbContextFactory databases = scope.ServiceProvider.GetRequiredService<ICompanyDbContextFactory>();
        await using VumaRetailDbContext companyDb = await databases.CreateAsync(cancellationToken).ConfigureAwait(false);
        PickWave? wave = await companyDb.PickWaves
            .SingleOrDefaultAsync(x => x.Id == waveId, cancellationToken)
            .ConfigureAwait(false);
        if (wave is null || wave.Status == PickWaveStatus.Cancelled)
        {
            return;
        }

        if (wave.Status == PickWaveStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped company wave cannot be compensated by cancellation.");
        }

        IReadOnlyList<PickTask> tasks = await companyDb.PickTasks
            .Where(x => x.PickWaveId == waveId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (PickTask task in tasks.Where(x => x.Status is not (PickTaskStatus.Cancelled or PickTaskStatus.Picked or PickTaskStatus.ShortPicked)))
        {
            task.Cancel();
        }

        wave.Cancel();
        await companyDb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CrossCompanyConsolidatedWavePayload ReadPayload(SagaIntent intent)
        => JsonSerializer.Deserialize<CrossCompanyConsolidatedWavePayload>(intent.Payload)
            ?? throw new InvalidOperationException("The wave saga payload is invalid.");

    private static void Bind(IServiceScope scope, Guid tenantId, Guid companyId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().SetCompany(companyId);
    }

    private static Guid DeterministicId(Guid legId, Guid? itemId, Guid? variantId, string uom, string packSize)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] input = System.Text.Encoding.UTF8.GetBytes(
            $"{legId:N}|{itemId:N}|{variantId:N}|{uom.Trim()}|{packSize.Trim()}");
        byte[] hash = sha.ComputeHash(input);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
