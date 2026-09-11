#pragma warning disable CS1591, IDE0011, CA1062
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Web.Api;

namespace VumaRetail.Web.Hr;

public static class HrEndpoints
{
    public static IEndpointRouteBuilder MapVumaHr(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapVumaApi();
        var hr = api.MapGroup("/hr").WithTags("HR");
        hr.MapGet("/employees", async (IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListEmployeesQuery(), ct)));
        hr.MapPost("/employees", async (CreateEmployeeRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/hr/employees", await d.SendAsync(new CreateEmployeeCommand(r.EmployeeNumber, r.FirstName, r.LastName, r.EmploymentType, r.PreferredName, r.Email, r.Phone), ct)));
        hr.MapGet("/employees/{employeeId:guid}/contracts", async (Guid employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListEmploymentContractsQuery(employeeId), ct)));
        hr.MapPost("/employees/{employeeId:guid}/contracts", async (Guid employeeId, EmploymentContractRequest r, IDispatcher d, CancellationToken ct) => Results.Created($"/api/v1/hr/employees/{employeeId}/contracts", await d.SendAsync(new CreateEmploymentContractCommand(employeeId, r.StartsOn, r.EndsOn, r.HourlyRate, r.Currency), ct)));
        hr.MapGet("/leave", async (Guid? employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListLeaveRequestsQuery(employeeId), ct)));
        hr.MapPost("/leave", async (CreateLeaveRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/hr/leave", await d.SendAsync(new CreateLeaveRequestCommand(r.EmployeeId, r.From, r.To, r.LeaveType, r.Reason), ct)));
        hr.MapPost("/leave/{id:guid}/decision", async (Guid id, LeaveDecisionRequest r, IDispatcher d, CancellationToken ct) => { await d.SendAsync(new DecideLeaveCommand(id, r.Approved), ct); return Results.NoContent(); });
        var workforce = api.MapGroup("/workforce").WithTags("Workforce");
        workforce.MapGet("/shifts", async (DateTimeOffset from, DateTimeOffset to, Guid? employeeId, IDispatcher d, CancellationToken ct) => Results.Ok(await d.QueryAsync(new ListShiftsQuery(from, to, employeeId), ct)));
        workforce.MapPost("/shifts", async (CreateShiftRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/shifts", await d.SendAsync(new CreateShiftCommand(r.EmployeeId, r.StartsAt, r.EndsAt, r.Role, r.StoreId), ct)));
        workforce.MapPost("/attendance", async (RecordAttendanceRequest r, IDispatcher d, CancellationToken ct) => Results.Created("/api/v1/workforce/attendance", await d.SendAsync(new RecordAttendanceCommand(r.EmployeeId, r.ShiftId, r.EventType, r.OccurredAt, r.Source), ct)));
        return endpoints;
    }
    public sealed record CreateEmployeeRequest(string EmployeeNumber, string FirstName, string LastName, EmploymentType EmploymentType, string? PreferredName, string? Email, string? Phone);
    public sealed record LeaveDecisionRequest(bool Approved);
    public sealed record EmploymentContractRequest(DateOnly StartsOn, DateOnly? EndsOn, decimal HourlyRate, string Currency);
    public sealed record CreateLeaveRequest(Guid EmployeeId, DateOnly From, DateOnly To, string LeaveType, string? Reason);
    public sealed record CreateShiftRequest(Guid EmployeeId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Role, Guid? StoreId);
    public sealed record RecordAttendanceRequest(Guid EmployeeId, Guid? ShiftId, AttendanceEventType EventType, DateTimeOffset OccurredAt, string? Source);
}
