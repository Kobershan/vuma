 #pragma warning disable CS1591, IDE0011, CA1062
using VumaRetail.Application.Abstractions;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;

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
public sealed record DecideLeaveCommand(Guid LeaveRequestId, bool Approved) : ICommand;
public sealed record ListEmployeesQuery : IQuery<IReadOnlyList<Employee>>;
public sealed record ListEmploymentContractsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmploymentContract>>;
public sealed record ListLeaveRequestsQuery(Guid? EmployeeId = null) : IQuery<IReadOnlyList<LeaveRequest>>;
public sealed record ListShiftsQuery(DateTimeOffset From, DateTimeOffset To, Guid? EmployeeId = null) : IQuery<IReadOnlyList<Shift>>;

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
    public async Task<Guid> HandleAsync(CreateShiftCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var s = Shift.Create(tenant.TenantId, c.EmployeeId, c.StartsAt, c.EndsAt, c.Role, c.StoreId); shifts.Add(s); return s.Id; }
}
public sealed class RecordAttendanceCommandHandler(IEmployeeRepository employees, IAttendanceRepository attendance, ITenantContext tenant) : ICommandHandler<RecordAttendanceCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordAttendanceCommand c, CancellationToken token = default) { if (await employees.FindAsync(c.EmployeeId, token) is null) throw new KeyNotFoundException("Employee was not found."); var a = AttendanceRecord.Record(tenant.TenantId, c.EmployeeId, c.ShiftId, c.EventType, c.OccurredAt, c.Source); attendance.Add(a); return a.Id; }
}
public sealed class DecideLeaveCommandHandler(ILeaveRepository leaves, IClock clock) : ICommandHandler<DecideLeaveCommand, Unit>
{
    public async Task<Unit> HandleAsync(DecideLeaveCommand c, CancellationToken token = default) { var leave = await leaves.FindAsync(c.LeaveRequestId, token) ?? throw new KeyNotFoundException("Leave request was not found."); if (c.Approved) leave.Approve(clock.UtcNow); else leave.Reject(clock.UtcNow); return Unit.Value; }
}
public sealed class ListEmployeesQueryHandler(IEmployeeRepository employees) : IQueryHandler<ListEmployeesQuery, IReadOnlyList<Employee>> { public Task<IReadOnlyList<Employee>> HandleAsync(ListEmployeesQuery q, CancellationToken t = default) => employees.ListAsync(t); }
public sealed class ListEmploymentContractsQueryHandler(IEmploymentContractRepository contracts) : IQueryHandler<ListEmploymentContractsQuery, IReadOnlyList<EmploymentContract>> { public Task<IReadOnlyList<EmploymentContract>> HandleAsync(ListEmploymentContractsQuery q, CancellationToken t = default) => contracts.ListAsync(q.EmployeeId, t); }
public sealed class ListLeaveRequestsQueryHandler(ILeaveRepository leaves) : IQueryHandler<ListLeaveRequestsQuery, IReadOnlyList<LeaveRequest>> { public Task<IReadOnlyList<LeaveRequest>> HandleAsync(ListLeaveRequestsQuery q, CancellationToken t = default) => leaves.ListAsync(q.EmployeeId, t); }
public sealed class ListShiftsQueryHandler(IShiftRepository shifts) : IQueryHandler<ListShiftsQuery, IReadOnlyList<Shift>> { public Task<IReadOnlyList<Shift>> HandleAsync(ListShiftsQuery q, CancellationToken t = default) => shifts.ListAsync(q.From, q.To, q.EmployeeId, t); }

public interface IEmployeeRepository { Task<Employee?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<Employee>> ListAsync(CancellationToken token = default); void Add(Employee employee); }
public interface IEmploymentContractRepository { void Add(EmploymentContract contract); Task<IReadOnlyList<EmploymentContract>> ListAsync(Guid employeeId, CancellationToken token = default); }
public interface IShiftRepository { void Add(Shift shift); Task<IReadOnlyList<Shift>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken token = default); }
public interface IAttendanceRepository { void Add(AttendanceRecord record); }
public interface ILeaveRepository { Task<LeaveRequest?> FindAsync(Guid id, CancellationToken token = default); Task<IReadOnlyList<LeaveRequest>> ListAsync(Guid? employeeId, CancellationToken token = default); void Add(LeaveRequest leave); }
