using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VumaRetail.Domain.HrManagement;
using VumaRetail.Domain.HrWorkforce;
using VumaRetail.Infrastructure.Persistence;

namespace VumaRetail.ArchitectureTests;

/// <summary>Stage 25/26 schema and isolation rules.</summary>
public sealed class HrStagesRulesTests
{
    private static readonly IModel Model = new DesignTimeDbContextFactory().CreateDbContext([]).Model;

    [Theory]
    [InlineData(typeof(Employee), "hr_management", "employees")]
    [InlineData(typeof(EmploymentContract), "hr_management", "employment_contracts")]
    [InlineData(typeof(LeaveRequest), "hr_management", "leave_requests")]
    [InlineData(typeof(Shift), "hr_workforce", "shifts")]
    [InlineData(typeof(AttendanceRecord), "hr_workforce", "attendance_records")]
    public void Hr_entities_use_their_module_schema(Type type, string schema, string table)
    {
        IEntityType entity = Model.FindEntityType(type)!;
        Assert.Equal(schema, entity.GetSchema());
        Assert.Equal(table, entity.GetTableName());
        Assert.NotNull(entity.GetQueryFilter());
    }

    [Fact]
    public void Hr_entities_have_no_foreign_keys_to_other_modules()
    {
        Assert.All(new[] { typeof(Employee), typeof(EmploymentContract), typeof(LeaveRequest), typeof(Shift), typeof(AttendanceRecord) },
            type => Assert.Empty(Model.FindEntityType(type)!.GetForeignKeys()));
    }
}
