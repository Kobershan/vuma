 #pragma warning disable CS1591, IDE0011, CA1062
using Microsoft.EntityFrameworkCore;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;

namespace VumaRetail.Infrastructure.Persistence.Repositories;

public sealed class EmployeeRepository(VumaRetailDbContext db) : IEmployeeRepository
{ public Task<Employee?> FindAsync(Guid id, CancellationToken t = default) => db.Employees.SingleOrDefaultAsync(x => x.Id == id, t); public async Task<IReadOnlyList<Employee>> ListAsync(CancellationToken t = default) => await db.Employees.OrderBy(x => x.EmployeeNumber).ToListAsync(t); public void Add(Employee e) => db.Employees.Add(e); }
public sealed class EmploymentContractRepository(VumaRetailDbContext db) : IEmploymentContractRepository
{ public void Add(EmploymentContract x) => db.EmploymentContracts.Add(x); public async Task<IReadOnlyList<EmploymentContract>> ListAsync(Guid employeeId, CancellationToken t = default) => await db.EmploymentContracts.Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.StartsOn).ToListAsync(t); }
public sealed class ShiftRepository(VumaRetailDbContext db) : IShiftRepository
{ public void Add(Shift x) => db.Shifts.Add(x); public async Task<IReadOnlyList<Shift>> ListAsync(DateTimeOffset from, DateTimeOffset to, Guid? employeeId, CancellationToken t = default) => await db.Shifts.Where(x => x.StartsAt < to && x.EndsAt > from && (employeeId == null || x.EmployeeId == employeeId)).OrderBy(x => x.StartsAt).ToListAsync(t); }
public sealed class AttendanceRepository(VumaRetailDbContext db) : IAttendanceRepository { public void Add(AttendanceRecord x) => db.AttendanceRecords.Add(x); }
public sealed class LeaveRepository(VumaRetailDbContext db) : ILeaveRepository { public Task<LeaveRequest?> FindAsync(Guid id, CancellationToken t = default) => db.LeaveRequests.SingleOrDefaultAsync(x => x.Id == id, t); public async Task<IReadOnlyList<LeaveRequest>> ListAsync(Guid? employeeId, CancellationToken t = default) => await db.LeaveRequests.Where(x => employeeId == null || x.EmployeeId == employeeId).OrderByDescending(x => x.From).ToListAsync(t); public void Add(LeaveRequest leave) => db.LeaveRequests.Add(leave); }
