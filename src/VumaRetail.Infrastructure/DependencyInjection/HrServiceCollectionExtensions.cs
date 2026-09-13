 #pragma warning disable CS1591, IDE0011
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Hr;
using VumaRetail.Infrastructure.Persistence.Repositories;

namespace VumaRetail.Infrastructure.DependencyInjection;

public static class HrServiceCollectionExtensions
{
    public static IServiceCollection AddVumaHr(this IServiceCollection services)
    { services.AddScoped<IEmployeeRepository, EmployeeRepository>(); services.AddScoped<IEmploymentContractRepository, EmploymentContractRepository>(); services.AddScoped<IShiftRepository, ShiftRepository>(); services.AddScoped<IShiftSwapRequestRepository, ShiftSwapRequestRepository>(); services.AddScoped<IRosterPublicationRepository, RosterPublicationRepository>(); services.AddScoped<IAttendanceRepository, AttendanceRepository>(); services.AddScoped<ILeaveRepository, LeaveRepository>(); services.AddScoped<IEmployeeDocumentRepository, EmployeeDocumentRepository>(); return services; }
}
