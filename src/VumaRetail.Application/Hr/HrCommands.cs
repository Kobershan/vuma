#pragma warning disable CS1591, IDE0011, CA1062
using System.Globalization;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Registry;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using System.Security.Cryptography;
using System.Text;

namespace VumaRetail.Application.Hr;

[CommandSideEffect(SideEffect.Write)]
public sealed record CreateEmployeeCommand(string EmployeeNumber, string FirstName, string LastName, EmploymentType EmploymentType, string? PreferredName = null, string? Email = null, string? Phone = null) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateEmploymentContractCommand(Guid EmployeeId, DateOnly StartsOn, DateOnly? EndsOn, decimal HourlyRate, string Currency) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateLeaveRequestCommand(Guid EmployeeId, DateOnly From, DateOnly To, string LeaveType, string? Reason = null) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record CreateShiftCommand(Guid EmployeeId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Role, Guid? StoreId = null) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordAttendanceCommand(Guid EmployeeId, Guid? ShiftId, AttendanceEventType EventType, DateTimeOffset OccurredAt, string? Source = null) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record RecordEmployeeDocumentCommand(Guid EmployeeId, string DocumentType, string BlobKey, string ContentSha256, DateOnly? ExpiresOn = null) : ICommand<Guid>;

[CommandSideEffect(SideEffect.Write)]
public sealed record SuspendEmployeeCommand(Guid EmployeeId) : ICommand;

[CommandSideEffect(SideEffect.Write)]
public sealed record ActivateEmployeeCommand(Guid EmployeeId) : ICommand;

[CommandSideEffect(SideEffect.Write)]
public sealed record TerminateEmployeeCommand(Guid EmployeeId, DateTimeOffset TerminatedAt) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record OpenDisciplinaryCaseCommand(Guid CompanyId, Guid EmployeeId, DateOnly IncidentOn, string Allegation) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record StartDisciplinaryInvestigationCommand(Guid CaseId, DateTimeOffset StartedAt) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record DecideDisciplinaryCaseCommand(Guid CaseId, string Decision, DateTimeOffset DecidedAt) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record DecideLeaveCommand(Guid LeaveRequestId, bool Approved) : ICommand;
public sealed record ListEmployeesQuery : IQuery<IReadOnlyList<Employee>>;
public sealed record ListEmploymentContractsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmploymentContract>>;
public sealed record ListLeaveRequestsQuery(Guid? EmployeeId = null) : IQuery<IReadOnlyList<LeaveRequest>>;
public sealed record ListShiftsQuery(DateTimeOffset From, DateTimeOffset To, Guid? EmployeeId = null) : IQuery<IReadOnlyList<Shift>>;
public sealed record GetEmployeeAvailabilityQuery(Guid EmployeeId, DateTimeOffset From, DateTimeOffset To) : IQuery<EmployeeAvailability>;
[CommandSideEffect(SideEffect.Write)]
public sealed record RequestShiftSwapCommand(Guid ShiftId, Guid FromEmployeeId, Guid ToEmployeeId, DateTimeOffset RequestedAt) : ICommand<Guid>;
[CommandSideEffect(SideEffect.Write)]
public sealed record DecideShiftSwapCommand(Guid ShiftSwapRequestId, bool Approved) : ICommand;
[CommandSideEffect(SideEffect.Write)]
public sealed record PublishRosterCommand(Guid CompanyId, DateTimeOffset From, DateTimeOffset To, Guid? StoreId = null) : ICommand<Guid>;
public sealed record ListEmployeeDocumentsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeDocument>>;
public sealed record ListDisciplinaryCasesQuery(Guid CompanyId, Guid? EmployeeId = null) : IQuery<IReadOnlyList<DisciplinaryCase>>;
public sealed record GeneratePayrollExportQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<PayrollExportRow>>;
public sealed record EmployeeAvailability(Guid EmployeeId, EmploymentStatus EmploymentStatus, bool Available, IReadOnlyList<Shift> ScheduledShifts);

public sealed class CreateEmployeeCommandHandler(IEmployeeRepository employees, ITenantContext tenant, IClock clock) : ICommandHandler<CreateEmployeeCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateEmployeeCommand c, CancellationToken token = default) { var e = Employee.Create(tenant.TenantId, c.EmployeeNumber, c.FirstName, c.LastName, clock.UtcNow, c.EmploymentType, c.PreferredName, c.Email, c.Phone); employees.Add(e); return Task.FromResult(e.Id); }
}
public sealed class CreateEmploymentContractCommandHandler(IEmployeeRepository employees, IEmploymentContractRepository contracts, ITenantContext tenant) : ICommandHandler<CreateEmploymentContractCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateEmploymentContractCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var contract = EmploymentContract.Create(tenant.TenantId, c.EmployeeId, c.StartsOn, c.EndsOn, c.HourlyRate, c.Currency); contracts.Add(contract); return contract.Id; }
}
public sealed class CreateLeaveRequestCommandHandler(IEmployeeRepository employees, ILeaveRepository leaves, ITenantContext tenant) : ICommandHandler<CreateLeaveRequestCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateLeaveRequestCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var leave = LeaveRequest.Create(tenant.TenantId, c.EmployeeId, c.From, c.To, c.LeaveType, c.Reason); leaves.Add(leave); return leave.Id; }
}
public sealed class CreateShiftCommandHandler(IEmployeeRepository employees, IShiftRepository shifts, ITenantContext tenant) : ICommandHandler<CreateShiftCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateShiftCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var s = Shift.Create(tenant.TenantId, c.EmployeeId, c.StartsAt, c.EndsAt, c.Role, c.StoreId); if ((await shifts.ListAsync(c.StartsAt, c.EndsAt, c.EmployeeId, token)).Any(existing => existing.Overlaps(c.StartsAt, c.EndsAt) && existing.Status != ShiftStatus.Cancelled)) throw new InvalidOperationException("An employee cannot have overlapping shifts."); shifts.Add(s); return s.Id; }
}
public sealed class RecordAttendanceCommandHandler(IEmployeeRepository employees, IAttendanceRepository attendance, ITenantContext tenant) : ICommandHandler<RecordAttendanceCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordAttendanceCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var a = AttendanceRecord.Record(tenant.TenantId, c.EmployeeId, c.ShiftId, c.EventType, c.OccurredAt, c.Source); attendance.Add(a); return a.Id; }
}
public sealed class RecordEmployeeDocumentCommandHandler(IEmployeeRepository employees, IEmployeeDocumentRepository documents, ITenantContext tenant) : ICommandHandler<RecordEmployeeDocumentCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordEmployeeDocumentCommand c, CancellationToken token = default)
    { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var document = EmployeeDocument.Record(tenant.TenantId, c.EmployeeId, c.DocumentType, c.BlobKey, c.ContentSha256, c.ExpiresOn); documents.Add(document); return document.Id; }
}

public sealed class SuspendEmployeeCommandHandler(IEmployeeRepository employees) : ICommandHandler<SuspendEmployeeCommand, Unit>
{
    public async Task<Unit> HandleAsync(SuspendEmployeeCommand command, CancellationToken token = default)
    {
        Employee employee = await employees.FindAsync(command.EmployeeId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Employee was not found.");
        employee.Suspend();
        return Unit.Value;
    }
}

public sealed class ActivateEmployeeCommandHandler(IEmployeeRepository employees) : ICommandHandler<ActivateEmployeeCommand, Unit>
{
    public async Task<Unit> HandleAsync(ActivateEmployeeCommand command, CancellationToken token = default)
    {
        Employee employee = await employees.FindAsync(command.EmployeeId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Employee was not found.");
        employee.Activate();
        return Unit.Value;
    }
}

public sealed class TerminateEmployeeCommandHandler(IEmployeeRepository employees) : ICommandHandler<TerminateEmployeeCommand, Unit>
{
    public async Task<Unit> HandleAsync(TerminateEmployeeCommand command, CancellationToken token = default)
    {
        Employee employee = await employees.FindAsync(command.EmployeeId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Employee was not found.");
        employee.Terminate(command.TerminatedAt);
        return Unit.Value;
    }
}
public sealed class OpenDisciplinaryCaseCommandHandler(IEmployeeRepository employees, IDisciplinaryCaseRepository cases, ITenantContext tenant, ICompanyContext company, IClock clock) : ICommandHandler<OpenDisciplinaryCaseCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenDisciplinaryCaseCommand command, CancellationToken token = default)
    {
        if (company.CompanyId is not { } active || active != command.CompanyId)
            throw new InvalidOperationException("The HR company is not the active company.");
        if (await employees.FindAsync(command.EmployeeId, token).ConfigureAwait(false) is null)
            throw new KeyNotFoundException("Employee was not found.");
        var @case = DisciplinaryCase.Open(tenant.TenantId, command.CompanyId, command.EmployeeId, command.IncidentOn, command.Allegation, clock.UtcNow);
        cases.Add(@case);
        return @case.Id;
    }
}
public sealed class StartDisciplinaryInvestigationCommandHandler(IDisciplinaryCaseRepository cases) : ICommandHandler<StartDisciplinaryInvestigationCommand, Unit>
{
    public async Task<Unit> HandleAsync(StartDisciplinaryInvestigationCommand command, CancellationToken token = default)
    {
        var @case = await cases.FindAsync(command.CaseId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Disciplinary case was not found.");
        @case.StartInvestigation(command.StartedAt);
        return Unit.Value;
    }
}
public sealed class DecideDisciplinaryCaseCommandHandler(IDisciplinaryCaseRepository cases) : ICommandHandler<DecideDisciplinaryCaseCommand, Unit>
{
    public async Task<Unit> HandleAsync(DecideDisciplinaryCaseCommand command, CancellationToken token = default)
    {
        var @case = await cases.FindAsync(command.CaseId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Disciplinary case was not found.");
        @case.Decide(command.Decision, command.DecidedAt);
        return Unit.Value;
    }
}
public sealed class DecideLeaveCommandHandler(ILeaveRepository leaves, IClock clock) : ICommandHandler<DecideLeaveCommand, Unit>
{
    public async Task<Unit> HandleAsync(DecideLeaveCommand c, CancellationToken token = default) { var leave = await leaves.FindAsync(c.LeaveRequestId, token) ?? throw new KeyNotFoundException("Leave request was not found."); if (c.Approved) leave.Approve(clock.UtcNow); else leave.Reject(clock.UtcNow); return Unit.Value; }
}
public sealed class ListEmployeesQueryHandler(IEmployeeRepository employees) : IQueryHandler<ListEmployeesQuery, IReadOnlyList<Employee>> { public Task<IReadOnlyList<Employee>> HandleAsync(ListEmployeesQuery q, CancellationToken t = default) => employees.ListAsync(t); }
public sealed class ListEmploymentContractsQueryHandler(IEmploymentContractRepository contracts) : IQueryHandler<ListEmploymentContractsQuery, IReadOnlyList<EmploymentContract>> { public Task<IReadOnlyList<EmploymentContract>> HandleAsync(ListEmploymentContractsQuery q, CancellationToken t = default) => contracts.ListAsync(q.EmployeeId, t); }
public sealed class ListLeaveRequestsQueryHandler(ILeaveRepository leaves) : IQueryHandler<ListLeaveRequestsQuery, IReadOnlyList<LeaveRequest>> { public Task<IReadOnlyList<LeaveRequest>> HandleAsync(ListLeaveRequestsQuery q, CancellationToken t = default) => leaves.ListAsync(q.EmployeeId, t); }
public sealed class ListShiftsQueryHandler(IShiftRepository shifts) : IQueryHandler<ListShiftsQuery, IReadOnlyList<Shift>> { public Task<IReadOnlyList<Shift>> HandleAsync(ListShiftsQuery q, CancellationToken t = default) => shifts.ListAsync(q.From, q.To, q.EmployeeId, t); }
public sealed class GetEmployeeAvailabilityQueryHandler(IEmployeeRepository employees, IShiftRepository shifts) : IQueryHandler<GetEmployeeAvailabilityQuery, EmployeeAvailability>
{
    public async Task<EmployeeAvailability> HandleAsync(GetEmployeeAvailabilityQuery query, CancellationToken token = default)
    {
        if (query.To <= query.From) throw new ArgumentException("Availability window must end after it starts.", nameof(query));
        Employee employee = await employees.FindAsync(query.EmployeeId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Employee was not found.");
        IReadOnlyList<Shift> scheduled = await shifts.ListAsync(query.From, query.To, query.EmployeeId, token).ConfigureAwait(false);
        var activeShifts = scheduled.Where(shift => shift.Status != ShiftStatus.Cancelled && shift.Overlaps(query.From, query.To)).ToArray();
        return new EmployeeAvailability(employee.Id, employee.Status, employee.Status == EmploymentStatus.Active && activeShifts.Length == 0, activeShifts);
    }
}
public sealed class RequestShiftSwapCommandHandler(IEmployeeRepository employees, IShiftRepository shifts, IShiftSwapRequestRepository swaps, ITenantContext tenant) : ICommandHandler<RequestShiftSwapCommand, Guid>
{
    public async Task<Guid> HandleAsync(RequestShiftSwapCommand c, CancellationToken token = default)
    {
        Shift shift = await shifts.FindAsync(c.ShiftId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shift was not found.");
        if (shift.EmployeeId != c.FromEmployeeId) throw new InvalidOperationException("The requesting employee does not own the shift.");
        if (shift.Status != ShiftStatus.Planned) throw new InvalidOperationException("Only planned shifts can be swapped.");
        if (await employees.FindAsync(c.ToEmployeeId, token).ConfigureAwait(false) is null) throw new KeyNotFoundException("Target employee was not found.");
        var request = ShiftSwapRequest.Request(tenant.TenantId, c.ShiftId, c.FromEmployeeId, c.ToEmployeeId, c.RequestedAt);
        swaps.Add(request);
        return request.Id;
    }
}
public sealed class PublishRosterCommandHandler(IShiftRepository shifts, IRosterPublicationRepository publications, ITenantContext tenant, ICompanyContext company, IClock clock) : ICommandHandler<PublishRosterCommand, Guid>
{
    public async Task<Guid> HandleAsync(PublishRosterCommand c, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (company.CompanyId is not { } active || active != c.CompanyId) throw new InvalidOperationException("The roster company is not the active company.");
        IReadOnlyList<Shift> roster = await shifts.ListAsync(c.From, c.To, null, token).ConfigureAwait(false);
        var rows = roster.Where(x => c.StoreId is null || x.StoreId == c.StoreId).OrderBy(x => x.StartsAt).ThenBy(x => x.Id).Select(x => $"{x.Id:D}|{x.EmployeeId:D}|{x.StartsAt:O}|{x.EndsAt:O}|{x.Role}|{x.Status}");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
        var publication = RosterPublication.Publish(tenant.TenantId, c.StoreId, c.CompanyId, c.From, c.To, roster.Count, hash, clock.UtcNow);
        publications.Add(publication);
        return publication.Id;
    }
}
public sealed class DecideShiftSwapCommandHandler(IShiftSwapRequestRepository swaps, IShiftRepository shifts) : ICommandHandler<DecideShiftSwapCommand, Unit>
{
    public async Task<Unit> HandleAsync(DecideShiftSwapCommand c, CancellationToken token = default)
    {
        var request = await swaps.FindAsync(c.ShiftSwapRequestId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shift swap request was not found.");
        if (!c.Approved) { request.Reject(); return Unit.Value; }
        Shift shift = await shifts.FindAsync(request.ShiftId, token).ConfigureAwait(false) ?? throw new KeyNotFoundException("Shift was not found.");
        if (shift.EmployeeId != request.FromEmployeeId) throw new InvalidOperationException("The shift owner changed while the swap was pending.");
        if ((await shifts.ListAsync(shift.StartsAt, shift.EndsAt, request.ToEmployeeId, token).ConfigureAwait(false))
            .Any(existing => existing.Id != shift.Id && existing.Status != ShiftStatus.Cancelled && existing.Overlaps(shift.StartsAt, shift.EndsAt)))
            throw new InvalidOperationException("The target employee has an overlapping shift.");
        request.Approve();
        shift.TransferTo(request.ToEmployeeId);
        return Unit.Value;
    }
}
public sealed class ListEmployeeDocumentsQueryHandler(IEmployeeDocumentRepository documents) : IQueryHandler<ListEmployeeDocumentsQuery, IReadOnlyList<EmployeeDocument>> { public Task<IReadOnlyList<EmployeeDocument>> HandleAsync(ListEmployeeDocumentsQuery q, CancellationToken t = default) => documents.ListAsync(q.EmployeeId, t); }
public sealed class ListDisciplinaryCasesQueryHandler(IDisciplinaryCaseRepository cases, ICompanyContext company) : IQueryHandler<ListDisciplinaryCasesQuery, IReadOnlyList<DisciplinaryCase>>
{
    public async Task<IReadOnlyList<DisciplinaryCase>> HandleAsync(ListDisciplinaryCasesQuery query, CancellationToken token = default)
    {
        if (company.CompanyId is not { } active || active != query.CompanyId)
            throw new InvalidOperationException("The HR company is not the active company.");
        return await cases.ListAsync(query.CompanyId, query.EmployeeId, token).ConfigureAwait(false);
    }
}
public sealed record PayrollExportRow(Guid EmployeeId, string EmployeeNumber, decimal Hours, decimal HourlyRate, decimal GrossAmount, string Currency);
public sealed class GeneratePayrollExportQueryHandler(IEmployeeRepository employees, IEmploymentContractRepository contracts, IAttendanceRepository attendance) : IQueryHandler<GeneratePayrollExportQuery, IReadOnlyList<PayrollExportRow>>
{
    public async Task<IReadOnlyList<PayrollExportRow>> HandleAsync(GeneratePayrollExportQuery query, CancellationToken token = default)
    {
        if (query.To < query.From) throw new ArgumentException("Payroll period cannot end before it starts.", nameof(query));
        DateTimeOffset from = new(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset to = new(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var allEmployees = await employees.ListAsync(token).ConfigureAwait(false);
        var events = await attendance.ListAsync(from, to, null, token).ConfigureAwait(false);
        var rows = new List<PayrollExportRow>();
        foreach (var employee in allEmployees)
        {
            var employeeEvents = events.Where(x => x.EmployeeId == employee.Id).OrderBy(x => x.OccurredAt).ToArray();
            decimal hours = PayrollHoursCalculator.Calculate(employeeEvents);
            var contract = (await contracts.ListAsync(employee.Id, token).ConfigureAwait(false))
                .Where(x => x.StartsOn <= query.To && (x.EndsOn is null || x.EndsOn >= query.From))
                .OrderByDescending(x => x.StartsOn).FirstOrDefault();
            if (contract is null && hours > 0) throw new InvalidOperationException($"Employee {employee.EmployeeNumber} has no contract for the payroll period.");
            if (contract is not null)
                rows.Add(new PayrollExportRow(employee.Id, employee.EmployeeNumber, hours, contract.HourlyRate,
                    decimal.Round(hours * contract.HourlyRate, 2, MidpointRounding.AwayFromZero), contract.Currency));
        }
        return rows;
    }
}
internal static class PayrollHoursCalculator
{
    public static decimal Calculate(IReadOnlyList<AttendanceRecord> events)
    {
        DateTimeOffset? clockIn = null;
        DateTimeOffset? breakStart = null;
        TimeSpan worked = TimeSpan.Zero;
        foreach (var item in events)
        {
            switch (item.EventType)
            {
                case AttendanceEventType.ClockIn when clockIn is null: clockIn = item.OccurredAt; break;
                case AttendanceEventType.ClockOut when clockIn is not null && breakStart is null:
                    worked += item.OccurredAt - clockIn.Value; clockIn = null; break;
                case AttendanceEventType.BreakStart when clockIn is not null && breakStart is null: breakStart = item.OccurredAt; break;
                case AttendanceEventType.BreakEnd when breakStart is not null: worked += breakStart.Value - clockIn!.Value; clockIn = item.OccurredAt; breakStart = null; break;
                default: throw new InvalidOperationException("Attendance events are not in a valid payroll sequence.");
            }
        }
        if (clockIn is not null || breakStart is not null) throw new InvalidOperationException("Payroll period contains an unclosed attendance session.");
        return (decimal)worked.TotalHours;
    }
}
public static class PayrollExportCsv
{
    public static string Serialize(IReadOnlyCollection<PayrollExportRow> rows)
    {
        var lines = new List<string> { "employee_id,employee_number,hours,hourly_rate,gross_amount,currency" };
        lines.AddRange(rows.OrderBy(x => x.EmployeeNumber).Select(x => string.Join(",",
            x.EmployeeId.ToString("D"), Escape(x.EmployeeNumber), x.Hours.ToString("0.####", CultureInfo.InvariantCulture),
            x.HourlyRate.ToString("0.####", CultureInfo.InvariantCulture), x.GrossAmount.ToString("0.##", CultureInfo.InvariantCulture), Escape(x.Currency))));
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string Escape(string value) => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}

public interface IEmployeeRepository { Task<Employee?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<Employee>> ListAsync(CancellationToken token = default); void Add(Employee employee); }
public interface IEmploymentContractRepository { void Add(EmploymentContract contract); Task<IReadOnlyList<EmploymentContract>> ListAsync(Guid employeeId, CancellationToken token = default); }
public interface IShiftRepository { void Add(Shift shift); Task<Shift?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<Shift>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken token = default); }
public interface IShiftSwapRequestRepository { void Add(ShiftSwapRequest request); Task<ShiftSwapRequest?> FindAsync(Guid id, CancellationToken token = default); }
public interface IRosterPublicationRepository { void Add(RosterPublication publication); }
public interface IAttendanceRepository { Task<IReadOnlyList<AttendanceRecord>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken token = default); void Add(AttendanceRecord record); }
public interface ILeaveRepository { Task<LeaveRequest?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<LeaveRequest>> ListAsync(Guid? employeeId, CancellationToken token = default); void Add(LeaveRequest leave); }
public interface IEmployeeDocumentRepository { Task<IReadOnlyList<EmployeeDocument>> ListAsync(Guid employeeId, CancellationToken token = default); void Add(EmployeeDocument document); }
public interface IDisciplinaryCaseRepository { Task<DisciplinaryCase?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<DisciplinaryCase>> ListAsync(Guid companyId, Guid? employeeId, CancellationToken token = default); void Add(DisciplinaryCase @case); }
