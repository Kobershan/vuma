#pragma warning disable CS1591
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.Assets;

namespace VumaRetail.Application.Assets;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateStoreChecklistCommand(Guid CompanyId, Guid? StoreId, string Code, string Name, IReadOnlyCollection<string> ItemCodes) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record SubmitChecklistExecutionCommand(Guid CompanyId, Guid? StoreId, Guid ChecklistId, Guid OperationId, string DeviceId, DateTimeOffset CapturedAt, DateTimeOffset SubmittedAt, string EvidenceReference) : ICommand<Guid>;

public sealed class CreateStoreChecklistCommandHandler(IChecklistRepository checklists, ITenantContext tenant, ICompanyContext company) : ICommandHandler<CreateStoreChecklistCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateStoreChecklistCommand c, CancellationToken token = default)
    { ArgumentNullException.ThrowIfNull(c); EnsureCompany(company, c.CompanyId); var checklist = StoreChecklist.Create(tenant.TenantId, c.StoreId, c.CompanyId, c.Code, c.Name, c.ItemCodes); checklists.Add(checklist); return Task.FromResult(checklist.Id); }
    internal static void EnsureCompany(ICompanyContext context, Guid expected) { if (context.CompanyId is not { } active || active != expected) { throw new InvalidOperationException("The asset company is not the active company."); } }
}
public sealed class SubmitChecklistExecutionCommandHandler(IChecklistRepository checklists, ITenantContext tenant, ICompanyContext company) : ICommandHandler<SubmitChecklistExecutionCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitChecklistExecutionCommand c, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(c); CreateStoreChecklistCommandHandler.EnsureCompany(company, c.CompanyId);
        var existing = await checklists.FindExecutionByOperationIdAsync(c.OperationId, token).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.CompanyId != c.CompanyId || existing.StoreId != c.StoreId || existing.ChecklistId != c.ChecklistId ||
                !string.Equals(existing.DeviceId, c.DeviceId.Trim(), StringComparison.Ordinal) ||
                existing.CapturedAt != c.CapturedAt || existing.SubmittedAt != c.SubmittedAt ||
                !string.Equals(existing.EvidenceReference, c.EvidenceReference.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The checklist operation was replayed with different content.");
            }
            return existing.Id;
        }
        StoreChecklist checklist = await checklists.FindAsync(c.ChecklistId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Checklist was not found.");
        if (checklist.CompanyId != c.CompanyId || checklist.StoreId != c.StoreId)
        {
            throw new InvalidOperationException("The checklist does not belong to the selected company or store.");
        }
        var execution = ChecklistExecution.Submit(tenant.TenantId, c.StoreId, c.CompanyId, c.ChecklistId, c.OperationId, c.DeviceId, c.CapturedAt, c.SubmittedAt, c.EvidenceReference);
        checklists.Add(execution); return execution.Id;
    }
}
