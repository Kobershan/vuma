#pragma warning disable CS1591, IDE0011, CA1062
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Web.Api;
using VumaRetail.Web.Licensing;

namespace VumaRetail.Web.Hr;

public static class HrEndpoints
{
    public static IEndpointRouteBuilder MapVumaHr(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapVumaApi();
        var hr = api.MapGroup("/hr").WithTags("HR").RequireModule("hr");
        hr.MapGet("/employees", async (IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListEmployeesQuery(), ct))).RequirePermission(HrPermissions.View);
        hr.MapPost("/employees", async (CreateEmployeeRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/hr/employees", await d.SendAsync(new CreateEmployeeCommand(r.EmployeeNumber, r.FirstName, r.LastName, r.EmploymentType, r.PreferredName, r.Email, r.Phone), ct))).RequirePermission(HrPermissions.Manage);
        hr.MapGet("/employees/{employeeId:guid}/contracts", async (Guid employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListEmploymentContractsQuery(employeeId), ct))).RequirePermission(HrPermissions.View);
        hr.MapPost("/employees/{employeeId:guid}/contracts", async (Guid employeeId, EmploymentContractRequest r, IDispatcher d, CancellationToken ct) => Results.Created($"/api/v1/hr/employees/{employeeId}/contracts", await d.SendAsync(new CreateEmploymentContractCommand(employeeId, r.StartsOn, r.EndsOn, r.HourlyRate, r.Currency), ct))).RequirePermission(HrPermissions.Manage);
        hr.MapGet("/employees/{employeeId:guid}/documents", async (Guid employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListEmployeeDocumentsQuery(employeeId), ct))).RequirePermission(HrPermissions.View);
        hr.MapPost("/employees/{employeeId:guid}/documents", async (Guid employeeId, EmployeeDocumentRequest r, IDispatcher d, CancellationToken ct) => Results.Created($"/api/v1/hr/employees/{employeeId}/documents", await d.SendAsync(new RecordEmployeeDocumentCommand(employeeId, r.DocumentType, r.BlobKey, r.ContentSha256, r.ExpiresOn), ct))).RequirePermission(HrPermissions.Manage);
        hr.MapPost("/employees/{employeeId:guid}/suspend", async (Guid employeeId, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new SuspendEmployeeCommand(employeeId), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.Manage);
        hr.MapPost("/employees/{employeeId:guid}/activate", async (Guid employeeId, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new ActivateEmployeeCommand(employeeId), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.Manage);
        hr.MapPost("/employees/{employeeId:guid}/terminate", async (Guid employeeId, TerminateEmployeeRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new TerminateEmployeeCommand(employeeId, r.TerminatedAt), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.Manage);
        hr.MapPost("/employees/{employeeId:guid}/disciplinary-cases", async (Guid employeeId, DisciplinaryCaseRequest r, IDispatcher d, CancellationToken ct) => Results.Created($"/api/v1/hr/employees/{employeeId}/disciplinary-cases", await d.SendAsync(new OpenDisciplinaryCaseCommand(r.CompanyId, employeeId, r.IncidentOn, r.Allegation), ct))).RequirePermission(HrPermissions.DisciplinaryManage);
        hr.MapPost("/disciplinary-cases/{id:guid}/investigate", async (Guid id, DisciplinaryInvestigationRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new StartDisciplinaryInvestigationCommand(id, r.StartedAt), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.DisciplinaryManage);
        hr.MapPost("/disciplinary-cases/{id:guid}/decision", async (Guid id, DisciplinaryDecisionRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DecideDisciplinaryCaseCommand(id, r.Decision, r.DecidedAt), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.DisciplinaryManage);
        hr.MapGet("/disciplinary-cases", async (Guid companyId, Guid? employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListDisciplinaryCasesQuery(companyId, employeeId), ct))).RequirePermission(HrPermissions.View);
        hr.MapGet("/payroll/export", async (DateOnly from, DateOnly to, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new GeneratePayrollExportQuery(from, to), ct))).RequirePermission(HrPermissions.PayrollExport);
        hr.MapGet("/leave", async (Guid? employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListLeaveRequestsQuery(employeeId), ct))).RequirePermission(HrPermissions.LeaveView);
        hr.MapPost("/leave", async (CreateLeaveRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/hr/leave", await d.SendAsync(new CreateLeaveRequestCommand(r.EmployeeId, r.From, r.To, r.LeaveType, r.Reason), ct))).RequirePermission(HrPermissions.LeaveManage);
        hr.MapPost("/leave/{id:guid}/decision", async (Guid id, LeaveDecisionRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DecideLeaveCommand(id, r.Approved), ct); return Results.NoContent(); }).RequirePermission(HrPermissions.LeaveManage);
        var workforce = api.MapGroup("/workforce").WithTags("Workforce").RequireModule("workforce");
        workforce.MapGet("/shifts", async (DateTimeOffset from, DateTimeOffset to, Guid? employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListShiftsQuery(from, to, employeeId), ct))).RequirePermission(WorkforcePermissions.View);
        workforce.MapGet("/employees/{employeeId:guid}/availability", async (Guid employeeId, DateTimeOffset from, DateTimeOffset to, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new GetEmployeeAvailabilityQuery(employeeId, from, to), ct))).RequirePermission(WorkforcePermissions.View);
        workforce.MapPost("/shifts", async (CreateShiftRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/shifts", await d.SendAsync(new CreateShiftCommand(r.EmployeeId, r.StartsAt, r.EndsAt, r.Role, r.StoreId), ct))).RequirePermission(WorkforcePermissions.Manage);
        workforce.MapPost("/shift-swaps", async (RequestShiftSwapRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/shift-swaps", await d.SendAsync(new RequestShiftSwapCommand(r.ShiftId, r.FromEmployeeId, r.ToEmployeeId, r.RequestedAt), ct))).RequirePermission(WorkforcePermissions.Manage);
        workforce.MapPost("/shift-swaps/{id:guid}/decision", async (Guid id, ShiftSwapDecisionRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DecideShiftSwapCommand(id, r.Approved), ct); return Results.NoContent(); }).RequirePermission(WorkforcePermissions.Manage);
        workforce.MapPost("/rosters/publish", async (PublishRosterRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/rosters", await d.SendAsync(new PublishRosterCommand(r.CompanyId, r.From, r.To, r.StoreId), ct))).RequirePermission(WorkforcePermissions.Manage);
        workforce.MapPost("/attendance", async (RecordAttendanceRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/attendance", await d.SendAsync(new RecordAttendanceCommand(r.EmployeeId, r.ShiftId, r.EventType, r.OccurredAt, r.Source), ct))).RequirePermission(WorkforcePermissions.AttendanceRecord);
        return endpoints;
    }
    public sealed record CreateEmployeeRequest(string EmployeeNumber, string FirstName, string LastName, EmploymentType EmploymentType, string? PreferredName, string? Email, string? Phone);
    public sealed record LeaveDecisionRequest(bool Approved);
    public sealed record EmploymentContractRequest(DateOnly StartsOn, DateOnly? EndsOn, decimal HourlyRate, string Currency);
    public sealed record EmployeeDocumentRequest(string DocumentType, string BlobKey, string ContentSha256, DateOnly? ExpiresOn);
    public sealed record TerminateEmployeeRequest(DateTimeOffset TerminatedAt);
    public sealed record DisciplinaryCaseRequest(Guid CompanyId, DateOnly IncidentOn, string Allegation);
    public sealed record DisciplinaryInvestigationRequest(DateTimeOffset StartedAt);
    public sealed record DisciplinaryDecisionRequest(string Decision, DateTimeOffset DecidedAt);
    public sealed record CreateLeaveRequest(Guid EmployeeId, DateOnly From, DateOnly To, string LeaveType, string? Reason);
    public sealed record CreateShiftRequest(Guid EmployeeId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Role, Guid? StoreId);
    public sealed record RecordAttendanceRequest(Guid EmployeeId, Guid? ShiftId, AttendanceEventType EventType, DateTimeOffset OccurredAt, string? Source);
    public sealed record RequestShiftSwapRequest(Guid ShiftId, Guid FromEmployeeId, Guid ToEmployeeId, DateTimeOffset RequestedAt);
    public sealed record ShiftSwapDecisionRequest(bool Approved);
    public sealed record PublishRosterRequest(Guid CompanyId, DateTimeOffset From, DateTimeOffset To, Guid? StoreId);
}
